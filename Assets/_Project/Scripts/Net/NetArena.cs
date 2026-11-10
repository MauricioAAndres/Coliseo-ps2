using System.Collections.Generic;
using Gladiator.Arena;
using Gladiator.Combat;
using Gladiator.UI;
using UnityEngine;

namespace Gladiator.Net
{
    /// <summary>
    /// Online arena.
    /// PvP (MP2): every player for himself, or teams by colour. Each game is the referee of its own gladiator: when you
    /// hit someone's copy, your game sends the blow ("HT") to that player, whose game applies it and tells everybody
    /// their health ("HP") and, if it comes to that, their death ("DIE").
    /// Co-op (MP3): all the players on one side against AI rivals (duel, waves, the champion, double). Only the host's
    /// game runs the rivals: it tells the others who they are ("BSP"), where they are and their health ("BST", 10 times a
    /// second), what they do ("BAT" attacks, "BMV" moves, "BEQ" gear) and when they fall ("BDIE"). The other games show
    /// harmless copies of them; a blow a player lands on a copy goes to the host ("BH"), and a blow a rival lands on a
    /// player's copy in the host's game goes to that player ("HT" with the rival's id).
    /// The host decides the end ("AEND") and takes everybody back to the ludus. Nothing here touches the campaign.
    /// </summary>
    public class NetArena : MonoBehaviour
    {
        public static NetArena Instance { get; private set; }
        public const float BackToLudusSeconds = 7f;
        /// <summary>Ids of AI rivals start here (players are 1..).</summary>
        public const int BotIdBase = 1000;

        NetSession session;
        NetAvatars avatars;
        ArenaBootstrap boot;
        Fighter local;
        GameHUD hud;
        readonly HashSet<int> dead = new HashSet<int>();
        float lastHp = -1f, hpTimer, endAt = -1f;
        bool ended;
        bool coop;

        // co-op, host: the real rivals; others: the copies
        readonly Dictionary<int, Fighter> bots = new Dictionary<int, Fighter>();
        readonly Dictionary<Fighter, int> botIds = new Dictionary<Fighter, int>();
        readonly Dictionary<int, Vector3> botLastPos = new Dictionary<int, Vector3>();
        readonly Dictionary<int, string> botEquip = new Dictionary<int, string>();
        readonly Dictionary<int, RemoteFighter> copies = new Dictionary<int, RemoteFighter>();
        int nextBotId = BotIdBase, wave, waveTotal;
        float stateTimer, equipTimer, requestTimer, nextWaveAt = -1f;
        bool gotBots;

        public bool Ended => ended;
        public bool Coop => coop;
        public string Winner { get; private set; }
        public int HitsSent { get; private set; }
        public int HitsTaken { get; private set; }
        /// <summary>Co-op: blows landed on rivals (sent to the host, or applied by the host) and rival states received.</summary>
        public int BotHitsSent { get; private set; }
        public int BotHitsApplied { get; private set; }
        public int BotStatesReceived { get; private set; }
        public int Wave => wave;
        public bool IsDead(int id) => dead.Contains(id);
        /// <summary>The rivals as this game sees them: the real ones (host) or their copies.</summary>
        public List<Fighter> Rivals
        {
            get
            {
                var l = new List<Fighter>();
                if (session != null && session.IsHost) foreach (var b in bots.Values) { if (b != null) l.Add(b); }
                else foreach (var c in copies.Values) if (c != null) l.Add(c.Fighter);
                return l;
            }
        }

        /// <summary>Team id in the arena: the colour (teams), the player (everyone for himself), or all one side (co-op).</summary>
        public static int TeamOf(NetSession s, NetPlayer p) => s.ArenaCoop ? 100 : s.ArenaTeams ? 100 + p.Color : 100 + p.Id;

        /// <summary>Where each player starts: around the sand facing the centre; co-op side by side on the south half.</summary>
        public static Vector3 SpawnFor(int slot, int count, bool coop = false)
        {
            if (coop) return new Vector3((slot - (count - 1) * 0.5f) * 1.8f, 0.05f, -5.5f);
            float a = (270f + 360f * slot / Mathf.Max(2, count)) * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 7f + Vector3.up * 0.05f;
        }

