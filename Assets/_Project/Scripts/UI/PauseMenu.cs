using System;
using System.Collections.Generic;
using Gladiator.Audio;
using Gladiator.Core;
using Gladiator.Net;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Gladiator.UI
{
    /// <summary>
    /// Esc (or the gamepad View/Select button) pauses the game anywhere and opens:
    /// CONTINUAR, NUEVA PARTIDA, SONIDO, GRAFICOS, SALIR DEL JUEGO.
    /// While open, time is stopped and GameInput is muted so nothing else reacts to the keys.
    /// Keyboard (W/S, A/D, Enter, Esc), mouse (hover, click; click left/right half of a value to change it) or gamepad.
    /// Lives on its own DontDestroyOnLoad object, created before the first scene.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class PauseMenu : MonoBehaviour
    {
        public static PauseMenu Instance { get; private set; }
        public bool IsOpen { get; private set; }

        public enum Page { Main, Sound, Graphics, Camera, Controls, ArenaOnline, ConfirmNew, ConfirmQuit, Multi, Lobby, ConfirmKick }
        public Page Current { get; private set; }

        class Row
        {
            public string Label;
            public Func<string> Value;      // null = plain button
            public Action<int> Change;      // -1 / +1
            public Action Activate;
            public RectTransform Rect;
            public Text LabelText, ValueText;
            public bool Wide;               // key binding row: label left, both columns on the right
        }

        readonly List<Row> rows = new List<Row>();
        int selected;
        RectTransform panel, list;
        Text title, note, hint;
        GameObject root;
        float prevTimeScale = 1f;
        float navRepeat;

        // ---- multiplayer pages
        string ipText;
        public bool EditingIp { get; private set; }
        int kickId;
        string pageSig;
        float sigTimer;

        static readonly Color RowBg = new Color(0.16f, 0.11f, 0.07f, 0.85f);
        static readonly Color RowSel = new Color(0.55f, 0.36f, 0.12f, 0.95f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Instance != null) return;
            var go = new GameObject("PauseMenu");
            DontDestroyOnLoad(go);
            go.AddComponent<PauseMenu>();
        }

        void Awake()
        {
            Instance = this;
            Build();
            root.SetActive(false);
        }

        // ------------------------------------------------------------------ UI
        void Build()
        {
            var canvasGo = new GameObject("PauseCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 896);
            scaler.matchWidthOrHeight = 1f;
            root = canvasGo;

            var dim = PS2UI.Rect(canvasGo.transform, "Dim", new Color(0f, 0f, 0f, 0.6f));
            PS2UI.Stretch(dim);
            panel = PS2UI.Anchored(PS2UI.Rect(canvasGo.transform, "Panel", PS2UI.PanelBg), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560, 520));
            var border = PS2UI.Rect(panel, "Border", PS2UI.Gold);
            PS2UI.Anchored(border, new Vector2(0.5f, 1f), new Vector2(0, -78), new Vector2(480, 2));
            title = PS2UI.Text(panel, "PAUSA", 40, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0, -40), new Vector2(500, 50), PS2UI.Gold);
            list = PS2UI.Anchored(new GameObject("List", typeof(RectTransform)).GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0, -100), new Vector2(480, 380));
            list.SetParent(panel, false);
            note = PS2UI.Text(panel, "", 14, TextAnchor.UpperCenter, new Vector2(0.5f, 0f), new Vector2(0, 62), new Vector2(500, 40), PS2UI.Parchment);
            note.horizontalOverflow = HorizontalWrapMode.Wrap; // long help texts (multiplayer) wrap and the panel grows
            note.verticalOverflow = VerticalWrapMode.Overflow;
            hint = PS2UI.Text(panel, "", 13, TextAnchor.MiddleCenter, new Vector2(0.5f, 0f), new Vector2(0, 22), new Vector2(520, 18), new Color(0.75f, 0.7f, 0.6f));
        }

        void Show(Page page)
        {
            Current = page;
            foreach (var r in rows) if (r.Rect != null) Destroy(r.Rect.gameObject);
            rows.Clear();
            note.text = "";
            switch (page)
            {
                case Page.Main:
                    title.text = "PAUSA";
                    Add("CONTINUAR", Close);
                    {
                        var ns = NetSession.Instance;
                        Add(ns != null && ns.Active ? "MULTIJUGADOR  (SALA " + Mathf.Max(1, ns.Players.Count) + "/" + NetSession.MaxPlayers + ")" : "MULTIJUGADOR",
                            () => Show(ns != null && ns.Active ? Page.Lobby : Page.Multi));
                    }
                    Add("NUEVA PARTIDA", () => Show(Page.ConfirmNew));
                    Add("SONIDO", () => Show(Page.Sound));
                    Add("GRAFICOS", () => Show(Page.Graphics));
                    Add("CAMARA", () => Show(Page.Camera));
                    Add("CONTROLES", () => Show(Page.Controls));
                    Add("SALIR DEL JUEGO", () => Show(Page.ConfirmQuit));
                    break;
                case Page.Sound:
                    title.text = "SONIDO";
                    AddValue("VOLUMEN GENERAL", () => Pct(GameSettings.MasterVolume), d => { GameSettings.MasterVolume = Step(GameSettings.MasterVolume, d); Changed(false); });
                    AddValue("MUSICA", () => Pct(GameSettings.MusicVolume), d => { GameSettings.MusicVolume = Step(GameSettings.MusicVolume, d); Changed(false); });
                    AddValue("EFECTOS", () => Pct(GameSettings.SfxVolume), d => { GameSettings.SfxVolume = Step(GameSettings.SfxVolume, d); Changed(false); });
                    Add("VOLVER", () => Show(Page.Main));
                    note.text = "Musica: lira, tambores y cuernos generados por codigo.\nEfectos: golpes, pasos, publico...";
                    break;
                case Page.Graphics:
                    title.text = "GRAFICOS";
                    AddValue("ESTILO", () => GameSettings.PS3 ? "PS3" : "PS2",
                        d => { GameSettings.GraphicsPreset = GameSettings.PS3 ? 0 : 1; Changed(false); });
                    AddValue("PANTALLA", () => GameSettings.Fullscreen ? "COMPLETA" : "VENTANA",
                        d => { GameSettings.Fullscreen = !GameSettings.Fullscreen; Changed(true); });
                    AddValue("RESOLUCION", ResolutionLabel, d =>
                    {
                        var res = GameSettings.Resolutions;
                        int i = GameSettings.ResolutionIndex < 0 ? CurrentResolutionIndex(res) : GameSettings.ResolutionIndex;
                        GameSettings.ResolutionIndex = (i + (d > 0 ? -1 : 1) + res.Count) % res.Count; // right = bigger
                        Changed(true);
                    });
                    AddValue("LIMITE DE FPS", () => { int c = GameSettings.FpsCaps[GameSettings.FpsCapIndex]; return c > 0 ? c.ToString() : "SIN LIMITE"; },
                        d => { GameSettings.FpsCapIndex = Wrap(GameSettings.FpsCapIndex + d, GameSettings.FpsCaps.Length); Changed(false); });
                    AddValue("VSYNC", () => GameSettings.VSync ? "SI" : "NO", d => { GameSettings.VSync = !GameSettings.VSync; Changed(false); });
                    AddValue("CALIDAD INTERNA", () => { int h = GameSettings.RenderHeight; return h == 448 ? "448 (PS2)" : h > 0 ? h.ToString() : "NATIVA"; },
                        d => { GameSettings.RenderHeightIndex = Wrap(GameSettings.RenderHeightIndex + d, GameSettings.RenderHeights.Length); Changed(false); });
                    AddValue("FILTRO PS2 (F1)", () => GameSettings.PostProcessing ? "SI" : "NO", d => { GameSettings.PostProcessing = !GameSettings.PostProcessing; Changed(false); });
                    AddValue("TEMBLOR DE VERTICES (F2)", () => GameSettings.VertexSnapping ? "SI" : "NO", d => { GameSettings.VertexSnapping = !GameSettings.VertexSnapping; Changed(false); });
                    Add("VOLVER", () => Show(Page.Main));
                    note.text = GameSettings.PS3
                        ? "PS3: resolucion nativa, antialiasing, luz por pixel y sombras\nreales del sol (calidad interna y filtro PS2 no se usan)."
                        : "PS2: el aspecto original. Si se traba: VSYNC SI, 60 FPS\ny el filtro PS2 apagado.";
                    break;
                case Page.Camera:
                    title.text = "CAMARA";
                    AddValue("TIPO", () => GameSettings.ShoulderCamera ? "AL HOMBRO" : "CLASICA",
                        d => { GameSettings.CameraMode = GameSettings.ShoulderCamera ? 0 : 1; Changed(false); });
                    AddValue("SENSIBILIDAD", () => Mathf.RoundToInt(GameSettings.CameraSensitivity * 100f) + "%",
                        d => { GameSettings.CameraSensitivity = Mathf.Clamp(Mathf.Round((GameSettings.CameraSensitivity + d * 0.1f) * 10f) / 10f, 0.3f, 2.5f); Changed(false); });
                    AddValue("INVERTIR EJE Y", () => GameSettings.InvertY ? "SI" : "NO", d => { GameSettings.InvertY = !GameSettings.InvertY; Changed(false); });
                    Add("VOLVER", () => Show(Page.Main));
                    note.text = GameSettings.ShoulderCamera
                        ? "Al hombro: el raton / stick derecho gira la camara.\nFIJAR objetivo: rueda del raton (clic) o TAB / R3 (patada: LB)."
                        : "Clasica: camara alta del coliseo, apuntas con el raton.\nGIRAR: manten la rueda (o TAB / R3) y mueve el raton o el stick; un toque la endereza.";
                    break;
                case Page.Controls:
                    title.text = "CONTROLES";
                    AddValue("SENSIBILIDAD CAMARA", () => Mathf.RoundToInt(GameSettings.CameraSensitivity * 100f) + "%",
                        d => { GameSettings.CameraSensitivity = Mathf.Clamp(Mathf.Round((GameSettings.CameraSensitivity + d * 0.1f) * 10f) / 10f, 0.3f, 2.5f); Changed(false); });
                    for (int ai = 0; ai < InputBindings.Count; ai++)
                    {
                        var act = (GameAction)ai;
                        AddRow(new Row
                        {
                            Label = InputBindings.Label(act), Wide = true,
                            Value = () => capturing == act ? "PULSA..." : InputBindings.Describe(act, false) + "   |   " + InputBindings.Describe(act, true),
                            Activate = () => { capturing = act; captureDelay = 0.15f; AudioManager.UI("ui_select"); },
                        });
                    }
                    Add("RESTAURAR POR DEFECTO", () => { InputBindings.ResetDefaults(); AudioManager.UI("ui_select"); Refresh(); });
                    Add("VOLVER", () => Show(Page.Main));
                    note.text = "Elige una accion y pulsa la tecla, el boton del raton o del mando nuevo\n(teclado | mando).  ESC cancela.  Si ya la usaba otra accion, se intercambian.";
                    break;
                case Page.ArenaOnline:
                {
                    var ns = NetSession.Instance;
                    title.text = "ARENA ONLINE";
                    // 0 todos contra todos, 1 por equipos, 2..5 co-op contra los bots del anfitrion (NetSession.ModeDuel..)
                    AddValue("MODO", () => ArenaModeLabel(arenaKind), d => arenaKind = (arenaKind + (d < 0 ? -1 : 1) + 6) % 6);
                    AddValue("MEJORAS", () => arenaUpgrades ? "SI (CADA UNO LAS SUYAS)" : "NO (TODOS IGUALES)", d => arenaUpgrades = !arenaUpgrades);
                    Add("¡A LA ARENA!", () =>
                    {
                        if (ns == null || !ns.IsHost) { Show(Page.Main); return; }
                        bool coop = arenaKind >= 2;
                        arenaTeams = arenaKind == 1;
                        if (!coop && ns.Players.Count < 2) { note.text = "Hace falta al menos otro jugador en la sala\n(contra bots se puede jugar desde uno)."; return; }
                        if (arenaTeams && ns.ColorsInUse < 2) { note.text = "Por equipos hacen falta al menos dos colores distintos\n(cada uno elige TU COLOR en la sala)."; return; }
                        Close();
                        ns.StartArena(arenaTeams, arenaUpgrades, coop ? arenaKind - 1 : NetSession.ModePvP);
                    });
                    Add("VOLVER", () => Show(Page.Main));
                    note.text = "Jugadores: gana el ultimo (o el ultimo color) en pie.  BOTS: todos juntos contra los rivales:\nduelo uno por jugador, oleadas, el campeon o doble (dos por jugador).  Mejoras: las de cada uno o todos iguales.";
                    break;
                }
                case Page.ConfirmNew:
                    title.text = "NUEVA PARTIDA";
                    note.text = "Se pierde el progreso de esta partida\n(dias, denarios, estadisticas y equipo).";
                    Add("SI, EMPEZAR DE NUEVO", NewGame);
                    Add("NO", () => Show(Page.Main));
                    break;
                case Page.Multi:
                    BuildMultiPage();
                    break;
                case Page.Lobby:
                    if (!BuildLobbyPage()) return;
                    break;
                case Page.ConfirmKick:
                    {
                        var ns = NetSession.Instance;
                        var p = ns != null ? ns.Get(kickId) : null;
                        if (p == null) { Show(Page.Lobby); return; }
                        title.text = "EXPULSAR";
                        note.text = "Expulsar a " + p.G.Name.ToUpperInvariant() + " de la sala?";
                        Add("SI, EXPULSAR", () => { NetSession.Instance?.Kick(kickId); Show(Page.Lobby); });
                        Add("NO", () => Show(Page.Lobby));
                    }
                    break;
                case Page.ConfirmQuit:
                    title.text = "SALIR DEL JUEGO";
                    note.text = "Los ajustes y tu gladiador quedan guardados.";
                    Add("SI, SALIR", Quit);
                    Add("NO", () => Show(Page.Main));
                    break;
            }
            selected = page == Page.ConfirmNew || page == Page.ConfirmQuit || page == Page.ConfirmKick ? rows.Count - 1 : 0;
            pageSig = PageSignature();
            if (page != Page.Multi && NetSession.Instance != null) NetSession.Instance.StopBrowsing();
            Layout();
            Refresh();
        }

        // ------------------------------------------------------------------ multiplayer
        void BuildMultiPage()
        {
            var ns = NetSession.Ensure();
            if (ns.Active) { Show(Page.Lobby); return; }
            if (ipText == null) ipText = PlayerPrefs.GetString("mp_ip", "");
            ns.StartBrowsing();
            title.text = "MULTIJUGADOR";
            Add("CREAR PARTIDA  (ANFITRION)", HostGame);
            var games = ns.FoundGames();
            foreach (var g in games)
            {
                var gg = g;
                Add("UNIRSE A " + g.HostName.ToUpperInvariant() + "  (" + g.Count + "/" + NetSession.MaxPlayers + ")" + (g.Phase == NetSession.PhaseLobby ? "" : " EN EL LUDUS"),
                    () => JoinGame(gg.Address + ":" + gg.Port));
            }
            AddValue("IP DEL ANFITRION", () => (ipText ?? "") + (EditingIp ? ((Time.unscaledTime % 1f) < 0.55f ? "_" : " ") : ""), d => BeginIpEdit());
            Add("UNIRSE POR IP", () => JoinGame(ipText));
            Add("VOLVER", () => Show(Page.Main));
            if (ns.LastHelp != null && Time.unscaledTime - ns.LastHelpTime < 120f)
                note.text = ns.LastHelp;
            else
                note.text = (games.Count == 0 ? "Buscando partidas (misma Wi-Fi o Radmin VPN: aparecen solas)..." : "Partidas encontradas.") +
                            "\nSi no aparece: escribe la IP que le sale al ANFITRION en su sala.\nPor internet: los dos en Radmin VPN (misma red) y la IP 26.x del anfitrion.";
        }

        /// <summary>The room: players (the host can kick), start (host only), back to the game, leave.</summary>
        bool BuildLobbyPage()
        {
            var ns = NetSession.Instance;
            if (ns == null || !ns.Active) { Show(Page.Multi); return false; }
            if (ns.Connecting || ns.LocalId <= 0)
            {
                title.text = "CONECTANDO...";
                note.text = "Conectando con " + ns.ConnectTarget + " ...";
                Add("CANCELAR", () => { ns.Leave(true); Show(Page.Multi); });
                return true;
            }
            title.text = "SALA  " + ns.Players.Count + "/" + NetSession.MaxPlayers;
            for (int i = 0; i < ns.Players.Count; i++)
            {
                var p = ns.Players[i];
                int id = p.Id;
                string label = (i + 1) + ". " + p.G.Name.ToUpperInvariant() + (p.Host ? "  (ANFITRION)" : "") + (p.Id == ns.LocalId ? "  (TU)" : "");
                if (ns.IsHost && p.Id != ns.LocalId) Add(label + "  - EXPULSAR", () => { kickId = id; Show(Page.ConfirmKick); });
                else Add(label, null);
            }
            if (ns.IsHost && ns.Phase == NetSession.PhaseLobby)
                Add("INICIAR: LUDUS COMPARTIDO", () => { Close(); ns.StartGame(); });
            {
                var me = ns.Local;
                AddValue("TU COLOR", () => ns.Local != null ? NetSession.ColorNames[ns.Local.Color] : "-",
                    d => { if (ns.Local != null) ns.SetColor((ns.Local.Color + d + NetSession.ColorNames.Length) % NetSession.ColorNames.Length); });
            }
            if (ns.IsHost && ns.Phase == NetSession.PhaseLudus)
                Add("ARENA ONLINE...", () => Show(Page.ArenaOnline));
            Add("VOLVER AL JUEGO", Close);
            Add(ns.IsHost ? "CERRAR LA SALA" : "SALIR DE LA SALA", () => { ns.Leave(); Show(Page.Multi); });
            if (ns.IsHost)
            {
                Add(ns.FetchingPublicIp ? "BUSCANDO TU IP DE INTERNET..." : "VER MI IP DE INTERNET", () => { ns.FetchPublicIp(); Show(Page.Lobby); });
                var lines = NetSession.AddressLines();
                bool vpn = lines.Exists(l => !l.StartsWith("MISMA"));
                string pub = ns.PublicIp == null ? "" : ns.PublicIp == "?" ? "\nINTERNET: no se pudo averiguar" :
                    "\nINTERNET: " + ns.PublicIp + " (solo si abres el puerto " + ns.Port + " TCP en el router)";
                note.text = "DALES UNA DE ESTAS IP:\n" + (lines.Count > 0 ? string.Join("\n", lines) : "?") + pub +
                            (vpn ? "" : "\nPor internet sin estar en la misma Wi-Fi: usad Radmin VPN (la de Wi-Fi no sirve).");
            }
            else note.text = ns.Phase == NetSession.PhaseLobby ? "Esperando a que el anfitrion inicie la partida..." : "Estais en el ludus compartido.";
            return true;
        }

        void HostGame()
        {
            EndIpEdit();
            if (NetSession.Ensure().Host()) Show(Page.Lobby);
        }

        void JoinGame(string address)
        {
            EndIpEdit();
            if (string.IsNullOrWhiteSpace(address)) return;
            PlayerPrefs.SetString("mp_ip", ipText ?? "");
            NetSession.Ensure().Join(address);
            Show(Page.Lobby);
        }

        void BeginIpEdit()
        {
            if (EditingIp) return;
            EditingIp = true;
            var k = Keyboard.current;
            if (k != null) k.onTextInput += OnIpChar;
        }

        void EndIpEdit()
        {
            if (!EditingIp) return;
            EditingIp = false;
            var k = Keyboard.current;
            if (k != null) k.onTextInput -= OnIpChar;
            PlayerPrefs.SetString("mp_ip", ipText ?? "");
        }

        void OnIpChar(char c)
        {
            if (!EditingIp || char.IsControl(c)) return;
            if (!(char.IsLetterOrDigit(c) || c == '.' || c == ':' || c == '-')) return;
            if ((ipText ?? "").Length < 40) ipText = (ipText ?? "") + c;
        }

        /// <summary>What the multiplayer pages show; when it changes (someone joins, a game appears) the page is rebuilt.</summary>
        string PageSignature()
        {
            var ns = NetSession.Instance;
            if (Current == Page.Lobby || Current == Page.ConfirmKick)
            {
                if (ns == null) return "none";
                var sb = new System.Text.StringBuilder();
                sb.Append(ns.Role).Append(ns.Connecting).Append(ns.LocalId).Append(ns.Phase).Append(ns.PublicIp).Append(ns.FetchingPublicIp);
                foreach (var p in ns.Players) sb.Append('|').Append(p.Id).Append(p.G != null ? p.G.Name : "");
                return sb.ToString();
            }
            if (Current == Page.Multi)
            {
                if (ns == null) return "none";
                if (ns.Active) return "active";
                var sb = new System.Text.StringBuilder();
                foreach (var g in ns.FoundGames()) sb.Append(g.Address).Append(g.Port).Append(g.Count).Append(g.HostName).Append(g.Phase).Append('|');
                return sb.ToString();
            }
            return "";
        }

        bool arenaTeams, arenaUpgrades = true;
        int arenaKind;

        static string ArenaModeLabel(int k)
        {
            switch (k)
            {
                case 0: return "TODOS CONTRA TODOS";
                case 1: return "POR EQUIPOS (COLORES)";
                case 2: return "BOTS: DUELO (1 POR JUGADOR)";
                case 3: return "BOTS: OLEADAS";
                case 4: return "BOTS: EL CAMPEON";
                default: return "BOTS: DOBLE (2 POR JUGADOR)";
            }
        }
        GameAction? capturing;
        float captureDelay;

        void Add(string label, Action activate) => AddRow(new Row { Label = label, Activate = activate });
        void AddValue(string label, Func<string> value, Action<int> change) =>
            AddRow(new Row { Label = label, Value = value, Change = change, Activate = () => change(1) });

        void AddRow(Row r)
        {
            r.Rect = PS2UI.Rect(list, r.Label, RowBg);
            r.LabelText = PS2UI.Text(r.Rect, r.Label, 20, r.Value == null ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft,
                new Vector2(0.5f, 0.5f), r.Value == null ? Vector2.zero : new Vector2(-40, 0), new Vector2(r.Value == null ? 440 : 360, 30), PS2UI.Parchment);
            if (r.Wide)
            {
                r.LabelText.fontSize = 15;
                r.LabelText.rectTransform.anchoredPosition = new Vector2(-130, 0);
                r.LabelText.rectTransform.sizeDelta = new Vector2(220, 30);
            }
            if (r.Value != null)
                r.ValueText = r.Wide
                    ? PS2UI.Text(r.Rect, "", 15, TextAnchor.MiddleRight, new Vector2(0.5f, 0.5f), new Vector2(80, 0), new Vector2(300, 30), PS2UI.Gold)
                    : PS2UI.Text(r.Rect, "", 20, TextAnchor.MiddleRight, new Vector2(0.5f, 0.5f), new Vector2(110, 0), new Vector2(200, 30), PS2UI.Gold);
            rows.Add(r);
        }

        void Layout()
        {
            float h = rows.Count > 12 ? 27f : rows.Count > 6 ? 38f : 46f, gap = rows.Count > 12 ? 3f : 6f;
            float total = rows.Count * h + (rows.Count - 1) * gap;
            // The note grows upwards when it has more than two lines; the panel grows with it.
            float noteH = Mathf.Max(40f, note.preferredHeight + 4f);
            float extra = noteH - 40f;
            note.rectTransform.sizeDelta = new Vector2(500, noteH);
            note.rectTransform.anchoredPosition = new Vector2(0, 62 + extra * 0.5f);
            panel.sizeDelta = new Vector2(560, 100 + total + 110 + extra);
            for (int i = 0; i < rows.Count; i++)
                PS2UI.Anchored(rows[i].Rect, new Vector2(0.5f, 1f), new Vector2(0, -i * (h + gap)), new Vector2(480, h));
        }

        void Refresh()
        {
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                bool sel = i == selected;
                r.Rect.GetComponent<Image>().color = sel ? RowSel : RowBg;
                r.LabelText.color = sel ? Color.white : PS2UI.Parchment;
                if (r.ValueText != null) r.ValueText.text = r.Wide ? r.Value() : (sel ? "< " : "") + r.Value() + (sel ? " >" : "");
            }
            bool pad = GameInput.UsingGamepad;
            hint.text = pad ? "(A) elegir   (B) volver   izq/der cambiar"
                            : "W/S elegir   A/D cambiar   ENTER aceptar   ESC volver   o usa el raton";
        }

        static string Pct(float v) => Mathf.RoundToInt(v * 100f) + "%";
        static float Step(float v, int d) => Mathf.Clamp01(Mathf.Round((v + d * 0.1f) * 10f) / 10f);
        static int Wrap(int i, int n) => (i % n + n) % n;

        static int CurrentResolutionIndex(List<Vector2Int> res)
        {
            for (int i = 0; i < res.Count; i++) if (res[i].x == Screen.width && res[i].y == Screen.height) return i;
            return 0;
        }

        static string ResolutionLabel()
        {
            var res = GameSettings.Resolutions;
            if (GameSettings.ResolutionIndex >= 0 && GameSettings.ResolutionIndex < res.Count)
                return res[GameSettings.ResolutionIndex].x + "x" + res[GameSettings.ResolutionIndex].y;
            return Screen.width + "x" + Screen.height;
        }

        static void Changed(bool window)
        {
            GameSettings.Save();
            GameSettings.Apply(window);
        }

        // ------------------------------------------------------------------ open / close
        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            prevTimeScale = Time.timeScale > 0.01f ? Time.timeScale : 1f;
            // In a multiplayer game the world goes on for the others: no freezing.
            if (NetSession.Instance == null || !NetSession.Instance.Active) Time.timeScale = 0f;
            GameInput.Paused = true;
            root.SetActive(true);
            Show(Page.Main);
            AudioManager.UI("ui_select");
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            EndIpEdit();
            if (NetSession.Instance != null) NetSession.Instance.StopBrowsing();
            root.SetActive(false);
            Time.timeScale = prevTimeScale;
            GameInput.Paused = false;
            GameInput.SuppressFrame = Time.frameCount; // the closing key press must not reach the game
        }

        public void ShowPage(Page p) { if (IsOpen) Show(p); }

        void NewGame()
        {
            Close();
            Time.timeScale = 1f;
            if (GameManager.Instance != null) Destroy(GameManager.Instance.gameObject); // fresh profile + day 1
            SceneManager.LoadScene("Creator");
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>Esc must not pause while another menu owns it (shop, creator confirmation).</summary>
        static bool AnotherMenuOwnsEscape()
        {
            var shop = FindAnyObjectByType<ShopMenu>();
            if (shop != null && shop.IsOpen) return true;
            if (ArenaGateMenu.Instance != null && ArenaGateMenu.Instance.IsOpen) return true;
            if (ChoiceMenu.Instance != null && ChoiceMenu.Instance.IsOpen) return true;
            var mg = Gladiator.Training.TrainingMinigame.PlayerRunning;
            if (mg != null && mg.FreeMode) return true; // Esc ends the free practice
            var creator = FindAnyObjectByType<CharacterCreatorMenu>();
            return creator != null && (creator.Confirming || creator.EditingName);
        }

        // ------------------------------------------------------------------ input (read directly: GameInput is muted)
        void Update()
        {
            var k = Keyboard.current; var g = Gamepad.current; var m = Mouse.current;
            bool esc = (k != null && k.escapeKey.wasPressedThisFrame) || (g != null && g.selectButton.wasPressedThisFrame);
            if (!IsOpen)
            {
                if (esc && !AnotherMenuOwnsEscape()) Open();
                return;
            }

            if (capturing.HasValue)
            {
                captureDelay -= Time.unscaledDeltaTime;
                if (esc) { capturing = null; AudioManager.UI("ui_back"); }
                else if (captureDelay <= 0f)
                {
                    string got = InputBindings.CaptureAny();
                    if (got != null && InputBindings.Set(capturing.Value, got)) { capturing = null; AudioManager.UI("ui_select"); }
                }
                Refresh();
                return;
            }

            if (EditingIp)
            {
                if (k != null && k.backspaceKey.wasPressedThisFrame && !string.IsNullOrEmpty(ipText)) ipText = ipText.Substring(0, ipText.Length - 1);
                bool done = esc || (k != null && (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame)) ||
                            (m != null && m.leftButton.wasPressedThisFrame);
                if (done) { EndIpEdit(); AudioManager.UI("ui_select"); }
                Refresh();
                return;
            }

            // Multiplayer pages follow the room live (joins, kicks, games found on the network).
            if (Current == Page.Multi || Current == Page.Lobby || Current == Page.ConfirmKick)
            {
                sigTimer -= Time.unscaledDeltaTime;
                if (sigTimer <= 0f)
                {
                    sigTimer = 0.4f;
                    if (PageSignature() != pageSig)
                    {
                        int keep = selected;
                        Show(Current);
                        if (!IsOpen) return;
                        selected = Mathf.Clamp(keep, 0, Mathf.Max(0, rows.Count - 1));
                        Refresh();
                    }
                }
            }

            bool back = esc || (g != null && g.buttonEast.wasPressedThisFrame) || (k != null && k.backspaceKey.wasPressedThisFrame);
            if (back)
            {
                AudioManager.UI("ui_back");
                if (Current == Page.Main) Close();
                else if (Current == Page.ConfirmKick) Show(Page.Lobby);
                else Show(Page.Main);
                return;
            }

            int dy = 0, dx = 0;
            if (k != null)
            {
                if (k.wKey.wasPressedThisFrame || k.upArrowKey.wasPressedThisFrame) dy = -1;
                if (k.sKey.wasPressedThisFrame || k.downArrowKey.wasPressedThisFrame) dy = 1;
                if (k.aKey.wasPressedThisFrame || k.leftArrowKey.wasPressedThisFrame) dx = -1;
                if (k.dKey.wasPressedThisFrame || k.rightArrowKey.wasPressedThisFrame) dx = 1;
            }
            if (g != null)
            {
                if (g.dpad.up.wasPressedThisFrame) dy = -1;
                if (g.dpad.down.wasPressedThisFrame) dy = 1;
                if (g.dpad.left.wasPressedThisFrame) dx = -1;
                if (g.dpad.right.wasPressedThisFrame) dx = 1;
                Vector2 st = g.leftStick.ReadValue();
                navRepeat -= Time.unscaledDeltaTime;
                if (st.magnitude > 0.6f && navRepeat <= 0f)
                {
                    navRepeat = 0.22f;
                    if (Mathf.Abs(st.y) > Mathf.Abs(st.x)) dy = st.y > 0 ? -1 : 1; else dx = st.x > 0 ? 1 : -1;
                }
                else if (st.magnitude < 0.3f) navRepeat = 0f;
            }
            bool accept = (k != null && (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame || k.eKey.wasPressedThisFrame))
                          || (g != null && g.buttonSouth.wasPressedThisFrame);

            // Mouse: hover selects, click activates (on a value row, the left half lowers and the right half raises).
            if (m != null)
            {
                Vector2 mp = m.position.ReadValue();
                for (int i = 0; i < rows.Count; i++)
                {
                    if (!RectTransformUtility.RectangleContainsScreenPoint(rows[i].Rect, mp, null)) continue;
                    if ((m.delta.ReadValue().sqrMagnitude > 0.1f || m.leftButton.wasPressedThisFrame) && selected != i) { selected = i; AudioManager.UI("ui_move"); }
                    if (m.leftButton.wasPressedThisFrame)
                    {
                        var r = rows[i];
                        AudioManager.UI(r.Change != null ? "ui_move" : "ui_select");
                        if (r.Change != null)
                        {
                            RectTransformUtility.ScreenPointToLocalPointInRectangle(r.Rect, mp, null, out Vector2 local);
                            r.Change(local.x < 0f ? -1 : 1);
                        }
                        else r.Activate?.Invoke();
                        if (IsOpen) Refresh();
                        return;
                    }
                }
            }

            if (dy != 0 && rows.Count > 0) { selected = Wrap(selected + dy, rows.Count); AudioManager.UI("ui_move"); }
            if (dx != 0 && selected < rows.Count && rows[selected].Change != null) { rows[selected].Change(dx); AudioManager.UI("ui_move"); }
            if (accept && selected < rows.Count) { AudioManager.UI("ui_select"); rows[selected].Activate?.Invoke(); }
            if (IsOpen) Refresh();
        }
    }
}
