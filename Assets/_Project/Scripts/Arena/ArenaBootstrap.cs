using System.Collections.Generic;
using Gladiator.Combat;
using Gladiator.Core;
using Gladiator.PS2Style;
using Gladiator.Progression;
using Gladiator.UI;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gladiator.Arena
{
    /// <summary>
    /// Builds the whole Arena scene at runtime from procedural low-poly geometry:
    /// sand floor, stone wall, stepped stands with a vertex-coloured crowd, columns, torches,
    /// sky dome, fog, camera + PS2 output, the gladiators and the HUD.
    /// Everything uses PS2_Lit and 64-128px procedural textures.
    /// </summary>
    public class ArenaBootstrap : MonoBehaviour
    {
        [Header("Shaders (assigned by the scene builder so they're included in builds)")]
        public Shader PS2LitShader;
        public Shader PS2PostShader;

        [Header("Encounter")]
        public EncounterType Encounter = EncounterType.Duel;
        [Tooltip("Take the encounter chosen at the ludus gate (off = use the field above).")]
        public bool UseDayCycleEncounter = true;
        public float ArenaRadius = 14f;
        public int Seed = 0;

        public static readonly Color FogColor = new Color(0.80f, 0.68f, 0.54f);
        public static readonly Color SkyTop = new Color(0.40f, 0.52f, 0.68f);
        static readonly Color AmbientSky = new Color(0.48f, 0.46f, 0.52f);
        static readonly Color AmbientGround = new Color(0.30f, 0.22f, 0.15f);
        const int Segments = 32;
        static readonly string[] OpponentNames = { "Murmillo", "Tracio", "Secutor", "Hoplomachus", "Dimachaerus" };
        static readonly Color[] EnemyTunics = { new Color(0.78f, 0.12f, 0.1f), new Color(0.2f, 0.32f, 0.62f), new Color(0.25f, 0.5f, 0.2f) };

        public Fighter Player { get; private set; }
        public List<Fighter> Enemies { get; } = new List<Fighter>();
        public ArenaManager Manager { get; private set; }
        public Weapon SpareWeapon { get; private set; }
        /// <summary>Weapons lying by the rack at the start (autotest).</summary>
        public List<Weapon> RackWeapons { get; } = new List<Weapon>();
        /// <summary>"Name: weapon + second weapon/shield" per opponent (autotest log).</summary>
        public List<string> EnemyLoadouts { get; } = new List<string>();

        /// <summary>Online arena (MP2): the players fight each other, no AI rivals, no rewards.</summary>
        public bool Online { get; private set; }
        /// <summary>Online co-op (MP3): the players together against AI rivals, which only the host's game runs.</summary>
        public bool OnlineCoop { get; private set; }
        /// <summary>The body each AI rival was built with (the host sends it to the others).</summary>
        public readonly Dictionary<Fighter, Gladiator.Character.CharacterAppearance> BotLooks = new Dictionary<Fighter, Gladiator.Character.CharacterAppearance>();
        /// <summary>Wave sizes are multiplied by this (online co-op: more players, more rivals).</summary>
        public float WaveScale = 1f;

        /// <summary>
        /// The body a player fights with online: their gladiator in a tunic of their colour; with upgrades their own
        /// armour, without them no armour (everybody equal).
        /// </summary>
        public static (Gladiator.Character.CharacterAppearance look, Gladiator.Character.ArmorSet armor) OnlineLook(
            Gladiator.Character.CharacterAppearance baseLook, Gladiator.Character.ArmorSet armor, int color, bool upgrades)
        {
            var a = JsonUtility.FromJson<Gladiator.Character.CharacterAppearance>(JsonUtility.ToJson(baseLook));
            a.Name = baseLook.Name;
            Color c = Gladiator.Net.NetSession.TeamColors[Mathf.Clamp(color, 0, Gladiator.Net.NetSession.TeamColors.Length - 1)];
            a.ClothOverride = c;
            a.CrestOverride = c;
            if (a.Outfit == 0) a.Outfit = 1; // a tunic, so the colour shows
            return (a, upgrades ? armor : Gladiator.Character.ArmorSet.Empty);
        }

        void Awake()
        {
            Time.timeScale = 1f;
            var gm = GameManager.Ensure();
            var net = Gladiator.Net.NetSession.Instance;
            Online = net != null && net.InGame && net.Phase == Gladiator.Net.NetSession.PhaseArena;
            OnlineCoop = Online && net.ArenaCoop;
            if (!Online) gm.DayCycle.NotifySceneStarted(DayPhase.Arena);
            if (Seed != 0) Random.InitState(Seed);
            PS2Materials.Init(PS2LitShader);
            SetupRenderSettings();

            var env = new GameObject("Environment");
            BuildLighting(env.transform);
            BuildArena(env.transform);
            BuildObstacles(env.transform);
            BuildSky(env.transform);
            Gladiator.PS2Style.DayNightCycle.Attach(env.transform); // dawn / day / dusk / night from the town clock, clouds
            Camera cam = BuildCamera();

            // ----- fighters
            var profile = gm.Profile;
            int day = gm.DayCycle.Day;
            if (UseDayCycleEncounter) Encounter = gm.DayCycle.NextEncounter;
            bool ffa = Encounter == EncounterType.FreeForAll;

            if (Online)
            {
                // our gladiator in our colour, at our place around the sand; with upgrades our gear and stats, else a gladius
                var me = net.Local;
                int slot = net.SlotOf(net.LocalId);
                var lk = OnlineLook(profile.Appearance, profile.ArmorSet, me != null ? me.Color : 0, net.ArenaUpgrades);
                Vector3 at = Gladiator.Net.NetArena.SpawnFor(slot, net.Players.Count, OnlineCoop);
                Player = CharacterFactory.Create(profile.Name, lk.look, at, Quaternion.LookRotation(-new Vector3(at.x, 0f, at.z)), 0, lk.armor);
                Player.TeamId = me != null ? Gladiator.Net.NetArena.TeamOf(net, me) : 100;
            }
            else
                Player = CharacterFactory.Create(profile.Name, profile.Appearance,
                    new Vector3(0, 0.05f, ffa ? -7f : -5f), Quaternion.identity, 0, profile.ArmorSet);
            if (!Online || net.ArenaUpgrades)
            {
                // Loadout chosen at the mercator: right hand, then a second weapon or a shield.
                Player.Equip(WeaponFactory.Create(WeaponLibrary.ByName(profile.MainHand) ?? WeaponLibrary.Gladius));
                var off = WeaponLibrary.ByName(profile.OffHand);
                if (off != null) Player.EquipOff(WeaponFactory.Create(off));
                StatEffects.ApplyToPlayer(Player, profile);
                var playerShield = ShieldLibrary.ById(profile.ShieldId);
                if (playerShield != null) Shield.Attach(Player, playerShield, profile.Appearance.ShieldTint);
            }
            else Player.Equip(WeaponFactory.Create(WeaponLibrary.Gladius)); // all equal: a gladius, base stats
            var pc = Player.gameObject.AddComponent<PlayerController>();
            pc.AimCamera = cam;

            day0 = day;
            switch (Online ? (EncounterType)(-1) : Encounter)
            {
                case EncounterType.FreeForAll:
                    // The other three corners of a square (player at the south one); every gladiator its own team.
                    for (int i = 0; i < 3; i++)
                    {
                        float ang = i * 90f * Mathf.Deg2Rad;
                        SpawnEnemy(new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * 7f, i + 1, true);
                    }
                    break;
                case EncounterType.TwoVsOne:
                    SpawnEnemy(new Vector3(-2.2f, 0, 5.5f), 1, true);
                    SpawnEnemy(new Vector3(2.2f, 0, 5.5f), 1, true);
                    break;
                case EncounterType.Champion:
                    SpawnChampion(new Vector3(0, 0, 6f));
                    break;
                case EncounterType.Waves:
                    SpawnWave(1);
                    break;
                case (EncounterType)(-1):
                    if (OnlineCoop && net.IsHost) SpawnCoop(net); // online co-op: only the host runs the rivals
                    break; // online PvP: the rivals are the other players
                default:
                    SpawnEnemy(new Vector3(0, 0, 5f), 1, false);
                    break;
            }

            // Armamentarium: a rack by the west wall with three weapons in the sand (the first one is always a spatha,
            // the other two random from the whole arsenal).
            BuildRack(env.transform);
            SpareWeapon = WeaponFactory.Create(WeaponLibrary.Spatha);
            SpareWeapon.transform.SetPositionAndRotation(new Vector3(-6f, 0.4f, -2f), Quaternion.Euler(0, 70f, 0));
            SpareWeapon.Drop(Vector3.zero);
            RackWeapons.Add(SpareWeapon);
            var arsenal = WeaponLibrary.Arsenal;
            Vector3[] spots = { new Vector3(-6.7f, 0.4f, -0.6f), new Vector3(-5.2f, 0.4f, -3.3f) };
            foreach (var spot in spots)
            {
                var w = WeaponFactory.Create(arsenal[Random.Range(0, arsenal.Length)]);
                w.transform.SetPositionAndRotation(spot, Quaternion.Euler(0, Random.Range(0f, 360f), 0));
                w.Drop(Vector3.zero);
                RackWeapons.Add(w);
            }

            var camCtl = cam.GetComponent<ArenaCamera>();
            camCtl.Player = Player;
            camCtl.SnapToTarget();

            // ----- HUD + rules
            var hud = new GameObject("HUD", typeof(RectTransform)).AddComponent<GameHUD>();
            hud.Init(cam, Player, pc, Enemies);
            Manager = new GameObject("ArenaManager").AddComponent<ArenaManager>();
            Manager.Online = Online;
            Manager.Begin(Encounter, Player, Enemies, hud, this);
            if (Online)
            {
                // the other players: copies in their colours and teams; the blows go to their owners (NetArena)
                var nav = gameObject.AddComponent<Gladiator.Net.NetAvatars>();
                nav.SceneCode = 2;
                nav.TeamFor = pl => Gladiator.Net.NetArena.TeamOf(net, pl);
                nav.LookFor = pl => OnlineLook(pl.G.Appearance(), pl.G.ArmorSet, pl.Color, net.ArenaUpgrades);
                nav.Init(Player, cam, slotIdx => Gladiator.Net.NetArena.SpawnFor(slotIdx, net.Players.Count, OnlineCoop), msg => hud.ShowMessage(msg, 2.5f));
                gameObject.AddComponent<Gladiator.Net.NetArena>().Init(Player, nav, hud, this);
            }
            else
                new GameObject("TownCrowd").AddComponent<Gladiator.Town.ArenaCrowdManager>().Init(Manager, hud);

            if (!Online && System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-autotest") >= 0)
                gameObject.AddComponent<AutoTestRunner>().Init(this, pc);
        }

        // ------------------------------------------------------------------ setup
        void SetupRenderSettings()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = FogColor;
            RenderSettings.fogStartDistance = 16f;
            RenderSettings.fogEndDistance = 52f;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = AmbientSky;
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
            cam.farClipPlane = 300f;
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
            sunGo.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.9f, 0.74f);
            sun.intensity = 0.85f;
            sun.shadows = LightShadows.None;
            RenderSettings.sun = sun;

            // Torches around the wall: point lights evaluated per vertex.
            Material wood = PS2Materials.Get("wood", PS2Textures.Wood, Color.white);
            Material iron = PS2Materials.Get("iron", PS2Textures.Metal, new Color(0.45f, 0.42f, 0.4f));
            Material flame = PS2Materials.Flat(new Color(1f, 0.62f, 0.15f), 0.3f);
            Material flameCore = PS2Materials.Flat(new Color(1f, 0.92f, 0.55f), 0.3f);
            const int torches = 6;
            for (int i = 0; i < torches; i++)
            {
                float a = (i + 0.5f) / torches * Mathf.PI * 2f;
                Vector3 pos = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (ArenaRadius - 0.5f);
                var t = new GameObject("Torch" + i).transform;
                t.SetParent(parent, false);
                t.position = pos;
                Spawn(t, "Pole", LowPolyMesh.Cylinder(0.07f, 2.3f, 6), Vector3.zero, wood);
                Spawn(t, "Bowl", LowPolyMesh.Cylinder(0.22f, 0.18f, 6), new Vector3(0, 2.3f, 0), iron);
                var flameRoot = new GameObject("Flame").transform;
                flameRoot.SetParent(t, false);
                flameRoot.localPosition = new Vector3(0, 2.45f, 0);
                Spawn(flameRoot, "F1", LowPolyMesh.Box(new Vector3(0.24f, 0.4f, 0.24f), new Vector3(0, 0.2f, 0)), Vector3.zero, flame).transform.localRotation = Quaternion.Euler(0, 45f, 0);
                Spawn(flameRoot, "F2", LowPolyMesh.Box(new Vector3(0.12f, 0.3f, 0.12f), new Vector3(0, 0.15f, 0)), Vector3.zero, flameCore);

                var lgo = new GameObject("TorchLight");
                lgo.transform.SetParent(t, false);
                lgo.transform.localPosition = new Vector3(0, 2.8f, 0) - pos.normalized * 0.6f;
                var l = lgo.AddComponent<Light>();
                l.type = LightType.Point;
                l.range = 9f;
                l.color = new Color(1f, 0.55f, 0.22f);
                l.intensity = 2.2f;
                l.shadows = LightShadows.None;
                var fl = t.gameObject.AddComponent<TorchFlicker>();
                fl.Light = l; fl.Flame = flameRoot; fl.BaseIntensity = l.intensity;
                LowPolyMesh.BakeAmbient(t.Find("Pole").gameObject, AmbientSky, AmbientGround);
                LowPolyMesh.BakeAmbient(t.Find("Bowl").gameObject, AmbientSky, AmbientGround);
            }
        }

        void BuildArena(Transform parent)
        {
            float R = ArenaRadius;
            Material sand = PS2Materials.Get("sand", PS2Textures.Sand, Color.white);
            Material stone = PS2Materials.Get("stone", PS2Textures.Stone, Color.white);
            Material stoneDark = PS2Materials.Get("stone_dark", PS2Textures.Stone, new Color(0.72f, 0.66f, 0.6f));
            Material banner = PS2Materials.Get("banner", PS2Textures.Cloth, new Color(0.62f, 0.1f, 0.08f));
            Material crowdMat = PS2Materials.Get("crowd", PS2Textures.Cloth, Color.white);
            Material gateMat = PS2Materials.Get("gate", PS2Textures.Wood, new Color(0.55f, 0.5f, 0.45f));

            // Floor: subdivided polar disc so torches light it per vertex; darkened near the wall (baked AO).
            var floorB = new MeshBuilder();
            floorB.AddPolarDisc(R, 8, Segments, 0f, Color.white, 1f / 3.5f);
            Mesh floorMesh = floorB.Build("ArenaFloor");
            var fcols = floorMesh.colors;
            var fverts = floorMesh.vertices;
            for (int i = 0; i < fverts.Length; i++)
            {
                float r = new Vector2(fverts[i].x, fverts[i].z).magnitude / R;
                float ao = Mathf.Lerp(1f, 0.55f, Mathf.SmoothStep(0.7f, 1f, r));
                fcols[i] = new Color(ao, ao, ao, 1f);
            }
            floorMesh.colors = fcols;
            var floor = Spawn(parent, "Floor", floorMesh, Vector3.zero, sand);

            // Wall (inward facing) + top + red banner band.
            var wallB = new MeshBuilder();
            float circumference = 2f * Mathf.PI * R;
            wallB.AddLathe(new[] { new MeshBuilder.Ring(R, 0f, 1, 0), new MeshBuilder.Ring(R, 1.5f, 1, 0), new MeshBuilder.Ring(R, 3f, 1, 0) },
                           Segments, Vector3.zero, Color.white, true, circumference / 3f, 1f / 3f);
            wallB.AddAnnulus(R, R + 0.7f, 3f, Segments, Color.white, 1f / 3f);
            var wall = Spawn(parent, "Wall", wallB.Build("ArenaWall"), Vector3.zero, stone);

            var bannerB = new MeshBuilder();
            bannerB.AddLathe(new[] { new MeshBuilder.Ring(R - 0.03f, 2.2f, 1, 0), new MeshBuilder.Ring(R - 0.03f, 2.75f, 1, 0) },
                             Segments, Vector3.zero, Color.white, true, Segments, 0f);
            var bannerGo = Spawn(parent, "BannerBand", bannerB.Build("Banner"), Vector3.zero, banner);

            // Townsfolk who come to watch (phase 4): their seats are planned first so the painted crowd leaves them free.
            if (Online) Gladiator.Town.ArenaCrowdManager.ClearPlan(); // online: just the painted crowd
            else Gladiator.Town.ArenaCrowdManager.Plan(R);

            // Stepped stands + crowd (one combined mesh, colours baked per vertex).
            var standB = new MeshBuilder();
            var crowdB = new MeshBuilder();
            float rr = R + 0.7f, y = 3f;
            Color[] clothes = {
                new Color(0.75f, 0.7f, 0.6f), new Color(0.55f, 0.18f, 0.12f), new Color(0.3f, 0.35f, 0.55f),
                new Color(0.85f, 0.82f, 0.75f), new Color(0.45f, 0.4f, 0.25f), new Color(0.6f, 0.45f, 0.3f)
            };
            for (int k = 0; k < 7; k++)
            {
                float y2 = y + 1.1f;
                standB.AddLathe(new[] { new MeshBuilder.Ring(rr, y, 1, 0), new MeshBuilder.Ring(rr, y2, 1, 0) },
                                Segments + k * 4, Vector3.zero, Color.white, true, (2f * Mathf.PI * rr) / 3f, 1f / 3f);
                standB.AddAnnulus(rr, rr + 1.6f, y2, Segments + k * 4, Color.white, 1f / 3f);

                int people = 44 + k * 8;
                for (int p = 0; p < people; p++)
                {
                    if (Random.value < 0.18f) continue;
                    float a = (p + Random.Range(-0.25f, 0.25f)) / people * Mathf.PI * 2f;
                    // Leave gaps behind the two gates.
                    float deg = a * Mathf.Rad2Deg % 360f;
                    if (Mathf.Abs(deg - 90f) < 8f || Mathf.Abs(deg - 270f) < 8f) continue;
                    if (Gladiator.Town.ArenaCrowdManager.Reserved(k, a, rr + 0.8f)) continue; // a real townsperson sits here
                    Vector3 d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    Vector3 c = d * (rr + 0.8f) + Vector3.up * (y2 + 0.35f);
                    Quaternion face = Quaternion.LookRotation(-d);
                    Color cloth = clothes[Random.Range(0, clothes.Length)] * Random.Range(0.8f, 1.1f);
                    cloth.a = 1f;
                    crowdB.AddBox(c, new Vector3(0.38f, 0.7f, 0.3f), face, cloth);
                    crowdB.AddBox(c + Vector3.up * 0.47f, new Vector3(0.2f, 0.22f, 0.2f), face, new Color(0.8f, 0.6f, 0.45f));
                }
                rr += 1.6f;
                y = y2;
            }
            var stands = Spawn(parent, "Stands", standB.Build("Stands"), Vector3.zero, stoneDark);
            var crowd = Spawn(parent, "Crowd", crowdB.Build("Crowd"), Vector3.zero, crowdMat);
            crowd.AddComponent<CrowdBob>();

            // Columns ring on the top tier.
            var cols = new GameObject("Columns");
            cols.transform.SetParent(parent, false);
            const int columnCount = 20;
            Mesh colMesh = LowPolyMesh.Cylinder(0.45f, 6f, 8, true, false, 2f, 1f / 3f);
            for (int i = 0; i < columnCount; i++)
            {
                float a = i / (float)columnCount * Mathf.PI * 2f;
                Vector3 p = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (rr + 0.6f) + Vector3.up * y;
                Spawn(cols.transform, "Column" + i, Object.Instantiate(colMesh), p, stone);
            }
            var lintelB = new MeshBuilder();
            lintelB.AddLathe(new[] { new MeshBuilder.Ring(rr - 0.1f, y + 6f, 1, 0), new MeshBuilder.Ring(rr - 0.1f, y + 6.8f, 1, 0) },
                             columnCount * 2, Vector3.zero, Color.white, true, columnCount * 2, 0f);
            Spawn(cols.transform, "Lintel", lintelB.Build("Lintel"), Vector3.zero, stone);

            // Gates (decorative) north & south.
            foreach (float z in new[] { R - 0.05f, -(R - 0.05f) })
            {
                var g = Spawn(parent, "Gate", LowPolyMesh.Box(new Vector3(3.2f, 2.7f, 0.3f), Vector3.zero, 0.8f), new Vector3(0, 1.35f, z), gateMat);
                for (int b = -3; b <= 3; b++)
                    Spawn(g.transform, "Bar", LowPolyMesh.Box(new Vector3(0.07f, 2.7f, 0.12f)), new Vector3(b * 0.42f, 0, -Mathf.Sign(z) * 0.2f),
                         PS2Materials.Get("iron", PS2Textures.Metal, new Color(0.45f, 0.42f, 0.4f)));
            }

            foreach (var go in new[] { floor, wall, bannerGo, stands, crowd, cols })
                LowPolyMesh.BakeAmbient(go, AmbientSky, AmbientGround);
            foreach (Transform t in parent) if (t.name == "Gate") LowPolyMesh.BakeAmbient(t.gameObject, AmbientSky * 0.8f, AmbientGround * 0.8f);

            // ----- colliders (simple boxes)
            var colliders = new GameObject("Colliders");
            colliders.transform.SetParent(parent, false);
            var floorCol = colliders.AddComponent<BoxCollider>();
            floorCol.center = new Vector3(0, -0.5f, 0);
            floorCol.size = new Vector3(R * 2f + 6f, 1f, R * 2f + 6f);
            for (int i = 0; i < Segments; i++)
            {
                float a = (i + 0.5f) / Segments * Mathf.PI * 2f;
                Vector3 d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var wc = new GameObject("WallCol" + i);
                wc.transform.SetParent(colliders.transform, false);
                wc.transform.SetPositionAndRotation(d * (R + 0.3f) + Vector3.up * 2.5f, Quaternion.LookRotation(-d));
                var bc = wc.AddComponent<BoxCollider>();
                bc.size = new Vector3(circumference / Segments + 0.4f, 5f, 0.6f);
            }
        }

        /// <summary>Stone blocks to vault onto (0.9 m) and platforms to climb (2.3 m), on the diagonals away from the spawns.</summary>
        void BuildObstacles(Transform parent)
        {
            Material stone = PS2Materials.Get("stone", PS2Textures.Stone, Color.white);
            Material block = PS2Materials.Get("stone_block", PS2Textures.Stone, new Color(0.85f, 0.78f, 0.68f));
            var root = new GameObject("Obstacles");
            root.transform.SetParent(parent, false);
            void Make(string name, Vector3 pos, float yaw, Vector3 size, Material mat)
            {
                var go = Spawn(root.transform, name, LowPolyMesh.Box(size, new Vector3(0, size.y * 0.5f, 0), 1f / 1.2f), pos, mat);
                go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
                var bc = go.AddComponent<BoxCollider>();
                bc.size = size;
                bc.center = new Vector3(0, size.y * 0.5f, 0);
                LowPolyMesh.BakeAmbient(go, AmbientSky, AmbientGround);
                Obstacles.Add(go.transform);
            }
            Make("BlockNE", new Vector3(6f, 0f, 6f), 45f, new Vector3(2.4f, 0.9f, 1.4f), block);
            Make("BlockSW", new Vector3(-6f, 0f, -6f), 45f, new Vector3(2.4f, 0.9f, 1.4f), block);
            Make("PlatformNW", new Vector3(-6.8f, 0f, 6.8f), 45f, new Vector3(3.2f, 2.3f, 3.2f), stone);
            Make("PlatformSE", new Vector3(6.8f, 0f, -6.8f), 45f, new Vector3(3.2f, 2.3f, 3.2f), stone);
        }

        int day0, spawned;

        /// <summary>One opponent: random body, weapon from the day's pool, armour by day, maybe a second weapon or a shield.</summary>
        public Fighter SpawnEnemy(Vector3 pos, int team, bool many) => SpawnEnemy(pos, team, many, false);

        public Fighter SpawnEnemy(Vector3 pos, int team, bool many, bool fodder)
        {
            int i = spawned++;
            int day = day0;
            pos.y = 0.05f;
            string name = OpponentNames[(day - 1 + i) % OpponentNames.Length] + (i >= OpponentNames.Length ? " " + Roman(i / OpponentNames.Length + 1) : "");
            var tunic = EnemyTunics[i % EnemyTunics.Length];
            var look = new GladiatorLook { Tunic = tunic, Skin = new Color(0.9f, 0.82f, 0.75f), Helmet = false, Crest = tunic };
            // Wave fodder never wears armour; the others follow the day rules.
            var enemyArmor = fodder ? Gladiator.Character.ArmorSet.Empty
                                    : Gladiator.Character.ArmorSet.RandomForDay(day, new System.Random(Random.Range(0, int.MaxValue)));
            Vector3 face = -pos; face.y = 0f;
            var app = CharacterFactory.FromLook(look, name);
            var enemy = CharacterFactory.Create(name, app, pos, Quaternion.LookRotation(face.sqrMagnitude > 0.01f ? face : Vector3.back), team, enemyArmor);
            BotLooks[enemy] = app;
            var pool = WeaponLibrary.PoolForDay(day);
            var ws = pool[Random.Range(0, pool.Length)];
            enemy.Equip(WeaponFactory.Create(ws));
            StatEffects.ApplyToEnemy(enemy, day, many);
            if (fodder) StatEffects.ApplyToWaveEnemy(enemy, day);
            if (!fodder && day >= 2 && ws.CanDualWield && Random.value < 0.3f)
            {
                WeaponStats second;
                do second = pool[Random.Range(0, pool.Length)]; while (!second.CanDualWield);
                enemy.EquipOff(WeaponFactory.Create(second));
            }
            else if (!fodder && day >= 2 && !ws.IsBow && Random.value < 0.45f)
                Shield.Attach(enemy, ShieldLibrary.All[Random.Range(0, ShieldLibrary.All.Length)], new Color(0.25f, 0.3f, 0.5f));
            EnemyLoadouts.Add(enemy.DisplayName + $" [{enemyArmor.Count} piezas {enemyArmor.Weight:0.#}kg]: " + ws.Name + (enemy.OffWeapon != null ? " + " + enemy.OffWeapon.Stats.Name : "") +
                              (enemy.EquippedShield != null ? " + " + enemy.EquippedShield.Stats.Name + (enemy.EquippedShield.OnBack ? " (espalda)" : "") : ""));
            var eai = enemy.gameObject.AddComponent<EnemyAI>();
            if (fodder) { eai.Aggression = 0.7f; eai.BlockChance = 0.15f; eai.DodgeChance = 0.05f; eai.DuckChance = 0.05f; eai.KickChance = 0.05f; }
            Enemies.Add(enemy);
            return enemy;
        }

        static string Roman(int n) => n <= 1 ? "" : n == 2 ? "II" : n == 3 ? "III" : n == 4 ? "IV" : "V";

        /// <summary>
        /// The champion: gold full plate, a trident in both hands, and an AI that blocks, dodges and ducks far more than
        /// the others.
        /// </summary>
        public Fighter SpawnChampion(Vector3 pos)
        {
            pos.y = 0.05f;
            var look = CharacterFactory.FromLook(new GladiatorLook
            {
                Tunic = new Color(0.45f, 0.12f, 0.45f), Skin = new Color(0.85f, 0.7f, 0.55f), Helmet = false, Crest = new Color(0.95f, 0.8f, 0.2f)
            }, "Carpophorus");
            look.Metal = 2; // gold
            look.Muscle = 0.9f; look.Height = 0.85f; look.Shoulders = 0.8f; look.Fat = 0.1f; look.Femininity = 0f;
            look.Shirt = 1; look.Pants = 3; look.Shoes = 1;
            var armor = Gladiator.Character.ArmorSet.Champion();
            var champ = CharacterFactory.Create("Carpophorus el Campeon", look, pos, Quaternion.LookRotation(-new Vector3(pos.x, 0, pos.z)), 1, armor);
            BotLooks[champ] = look;
            champ.Equip(WeaponFactory.Create(WeaponLibrary.Trident));
            StatEffects.ApplyToChampion(champ, day0);
            var ai = champ.gameObject.AddComponent<EnemyAI>();
            ai.BlockChance = 0.75f;
            ai.DodgeChance = 0.35f;
            ai.DuckChance = 0.25f;
            ai.Aggression = 1.3f;
            ai.HeavyChance = 0.4f;
            ai.KickChance = 0.2f;
            EnemyLoadouts.Add(champ.DisplayName + $" [campeon, {armor.Count} piezas de oro {armor.Weight:0.#}kg]: Tridente");
            Enemies.Add(champ);
            return champ;
        }

        /// <summary>
        /// Online co-op, on the host: the rivals for the chosen mode on the north half of the sand (the players start on
        /// the south one). Duel: one per player; double: two per player; the champion, tougher the more players; waves:
        /// the first wave, bigger the more players.
        /// </summary>
        void SpawnCoop(Gladiator.Net.NetSession net)
        {
            int players = Mathf.Max(1, net.Players.Count);
            switch (net.ArenaMode)
            {
                case Gladiator.Net.NetSession.ModeChampion:
                {
                    var c = SpawnChampion(new Vector3(0, 0, 6f));
                    float hp = c.Health.Max * (1f + 0.75f * (players - 1));
                    c.Health.SetMax(hp, true);
                    break;
                }
                case Gladiator.Net.NetSession.ModeWaves:
                    WaveScale = 1f + 0.5f * (players - 1);
                    SpawnWave(1);
                    break;
                default:
                {
                    int n = players * (net.ArenaMode == Gladiator.Net.NetSession.ModeDouble ? 2 : 1);
                    for (int k = 0; k < n; k++)
                    {
                        float x = (k - (n - 1) * 0.5f) * 2.2f;
                        SpawnEnemy(new Vector3(x, 0, 5.5f + (k % 2) * 1.2f), 1, n > 1);
                    }
                    break;
                }
            }
            Debug.Log($"[NETARENA] host spawned {Enemies.Count} rivals for mode {net.ArenaMode} ({players} players)");
        }

        /// <summary>Wave mode: 1, 2, 4, 6, 8 and 10 opponents.</summary>
        public static readonly int[] WaveSizes = { 1, 2, 4, 6, 8, 10 };

        /// <summary>Spawns wave n (1-based) around the arena, away from the player; returns the new opponents.</summary>
        public List<Fighter> SpawnWave(int n)
        {
            var list = new List<Fighter>();
            int count = Mathf.Min(14, Mathf.CeilToInt(WaveSizes[Mathf.Clamp(n - 1, 0, WaveSizes.Length - 1)] * WaveScale));
            Vector3 p = Player != null ? Player.transform.position : Vector3.zero;
            float baseAng = Mathf.Atan2(-p.z, -p.x); // opposite side of the arena from the player
            for (int k = 0; k < count; k++)
            {
                float spread = count == 1 ? 0f : Mathf.Lerp(-1.4f, 1.4f, k / (float)(count - 1));
                float ang = baseAng + spread;
                float r = 9.5f + (k % 2) * 1.5f;
                list.Add(SpawnEnemy(new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * r, 1, true, true));
            }
            return list;
        }

        /// <summary>A simple wooden weapon rack by the west wall, behind the weapons lying in the sand.</summary>
        void BuildRack(Transform parent)
        {
            Material wood = PS2Materials.Get("wood", PS2Textures.Wood, Color.white);
            var rack = new GameObject("Armamentarium").transform;
            rack.SetParent(parent, false);
            rack.SetPositionAndRotation(new Vector3(-7.6f, 0f, -2f), Quaternion.Euler(0, 60f, 0));
            PS2Spawn.Box(rack, "PostL", new Vector3(0.12f, 1.4f, 0.12f), new Vector3(-0.9f, 0.7f, 0), wood);
            PS2Spawn.Box(rack, "PostR", new Vector3(0.12f, 1.4f, 0.12f), new Vector3(0.9f, 0.7f, 0), wood);
            PS2Spawn.Box(rack, "BarTop", new Vector3(1.95f, 0.1f, 0.14f), new Vector3(0, 1.3f, 0), wood);
            PS2Spawn.Box(rack, "BarLow", new Vector3(1.95f, 0.08f, 0.12f), new Vector3(0, 0.45f, 0), wood);
            for (int i = 0; i < 4; i++)
                PS2Spawn.Box(rack, "Peg" + i, new Vector3(0.04f, 0.04f, 0.25f), new Vector3(-0.6f + i * 0.4f, 1.2f, 0.12f), wood);
            LowPolyMesh.BakeAmbient(rack.gameObject, AmbientSky, AmbientGround);
            var col = rack.gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0, 0.7f, 0);
            col.size = new Vector3(2f, 1.4f, 0.3f);
        }

        /// <summary>Vault blocks and climbing platforms (for the autotest).</summary>
        public List<Transform> Obstacles { get; } = new List<Transform>();

        void BuildSky(Transform parent)
        {
            var b = new MeshBuilder();
            const int rings = 8;
            var prof = new List<MeshBuilder.Ring>();
            for (int k = 0; k <= rings; k++)
            {
                float t = Mathf.Lerp(-20f, 90f, k / (float)rings) * Mathf.Deg2Rad;
                prof.Add(new MeshBuilder.Ring(Mathf.Cos(t) * 200f, Mathf.Sin(t) * 200f, Mathf.Cos(t), Mathf.Sin(t)));
            }
            b.AddLathe(prof, 16, Vector3.zero, Color.white, true);
            Mesh m = b.Build("SkyDome");
            var v = m.vertices;
            var c = new Color[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                float h = Mathf.Clamp01(v[i].y / 200f);
                c[i] = Color.Lerp(FogColor, SkyTop, Mathf.Pow(h, 0.6f));
            }
            m.colors = c;
            var mat = PS2Materials.Get("sky", PS2Textures.White, Color.white, 0f, 0f, null, CullMode.Off);
            Spawn(parent, "SkyDome", m, Vector3.zero, mat);
        }

        static GameObject Spawn(Transform parent, string name, Mesh mesh, Vector3 localPos, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.On; // PS3 look only (unlit things clip themselves out of the shadow pass)
            mr.receiveShadows = false;
            return go;
        }
    }

    /// <summary>Cheap crowd "animation": the whole crowd mesh bobs a few cm.</summary>
    public class CrowdBob : MonoBehaviour
    {
        void Update()
        {
            float t = Time.time;
            transform.localPosition = new Vector3(0, Mathf.Abs(Mathf.Sin(t * 5f)) * 0.06f, 0);
        }
    }
}
