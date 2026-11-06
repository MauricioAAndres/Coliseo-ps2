using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Gladiator.Character;
using Gladiator.Core;
using Gladiator.Progression;
using Gladiator.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Gladiator.Net
{
    public enum NetRole { None, Host, Client }

    /// <summary>What each player brings to a multiplayer game: their saved gladiator (look, gear, stats).</summary>
    [Serializable]
    public class NetGladiator
    {
        public string Name;
        public string Look;          // CharacterAppearance as JSON
        public string[] Armor;
        public int[] Sides;
        public string Main, Off, Shield;
        public int Str, Agi, Res;

        public static NetGladiator FromProfile(PlayerProfile p)
        {
            return new NetGladiator
            {
                Name = string.IsNullOrEmpty(p.Name) ? "Gladiador" : p.Name,
                Look = JsonUtility.ToJson(p.Appearance),
                Armor = (string[])p.Armor.Clone(),
                Sides = (int[])p.ArmorSides.Clone(),
                Main = p.MainHand, Off = p.OffHand, Shield = p.ShieldId,
                Str = p.Strength, Agi = p.Agility, Res = p.Resistance,
            };
        }

        public CharacterAppearance Appearance()
        {
            CharacterAppearance a = null;
            try { a = JsonUtility.FromJson<CharacterAppearance>(Look); } catch { }
            if (a == null) a = new CharacterAppearance();
            a.Name = Name;
            return a;
        }

        public ArmorSet ArmorSet => ArmorSet.FromIds(Armor ?? new string[8], Sides);
        public string LookSignature => Look + "|" + string.Join(",", Armor ?? new string[0]) + "|" + string.Join(",", Sides ?? new int[0]);
    }

    [Serializable]
    public class NetPlayer
    {
        public int Id;
        public bool Host;
        public string Address;
        public NetGladiator G;
    }

    [Serializable]
    class RosterMsg
    {
        public string Phase;
        public List<NetPlayer> Players = new List<NetPlayer>();
    }

    /// <summary>One TCP connection: length-prefixed UTF-8 messages, read on a background thread into a queue.</summary>
    class NetConn
    {
        public readonly TcpClient Tcp;
        readonly NetworkStream stream;
        readonly object sendLock = new object();
        public readonly ConcurrentQueue<string> Inbox = new ConcurrentQueue<string>();
        volatile bool closed;
        public bool Closed => closed;
        public int Id;
        public bool Welcomed;
        public float Since;
        public string Address;

        public NetConn(TcpClient tcp)
        {
            Tcp = tcp;
            tcp.NoDelay = true;
            tcp.SendTimeout = 3000;
            stream = tcp.GetStream();
            try { Address = (tcp.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString(); } catch { Address = "?"; }
            new Thread(ReadLoop) { IsBackground = true, Name = "NetRead" }.Start();
        }

        void ReadLoop()
        {
            var head = new byte[4];
            try
            {
                while (!closed)
                {
                    if (!ReadExact(head, 4)) break;
                    int len = BitConverter.ToInt32(head, 0);
                    if (len < 0 || len > (1 << 20)) break;
                    var buf = new byte[len];
                    if (!ReadExact(buf, len)) break;
                    Inbox.Enqueue(Encoding.UTF8.GetString(buf));
                }
            }
            catch { }
            closed = true;
        }

        bool ReadExact(byte[] b, int n)
        {
            int got = 0;
            while (got < n)
            {
                int r = stream.Read(b, got, n - got);
                if (r <= 0) return false;
                got += r;
            }
            return true;
        }

        public void Send(string msg)
        {
            if (closed) return;
            byte[] body = Encoding.UTF8.GetBytes(msg);
            var pkt = new byte[body.Length + 4];
            BitConverter.GetBytes(body.Length).CopyTo(pkt, 0);
            body.CopyTo(pkt, 4);
            try { lock (sendLock) stream.Write(pkt, 0, pkt.Length); }
            catch { closed = true; }
        }

        public void Close()
        {
            closed = true;
            try { Tcp.Close(); } catch { }
        }
    }

    /// <summary>
    /// Multiplayer session over LAN / direct IP (TCP 7777), up to 4 players. The host keeps the roster, accepts and
    /// kicks players, starts the game and relays every game message to the others; each client only talks to the host.
    /// Games on the same network announce themselves by UDP broadcast (7778), so they show up in the join list.
    /// Game messages ("G") carry the sender id: Send(type, payload) -> GameMessage(fromId, type, payload) everywhere else.
    /// Lives on its own DontDestroyOnLoad object.
    /// </summary>
    public class NetSession : MonoBehaviour
    {
        public const int DefaultPort = 7777, DiscoveryPort = 7778, MaxPlayers = 4;
        public const string Version = "COLISEO-MP1";
        public const string PhaseLobby = "lobby", PhaseLudus = "ludus";

        public static NetSession Instance { get; private set; }

        public NetRole Role { get; private set; }
        public bool Active => Role != NetRole.None;
        public bool IsHost => Role == NetRole.Host;
        /// <summary>Client still waiting for the host's welcome.</summary>
        public bool Connecting { get; private set; }
        public string ConnectTarget { get; private set; }
        public int LocalId { get; private set; }
        public string Phase { get; private set; } = PhaseLobby;
        /// <summary>Connected and the host has started (everybody is in the shared ludus).</summary>
        public bool InGame => Active && LocalId > 0 && Phase != PhaseLobby;
        public readonly List<NetPlayer> Players = new List<NetPlayer>();
        public int Port { get; private set; } = DefaultPort;

        public event Action RosterChanged;
        /// <summary>fromId, type, payload.</summary>
        public event Action<int, string, string> GameMessage;
        /// <summary>Something to tell the player (kicked, host left, could not connect...).</summary>
        public event Action<string> Notice;

        TcpListener listener;
        readonly ConcurrentQueue<TcpClient> accepted = new ConcurrentQueue<TcpClient>();
        readonly List<NetConn> conns = new List<NetConn>();
        NetConn hostConn;
        int nextId = 2;
        volatile bool stopping;
        float broadcastTimer;
        UdpClient broadcaster;

        // Client connect runs on a thread so the menu never freezes.
        TcpClient connecting;
        volatile int connectResult; // 0 waiting, 1 ok, -1 failed

        public static NetSession Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("NetSession");
            DontDestroyOnLoad(go);
            return go.AddComponent<NetSession>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            BuildToast();
        }

        void OnDestroy()
        {
            if (Instance == this) { Leave(true); Instance = null; }
            StopBrowsing();
        }

        void OnApplicationQuit() { Leave(true); StopBrowsing(); }

        public NetPlayer Get(int id) => Players.Find(p => p.Id == id);
        public NetPlayer Local => Get(LocalId);
        /// <summary>Order in the roster (0 = host): spawn slot and colour order.</summary>
        public int SlotOf(int id) { int i = Players.FindIndex(p => p.Id == id); return i < 0 ? 0 : i; }

        static NetGladiator LocalGladiator()
        {
            var gm = GameManager.Ensure();
            return NetGladiator.FromProfile(gm.Profile);
        }

        // ------------------------------------------------------------------ host
        public bool Host(int port = DefaultPort)
        {
            if (Active) Leave();
            try
            {
                listener = new TcpListener(IPAddress.Any, port);
                listener.Start();
            }
            catch (Exception e)
            {
                listener = null;
                Say("NO SE PUDO CREAR LA PARTIDA: el puerto " + port + " esta ocupado");
                Debug.LogWarning("[NET] host failed: " + e.Message);
                return false;
            }
            stopping = false;
            Port = port;
            Role = NetRole.Host;
            LocalId = 1;
            nextId = 2;
            Phase = PhaseLobby;
            Players.Clear();
            Players.Add(new NetPlayer { Id = 1, Host = true, Address = "", G = LocalGladiator() });
            var l = listener;
            new Thread(() =>
            {
                while (!stopping)
                {
                    try { accepted.Enqueue(l.AcceptTcpClient()); }
                    catch { if (stopping) break; Thread.Sleep(50); }
                }
            }) { IsBackground = true, Name = "NetAccept" }.Start();
            try { broadcaster = new UdpClient { EnableBroadcast = true }; } catch { broadcaster = null; }
            Debug.Log("[NET] hosting on port " + port + "  ips=" + string.Join(",", LocalAddresses()));
            RosterChanged?.Invoke();
            return true;
        }

        public void StartGame()
        {
            if (!IsHost) return;
            Phase = PhaseLudus;
            BroadcastRoster();
            Broadcast("START\n" + Phase);
            Debug.Log("[NET] start " + Phase + " players=" + Players.Count);
            LoadPhase();
        }

        public void Kick(int id)
        {
            if (!IsHost || id == LocalId) return;
            var c = conns.Find(x => x.Id == id);
            var p = Get(id);
            if (c != null) { c.Send("KICK\nEl anfitrion te expulso de la sala"); c.Close(); conns.Remove(c); }
            if (p != null)
            {
                Players.Remove(p);
                Say(p.G.Name.ToUpperInvariant() + " FUE EXPULSADO");
                Debug.Log("[NET] kicked " + id + " " + p.G.Name);
            }
            BroadcastRoster();
        }

        void HostUpdate()
        {
            while (accepted.TryDequeue(out var tcp))
            {
                try { conns.Add(new NetConn(tcp) { Since = Time.unscaledTime }); }
                catch { try { tcp.Close(); } catch { } }
            }

            for (int i = conns.Count - 1; i >= 0; i--)
            {
                var c = conns[i];
                while (c.Inbox.TryDequeue(out string msg)) HostHandle(c, msg);
                bool stale = !c.Welcomed && Time.unscaledTime - c.Since > 8f;
                if (c.Closed || stale)
                {
                    c.Close();
                    conns.RemoveAt(i);
                    var p = Get(c.Id);
                    if (c.Welcomed && p != null)
                    {
                        Players.Remove(p);
                        Say(p.G.Name.ToUpperInvariant() + " SALIO DE LA PARTIDA");
                        Debug.Log("[NET] player left " + c.Id);
                        BroadcastRoster();
                    }
                }
            }

            broadcastTimer -= Time.unscaledDeltaTime;
            if (broadcastTimer <= 0f && broadcaster != null)
            {
                broadcastTimer = 1f;
                try
                {
                    string name = Players.Count > 0 ? Players[0].G.Name : "Partida";
                    byte[] b = Encoding.UTF8.GetBytes("COLISEO|" + Version + "|" + name.Replace("|", " ") + "|" + Port + "|" + Players.Count + "|" + Phase);
                    broadcaster.Send(b, b.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
                }
                catch { }
            }
        }

        void HostHandle(NetConn c, string msg)
        {
            Split(msg, out string type, out string body);
            if (!c.Welcomed)
            {
                if (type != "HELLO") return;
                Split(body, out string version, out string json);
                if (version != Version) { Reject(c, "version distinta del juego (" + version + ")"); return; }
                if (Players.Count >= MaxPlayers) { Reject(c, "la sala esta llena (" + MaxPlayers + " jugadores)"); return; }
                NetGladiator g = null;
                try { g = JsonUtility.FromJson<NetGladiator>(json); } catch { }
                if (g == null) { Reject(c, "datos del gladiador invalidos"); return; }
                c.Id = nextId++;
                c.Welcomed = true;
                Players.Add(new NetPlayer { Id = c.Id, Address = c.Address, G = g });
                c.Send("WELCOME\n" + c.Id);
                BroadcastRoster();
                if (Phase != PhaseLobby) c.Send("START\n" + Phase);
                Say(g.Name.ToUpperInvariant() + " SE UNIO A LA PARTIDA");
                Debug.Log("[NET] player joined " + c.Id + " " + g.Name + " from " + c.Address);
                return;
            }
            switch (type)
            {
                case "PROFILE":
                    NetGladiator g = null;
                    try { g = JsonUtility.FromJson<NetGladiator>(body); } catch { }
                    var p = Get(c.Id);
                    if (g != null && p != null) { p.G = g; BroadcastRoster(); }
                    break;
                case "G":
                    Split(body, out string gt, out string gp);
                    GameMessage?.Invoke(c.Id, gt, gp);
                    string relay = "G\n" + c.Id + "\n" + gt + "\n" + gp;
                    foreach (var o in conns) if (o != c && o.Welcomed) o.Send(relay);
                    break;
                case "BYE":
                    c.Close();
                    break;
            }
        }

        void Reject(NetConn c, string reason)
        {
            c.Send("REJECT\n" + reason);
            c.Close();
            Debug.Log("[NET] rejected " + c.Address + ": " + reason);
        }

        void Broadcast(string msg)
        {
            foreach (var c in conns) if (c.Welcomed) c.Send(msg);
        }

        void BroadcastRoster()
        {
            var r = new RosterMsg { Phase = Phase, Players = Players };
            Broadcast("ROSTER\n" + JsonUtility.ToJson(r));
            RosterChanged?.Invoke();
        }

        // ------------------------------------------------------------------ client
        public void Join(string address, int port = DefaultPort)
        {
            if (Active) Leave();
            address = (address ?? "").Trim();
            int colon = address.LastIndexOf(':');
            if (colon > 0 && int.TryParse(address.Substring(colon + 1), out int p2)) { port = p2; address = address.Substring(0, colon); }
            Role = NetRole.Client;
            Connecting = true;
            ConnectTarget = address;
            LocalId = 0;
            Phase = PhaseLobby;
            Players.Clear();
            connectResult = 0;
            var tcp = new TcpClient();
            connecting = tcp;
            string a = address; int pt = port;
            new Thread(() =>
            {
                try
                {
                    var ar = tcp.BeginConnect(a, pt, null, null);
                    if (ar.AsyncWaitHandle.WaitOne(5000) && tcp.Connected) { tcp.EndConnect(ar); connectResult = 1; }
                    else connectResult = -1;
                }
                catch { connectResult = -1; }
            }) { IsBackground = true, Name = "NetConnect" }.Start();
            Debug.Log("[NET] connecting to " + address + ":" + port);
            RosterChanged?.Invoke();
        }

        void ClientUpdate()
        {
            if (connecting != null)
            {
                if (connectResult == 0) return;
                var tcp = connecting;
                connecting = null;
                if (connectResult < 0)
                {
                    try { tcp.Close(); } catch { }
                    Fail("NO SE PUDO CONECTAR CON " + ConnectTarget + ": revisa la IP y que el anfitrion haya creado la partida");
                    return;
                }
                hostConn = new NetConn(tcp);
                hostConn.Send("HELLO\n" + Version + "\n" + JsonUtility.ToJson(LocalGladiator()));
            }
            if (hostConn == null) return;
            while (hostConn != null && hostConn.Inbox.TryDequeue(out string msg)) ClientHandle(msg);
            if (hostConn != null && hostConn.Closed)
                Fail(LocalId > 0 ? "SE PERDIO LA CONEXION CON EL ANFITRION" : "EL ANFITRION NO RESPONDE");
        }

        void ClientHandle(string msg)
        {
            Split(msg, out string type, out string body);
            switch (type)
            {
                case "WELCOME":
                    int.TryParse(body, out int id);
                    LocalId = id;
                    Connecting = false;
                    Debug.Log("[NET] welcome id=" + id);
                    RosterChanged?.Invoke();
                    break;
                case "REJECT":
                    Fail("NO PUDISTE UNIRTE: " + body);
                    break;
                case "KICK":
                    Fail(string.IsNullOrEmpty(body) ? "TE EXPULSARON DE LA SALA" : body.ToUpperInvariant());
                    break;
                case "CLOSE":
                    Fail("EL ANFITRION CERRO LA PARTIDA");
                    break;
                case "ROSTER":
                    RosterMsg r = null;
                    try { r = JsonUtility.FromJson<RosterMsg>(body); } catch { }
                    if (r == null) break;
                    Players.Clear();
                    if (r.Players != null) Players.AddRange(r.Players);
                    Phase = string.IsNullOrEmpty(r.Phase) ? PhaseLobby : r.Phase;
                    RosterChanged?.Invoke();
                    break;
                case "START":
                    Phase = body;
                    Debug.Log("[NET] start " + body);
                    LoadPhase();
                    break;
                case "G":
                    Split(body, out string from, out string rest);
                    Split(rest, out string gt, out string gp);
                    int.TryParse(from, out int fid);
                    GameMessage?.Invoke(fid, gt, gp);
                    break;
            }
        }

        void Fail(string reason)
        {
            Debug.Log("[NET] session ended: " + reason);
            Leave(true);
            Say(reason);
        }

        // ------------------------------------------------------------------ common
        /// <summary>Sends a game message to everybody else (through the host).</summary>
        public void Send(string type, string payload)
        {
            if (!Active || LocalId <= 0) return;
            if (IsHost) Broadcast("G\n" + LocalId + "\n" + type + "\n" + payload);
            else hostConn?.Send("G\n" + type + "\n" + payload);
        }

        /// <summary>The local gladiator changed (mercator): tell the others.</summary>
        public void UpdateProfile()
        {
            if (!Active || LocalId <= 0) return;
            var g = LocalGladiator();
            if (IsHost)
            {
                var me = Local;
                if (me != null) me.G = g;
                BroadcastRoster();
            }
            else hostConn?.Send("PROFILE\n" + JsonUtility.ToJson(g));
        }

        /// <summary>Leaves the game (a client) or closes it for everybody (the host). Single player carries on.</summary>
        public void Leave(bool silent = false)
        {
            if (!Active) return;
            if (IsHost)
            {
                Broadcast("CLOSE\n");
                stopping = true;
                try { listener?.Stop(); } catch { }
                listener = null;
                foreach (var c in conns) c.Close();
                conns.Clear();
                try { broadcaster?.Close(); } catch { }
                broadcaster = null;
            }
            else
            {
                hostConn?.Send("BYE\n");
                hostConn?.Close();
                hostConn = null;
                if (connecting != null) { try { connecting.Close(); } catch { } connecting = null; }
            }
            Role = NetRole.None;
            Connecting = false;
            LocalId = 0;
            Phase = PhaseLobby;
            Players.Clear();
            if (!silent) Say("SALISTE DE LA PARTIDA MULTIJUGADOR");
            RosterChanged?.Invoke();
        }

        void LoadPhase()
        {
            if (PauseMenu.Instance != null && PauseMenu.Instance.IsOpen) PauseMenu.Instance.Close();
            if (ChoiceMenu.Instance != null) ChoiceMenu.Instance.Close();
            Time.timeScale = 1f;
            if (Phase == PhaseLudus) SceneManager.LoadScene("Training");
        }

        void Update()
        {
            if (Role == NetRole.Host) HostUpdate();
            else if (Role == NetRole.Client) ClientUpdate();
            UpdateToast();
        }

        static void Split(string s, out string head, out string rest)
        {
            int i = s.IndexOf('\n');
            if (i < 0) { head = s; rest = ""; return; }
            head = s.Substring(0, i);
            rest = s.Substring(i + 1);
        }

        public static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        public static float PF(string s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;

        /// <summary>This machine's IPv4 addresses on the local network (what the others type to join).</summary>
        public static List<string> LocalAddresses()
        {
            var list = new List<string>();
            try
            {
                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                        if (ua.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ua.Address))
                        {
                            string s = ua.Address.ToString();
                            if (!s.StartsWith("169.254.") && !list.Contains(s)) list.Add(s);
                        }
                }
            }
            catch { }
            if (list.Count == 0)
            {
                try
                {
                    foreach (var a in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                        if (a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)) list.Add(a.ToString());
                }
                catch { }
            }
            return list;
        }

        // ------------------------------------------------------------------ LAN discovery (join list)
        public class FoundGame
        {
            public string Address, HostName, Phase;
            public int Port, Count;
            public float Seen;
        }

        readonly Dictionary<string, FoundGame> found = new Dictionary<string, FoundGame>();
        readonly ConcurrentQueue<KeyValuePair<string, string>> discovered = new ConcurrentQueue<KeyValuePair<string, string>>();
        UdpClient browser;
        volatile bool browsing;

        public bool Browsing => browsing;

        public void StartBrowsing()
        {
            if (browsing) return;
            try
            {
                var u = new UdpClient();
                u.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                u.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
                u.Client.ReceiveTimeout = 500;
                browser = u;
            }
            catch (Exception e) { Debug.LogWarning("[NET] discovery unavailable: " + e.Message); return; }
            browsing = true;
            var b = browser;
            new Thread(() =>
            {
                while (browsing)
                {
                    try
                    {
                        var ep = new IPEndPoint(IPAddress.Any, 0);
                        byte[] data = b.Receive(ref ep);
                        discovered.Enqueue(new KeyValuePair<string, string>(ep.Address.ToString(), Encoding.UTF8.GetString(data)));
                    }
                    catch { }
                }
            }) { IsBackground = true, Name = "NetDiscovery" }.Start();
        }

        public void StopBrowsing()
        {
            browsing = false;
            try { browser?.Close(); } catch { }
            browser = null;
        }

        /// <summary>Games announced on the network in the last 3 seconds (not our own).</summary>
        public List<FoundGame> FoundGames()
        {
            while (discovered.TryDequeue(out var kv))
            {
                string[] f = kv.Value.Split('|');
                if (f.Length < 6 || f[0] != "COLISEO" || f[1] != Version) continue;
                int.TryParse(f[3], out int port);
                int.TryParse(f[4], out int count);
                string key = kv.Key + ":" + port;
                found[key] = new FoundGame { Address = kv.Key, HostName = f[2], Port = port, Count = count, Phase = f[5], Seen = Time.unscaledTime };
            }
            var list = new List<FoundGame>();
            var mine = IsHost ? LocalAddresses() : null;
            foreach (var g in found.Values)
            {
                if (Time.unscaledTime - g.Seen > 3f) continue;
                if (mine != null && mine.Contains(g.Address) && g.Port == Port) continue;
                list.Add(g);
            }
            list.Sort((a, b) => string.CompareOrdinal(a.Address, b.Address));
            return list;
        }

        // ------------------------------------------------------------------ toast (notices on any scene)
        Text toastText;
        GameObject toastRoot;
        float toastTimer;
        public string LastNotice { get; private set; }

        void Say(string s)
        {
            LastNotice = s;
            Notice?.Invoke(s);
            if (toastText == null) return;
            toastText.text = s;
            toastTimer = 4f;
            toastRoot.SetActive(true);
        }

        void BuildToast()
        {
            var canvasGo = new GameObject("NetToast", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 600;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 896);
            scaler.matchWidthOrHeight = 1f;
            var bg = PS2UI.Anchored(PS2UI.Rect(canvasGo.transform, "Bg", new Color(0.06f, 0.04f, 0.03f, 0.85f)), new Vector2(0.5f, 1f), new Vector2(0, -60), new Vector2(900, 40));
            toastText = PS2UI.Text(bg, "", 18, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(880, 30), PS2UI.Gold);
            toastRoot = canvasGo;
            toastRoot.SetActive(false);
        }

        void UpdateToast()
        {
            if (toastTimer <= 0f) return;
            toastTimer -= Time.unscaledDeltaTime;
            if (toastTimer <= 0f) toastRoot.SetActive(false);
        }
    }
}