        public void Init(Fighter localFighter, NetAvatars netAvatars, GameHUD gameHud, ArenaBootstrap bootstrap = null)
        {
            Instance = this;
            session = NetSession.Instance;
            local = localFighter;
            avatars = netAvatars;
            hud = gameHud;
            boot = bootstrap;
            coop = session.ArenaCoop;
            session.GameMessage += OnGame;
            session.RosterChanged += OnRoster;
            Fighter.AnyHitResolved += OnHit;
            Fighter.AnyDied += OnDied;
            Fighter.AnyAttackStarted += OnAttackStarted;
            Fighter.AnyMove += OnMoveAny;
            avatars.RemoteSpawned += _ => RefreshBars();
            string mode = coop ? "CONTRA BOTS: " + NetSession.ModeNames[session.ArenaMode]
                               : session.ArenaTeams ? "POR EQUIPOS" : "TODOS CONTRA TODOS";
            hud?.ShowMessage("ARENA ONLINE  -  " + mode + (session.ArenaUpgrades ? "  -  CON MEJORAS" : "  -  SIN MEJORAS (TODOS IGUALES)"), 3.5f);
            if (coop && session.IsHost && boot != null)
            {
                foreach (var e in boot.Enemies) RegisterBot(e);
                if (session.ArenaMode == NetSession.ModeWaves) { wave = 1; waveTotal = ArenaBootstrap.WaveSizes.Length; }
                foreach (var id in bots.Keys) SendSpawn(id);
                RefreshBars();
                UpdateCoopStatus();
            }
            Debug.Log($"[NETARENA] start mode={session.ArenaMode} teams={session.ArenaTeams} upgrades={session.ArenaUpgrades} me={session.LocalId} team={local.TeamId} hp={local.Health.Max:0} bots={bots.Count}");
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (session != null) { session.GameMessage -= OnGame; session.RosterChanged -= OnRoster; }
            Fighter.AnyHitResolved -= OnHit;
            Fighter.AnyDied -= OnDied;
            Fighter.AnyAttackStarted -= OnAttackStarted;
            Fighter.AnyMove -= OnMoveAny;
        }

        /// <summary>Health bars on the right: the rivals (co-op), or the other players / colours.</summary>
        void RefreshBars()
        {
            if (hud == null || avatars == null) return;
            if (coop) { hud.ResetEnemies(Rivals); return; }
            var rivals = new List<Fighter>();
            foreach (var r in avatars.Remotes) if (r != null && r.Fighter.TeamId != local.TeamId) rivals.Add(r.Fighter);
            hud.ResetEnemies(rivals);
        }

        // ------------------------------------------------------------------ co-op rivals: the host's side
        void RegisterBot(Fighter f)
        {
            if (f == null || botIds.ContainsKey(f)) return;
            int id = nextBotId++;
            bots[id] = f;
            botIds[f] = id;
            botLastPos[id] = f.transform.position;
            botEquip[id] = Equip(f);
        }

        static string Equip(Fighter f) =>
            (f.CurrentWeapon != null ? f.CurrentWeapon.Stats.Id : "") + "|" + (f.OffWeapon != null ? f.OffWeapon.Stats.Id : "") + "|" +
            (f.EquippedShield != null ? f.EquippedShield.Stats.Id : "");

        /// <summary>Tells everybody who rival <paramref name="id"/> is: body, armour, gear, where, health.</summary>
        void SendSpawn(int id)
        {
            if (!bots.TryGetValue(id, out var f) || f == null) return;
            var g = new NetGladiator { Name = f.DisplayName, Armor = new string[8], Sides = new int[8] };
            if (boot != null && boot.BotLooks.TryGetValue(f, out var app)) g.Look = JsonUtility.ToJson(app);
            var arm = f.Armor;
            if (arm != null)
                for (int i = 0; i < 8; i++) { g.Armor[i] = arm.Pieces[i] != null ? arm.Pieces[i].Id : null; g.Sides[i] = (int)arm.Sides[i]; }
            g.Main = f.CurrentWeapon != null ? f.CurrentWeapon.Stats.Id : "";
            g.Off = f.OffWeapon != null ? f.OffWeapon.Stats.Id : "";
            g.Shield = f.EquippedShield != null ? f.EquippedShield.Stats.Id : "";
            Vector3 p = f.transform.position;
            session.Send("BSP", id + "|" + NetSession.F(p.x) + "|" + NetSession.F(p.y) + "|" + NetSession.F(p.z) + "|" +
                                NetSession.F(f.transform.eulerAngles.y) + "|" + NetSession.F(f.Health.Current) + "|" + NetSession.F(f.Health.Max) + "|" +
                                JsonUtility.ToJson(g));
        }

