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
        public TownGuards Guards { get; private set; }
        /// <summary>The baths and the basilica: bathing, errands, wagers, fines.</summary>
        public TownServices Services { get; private set; }
        /// <summary>The people who find you asleep in their house.</summary>
        public HomeOwners Owners { get; private set; }
        /// <summary>The governor, in his domus on the plaza (null if the town has none).</summary>
        public GovernorNPC Governor { get; private set; }
        /// <summary>The prisoner in the jail (null if the town has none).</summary>
        public JailNPC Prisoner { get; private set; }
        /// <summary>The carpenter on the plaza (furniture for your houses).</summary>
        void SpawnArmorers()
        {
            string[] names = { "Vulcanus", "Marcus Ferrarius", "Brutus", "Titus Faber" };
            int k = 0;
            foreach (var lot in Layout.Lots)
            {
                if (lot.Kind != BuildingKind.Smithy || lot.Hidden) continue;
                string name = names[k % names.Length] + " el armero";
                var n = ServiceNPC.Spawn(name, new Pose(lot.ArmorerSpot + Vector3.up * 0.05f, lot.Rotation), Layout.Seed * 23 + lot.Id, 1, 2, 0.3f);
                var f = n.Fighter;
                // a small hammer in his right hand (the weapon socket follows the hand)
                if (f != null && f.WeaponSocket != null)
                {
                    var h = new GameObject("Hammer").transform;
                    h.SetParent(f.WeaponSocket, false);
                    var dark = Gladiator.PS2Style.PS2Materials.Get("furn_dark", Gladiator.PS2Style.PS2Textures.Wood, new Color(0.36f, 0.24f, 0.14f));
                    var iron = Gladiator.PS2Style.PS2Materials.Get("furn_iron", Gladiator.PS2Style.PS2Textures.Metal, new Color(0.3f, 0.3f, 0.32f));
                    Gladiator.PS2Style.PS2Spawn.Box(h, "Handle", new Vector3(0.03f, 0.03f, 0.3f), new Vector3(0f, 0f, 0.09f), dark);
                    Gladiator.PS2Style.PS2Spawn.Box(h, "Head", new Vector3(0.07f, 0.15f, 0.07f), new Vector3(0f, 0f, 0.25f), iron);
                }
                Armorers.Add(n);
                k++;
                Debug.Log($"[TOWN] armorer {name} at {lot.ArmorerSpot} (lot {lot.Id})");
            }
        }

        public CarpenterNPC Carpenter { get; private set; }
        /// <summary>The armourer of each smithy, at his anvil with a hammer: he sells weapons, armour and shields.</summary>
        public readonly System.Collections.Generic.List<ServiceNPC> Armorers = new System.Collections.Generic.List<ServiceNPC>();
        /// <summary>Online: houses bought by the other players (lot id -> owner's name).</summary>
        public readonly System.Collections.Generic.Dictionary<int, string> OtherOwners = new System.Collections.Generic.Dictionary<int, string>();
        /// <summary>The ground floor of a house (by lot id).</summary>
        public HouseInterior HouseOfLot(int lotId) => Houses != null ? Houses.Find(h => h.Lot.Id == lotId && h.Floor == 0 && h.Door != null) : null;
        /// <summary>The jail by the arena.</summary>
        public HouseInterior Jail => Houses != null ? Houses.Find(h => h.CellSpots.Count > 0) : null;
        /// <summary>The ludus yard inside the town (+z towards the town) and its training manager.</summary>
        public Transform Ludus { get; private set; }
        public Gladiator.Training.TrainingManager Training { get; private set; }
        Transform ludusMarkers;
        System.Collections.Generic.List<Gladiator.Training.TrainingMinigame> ludusStations;
        public bool InLudus(Vector3 p) => Ludus != null && Gladiator.Training.LudusInTown.Inside(Ludus, p);

        /// <summary>After the guards beat the player: wake up in the infirmary with half the health. Returns where (for the message).</summary>
        public string WakeUpAfterBeating(out Vector3 at)
        {
            if (Player.InActivity) Player.StopActivity();
            Player.Health.Heal(Player.Health.Max * 0.5f - Player.Health.Current);
            if (PutInInfirmaryBed(out at)) return "en la enfermeria";
            at = Layout.LudusGate + Layout.LudusFacing * 3.5f + Vector3.up * 0.05f;
            Teleport(at, Player.transform.rotation);
            return "junto a la puerta del ludus";
        }

        /// <summary>The infirmary (null if this town has none).</summary>
        public HouseInterior Infirmary => Houses != null ? Houses.Find(h => h.Public && h.BedSpots.Count > 0) : null;
        /// <summary>The bed the player last woke up in (tests).</summary>
        public int LastBed { get; private set; } = -1;
        /// <summary>This visit started in an infirmary bed (after a lost fight).</summary>
        public bool WokeInInfirmary { get; private set; }

        void Teleport(Vector3 at, Quaternion rot)
        {
            var cc = Player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            Player.transform.SetPositionAndRotation(at, rot);
            if (cc != null) cc.enabled = true;
            if (Cam != null) Cam.SnapToTarget();
        }

        /// <summary>Lays the player in a free infirmary bed (lying, already asleep). False if there is no infirmary.</summary>
        public bool PutInInfirmaryBed(out Vector3 at)
        {
            at = Vector3.zero;
            var inf = Infirmary;
            if (inf == null) return false;
            int bed = Random.Range(0, inf.BedSpots.Count);
            var spot = inf.BedSpots[bed];
            LastBed = bed;
            at = spot.position;
            Teleport(spot.position + Vector3.up * 0.05f, spot.rotation);
            Player.SetFacing(spot.rotation * Vector3.forward);
            Vector3? stand = null;
            foreach (var f in inf.Furniture) if ((f.At.position - spot.position).sqrMagnitude < 0.01f) stand = f.Stand;
            StartCoroutine(LieDownNextFrame(spot, stand ?? spot.position));
            return true;
        }

        System.Collections.IEnumerator Later(float seconds, System.Action a)
        {
            yield return new WaitForSeconds(seconds);
            a();
        }

        System.Collections.IEnumerator LieDownNextFrame(Pose bed, Vector3 stand)
        {
            // asleep in the bed (a fresh scene may need a few frames)
            for (float t = 0f; t < 3f; t += Time.deltaTime)
            {
                yield return null;
                if (Player.InActivity || Player.TryActivityAt(Fighter.ActLie, bed, stand, true)) yield break;
            }
        }
        /// <summary>The homes you can walk into.</summary>
        public System.Collections.Generic.List<HouseInterior> Houses { get; private set; }

        /// <summary>Hide the "for sale" boards of the houses already bought.</summary>
        void MarkOwned()
        {
            foreach (var h in Houses)
                if (h.Lot.ForSale && h.SaleSign != null && HouseDeeds.Owns(Layout.Seed, h.Lot.Id)) h.SaleSign.SetActive(false);
        }

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
            void Try(TownDoor door)
            {
                Vector3 d = door.Center - p;
                if (Mathf.Abs(d.y) > 1.3f) return; // a door upstairs is not this one
                d.y = 0f;
                if (d.magnitude < bd) { bd = d.magnitude; best = door; }
            }
            foreach (var h in Houses)
            {
                if (h.Door != null) Try(h.Door);
                foreach (var x in h.ExtraDoors) Try(x);
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
            // the furniture follows who lives in each home (a double bed for a couple, singles for children and elders)
            var homes = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<CitizenData>>();
            foreach (var c in sim.Citizens)
            {
                if (!homes.TryGetValue(c.HomeId, out var lst)) homes[c.HomeId] = lst = new System.Collections.Generic.List<CitizenData>();
                lst.Add(c);
            }
            builder.Household = (lotId, floor) =>
            {
                if (floor > 0) return (true, 0, 2);   // the flat upstairs: a couple
                if (!homes.TryGetValue("home" + lotId, out var people) || people.Count == 0) return (true, 0, 2);
                bool man = people.Exists(c => !c.Female && c.Age >= 16), woman = people.Exists(c => c.Female && c.Age >= 16);
                bool couple = man && woman;
                int singles = Mathf.Clamp(people.Count - (couple ? 2 : 0), 0, 2);
                return (couple, singles, people.Count);
            };
            builder.Build();
            Buildings = builder.BuildingCount;
            Houses = builder.Houses;
            MarkOwned();
            // the ludus: its training yard inside the town, the gate opens onto the street (no loading, no E)
            Ludus = new GameObject("Ludus").transform;
            Ludus.SetPositionAndRotation(Layout.LudusCenter, Quaternion.LookRotation(Layout.LudusFacing));
            ludusMarkers = new GameObject("LudusMarkers").transform;
            ludusStations = Gladiator.Training.LudusInTown.BuildStructure(Ludus, ludusMarkers);
            // streets for the townsfolk: navmesh baked from the colliders just built (before any fighter exists)
            Vector3 mid = (Layout.WallCorners[0] + Layout.WallCorners[2]) * 0.5f;
            float span = Mathf.Max(TownLayout.WallX * 2f, TownLayout.WallZMax - TownLayout.WallZMin) + 10f;
            TownNavMesh.Build(mid, new Vector3(span, 40f, span), Houses);
            BuildSky(env);
            Gladiator.PS2Style.DayNightCycle.Attach(env); // dawn / day / dusk / night from the town clock, clouds
            Camera cam = BuildCamera();

            // the day starts in the ludus yard (online: side by side)
            var netS = Gladiator.Net.NetSession.Instance;
            bool multiS = netS != null && netS.InGame;
            Vector3 spawn = multiS ? Gladiator.Training.LudusInTown.Spawn(Ludus, netS.SlotOf(netS.LocalId)) : Ludus.TransformPoint(new Vector3(0f, 0.05f, -7.5f));
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
            Hud.SetHint("WASD mover  BLOQ MAYUS caminar/correr  T burlas  X acciones  E interactuar / entrenar  puerta de la arena: combate  F3 el pueblo  F4 donde esta cada uno  ESC menu");
            Training = Gladiator.Training.LudusInTown.Begin(Ludus, ludusMarkers, ludusStations, Player, pc, Hud, Cam, cam);
            Spawner = new GameObject("Citizens").AddComponent<CitizenSpawner>();
            Spawner.Init(Player);
            Guards = new GameObject("TownGuards").AddComponent<TownGuards>();
            Guards.Init(this);
            Governor = GovernorNPC.Spawn(this);
            Prisoner = JailNPC.Spawn(this);
            Carpenter = CarpenterNPC.Spawn(this);
            SpawnArmorers();
            Services = gameObject.AddComponent<TownServices>();
            TownProps.Spawn(this);
            Hud.TrackHealth(Player);
            // the furniture bought for the houses you own
            foreach (var h in Houses)
                if (h.Floor == 0 && h.Lot.ForSale && HouseDeeds.Owns(Layout.Seed, h.Lot.Id)) HomeFurniture.Apply(h, HomeFurniture.Owned(Layout.Seed, h.Lot.Id));
            Info = new GameObject("TownInfo", typeof(RectTransform)).AddComponent<TownInfoPanel>();
            Info.Init(this, cam);
            // online: the other players who are in the town too
            var net = Gladiator.Net.NetSession.Instance;
            if (net != null && net.InGame)
            {
                var nav = gameObject.AddComponent<Gladiator.Net.NetAvatars>();
                nav.SceneCode = 1;
                nav.Init(Player, cam, slot => Gladiator.Training.LudusInTown.Spawn(Ludus, slot), msg => Hud.ShowMessage(msg, 3f));
            }
            Player.SetSheathed(true, true); // in town the weapon hangs from the belt (R, or a blow, draws it)
            var tmgr = gameObject.AddComponent<TownManager>();
            tmgr.Init(this, pc);
            Owners = gameObject.AddComponent<HomeOwners>();
            Owners.Init(this, tmgr);
            Services.Init(this, tmgr, pc);
            if (Gladiator.Progression.DayCycleManager.WakeInInfirmary)
            {
                // lost in the arena: wake up in an infirmary bed (move to get up)
                Gladiator.Progression.DayCycleManager.WakeInInfirmary = false;
                if (PutInInfirmaryBed(out _))
                {
                    Player.Health.Heal(Player.Health.Max);
                    StartCoroutine(Later(1.2f, () => Hud.ShowMessage("PERDISTE EN LA ARENA: DESPIERTAS EN LA ENFERMERIA", 6f)));
                    WokeInInfirmary = true;
                }
            }
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

        // ---------------------------------------------------------------- menus opened here: the player stands still
        bool myMenu;
        static bool AnyMenu => (ChoiceMenu.Instance != null && ChoiceMenu.Instance.IsOpen) || (InventoryPanel.Instance != null && InventoryPanel.Instance.IsOpen);
        /// <summary>A menu opened by somebody else (the scribe): same rules.</summary>
        public void MenuOpenedByService() { MenuOpened(); }
        void MenuOpened() { myMenu = true; if (pc != null) pc.enabled = false; town.Player.Move(Vector3.zero); }

        // ---------------------------------------------------------------- night: houses lock their doors
        public static bool IsNight(float hour) => hour >= 22f || hour < 6f;
        public bool Night { get { var s = TownSimulation.Instance; return s != null && IsNight(s.Hour); } }

        bool Mine(HouseInterior h) => h != null && HouseDeeds.Owns(town.Layout.Seed, h.Lot.Id);

        /// <summary>From 22:00 to 6:00 a house that is not yours does not open from outside (the jail never closes).</summary>
        public bool LockedAtNight(HouseInterior h, Vector3 from)
        {
            return h != null && Night && !Mine(h) && h.CellSpots.Count == 0 && !h.Contains(from);
        }

        void OnHour(int hour)
        {
            if (hour != 22 || town == null || town.Houses == null) return;
            int closed = 0;
            foreach (var h in town.Houses)
                if (h.Door != null && h.Door.IsOpen && !Mine(h) && h.CellSpots.Count == 0) { h.Door.SetOpen(false); closed++; }
            town.Hud.ShowMessage("LAS 22:00: LAS CASAS CIERRAN SUS PUERTAS HASTA LAS 6:00", 4f);
            Debug.Log("[TOWN] 22:00 doors closed=" + closed);
        }

        // ---------------------------------------------------------------- sleeping: pick the hours, an hour a second
        float sleepLeft;
        int sleepHours;
        public bool Sleeping => sleepLeft > 0f;
        public const float SleepHealPerHour = 0.1f;

        /// <summary>Beds you may sleep in: any. In a house that is not yours the owner wakes you up when he finds you
        /// (see <see cref="HomeOwners"/>).</summary>
        public bool CanSleepIn(HouseInterior h) => h != null;
        /// <summary>Somebody else's home: sleeping there is at your own risk.</summary>
        public bool SleepIsTrespass(HouseInterior h) => town.Owners != null && town.Owners.Foreign(h);

        static string Clock(float hour) { int m = Mathf.FloorToInt(Mathf.Repeat(hour, 24f) * 60f); return (m / 60).ToString("00") + ":" + (m % 60).ToString("00"); }

        public void OpenSleepMenu()
        {
            var sim = TownSimulation.Instance;
            var opts = new System.Collections.Generic.List<ChoiceMenu.Option>();
            for (int h = 1; h <= 12; h++)
            {
                int hours = h;
                float wake = sim != null ? sim.Hour + hours : hours;
                opts.Add(new ChoiceMenu.Option
                {
                    Label = hours + (hours == 1 ? " HORA" : " HORAS"),
                    Blurb = "Despiertas a las " + Clock(wake) + (wake >= 24f ? " de manana (nuevo dia)" : "") + ".  Recuperas " + Mathf.RoundToInt(hours * SleepHealPerHour * 100f) + "% de vida.",
                    OnChoose = () => StartSleep(hours),
                });
            }
            ChoiceMenu.Ensure().Open("DORMIR: ¿CUANTAS HORAS?", opts);
            MenuOpened();
        }

        /// <summary>Sleep (lying in a bed): the clock runs an hour a second, health comes back. Online only the host's sleep moves the clock.</summary>
        public bool StartSleep(int hours)
        {
            var pl = town.Player;
            if (!pl.InActivity || pl.ActivityKind != Fighter.ActLie || !pl.OnFurniture) return false;
            var net = Gladiator.Net.NetSession.Instance;
            bool online = net != null && net.InGame;
            if (online && !net.IsHost)
            {
                town.Hud.ShowMessage("EL ANFITRION ELIGE CUANTO SE DUERME: ESPERA ACOSTADO", 4f);
                return false;
            }
            if (online)
            {
                // everybody in bed, or nobody sleeps
                BedCount(out int lying, out int total);
                if (lying < total)
                {
                    town.Hud.ShowMessage("PARA DORMIR TIENEN QUE ESTAR TODOS ACOSTADOS  (" + lying + " DE " + total + ")", 4f);
                    return false;
                }
            }
            BeginSleep(Mathf.Clamp(hours, 1, 12), true);
            if (online) net.Send("SL", sleepHours.ToString());
            return true;
        }

        // asleep from now: the host's (or the only player's) sleep runs the clock, a guest's just follows it
        void BeginSleep(int hours, bool runClock)
        {
            sleepHours = hours;
            sleepLeft = sleepHours;
            if (runClock && TownSimulation.Instance != null) TownSimulation.Instance.ClockRate = 60f; // an hour a second
            if (pc != null) pc.enabled = false;
            Debug.Log("[TOWN] sleeping " + sleepHours + "h");
        }

        static bool Online { get { var n = Gladiator.Net.NetSession.Instance; return n != null && n.InGame; } }
        static bool OnlineGuest { get { var n = Gladiator.Net.NetSession.Instance; return n != null && n.InGame && !n.IsHost; } }

        /// <summary>Lying on a bed (not getting up).</summary>
        public static bool InBed(Fighter f) => f != null && f.InActivity && f.ActivityKind == Fighter.ActLie && f.OnFurniture && !f.ActivityLeaving;

        /// <summary>Online: how many players are lying in a bed, and how many players there are (offline: you).</summary>
        public void BedCount(out int lying, out int total)
        {
            lying = InBed(town.Player) ? 1 : 0;
            total = 1;
            var net = Gladiator.Net.NetSession.Instance;
            if (net == null || !net.InGame) return;
            total = Mathf.Max(1, net.Players.Count);
            var av = Gladiator.Net.NetAvatars.Instance;
            if (av != null) foreach (var r in av.Remotes) if (r != null && r.Present && InBed(r.Fighter)) lying++;
        }

        void UpdateSleep()
        {
            var pl = town.Player;
            var sim = TownSimulation.Instance;
            float dt = Mathf.Min(Time.deltaTime, sleepLeft);
            sleepLeft -= Time.deltaTime;
            pl.Health.Heal(pl.Health.Max * SleepHealPerHour * dt);
            string key = GameInput.UsingGamepad ? "(A)" : "[" + InputBindings.Short(GameAction.Interact) + "]";
            town.Hud.SetPrompt("");
            town.Hud.SetSleep(true, "DURMIENDO...   " + (sim != null ? Clock(sim.Hour) + "   DIA " + sim.Day : "") + "\n<size=15>" + key + (Online ? " despertar a todos" : " despertar") + "</size>");
            bool wake = GameInput.InteractPressed;
            // online: one who gets up wakes everybody (a guest does it by getting out of bed)
            if (wake && OnlineGuest && pl.InActivity) pl.StopActivity();
            bool someoneUp = false;
            if (Online && !OnlineGuest) { BedCount(out int lying, out int total); someoneUp = lying < total; }
            if (sleepLeft <= 0f || wake || !pl.InActivity || someoneUp) EndSleep();
        }

        public void EndSleep()
        {
            if (sleepLeft <= 0f && sleepHours == 0) return;
            float slept = sleepHours - Mathf.Max(0f, sleepLeft);
            sleepLeft = 0f;
            sleepHours = 0;
            var sim = TownSimulation.Instance;
            if (sim != null) sim.ClockRate = 1f;
            var netE = Gladiator.Net.NetSession.Instance;
            if (netE != null && netE.InGame && netE.IsHost) netE.Send("SLE", "1"); // everybody wakes up
            town.Hud.SetSleep(false);
            if (pc != null) pc.enabled = true;
            town.Hud.ShowMessage("DESCANSASTE " + Mathf.RoundToInt(slept) + (Mathf.RoundToInt(slept) == 1 ? " HORA" : " HORAS") + (sim != null ? ":  SON LAS " + Clock(sim.Hour) + " DEL DIA " + sim.Day : ""), 5f);
        }

        // ---------------------------------------------------------------- your houses: furniture, chests, online
        /// <summary>The house the carpenter delivers a piece to: the first of yours that lacks it.</summary>
        public HouseInterior HouseFor(string itemId)
        {
            HouseInterior first = null;
            foreach (var h in town.Houses)
            {
                if (h.Floor != 0 || !h.Lot.ForSale || !Mine(h)) continue;
                if (first == null) first = h;
                var have = HomeFurniture.Owned(town.Layout.Seed, h.Lot.Id);
                if (!have.Contains(itemId) && !(itemId == "bed" && have.Contains("bed2"))) return h;
            }
            return null == first ? null : (itemId == null ? first : null);
        }

        public int HousesOwned() { int n = 0; foreach (var h in town.Houses) if (h.Floor == 0 && h.Lot.ForSale && Mine(h)) n++; return n; }

        public void OpenCarpenter()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;
            int houses = HousesOwned();
            var opts = new System.Collections.Generic.List<ChoiceMenu.Option>();
            foreach (var it in HomeFurniture.Catalog)
            {
                var item = it;
                var target = HouseFor(item.Id);
                bool afford = gm.Profile.Denarii >= item.Price;
                opts.Add(new ChoiceMenu.Option
                {
                    Label = item.Name + "   " + item.Price + " d.",
                    Enabled = houses > 0 && target != null && afford,
                    Blurb = houses == 0 ? "Primero compra una casa (las que tienen el cartel SE VENDE)."
                          : target == null ? "Ya lo tienes en tu casa."
                          : !afford ? "No te alcanza: cuesta " + item.Price + " y tienes " + gm.Profile.Denarii + " denarios."
                          : item.Blurb,
                    OnChoose = () => { BuyFurniture(item.Id); OpenCarpenter(); },
                });
            }
            ChoiceMenu.Ensure().Open("CARPINTERO   (" + gm.Profile.Denarii + " denarios)", opts);
            MenuOpened();
        }

        /// <summary>Pay the carpenter: the piece is built in your house at once (saved).</summary>
        public bool BuyFurniture(string id)
        {
            var gm = GameManager.Instance;
            var item = HomeFurniture.ById(id);
            var h = HouseFor(id);
            if (gm == null || item == null || h == null) return false;
            if (!gm.Profile.Spend(item.Price)) return false;
            HomeFurniture.Add(town.Layout.Seed, h.Lot.Id, id);
            HomeFurniture.Apply(h, HomeFurniture.Owned(town.Layout.Seed, h.Lot.Id));
            town.Hud.ShowMessage(item.Name + ": YA ESTA EN TU CASA", 3.5f);
            SendHouse(h.Lot.Id);
            return true;
        }

        public Vector3? NearestChest(HouseInterior h, Vector3 p, float range)
        {
            if (h == null) return null;
            Vector3? best = null; float bd = range;
            foreach (var c in h.Chests)
            {
                if (Mathf.Abs(c.y - p.y) > 1f) continue;
                float d = Flat(c - p);
                if (d < bd) { bd = d; best = c; }
            }
            return best;
        }

        /// <summary>The chest: everything you own.</summary>
        public void OpenInventory()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;
            var pr = gm.Profile;
            var weapons = new System.Collections.Generic.List<string>();
            var shields = new System.Collections.Generic.List<string>();
            var armour = new System.Collections.Generic.List<string>();
            foreach (var id in pr.Owned)
            {
                var w = WeaponLibrary.ByName(id);
                if (w != null) { weapons.Add(w.Name + (pr.MainHand == w.Id ? " (en la mano)" : pr.OffHand == w.Id ? " (mano izquierda)" : "")); continue; }
                var sh = ShieldLibrary.ById(id);
                if (sh != null) { shields.Add(sh.Name + (pr.ShieldId == sh.Id ? " (puesto)" : "")); continue; }
                var ap = Gladiator.Character.ArmorLibrary.ById(id);
                if (ap != null) { armour.Add(ap.Name + (System.Array.IndexOf(pr.Armor, ap.Id) >= 0 ? " (puesta)" : "")); continue; }
            }
            var homes = new System.Collections.Generic.List<string>();
            foreach (var h in town.Houses)
            {
                if (h.Floor != 0 || !h.Lot.ForSale || !Mine(h)) continue;
                var names = new System.Collections.Generic.List<string>();
                foreach (var f in HomeFurniture.Owned(town.Layout.Seed, h.Lot.Id)) names.Add(HomeFurniture.ById(f).Name.ToLowerInvariant());
                homes.Add("Casa " + h.Lot.Id + ": " + (names.Count > 0 ? string.Join(", ", names) : "sin muebles"));
            }
            var sections = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>>
            {
                new System.Collections.Generic.KeyValuePair<string, string>("DENARIOS", pr.Denarii.ToString()),
                new System.Collections.Generic.KeyValuePair<string, string>("ARMAS", string.Join(",  ", weapons)),
                new System.Collections.Generic.KeyValuePair<string, string>("ESCUDOS", string.Join(",  ", shields)),
                new System.Collections.Generic.KeyValuePair<string, string>("ARMADURA", string.Join(",  ", armour)),
                new System.Collections.Generic.KeyValuePair<string, string>("CASAS Y MUEBLES", string.Join("\n", homes)),
            };
            InventoryPanel.Ensure().Open("COFRE: TU INVENTARIO", sections);
            MenuOpened();
        }

        float houseSend;
        /// <summary>Online: tell the others this house is ours and what is in it.</summary>
        public void SendHouse(int lotId)
        {
            var s = Gladiator.Net.NetSession.Instance;
            var gm = GameManager.Instance;
            if (s == null || !s.InGame || gm == null) return;
            s.Send("HB", lotId + "|" + gm.Profile.Name.Replace("|", " ") + "|" + string.Join(",", HomeFurniture.Owned(town.Layout.Seed, lotId)));
        }

        void OnHouseNet(string payload)
        {
            var a = payload.Split('|');
            if (a.Length < 2 || !int.TryParse(a[0], out int lotId)) return;
            town.OtherOwners[lotId] = a[1];
            var h = town.HouseOfLot(lotId);
            if (h == null) return;
            if (h.SaleSign != null) h.SaleSign.SetActive(false);
            if (a.Length > 2 && a[2].Length > 0) HomeFurniture.Apply(h, a[2].Split(','));
        }

        public void Init(TownBootstrap t, PlayerController controller)
        {
            town = t; pc = controller;
            TownProp.OnBroken = PropBroken;
            if (TownSimulation.Instance != null) TownSimulation.Instance.HourChanged += OnHour;
            town.Hud.Shop.Closed += () => { if (pc != null) pc.enabled = true; };
        }

        Gladiator.Net.NetSession hooked;

        void OnEnable() { }

        void OnDestroy()
        {
            if (hooked != null) hooked.GameMessage -= OnNet;
            if (TownSimulation.Instance != null) { TownSimulation.Instance.HourChanged -= OnHour; TownSimulation.Instance.ClockRate = 1f; }
        }

        void LateUpdate()
        {
            var s = Gladiator.Net.NetSession.Instance;
            if (s != hooked) { if (hooked != null) hooked.GameMessage -= OnNet; hooked = s; if (s != null) s.GameMessage += OnNet; }
        }

        public static void SendDoor(TownDoor door)
        {
            var s = Gladiator.Net.NetSession.Instance;
            var h = door != null ? door.GetComponentInParent<HouseInterior>() : null;
            if (s == null || !s.InGame || h == null) return;
            int idx = h.Door == door ? 0 : h.ExtraDoors.IndexOf(door) + 1;
            s.Send("DR", h.Lot.Id + "|" + (door.IsOpen ? 1 : 0) + "|" + h.Floor + "|" + idx);
        }

        /// <summary>Another player opened or closed a door in the same town.</summary>
        void OnNet(int from, string type, string payload)
        {
            if (type == "HB" && town != null && town.Houses != null) { OnHouseNet(payload); return; }
            if (type == "PB")
            {
                // another player smashed that crate or jar
                if (int.TryParse(payload, out int pid)) { var bp = TownProp.Find(pid); if (bp != null) bp.Break(null, true); }
                return;
            }
            if (type == "SL" || type == "SLE")
            {
                // the host put everybody to sleep / woke everybody up
                var ns = Gladiator.Net.NetSession.Instance;
                var sender = ns != null ? ns.Get(from) : null;
                if (town == null || ns == null || ns.IsHost || sender == null || !sender.Host) return;
                if (type == "SLE") { if (Sleeping) EndSleep(); return; }
                if (int.TryParse(payload, out int hrs) && InBed(town.Player) && !Sleeping) BeginSleep(Mathf.Clamp(hrs, 1, 12), false);
                return;
            }
            if (type != "DR" || town == null || town.Houses == null) return;
            var a = payload.Split('|');
            if (a.Length < 2 || !int.TryParse(a[0], out int lotId)) return;
            int floor = a.Length > 2 && int.TryParse(a[2], out int fl) ? fl : 0;
            int idx = a.Length > 3 && int.TryParse(a[3], out int di) ? di : 0;
            foreach (var h in town.Houses)
                if (h.Lot.Id == lotId && h.Floor == floor)
                {
                    var door = idx == 0 ? h.Door : idx - 1 < h.ExtraDoors.Count ? h.ExtraDoors[idx - 1] : null;
                    if (door != null) { door.SetOpen(a[1] == "1"); break; }
                }
        }

        /// <summary>The nearest smithy whose armourer (at his anvil) is within a range.</summary>
        public TownLot NearestSmithy(Vector3 p, float range)
        {
            TownLot best = null; float bd = range;
            foreach (var lot in town.Layout.Lots)
            {
                if (lot.Kind != BuildingKind.Smithy) continue;
                Vector3 d = lot.ArmorerSpot - p; d.y = 0f;
                if (d.magnitude < bd) { bd = d.magnitude; best = lot; }
            }
            return best;
        }

        /// <summary>A crate or a jar was smashed: now and then there were a few coins in it; online the others see it break.</summary>
        void PropBroken(TownProp prop, Fighter by, bool fromNet)
        {
            if (fromNet) return;
            var net = Gladiator.Net.NetSession.Instance;
            if (net != null && net.InGame) net.Send("PB", prop.Id.ToString());
            if (town == null || by == null || by != town.Player) return;
            var gm = GameManager.Instance;
            if (gm == null || Random.value > 0.3f) return;
            int coins = Random.Range(1, 6);
            gm.Profile.AddDenarii(coins);
            town.Hud.ShowMessage("HABIA " + coins + (coins == 1 ? " DENARIO" : " DENARIOS") + " DENTRO", 2.5f);
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

        /// <summary>A cell gate and the prisoner both in reach: the nearer wins.</summary>
        public bool PrisonerFirst(TownDoor door, Vector3 p)
        {
            var pr = town.Prisoner;
            if (pr == null || pr.Fighter == null || door.Noun != "LA CELDA") return false;
            float dp = Flat(pr.Fighter.transform.position - p);
            return dp < 2.4f && dp < Flat(door.Center - p);
        }

        /// <summary>Lie down on the bed / sit on the chair / kneel at the altar (public for tests).</summary>
        public bool UseFurniture(FurnitureSpot fs)
        {
            var pl = town.Player;
            Vector3 stand = fs.Stand ?? pl.transform.position;
            bool ok = pl.TryActivityAt(fs.Kind, fs.At, stand);
            if (ok && fs.Kind == Fighter.ActPray) town.Hud.ShowMessage("REZAS A LOS DIOSES...", 2.5f);
            return ok;
        }

        void Update()
        {
            GameInput.UpdateDeviceMode();
            if (town == null || leaving) return;
            if (ArenaGateMenu.Instance != null && ArenaGateMenu.Instance.IsOpen) { town.Hud.SetPrompt(""); return; }
            if (town.Hud.Shop.IsOpen) { town.Hud.SetPrompt(""); return; } // the shop has its own keys (Esc closes)
            string key = GameInput.UsingGamepad ? "(A) " : "[" + InputBindings.Short(GameAction.Interact) + "] ";
            Vector3 p = town.Player.transform.position;
            // a menu opened here (sleep, carpenter, chest): nothing else until it closes
            if (myMenu)
            {
                if (AnyMenu) { town.Hud.SetPrompt(""); return; }
                myMenu = false;
                if (pc != null && !Sleeping) pc.enabled = true;
                return; // the key that closed it does nothing else this frame
            }
            if (Sleeping) { UpdateSleep(); return; }
            // online: the others learn (and are reminded) which houses are ours
            if ((houseSend -= Time.unscaledDeltaTime) <= 0f)
            {
                houseSend = 6f;
                var ns = Gladiator.Net.NetSession.Instance;
                if (ns != null && ns.InGame)
                    foreach (var oh in town.Houses) if (oh.Floor == 0 && oh.Lot.ForSale && Mine(oh)) SendHouse(oh.Lot.Id);
            }
            if (town.InLudus(p)) return; // in the ludus yard: the training manager has the prompts
            // inside a house, or at its door: its roof and ceiling go away so the camera sees in; say which room
            var inHouse = town.HouseAt(p);
            if (inHouse == null)
            {
                var atDoor = town.NearestDoor(p, 2.6f);
                if (atDoor != null) inHouse = atDoor.GetComponentInParent<HouseInterior>();
            }
            if (inHouse != house)
            {
                if (house != null) { house.ShowUpper(true); house.SetLights(false); }
                house = inHouse;
                if (house != null) { house.ShowUpper(false); house.SetLights(true); }
                room = null;
            }
            if (house != null)
            {
                string rn = house.RoomAt(p); // null at the door, outside
                if (rn != null && rn != room) { room = rn; town.Hud.ShowMessage(rn == "BANO" ? "BAÑO" : rn == "HABITACION" ? "HABITACIÓN" : rn, 1.6f); }
            }
            // on a bed, a chair, at the altar: E gets up
            var pl = town.Player;
            if (pl.KnockedOut) { town.Hud.SetPrompt(""); return; } // out cold: nothing to do
            if (pl.Carried != null)
            {
                // arms full: put it down or throw it
                town.Hud.SetPrompt(key + "SOLTAR " + pl.Carried.Noun + "      ATACAR: LANZAR");
                if (GameInput.InteractPressed) pl.Carried.Drop();
                return;
            }
            // the baths and the basilica (bathing, the scribe's counter)
            if (town.Services != null && town.Services.Handle(p, key)) return;
            if (pl.InActivity && pl.OnFurniture)
            {
                // in a bed you may sleep in: E picks the hours; moving gets you up
                bool bed = pl.ActivityKind == Fighter.ActLie && CanSleepIn(house ?? town.HouseAt(p));
                bool notMine = bed && SleepIsTrespass(house ?? town.HouseAt(p));
                if (bed && Online && !pl.ActivityLeaving)
                {
                    // online: everybody has to be in bed, and the host says for how long
                    BedCount(out int lying, out int total);
                    bool all = lying >= total;
                    if (OnlineGuest)
                    {
                        town.Hud.SetPrompt((all ? "TODOS ACOSTADOS: EL ANFITRION ELIGE CUANTO DORMIR" : "ACOSTADOS " + lying + " DE " + total + ": FALTAN JUGADORES") + "      " + key + "LEVANTARSE");
                        if (GameInput.InteractPressed) pl.StopActivity();
                    }
                    else
                    {
                        town.Hud.SetPrompt((all ? key + "DORMIR (ELEGIR HORAS)" : "ACOSTADOS " + lying + " DE " + total + ": FALTAN JUGADORES") + "      MOVERSE: LEVANTARSE");
                        if (GameInput.InteractPressed)
                        {
                            if (all) OpenSleepMenu();
                            else town.Hud.ShowMessage("PARA DORMIR TIENEN QUE ESTAR TODOS ACOSTADOS  (" + lying + " DE " + total + ")", 4f);
                        }
                    }
                    return;
                }
                town.Hud.SetPrompt(pl.ActivityLeaving ? "" : bed ? key + (notMine ? "DORMIR EN CAMA AJENA (ELEGIR HORAS)" : "DORMIR (ELEGIR HORAS)") + "      MOVERSE: LEVANTARSE" : key + "LEVANTARSE");
                if (GameInput.InteractPressed && !pl.ActivityLeaving) { if (bed) OpenSleepMenu(); else pl.StopActivity(); }
                return;
            }
            // a chest of yours: your inventory
            if (NearestChest(house, p, 1.3f).HasValue && pl.State == FighterState.Idle)
            {
                town.Hud.SetPrompt(key + "ABRIR EL COFRE (INVENTARIO)");
                if (GameInput.InteractPressed) OpenInventory();
                return;
            }
            // a bed, a chair, the temple's altar: E to lie down, sit, pray
            var furn = house != null && pl.State == FighterState.Idle ? house.NearestFurniture(p, 1.25f, pl) : null;
            if (furn.HasValue && town.NearestDoor(p, 1.2f) == null)
            {
                var fs = furn.Value;
                if (HouseInterior.Occupied(fs, pl)) { town.Hud.SetPrompt(fs.Kind == Fighter.ActLie ? "CAMA OCUPADA" : "OCUPADO"); return; }
                town.Hud.SetPrompt(key + fs.Verb);
                if (GameInput.InteractPressed) UseFurniture(fs);
                return;
            }
            // the smithies: weapons, armour and shields
            var smithy = NearestSmithy(p, 2.5f); // in front of the armourer at his table
            if (smithy != null)
            {
                var gmS = GameManager.Instance;
                town.Hud.SetPrompt(key + "ARMERO: ARMAS, ARMADURAS Y ESCUDOS   (" + (gmS != null ? gmS.Profile.Denarii : 0) + " denarios)");
                if (GameInput.InteractPressed) OpenShop();
                return;
            }
            var door = town.NearestDoor(p, 1.7f);
            if (door != null && PrisonerFirst(door, p)) door = null; // at the bars in front of the prisoner: talk to him
            var dh = door != null && door.Noun == "LA PUERTA" ? door.GetComponentInParent<HouseInterior>() : null;
            if (dh != null && dh.Lot.ForSale && !HouseDeeds.Owns(town.Layout.Seed, dh.Lot.Id) && !town.OtherOwners.ContainsKey(dh.Lot.Id))
            {
                var gmB = GameManager.Instance;
                int money = gmB != null ? gmB.Profile.Denarii : 0;
                town.Hud.SetPrompt(key + "COMPRAR ESTA CASA: " + dh.Lot.Price + " DENARIOS   (tienes " + money + ")");
                if (GameInput.InteractPressed) TryBuy(dh);
                return;
            }
            if (door != null)
            {
                if (LockedAtNight(dh, p)) { town.Hud.SetPrompt("CERRADO DE NOCHE: ABREN A LAS 6:00"); return; }
                bool mine = Mine(dh) && dh.Lot.ForSale;
                string whose = mine ? "   (TU CASA)" : dh != null && town.OtherOwners.TryGetValue(dh.Lot.Id, out string ow) ? "   (CASA DE " + ow.ToUpperInvariant() + ")" : "";
                town.Hud.SetPrompt(key + (door.IsOpen ? "CERRAR " : "ABRIR ") + door.Noun + whose);
                if (GameInput.InteractPressed) { door.Toggle(); SendDoor(door); }
                return;
            }
            var gov = town.Governor;
            if (gov != null && gov.Fighter != null && Flat(gov.Fighter.transform.position - p) < 2.4f)
            {
                town.Hud.SetPrompt(key + "HABLAR CON EL GOBERNADOR " + gov.Name.ToUpperInvariant());
                if (GameInput.InteractPressed) town.Hud.ShowMessage(gov.Talk(town.Player), 5.5f);
                return;
            }
            var carp = town.Carpenter;
            if (carp != null && carp.Fighter != null && Flat(carp.Fighter.transform.position - p) < 2.6f)
            {
                town.Hud.SetPrompt(key + "CARPINTERO: MUEBLES PARA TU CASA");
                if (GameInput.InteractPressed) OpenCarpenter();
                return;
            }
            var pr = town.Prisoner;
            if (pr != null && pr.Fighter != null && Flat(pr.Fighter.transform.position - p) < 2.4f)
            {
                town.Hud.SetPrompt(key + "HABLAR CON EL PRESO");
                if (GameInput.InteractPressed) town.Hud.ShowMessage(pr.Talk(), 4.5f);
                return;
            }
            // a crate or a jar right in front: pick it up
            var nearProp = TownProp.Nearest(p + pl.transform.forward * 0.45f, 0.75f);
            if (nearProp != null && (pl.State == FighterState.Idle || pl.State == FighterState.Blocking))
            {
                town.Hud.SetPrompt(key + "LEVANTAR " + nearProp.Noun + "      GOLPEAR: ROMPER");
                if (GameInput.InteractPressed) nearProp.PickUp(pl);
                return;
            }
            var npc = town.Spawner != null ? town.Spawner.Nearest(p, 2.3f) : null;
            if (npc != null && npc.Controlled) npc = null; // a guard warning or fighting you does not chat
            if (npc != null && npc.Citizen != null && !npc.Fleeing)
            {
                town.Hud.SetPrompt(key + "HABLAR CON " + npc.Citizen.Name.ToUpperInvariant() + " (" + npc.Citizen.JobName + ")");
                if (GameInput.InteractPressed) TalkTo(npc);
                return;
            }
            if (Flat(town.Layout.ArenaGate - p) < GateRadius)
            {
                var gmA = GameManager.Instance;
                if (gmA != null && gmA.DayCycle.FoughtToday)
                {
                    town.Hud.SetPrompt("YA COMBATISTE HOY: DUERME EN UNA CAMA PARA PASAR EL DIA");
                    return;
                }
                if (!Gladiator.Progression.DayCycleManager.ArenaOpenNow)
                {
                    town.Hud.SetPrompt("LA ARENA ABRE DE 8:00 A 20:00");
                    return;
                }
                town.Hud.SetPrompt(key + "PUERTA DE LA ARENA: ELEGIR COMBATE");
                if (GameInput.InteractPressed) OpenArenaGate();
                return;
            }
            town.Hud.SetPrompt(town.Services != null ? town.Services.IdlePrompt : "");
        }

        /// <summary>Buy a house for sale: pay its price, it is yours (saved), its door opens.</summary>
        public bool TryBuy(HouseInterior h)
        {
            var gm = GameManager.Instance;
            if (gm == null || h == null || !h.Lot.ForSale || HouseDeeds.Owns(town.Layout.Seed, h.Lot.Id) || town.OtherOwners.ContainsKey(h.Lot.Id)) return false;
            if (!gm.Profile.Spend(h.Lot.Price))
            {
                town.Hud.ShowMessage("NO TE ALCANZA: CUESTA " + h.Lot.Price + " DENARIOS Y TIENES " + gm.Profile.Denarii, 3f);
                return false;
            }
            HouseDeeds.Add(town.Layout.Seed, h.Lot.Id);
            if (h.SaleSign != null) h.SaleSign.SetActive(false);
            h.Door.SetOpen(true);
            SendDoor(h.Door);
            town.Hud.ShowMessage("¡CASA COMPRADA!  YA ES TUYA.  EL CARPINTERO DE LA PLAZA VENDE LOS MUEBLES", 5f);
            SendHouse(h.Lot.Id);
            Gladiator.Audio.AudioManager.UI("ui_select");
            return true;
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

        /// <summary>The ludus is part of the town now: just walks the player into the yard (tests, menus).</summary>
        public void GoToLudus()
        {
            var cc = town.Player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            town.Player.transform.SetPositionAndRotation(town.Ludus.TransformPoint(new Vector3(0f, 0.05f, -7.5f)), town.Ludus.rotation);
            if (cc != null) cc.enabled = true;
            if (town.Cam != null) town.Cam.SnapToTarget();
        }

        public void OpenArenaGate()
        {
            var gm = GameManager.Instance;
            if (gm == null || gm.DayCycle.FoughtToday || !Gladiator.Progression.DayCycleManager.ArenaOpenNow) return;
            ArenaGateMenu.Ensure().Open(gm.DayCycle.Day, mode => { leaving = true; gm.DayCycle.GoToArena(mode); });
        }

        static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }
    }
}
