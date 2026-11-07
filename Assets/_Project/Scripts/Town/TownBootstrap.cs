using Gladiator.Arena;
using Gladiator.Combat;
using Gladiator.Core;
using Gladiator.PS2Style;
using Gladiator.Progression;
using Gladiator.Training;
using Gladiator.UI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Gladiator.Town
{
    /// <summary>
    /// Builds the town at runtime (scene "Town"): a plaza with a fountain and market, streets lined with low-poly
    /// houses, the colosseum at one end and the ludus at the other, inside a walled rectangle. Same PS2 pipeline,
    /// shader, materials and light as the ludus. The plan comes from <see cref="TownLayout"/> (one seed per save
    /// on this PC), the geometry from <see cref="TownBuilder"/>. No citizens yet.
    /// You arrive by the ludus' east door; walk back to the ludus gate, or to the arena gate to choose a fight.
    /// </summary>
    public class TownBootstrap : MonoBehaviour
    {
        [Header("Shaders (assigned by the scene builder so they're included in builds)")]
        public Shader PS2LitShader;
        public Shader PS2PostShader;

        public static readonly Color FogColor = TrainingBootstrap.FogColor;
        public static readonly Color SkyTop = TrainingBootstrap.SkyTop;

        public const string SceneName = "Town";
        const string SeedKey = "Gladiator.TownSeed";

        public static TownBootstrap Instance { get; private set; }
        public TownLayout Layout { get; private set; }
        public Fighter Player { get; private set; }
        public ArenaCamera Cam { get; private set; }
        public TrainingHUD Hud { get; private set; }
        public int Buildings { get; private set; }
        public TownInfoPanel Info { get; private set; }
        public CitizenSpawner Spawner { get; private set; }
        /// <summary>The homes you can walk into.</summary>
        public System.Collections.Generic.List<HouseInterior> Houses { get; private set; }

        public HouseInterior HouseAt(Vector3 p)
        {
            if (Houses == null) return null;
            foreach (var h in Houses) if (h.Contains(p)) return h;
            return null;
        }

        public TownDoor NearestDoor(Vector3 p, float range)
        {
            TownDoor best = null; float bd = range;
            if (Houses == null) return null;
            foreach (var h in Houses)
            {
                if (h.Door == null) continue;
                Vector3 d = h.Door.Center - p; d.y = 0f;
                if (d.magnitude < bd) { bd = d.magnitude; best = h.Door; }
            }
            return best;
        }

        /// <summary>The town in use: online, the host's; alone, this PC's.</summary>
        public static int CurrentSeed()
        {
            var net = Gladiator.Net.NetSession.Instance;
            if (net != null && net.Active && net.LocalId > 0 && net.TownSeed > 0) return net.TownSeed;
            return LocalSeed();
        }

        /// <summary>The town of this PC: the seed is made once and kept (-townseed N overrides it, for tests).</summary>
        public static int LocalSeed()
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-townseed");
            if (i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int forced)) return forced;
            if (!PlayerPrefs.HasKey(SeedKey)) { PlayerPrefs.SetInt(SeedKey, Random.Range(1, 1000000)); PlayerPrefs.Save(); }
            return PlayerPrefs.GetInt(SeedKey);
        }

        void Awake()
        {
            Instance = this;
            Time.timeScale = 1f;
            var gm = GameManager.Ensure();
            gm.DayCycle.NotifySceneStarted(DayPhase.Training); // the town belongs to the training part of the day
            PS2Materials.Init(PS2LitShader);
            SetupRenderSettings();

            // The same plan the simulation uses (same seed): houses and workplaces match the citizens' data.
            var sim = TownSimulation.Ensure();
            sim.EnsureSeed(CurrentSeed()); // online: the host's town
            Layout = sim.Layout;
            var env = new GameObject("Environment").transform;
            BuildLighting(env);
            var builder = new TownBuilder(Layout, env);
            builder.Build();
            Buildings = builder.BuildingCount;
            Houses = builder.Houses;
            // streets for the townsfolk: navmesh baked from the colliders just built (before any fighter exists)
            Vector3 mid = (Layout.WallCorners[0] + Layout.WallCorners[2]) * 0.5f;
            float span = Mathf.Max(TownLayout.WallX * 2f, TownLayout.WallZMax - TownLayout.WallZMin) + 10f;
            TownNavMesh.Build(mid, new Vector3(span, 40f, span), Houses);
            BuildSky(env);
            Camera cam = BuildCamera();

            Vector3 spawn = Layout.LudusGate + Layout.LudusFacing * 3.5f + Vector3.up * 0.05f; // clear of the ludus wall (camera)
            Player = CharacterFactory.Create(gm.Profile.Name, gm.Profile.Appearance, spawn, Quaternion.LookRotation(Layout.LudusFacing), 0, gm.Profile.ArmorSet);
            Player.Equip(WeaponFactory.Create(WeaponLibrary.Rudis));
            Player.DisarmResistance = 1f;
            // bought clothes or armour at the smithy: dress the gladiator at once
            var profileRef = gm.Profile;
            var playerRef = Player;
            string Dress() => string.Join(",", profileRef.Armor) + "|" + string.Join(",", profileRef.ArmorSides) + "|" + JsonUtility.ToJson(profileRef.Appearance);
            string dressed = Dress();
            System.Action onChanged = null;
            onChanged = () =>
            {
                if (playerRef == null) { profileRef.Changed -= onChanged; return; }
                string now = Dress();
                if (now == dressed) return;
                dressed = now;
                CharacterFactory.RebuildBody(playerRef, profileRef.Appearance, profileRef.ArmorSet);
            };
            profileRef.Changed += onChanged;
            var pc = Player.gameObject.AddComponent<PlayerController>();
            pc.AimCamera = cam;

            Cam = cam.GetComponent<ArenaCamera>();
            Cam.Player = Player;
            Cam.Distance = 11f;
            Cam.Pitch = 40f;
            Cam.EnemyBias = 0f;
            // The arena is "up" on screen whatever way the town was turned.
            Cam.Yaw = Quaternion.LookRotation(-Layout.ArenaFacing).eulerAngles.y;
            Cam.SnapToTarget();

            Hud = new GameObject("TownHUD", typeof(RectTransform)).AddComponent<TrainingHUD>();
            Hud.Init(cam);
            Hud.SetHint("WASD mover   E interactuar   puerta del ludus: volver   puerta de la arena: elegir combate   F3 el pueblo   F4 donde esta cada uno   ESC menu");
            Spawner = new GameObject("Citizens").AddComponent<CitizenSpawner>();
            Spawner.Init(Player);
            Info = new GameObject("TownInfo", typeof(RectTransform)).AddComponent<TownInfoPanel>();
            Info.Init(this, cam);
            // online: the other players who are in the town too
            var net = Gladiator.Net.NetSession.Instance;
            if (net != null && net.InGame)
            {
                var nav = gameObject.AddComponent<Gladiator.Net.NetAvatars>();
                nav.SceneCode = 1;
                nav.Init(Player, cam, slot => Layout.LudusGate + Layout.LudusFacing * (3.5f + slot), msg => Hud.ShowMessage(msg, 3f));
            }
            gameObject.AddComponent<TownManager>().Init(this, pc);
            Hud.ShowMessage("EL PUEBLO", 2.5f);
            Debug.Log($"[TOWN] seed={Layout.Seed} rot={Layout.RotationDeg} mirrored={Layout.Mirrored} lots={Layout.Lots.Count} places={Layout.Places.Count} buildings={Buildings}");
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            TownNavMesh.Clear();
        }

        void SetupRenderSettings()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = FogColor;
            RenderSettings.fogStartDistance = 30f;
            RenderSettings.fogEndDistance = 150f;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = TownBuilder.AmbientSky;
            RenderSettings.skybox = null;
            QualitySettings.antiAliasing = 0;
        }

        Camera BuildCamera()
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = FogColor;
            cam.fieldOfView = 45f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 320f;
            cam.allowMSAA = false;
            cam.allowHDR = false;
            go.AddComponent<ArenaCamera>();
            var ps2 = go.AddComponent<PS2RenderSetup>();
            ps2.PostShader = PS2PostShader;
            return cam;
        }

        void BuildLighting(Transform parent)
        {
            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(parent, false);
            sunGo.transform.rotation = Quaternion.Euler(42f, 55f, 0f);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.92f, 0.8f);
            sun.intensity = 0.95f;
            sun.shadows = LightShadows.None;
            RenderSettings.sun = sun;
        }

        void BuildSky(Transform parent)
        {
            var b = new MeshBuilder();
            const int rings = 8;
            var prof = new System.Collections.Generic.List<MeshBuilder.Ring>();
            for (int k = 0; k <= rings; k++)
            {
                float t = Mathf.Lerp(-20f, 90f, k / (float)rings) * Mathf.Deg2Rad;
                prof.Add(new MeshBuilder.Ring(Mathf.Cos(t) * 260f, Mathf.Sin(t) * 260f, Mathf.Cos(t), Mathf.Sin(t)));
            }
            b.AddLathe(prof, 16, Vector3.zero, Color.white, true);
            Mesh m = b.Build("TownSky");
            var v = m.vertices;
            var c = new Color[v.Length];
            for (int i = 0; i < v.Length; i++) c[i] = Color.Lerp(FogColor, SkyTop, Mathf.Pow(Mathf.Clamp01(v[i].y / 260f), 0.55f));
            m.colors = c;
            var mat = PS2Materials.Get("sky_ludus", PS2Textures.White, Color.white, 0f, 0f, null, CullMode.Off);
            PS2Spawn.Mesh(parent, "SkyDome", m, (Layout.WallCorners[0] + Layout.WallCorners[2]) * 0.5f, mat);
        }
    }

    /// <summary>Town interactions: the ludus gate (back to the yard) and the arena gate (choose a fight).</summary>
    public class TownManager : MonoBehaviour
    {
        const float GateRadius = 3.2f;
        TownBootstrap town;
        HouseInterior house;
        string room;
        /// <summary>The house the player is in (null outside) and the room (tests).</summary>
        public HouseInterior CurrentHouse => house;
        public string CurrentRoom => room;
        PlayerController pc;
        bool leaving;

        public void Init(TownBootstrap t, PlayerController controller)
        {
            town = t; pc = controller;
            town.Hud.Shop.Closed += () => { if (pc != null) pc.enabled = true; };
        }

        Gladiator.Net.NetSession hooked;

        void OnEnable() { }

        void OnDestroy() { if (hooked != null) hooked.GameMessage -= OnNet; }

        void LateUpdate()
        {
            var s = Gladiator.Net.NetSession.Instance;
            if (s != hooked) { if (hooked != null) hooked.GameMessage -= OnNet; hooked = s; if (s != null) s.GameMessage += OnNet; }
        }

        public static void SendDoor(TownDoor door)
        {
            var s = Gladiator.Net.NetSession.Instance;
            var h = door != null ? door.GetComponent<HouseInterior>() : null;
            if (s == null || !s.InGame || h == null) return;
            s.Send("DR", h.Lot.Id + "|" + (door.IsOpen ? 1 : 0));
        }

        /// <summary>Another player opened or closed a door in the same town.</summary>
        void OnNet(int from, string type, string payload)
        {
            if (type != "DR" || town == null || town.Houses == null) return;
            var a = payload.Split('|');
            if (a.Length < 2 || !int.TryParse(a[0], out int lotId)) return;
            foreach (var h in town.Houses)
                if (h.Lot.Id == lotId && h.Door != null) { h.Door.SetOpen(a[1] == "1"); break; }
        }

        /// <summary>The front of the nearest smithy (where the smith sells), within a range.</summary>
        public TownLot NearestSmithy(Vector3 p, float range)
        {
            TownLot best = null; float bd = range;
            foreach (var lot in town.Layout.Lots)
            {
                if (lot.Kind != BuildingKind.Smithy) continue;
                Vector3 d = lot.Door - p; d.y = 0f;
                if (d.magnitude < bd) { bd = d.magnitude; best = lot; }
            }
            return best;
        }

        public void OpenShop()
        {
            var gm = GameManager.Instance;
            if (gm == null || town.Hud.Shop.IsOpen) return;
            if (pc != null) pc.enabled = false;
            town.Player.Move(Vector3.zero);
            town.Player.SetBlock(false);
            town.Hud.Shop.Open(gm.Profile);
        }

        void Update()
        {
            GameInput.UpdateDeviceMode();
            if (town == null || leaving) return;
            if (ArenaGateMenu.Instance != null && ArenaGateMenu.Instance.IsOpen) { town.Hud.SetPrompt(""); return; }
            if (town.Hud.Shop.IsOpen) { town.Hud.SetPrompt(""); return; } // the shop has its own keys (Esc closes)
            string key = GameInput.UsingGamepad ? "(A) " : "[" + InputBindings.Short(GameAction.Interact) + "] ";
            Vector3 p = town.Player.transform.position;
            // inside a house, or at its door: its roof and ceiling go away so the camera sees in; say which room
            var inHouse = town.HouseAt(p);
            if (inHouse == null)
            {
                var atDoor = town.NearestDoor(p, 2.6f);
                if (atDoor != null) inHouse = atDoor.GetComponent<HouseInterior>();
            }
            if (inHouse != house)
            {
                if (house != null) house.ShowUpper(true);
                house = inHouse;
                if (house != null) house.ShowUpper(false);
                room = null;
            }
            if (house != null)
            {
                string rn = house.RoomAt(p); // null at the door, outside
                if (rn != null && rn != room) { room = rn; town.Hud.ShowMessage(rn == "BANO" ? "BAÑO" : rn == "HABITACION" ? "HABITACIÓN" : rn, 1.6f); }
            }
            // the smithies: weapons, armour and shields
            var smithy = NearestSmithy(p, 2.6f);
            if (smithy != null)
            {
                var gmS = GameManager.Instance;
                town.Hud.SetPrompt(key + "HERRERIA: ARMAS, ARMADURAS Y ESCUDOS   (" + (gmS != null ? gmS.Profile.Denarii : 0) + " denarios)");
                if (GameInput.InteractPressed) OpenShop();
                return;
            }
            var door = town.NearestDoor(p, 1.7f);
            if (door != null)
            {
                town.Hud.SetPrompt(key + (door.IsOpen ? "CERRAR LA PUERTA" : "ABRIR LA PUERTA"));
                if (GameInput.InteractPressed) { door.Toggle(); SendDoor(door); }
                return;
            }
            var npc = town.Spawner != null ? town.Spawner.Nearest(p, 2.3f) : null;
            if (npc != null && npc.Citizen != null && !npc.Fleeing)
            {
                town.Hud.SetPrompt(key + "HABLAR CON " + npc.Citizen.Name.ToUpperInvariant() + " (" + npc.Citizen.JobName + ")");
                if (GameInput.InteractPressed) TalkTo(npc);
                return;
            }
            if (Flat(town.Layout.LudusGate - p) < GateRadius)
            {
                town.Hud.SetPrompt(key + "VOLVER AL LUDUS");
                if (GameInput.InteractPressed) GoToLudus();
                return;
            }
            if (Flat(town.Layout.ArenaGate - p) < GateRadius)
            {
                town.Hud.SetPrompt(key + "PUERTA DE LA ARENA: ELEGIR COMBATE");
                if (GameInput.InteractPressed) OpenArenaGate();
                return;
            }
            town.Hud.SetPrompt("");
        }

        /// <summary>A line from a townsperson (shown as the message on top). Returns it (tests).</summary>
        public string TalkTo(CitizenNPC npc)
        {
            string line = npc.Say();
            town.Hud.ShowMessage(line, 4.5f);
            Vector3 to = town.Player.transform.position - npc.transform.position; to.y = 0f;
            npc.Fighter.SetFacing(to);
            return line;
        }

        public void GoToLudus()
        {
            if (leaving) return;
            leaving = true;
            TrainingBootstrap.ArriveFromTown = true;
            SceneManager.LoadScene("Training");
        }

        public void OpenArenaGate()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;
            ArenaGateMenu.Ensure().Open(gm.DayCycle.Day, mode => { leaving = true; gm.DayCycle.GoToArena(mode); });
        }

        static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }
    }
}