        /// <summary>Host: where every rival is, how it moves and its health, in one message.</summary>
        void SendBotStates()
        {
            if (bots.Count == 0) return;
            var sb = new System.Text.StringBuilder();
            foreach (var kv in bots)
            {
                var f = kv.Value;
                if (f == null) continue;
                Vector3 p = f.transform.position;
                Vector3 v = (p - botLastPos[kv.Key]) / Mathf.Max(0.01f, stateIntervalUsed);
                v.y = 0f;
                if (v.magnitude > 12f) v = Vector3.zero;
                botLastPos[kv.Key] = p;
                int flags = (f.State == FighterState.Blocking ? 1 : 0) | (f.Crouching ? 2 : 0) | (f.MeleeCharging || f.Charging ? 4 : 0);
                if (sb.Length > 0) sb.Append(';');
                sb.Append(kv.Key).Append(',').Append(NetSession.F(p.x)).Append(',').Append(NetSession.F(p.y)).Append(',').Append(NetSession.F(p.z)).Append(',')
                  .Append(NetSession.F(f.transform.eulerAngles.y)).Append(',').Append(NetSession.F(v.x)).Append(',').Append(NetSession.F(v.z)).Append(',')
                  .Append(flags).Append(',').Append(NetSession.F(f.Health.Current)).Append(',').Append(NetSession.F(f.Health.Max));
            }
            session.Send("BST", sb.ToString());
        }

        float stateIntervalUsed = 0.1f;

        void OnAttackStarted(Fighter f, AttackType type)
        {
            if (!coop || session == null || !session.IsHost || !botIds.TryGetValue(f, out int id)) return;
            session.Send("BAT", id + "|" + (int)type + "|" + PosYaw(f));
        }

        void OnMoveAny(Fighter f, string kind)
        {
            if (!coop || session == null || !session.IsHost || !botIds.TryGetValue(f, out int id)) return;
            switch (kind)
            {
                case "jump": case "vault": case "climb": case "dodge": case "dodge_back": case "taunt": case "cheer": case "gesture":
                    session.Send("BMV", id + "|" + kind + "|" + f.TauntKind + "|" + PosYaw(f));
                    break;
            }
        }

        static string PosYaw(Fighter f)
        {
            Vector3 p = f.transform.position;
            return NetSession.F(p.x) + "|" + NetSession.F(p.y) + "|" + NetSession.F(p.z) + "|" + NetSession.F(f.transform.eulerAngles.y);
        }

        // ------------------------------------------------------------------ co-op rivals: the other games' copies
        void SpawnCopy(int id, Vector3 pos, float yaw, float hp, float max, NetGladiator g)
        {
            if (copies.TryGetValue(id, out var have) && have != null) { have.Fighter.Health.SetNet(hp, max); return; }
            var look = g.Appearance();
            var f = CharacterFactory.Create(g.Name, look, pos, Quaternion.Euler(0f, yaw, 0f), 1, g.ArmorSet);
            f.Harmless = true; // the host's game decides what it does and what happens to it
            f.DisarmResistance = 1f;
            f.Health.SetMax(max, true);
            var r = f.gameObject.AddComponent<RemoteFighter>();
            r.Init(id, f, Camera.main, g.Name, new Color(0.25f, 0.3f, 0.5f));
            r.OnEquip(g.Main, g.Off, g.Shield);
            r.HideTag();
            r.OnState(pos, yaw, Vector3.zero, 0);
            copies[id] = r;
            gotBots = true;
            if (hp <= 0f) KillCopy(r);
            else f.Health.SetNet(hp, max);
            RefreshBars();
            UpdateCoopStatus();
            Debug.Log($"[NETARENA] rival copy {id} {g.Name} hp={hp:0}/{max:0} weapon={g.Main}");
        }

        static void KillCopy(RemoteFighter r)
        {
            if (r == null || !r.Fighter.IsAlive) return;
            r.Fighter.Health.SetNet(1f, r.Fighter.Health.Max);
            r.Fighter.Health.TakeDamage(10f);
        }

        void OnBotStates(string payload)
        {
            foreach (var e in payload.Split(';'))
            {
                var a = e.Split(',');
                if (a.Length < 10 || !int.TryParse(a[0], out int id) || !copies.TryGetValue(id, out var r) || r == null) continue;
                BotStatesReceived++;
                if (!r.Fighter.IsAlive) continue;
                r.OnState(new Vector3(NetSession.PF(a[1]), NetSession.PF(a[2]), NetSession.PF(a[3])), NetSession.PF(a[4]),
                          new Vector3(NetSession.PF(a[5]), 0f, NetSession.PF(a[6])), int.TryParse(a[7], out int fl) ? fl : 0);
                float hp = NetSession.PF(a[8]);
                if (hp > 0f) r.Fighter.Health.SetNet(hp, NetSession.PF(a[9]));
            }
        }

        // ------------------------------------------------------------------ blows
        void OnHit(HitResult r)
        {
            if (ended || r.Target == null) return;
            // We hit someone's copy: their game (or, for a rival, the host's) applies it.
            if (r.Attacker == local)
            {
                var rf = r.Target.GetComponent<RemoteFighter>();
                if (rf == null) return;
                Vector3 d = r.Target.transform.position - local.transform.position; d.y = 0f;
                d = d.sqrMagnitude > 0.01f ? d.normalized : local.transform.forward;
                if (rf.PlayerId >= BotIdBase)
                {
                    if (!r.Target.IsAlive) return;
                    session.Send("BH", rf.PlayerId + "|" + NetSession.F(r.Target.LastRawDamage) + "|" + (int)r.Type + "|" + NetSession.F(d.x) + "|" + NetSession.F(d.z));
                    BotHitsSent++;
                    return;
                }
                if (dead.Contains(rf.PlayerId) || (coop && rf.Fighter.TeamId == local.TeamId)) return;
                session.Send("HT", rf.PlayerId + "|" + NetSession.F(r.Target.LastRawDamage) + "|" + (int)r.Type + "|" + NetSession.F(d.x) + "|" + NetSession.F(d.z));
                HitsSent++;
                return;
            }
            // Co-op, host: a rival hit a player's copy here: that player's game applies it.
            if (coop && session.IsHost && r.Attacker != null && botIds.TryGetValue(r.Attacker, out int botId))
            {
                var rf = r.Target.GetComponent<RemoteFighter>();
                if (rf == null || rf.PlayerId >= BotIdBase || dead.Contains(rf.PlayerId)) return;
                Vector3 d = r.Target.transform.position - r.Attacker.transform.position; d.y = 0f;
                d = d.sqrMagnitude > 0.01f ? d.normalized : r.Attacker.transform.forward;
                session.Send("HT", rf.PlayerId + "|" + NetSession.F(r.Target.LastRawDamage) + "|" + (int)r.Type + "|" + NetSession.F(d.x) + "|" + NetSession.F(d.z) + "|" + botId);
            }
        }

        void OnDied(Fighter f)
        {
            if (coop && session != null && session.IsHost && botIds.TryGetValue(f, out int bid))
            {
                session.Send("BDIE", bid.ToString());
                Debug.Log("[NETARENA] rival " + bid + " " + f.DisplayName + " fell");
                UpdateCoopStatus();
                CheckEnd();
                return;
            }
            if (f != local || dead.Contains(session.LocalId)) return;
            dead.Add(session.LocalId);
            int killer = local.LastAttacker != null && local.LastAttacker.GetComponent<RemoteFighter>() != null
                ? local.LastAttacker.GetComponent<RemoteFighter>().PlayerId : 0;
            session.Send("DIE", killer.ToString());
            SendHp(true);
            string by = killer > 0 && killer < BotIdBase && session.Get(killer) != null ? "  -  TE VENCIO " + session.Get(killer).G.Name.ToUpperInvariant()
                      : local.LastAttacker != null && coop ? "  -  TE VENCIO " + local.LastAttacker.DisplayName.ToUpperInvariant() : "";
            hud?.ShowMessage("HAS CAIDO" + by + (coop ? "  -  TUS COMPAÑEROS SIGUEN" : ""), 3f);
            Debug.Log("[NETARENA] I died, killer=" + killer);
            CheckEnd();
        }

        void OnGame(int from, string type, string payload)
        {
            string[] a = payload.Split('|');
            switch (type)
            {
                case "HT":
                {
                    if (a.Length < 5 || !int.TryParse(a[0], out int target) || target != session.LocalId || ended || !local.IsAlive) return;
                    Fighter attacker = null;
                    if (a.Length > 5 && int.TryParse(a[5], out int bid) && copies.TryGetValue(bid, out var bc) && bc != null) attacker = bc.Fighter;
                    else { var rf = avatars != null ? avatars.Remote(from) : null; attacker = rf != null ? rf.Fighter : null; }
                    Vector3 dir = new Vector3(NetSession.PF(a[3]), 0f, NetSession.PF(a[4]));
                    var type2 = (AttackType)Mathf.Clamp(int.TryParse(a[2], out int t) ? t : 0, 0, 2);
                    local.ReceiveHit(attacker, NetSession.PF(a[1]), type2, dir.sqrMagnitude > 0.01f ? dir.normalized : -local.transform.forward);
                    HitsTaken++;
                    SendHp(true);
                    break;
                }
                case "HP":
                {
                    if (a.Length < 2) return;
                    var rf = avatars != null ? avatars.Remote(from) : null;
                    if (rf != null) rf.Fighter.Health.SetNet(NetSession.PF(a[0]), NetSession.PF(a[1]));
                    break;
                }
                case "DIE":
                {
                    if (dead.Contains(from)) return;
                    dead.Add(from);
                    var rf = avatars != null ? avatars.Remote(from) : null;
                    if (rf != null && rf.Fighter.IsAlive) { rf.Fighter.Health.SetNet(1f, rf.Fighter.Health.Max); rf.Fighter.Health.TakeDamage(10f); }
                    var p = session.Get(from);
                    int.TryParse(payload, out int killer);
                    string who = p != null ? p.G.Name.ToUpperInvariant() : "UN GLADIADOR";
                    string by = killer == session.LocalId ? "  -  ¡LO HAS VENCIDO TU!" : killer > 0 && session.Get(killer) != null ? "  -  LO VENCIO " + session.Get(killer).G.Name.ToUpperInvariant() : "";
                    hud?.ShowMessage("¡" + who + " CAE!" + by, 2.5f);
                    Debug.Log("[NETARENA] player " + from + " died, killer=" + killer);
                    CheckEnd();
                    break;
                }
                case "AEND":
                    ShowEnd(payload);
                    break;

                // ---- co-op rivals
                case "BREQ": // a game that just loaded the arena asks for the rivals
                    if (coop && session.IsHost)
                    {
                        foreach (var id in bots.Keys) SendSpawn(id);
                        if (wave > 0) session.Send("BWV", wave + "|" + waveTotal);
                    }
                    break;
                case "BSP":
                {
                    if (session.IsHost) return;
                    var b = payload.Split(new[] { '|' }, 8);
                    if (b.Length < 8 || !int.TryParse(b[0], out int id)) return;
                    NetGladiator g = null;
                    try { g = JsonUtility.FromJson<NetGladiator>(b[7]); } catch { }
                    if (g == null) return;
                    SpawnCopy(id, new Vector3(NetSession.PF(b[1]), NetSession.PF(b[2]), NetSession.PF(b[3])), NetSession.PF(b[4]),
                              NetSession.PF(b[5]), NetSession.PF(b[6]), g);
                    break;
                }
                case "BST":
                    if (!session.IsHost) OnBotStates(payload);
                    break;
                case "BAT":
                {
                    if (session.IsHost || a.Length < 6 || !int.TryParse(a[0], out int id) || !copies.TryGetValue(id, out var r) || r == null || !r.Fighter.IsAlive) return;
                    r.OnAttack((AttackType)Mathf.Clamp(int.TryParse(a[1], out int t) ? t : 0, 0, 2),
                               new Vector3(NetSession.PF(a[2]), NetSession.PF(a[3]), NetSession.PF(a[4])), NetSession.PF(a[5]));
                    break;
                }
                case "BMV":
                {
                    if (session.IsHost || a.Length < 7 || !int.TryParse(a[0], out int id) || !copies.TryGetValue(id, out var r) || r == null || !r.Fighter.IsAlive) return;
                    r.OnMove(a[1], int.TryParse(a[2], out int k) ? k : 0,
                             new Vector3(NetSession.PF(a[3]), NetSession.PF(a[4]), NetSession.PF(a[5])), NetSession.PF(a[6]));
                    break;
                }
                case "BEQ":
                {
                    if (session.IsHost || a.Length < 4 || !int.TryParse(a[0], out int id) || !copies.TryGetValue(id, out var r) || r == null) return;
                    r.OnEquip(a[1], a[2], a[3]);
                    break;
                }
                case "BDIE":
                {
                    if (session.IsHost || !int.TryParse(payload, out int id) || !copies.TryGetValue(id, out var r)) return;
                    KillCopy(r);
                    UpdateCoopStatus();
                    break;
                }
                case "BWV":
                {
                    if (session.IsHost || a.Length < 2) return;
                    int.TryParse(a[0], out int w); int.TryParse(a[1], out waveTotal);
                    if (w > wave && w > 1) hud?.ShowMessage($"OLEADA {w}  -  ¡MAS RIVALES!", 2.5f);
                    wave = w;
                    UpdateCoopStatus();
                    break;
                }
                case "BH": // host: a player hit the copy of a rival in their game
                {
                    if (!session.IsHost || a.Length < 5 || !int.TryParse(a[0], out int id) || !bots.TryGetValue(id, out var bot) || bot == null || !bot.IsAlive || ended) return;
                    var rf = avatars != null ? avatars.Remote(from) : null;
                    Vector3 dir = new Vector3(NetSession.PF(a[3]), 0f, NetSession.PF(a[4]));
                    bot.ReceiveHit(rf != null ? rf.Fighter : null, NetSession.PF(a[1]), (AttackType)Mathf.Clamp(int.TryParse(a[2], out int t) ? t : 0, 0, 2),
                                   dir.sqrMagnitude > 0.01f ? dir.normalized : -bot.transform.forward);
                    BotHitsApplied++;
                    break;
                }
            }
        }

        void OnRoster() { if (!ended) CheckEnd(); } // somebody left: they are out

        void SendHp(bool force)
        {
            float hp = local.Health.Current;
            if (!force && Mathf.Approximately(hp, lastHp)) return;
            lastHp = hp;
            session.Send("HP", NetSession.F(hp) + "|" + NetSession.F(local.Health.Max));
        }

        void UpdateCoopStatus()
        {
            if (!coop || hud == null) return;
            int alive = 0, total = 0;
            foreach (var f in Rivals) { total++; if (f.IsAlive) alive++; }
            hud.SetStatus((wave > 0 ? $"OLEADA {wave}/{waveTotal}    " : "") + $"RIVALES EN PIE {alive}/{total}");
        }

        // ------------------------------------------------------------------ the end (the host decides)
        void CheckEnd()
        {
            if (ended || session == null || !session.IsHost) return;
            var standing = new List<NetPlayer>();
            foreach (var p in session.Players) if (!dead.Contains(p.Id)) standing.Add(p);
            string text;
            if (coop)
            {
                if (standing.Count == 0) text = "DERROTA  -  LOS RIVALES GANAN";
                else
                {
                    foreach (var b in bots.Values) if (b != null && b.IsAlive) return;
                    if (session.ArenaMode == NetSession.ModeWaves && wave < waveTotal)
                    {
                        if (nextWaveAt < 0f)
                        {
                            nextWaveAt = Time.time + 3.5f;
                            string msg = $"¡OLEADA {wave} SUPERADA!";
                            hud?.ShowMessage(msg, 3f);
                        }
                        return;
                    }
                    text = "VICTORIA  -  " + (session.ArenaMode == NetSession.ModeChampion ? "¡EL CAMPEON HA CAIDO!" :
                                              session.ArenaMode == NetSession.ModeWaves ? "¡TODAS LAS OLEADAS SUPERADAS!" : "¡TODOS LOS RIVALES HAN CAIDO!");
                }
            }
            else
            {
                var sides = new HashSet<int>();
                foreach (var p in standing) sides.Add(TeamOf(session, p));
                if (sides.Count > 1) return;
                if (standing.Count == 0) text = "EMPATE";
                else if (session.ArenaTeams) text = "GANA EL EQUIPO " + NetSession.ColorNames[standing[0].Color];
                else text = "GANA " + standing[0].G.Name.ToUpperInvariant();
            }
            session.Send("AEND", text);
            ShowEnd(text);
        }

        void ShowEnd(string text)
        {
            if (ended) return;
            ended = true;
            Winner = text;
            endAt = Time.unscaledTime + BackToLudusSeconds;
            Debug.Log("[NETARENA] end: " + text);
            bool iWon = coop ? text.StartsWith("VICTORIA")
                             : text.Contains(session.ArenaTeams && session.Local != null ? NetSession.ColorNames[session.Local.Color]
                                                                                        : (session.Local != null ? session.Local.G.Name.ToUpperInvariant() : "#"));
            hud?.ShowBanner(text, session.IsHost ? "VOLVEIS AL LUDUS EN UNOS SEGUNDOS" : "EL ANFITRION OS LLEVARA AL LUDUS",
                            iWon ? new Color(1f, 0.8f, 0.25f) : new Color(0.85f, 0.15f, 0.1f));
        }

        /// <summary>Autotest: the host brings every rival down at once.</summary>
        public void DebugKillAllBots()
        {
            if (!session.IsHost) return;
            foreach (var b in new List<Fighter>(bots.Values)) if (b != null && b.IsAlive) b.Health.TakeDamage(99999f);
        }

        void Update()
        {
            if (session == null || !session.Active) return;
            hpTimer -= Time.deltaTime;
            if (hpTimer <= 0f) { hpTimer = 1f; SendHp(true); } // also tells late joiners
            else SendHp(false);

            if (coop)
            {
                if (session.IsHost)
                {
                    stateTimer -= Time.deltaTime;
                    if (stateTimer <= 0f) { stateIntervalUsed = Mathf.Max(0.02f, 0.1f - stateTimer); stateTimer = 0.1f; SendBotStates(); }
                    equipTimer -= Time.deltaTime;
                    if (equipTimer <= 0f)
                    {
                        equipTimer = 0.4f;
                        foreach (var kv in bots)
                        {
                            if (kv.Value == null) continue;
                            string eq = Equip(kv.Value);
                            if (botEquip[kv.Key] != eq) { botEquip[kv.Key] = eq; session.Send("BEQ", kv.Key + "|" + eq); }
                        }
                    }
                    if (!ended && nextWaveAt > 0f && Time.time >= nextWaveAt && boot != null)
                    {
                        nextWaveAt = -1f;
                        wave++;
                        var fresh = boot.SpawnWave(wave);
                        foreach (var f in fresh) { RegisterBot(f); SendSpawn(botIds[f]); }
                        session.Send("BWV", wave + "|" + waveTotal);
                        hud?.ShowMessage($"OLEADA {wave}  -  {fresh.Count} RIVALES", 2.5f);
                        RefreshBars();
                        UpdateCoopStatus();
                        Debug.Log($"[NETARENA] wave {wave}: {fresh.Count} rivals");
                    }
                }
                else if (!gotBots)
                {
                    requestTimer -= Time.unscaledDeltaTime;
                    if (requestTimer <= 0f) { requestTimer = 1f; session.Send("BREQ", ""); }
                }
            }

            if (ended && session.IsHost && endAt > 0f && Time.unscaledTime >= endAt) { endAt = -1f; session.StartGame(); }
        }
    }
}
