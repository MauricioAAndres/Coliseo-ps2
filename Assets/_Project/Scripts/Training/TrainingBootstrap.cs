using System.Collections.Generic;
using Gladiator.Arena;
using Gladiator.Combat;
using Gladiator.Core;
using Gladiator.PS2Style;
using Gladiator.Progression;
using Gladiator.UI;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gladiator.Training
{
    /// <summary>
    /// Builds the ludus (gladiator school courtyard) at runtime: walled sand yard, portico, gate to the arena,
    /// the colosseum on the horizon, and the three training stations. Same PS2 pipeline as the arena.
    /// </summary>
    public class TrainingBootstrap : MonoBehaviour
    {
        [Header("Shaders (assigned by the scene builder so they're included in builds)")]
        public Shader PS2LitShader;
        public Shader PS2PostShader;

        public static readonly Color FogColor = new Color(0.82f, 0.74f, 0.62f);
        public static readonly Color SkyTop = new Color(0.42f, 0.56f, 0.74f);
        public static readonly Color AmbientSky = new Color(0.52f, 0.5f, 0.54f);
        public static readonly Color AmbientGround = new Color(0.32f, 0.25f, 0.17f);

        const float HalfX = 13f, HalfZ = 11f, WallH = 4.5f, WallT = 1f, GateHalf = 2f;

        public Fighter Player { get; private set; }
        public TrainingManager Manager { get; private set; }

        /// <summary>Set by the town before loading the ludus: the player comes in through the east door.</summary>
        public static bool ArriveFromTown;
        /// <summary>The east door to the town (inside the yard, where you stand to use it).</summary>
        public static readonly Vector3 TownDoorPoint = new Vector3(HalfX - 1.1f, 0f, TownDoorZ);
        const float TownDoorZ = 4.5f;

        /// <summary>The ludus now stands inside the town (LudusInTown): loading the old "Training" scene goes to the town.</summary>
        public static bool RedirectToTown = true;

        void Awake()
        {
            Time.timeScale = 1f;
            var gm = GameManager.Ensure();
            gm.DayCycle.NotifySceneStarted(DayPhase.Training);
            if (RedirectToTown)
            {
                ArriveFromTown = false;
                UnityEngine.SceneManagement.SceneManager.LoadScene(Gladiator.Town.TownBootstrap.SceneName);
                return;
            }
            PS2Materials.Init(PS2LitShader);
            SetupRenderSettings();

            var env = new GameObject("Environment").transform;
            BuildLighting(env);
            BuildYard(env);
            BuildSurroundings(env);
            BuildSky(env);
            Gladiator.PS2Style.DayNightCycle.Attach(env); // dawn / day / dusk / night from the town clock, clouds
            Camera cam = BuildCamera();

            var net = Gladiator.Net.NetSession.Instance;
            bool multi = net != null && net.InGame;
            bool fromTown = ArriveFromTown;
            ArriveFromTown = false;
            Player = CharacterFactory.Create(gm.Profile.Name, gm.Profile.Appearance,
                fromTown ? TownDoorPoint + new Vector3(-0.8f, 0.05f, 0f) : multi ? SlotSpawn(net.SlotOf(net.LocalId)) : new Vector3(0, 0.05f, -7.5f),
                fromTown ? Quaternion.LookRotation(Vector3.left) : Quaternion.identity, 0, gm.Profile.ArmorSet);
            // Clothes / armour changed at the mercator: dress the gladiator at once.
            var profileRef = gm.Profile;
            var playerRef = Player;
            System.Action onChanged = null;
            string Dress() => string.Join(",", profileRef.Armor) + "|" + string.Join(",", profileRef.ArmorSides) + "|" + JsonUtility.ToJson(profileRef.Appearance);
            string dressed = Dress();
            onChanged = () =>
            {
                if (playerRef == null) { profileRef.Changed -= onChanged; return; }
                string now = Dress();
                if (now == dressed) return; // stats / denarii changes: nothing to redress
                dressed = now;
                CharacterFactory.RebuildBody(playerRef, profileRef.Appearance, profileRef.ArmorSet);
            };
            profileRef.Changed += onChanged;
            Player.Equip(WeaponFactory.Create(WeaponLibrary.Rudis));
            Player.DisarmResistance = 1f;
            var pc = Player.gameObject.AddComponent<PlayerController>();
            pc.AimCamera = cam;

            // ---- stations
            var stations = new List<TrainingMinigame>
            {
                MakeStation<PostStrikeMinigame>("Station_Palus", new Vector3(-6.5f, 0, 4.5f), 90f),
                MakeStation<WeightLiftMinigame>("Station_Pesas", new Vector3(-5.25f, 0, -2.5f), 180f),
                MakeStation<DodgeMinigame>("Station_Esquiva", new Vector3(6.5f, 0, -5.5f), 0f),
            };
            foreach (var s in stations) MakeStationMarker(env, s);
            // The mercator's stall is gone: weapons, armour and shields are bought at the town's two smithies.
            Vector3 shopPoint = new Vector3(100000f, 0f, 100000f);

            var camCtl = cam.GetComponent<ArenaCamera>();
            camCtl.Player = Player;
            camCtl.Distance = 10f;
            camCtl.Pitch = 38f;
            camCtl.EnemyBias = 0f; // the merchant is a Fighter too; don't frame him
            camCtl.SnapToTarget();

            var hud = new GameObject("TrainingHUD", typeof(RectTransform)).AddComponent<TrainingHUD>();
            hud.Init(cam);

            Manager = new GameObject("TrainingManager").AddComponent<TrainingManager>();
            Manager.Init(Player, pc, hud, camCtl, stations, new Vector3(0, 0, HalfZ - 1.6f), shopPoint);
            Manager.TownDoor = TownDoorPoint;
            Partners = BuildPartners(env, cam, hud);
            // Multiplayer: the other players' gladiators share the yard.
            if (multi) gameObject.AddComponent<Gladiator.Net.NetAvatars>().Init(Player, cam, SlotSpawn, msg => hud.ShowMessage(msg, 3f));

            if (GameManager.AutoTest) gameObject.AddComponent<TrainingAutoTest>().Init(this);
        }

        /// <summary>Where each player of a multiplayer game appears (roster order), side by side in the south of the yard.</summary>
        public static Vector3 SlotSpawn(int slot) => new Vector3(-2.4f + 1.6f * Mathf.Clamp(slot, 0, 3), 0.05f, -7.5f);

        /// <summary>When set, stations are built as children of this transform (positions are then local to it: the ludus in the town).</summary>
        internal static Transform StationRoot;

        internal static T MakeStation<T>(string name, Vector3 pos, float yaw, System.Action<T> configure = null) where T : TrainingMinigame
        {
            var go = new GameObject(name);
            if (StationRoot != null)
            {
                go.transform.SetParent(StationRoot, false);
                go.transform.localPosition = pos;
                go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            }
            else go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            var s = go.AddComponent<T>();
            configure?.Invoke(s);
            s.Build();
            return s;
        }

        static readonly string[] PartnerNames = { "Priscus", "Verus", "Crixo", "Tetraites", "Spiculus", "Amazonia" };
        static readonly Color[] PartnerTunics =
        {
            new Color(0.55f, 0.35f, 0.2f), new Color(0.3f, 0.45f, 0.3f), new Color(0.45f, 0.42f, 0.38f),
            new Color(0.5f, 0.2f, 0.18f), new Color(0.25f, 0.32f, 0.5f), new Color(0.6f, 0.55f, 0.4f)
        };

        /// <summary>
        /// Two more stations of each kind, each taken by a training partner who practises all day (the player has the
        /// third, original one). Partners have their own wooden gladius; hitting them does no damage.
        /// </summary>
        internal static List<TrainingMinigame> BuildPartners(Transform env, Camera cam, TrainingHUD hud)
        {
            var silent = new GameObject("PartnersHUD", typeof(RectTransform)).AddComponent<TrainingHUD>();
            silent.Init(cam);
            foreach (var c in silent.GetComponentsInChildren<Canvas>(true)) c.enabled = false;
            var c0 = silent.GetComponent<Canvas>(); if (c0 != null) c0.enabled = false;

            var extra = new List<TrainingMinigame>
            {
                MakeStation<PostStrikeMinigame>("Station_Palus_B", new Vector3(-6.5f, 0, 7.6f), 90f),
                MakeStation<PostStrikeMinigame>("Station_Palus_C", new Vector3(-6.5f, 0, 1.4f), 90f),
                MakeStation<WeightLiftMinigame>("Station_Pesas_B", new Vector3(-5.25f, 0, -6.4f), 180f),
                MakeStation<WeightLiftMinigame>("Station_Pesas_C", new Vector3(-2.2f, 0, -9.3f), 0f),
                MakeStation<DodgeMinigame>("Station_Esquiva_B", new Vector3(10.6f, 0, -5.0f), 0f, d => d.LauncherDistance = 6f),
                MakeStation<DodgeMinigame>("Station_Esquiva_C", new Vector3(3.0f, 0, -8.9f), 90f, d => d.LauncherDistance = 6f),
            };
            for (int i = 0; i < extra.Count; i++)
            {
                var st = extra[i];
                MakeStationMarker(env, st);
                var look = CharacterFactory.FromLook(new GladiatorLook
                {
                    Tunic = PartnerTunics[i % PartnerTunics.Length], Skin = new Color(0.88f, 0.72f, 0.58f), Helmet = false, Crest = Color.white
                }, PartnerNames[i]);
                var f = CharacterFactory.Create(PartnerNames[i], look, st.PlayerSpot + Vector3.up * 0.05f, Quaternion.LookRotation(st.PlayerFacing), 2);
                f.Equip(WeaponFactory.Create(WeaponLibrary.Rudis));
                var npc = f.gameObject.AddComponent<TrainingNPC>();
                npc.Begin(f, st, silent, hud);
            }
            return extra;
        }

        /// <summary>The partners' stations (autotest).</summary>
        public List<TrainingMinigame> Partners { get; private set; }

        /// <summary>Ring on the sand + a small signpost coloured by stat, so stations read from afar.</summary>
        internal static void MakeStationMarker(Transform parent, TrainingMinigame s)
        {
            Color c = s.Stat == StatType.Strength ? new Color(0.75f, 0.2f, 0.12f)
                    : s.Stat == StatType.Agility ? new Color(0.25f, 0.6f, 0.2f) : new Color(0.25f, 0.35f, 0.7f);
            var b = new MeshBuilder();
            b.AddAnnulus(0.85f, 1.0f, 0.02f, 16, Color.white, 1f);
            PS2Spawn.Mesh(parent, "Marker_" + s.Id, b.Build("Marker"), s.InteractPoint, PS2Materials.Flat(c * 0.9f, 0.6f));

            Vector3 side = Vector3.Cross(Vector3.up, s.PlayerFacing).normalized;
            Vector3 signPos = s.InteractPoint - side * 2.7f - s.PlayerFacing * 0.6f;
            var sign = new GameObject("Sign_" + s.Id).transform;
            sign.SetParent(parent, false);
            sign.SetPositionAndRotation(signPos, Quaternion.LookRotation(-s.PlayerFacing));
            Material wood = PS2Materials.Get("wood", PS2Textures.Wood, Color.white);
            PS2Spawn.Box(sign, "Post", new Vector3(0.1f, 1.5f, 0.1f), new Vector3(0, 0.75f, 0), wood);
            PS2Spawn.Box(sign, "Board", new Vector3(0.7f, 0.42f, 0.06f), new Vector3(0, 1.35f, 0), wood);
            PS2Spawn.Box(sign, "Stripe", new Vector3(0.58f, 0.12f, 0.08f), new Vector3(0, 1.35f, 0), PS2Materials.Get("sign_" + s.Id, PS2Textures.Cloth, c));
            LowPolyMesh.BakeAmbient(sign.gameObject, AmbientSky, AmbientGround);
        }

        /// <summary>Mercator's stall in the south-east corner: awning, table with the goods on display, the merchant.</summary>
        static Vector3 BuildMerchant(Transform parent)
        {
            Material wood = PS2Materials.Get("wood", PS2Textures.Wood, Color.white);
            Material darkWood = PS2Materials.Get("wood_dark", PS2Textures.Wood, new Color(0.6f, 0.5f, 0.42f));
            Material awningA = PS2Materials.Get("awning_a", PS2Textures.Cloth, new Color(0.85f, 0.75f, 0.55f));
            Material awningB = PS2Materials.Get("awning_b", PS2Textures.Cloth, new Color(0.25f, 0.4f, 0.65f));
            Material metal = PS2Materials.Get("metal_FFF2CC", PS2Textures.Metal, new Color(1f, 0.95f, 0.8f));
            Material shieldMat = PS2Materials.Get("shield_B21F14", PS2Textures.Cloth, new Color(0.7f, 0.12f, 0.08f));
            Material bronze = PS2Materials.Get("bronze", PS2Textures.Metal, new Color(0.95f, 0.72f, 0.4f));

            var stall = new GameObject("MerchantStall").transform;
            stall.SetParent(parent, false);
            stall.SetPositionAndRotation(new Vector3(HalfX - 1.6f, 0, -HalfZ + 2.4f), Quaternion.Euler(0, -90f, 0)); // faces the yard (-x)
            PS2Spawn.Box(stall, "Table", new Vector3(2.6f, 0.12f, 0.9f), new Vector3(0, 0.95f, 0.4f), wood);
            PS2Spawn.Box(stall, "Front", new Vector3(2.6f, 0.85f, 0.08f), new Vector3(0, 0.45f, 0.85f), darkWood, null, 1f);
            foreach (float x in new[] { -1.25f, 1.25f })
                foreach (float z in new[] { -0.6f, 0.85f })
                    PS2Spawn.Box(stall, "Pole", new Vector3(0.1f, 2.6f, 0.1f), new Vector3(x, 1.3f, z), wood);
            for (int i = 0; i < 6; i++)
                PS2Spawn.Box(stall, "Awning", new Vector3(0.45f, 0.06f, 1.9f), new Vector3(-1.125f + i * 0.45f, 2.65f, 0.15f),
                    i % 2 == 0 ? awningA : awningB, Quaternion.Euler(-10f, 0, 0));
            // Goods: a sword lying on the table and a scutum leaning against it.
            PS2Spawn.Box(stall, "SwordBlade", new Vector3(0.07f, 0.02f, 0.9f), new Vector3(-0.6f, 1.03f, 0.45f), metal, Quaternion.Euler(0, 70f, 0));
            PS2Spawn.Box(stall, "SwordGuard", new Vector3(0.22f, 0.05f, 0.05f), new Vector3(-0.17f, 1.04f, 0.6f), bronze, Quaternion.Euler(0, 70f, 0));
            PS2Spawn.Box(stall, "Scutum", new Vector3(0.58f, 0.86f, 0.06f), new Vector3(0.7f, 1.45f, 0.3f), shieldMat, Quaternion.Euler(-15f, 0, 0));
            PS2Spawn.Box(stall, "Boss", new Vector3(0.16f, 0.16f, 0.06f), new Vector3(0.7f, 1.46f, 0.36f), bronze, Quaternion.Euler(-15f, 0, 0));
            PS2Spawn.Box(stall, "Amphora", new Vector3(0.3f, 0.6f, 0.3f), new Vector3(1.6f, 0.3f, 0.6f), PS2Materials.Get("clay", PS2Textures.Sand, new Color(0.75f, 0.45f, 0.3f)));
            PS2Spawn.BoxCollider(stall, new Vector3(0, 0.6f, 0.2f), new Vector3(2.7f, 1.2f, 1.4f));
            LowPolyMesh.BakeAmbient(stall.gameObject, AmbientSky, AmbientGround);

            // The mercator himself (same team as the player, so he can't be hurt).
            var mLook = CharacterFactory.FromLook(new GladiatorLook { Tunic = new Color(0.45f, 0.25f, 0.5f), Helmet = false, Crest = Color.white }, "Mercator");
            mLook.Fat = 0.95f; mLook.Muscle = 0.15f; mLook.Height = 0.3f; mLook.Femininity = 0f;
            mLook.Outfit = 2; mLook.Armor = 0; mLook.HairStyle = 0; mLook.Beard = 3; mLook.HairColor = 4;
            var merchant = CharacterFactory.Create("Mercator", mLook,
                stall.TransformPoint(new Vector3(0, 0.05f, -0.35f)), stall.rotation, 0);
            merchant.DisarmResistance = 1f;

            // Ring + interaction point in front of the table.
            Vector3 point = stall.TransformPoint(new Vector3(0, 0, 2.0f));
            point.y = 0f;
            var b = new MeshBuilder();
            b.AddAnnulus(0.85f, 1.0f, 0.02f, 16, Color.white, 1f);
            PS2Spawn.Mesh(parent, "Marker_shop", b.Build("Marker"), point, PS2Materials.Flat(new Color(0.85f, 0.65f, 0.15f), 0.6f));
            return point;
        }

        // ------------------------------------------------------------------ setup
        void SetupRenderSettings()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = FogColor;
            RenderSettings.fogStartDistance = 22f;
            RenderSettings.fogEndDistance = 110f;
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
            sunGo.transform.rotation = Quaternion.Euler(40f, 55f, 0f);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.92f, 0.8f);
            sun.intensity = 0.95f;
            sun.shadows = LightShadows.None;
            RenderSettings.sun = sun;

            // Two torches flanking the arena gate.
            Material wood = PS2Materials.Get("wood", PS2Textures.Wood, Color.white);
            Material iron = PS2Materials.Get("iron", PS2Textures.Metal, new Color(0.45f, 0.42f, 0.4f));
            Material flame = PS2Materials.Flat(new Color(1f, 0.62f, 0.15f), 0.3f);
            foreach (float x in new[] { -GateHalf - 0.8f, GateHalf + 0.8f })
            {
                var t = new GameObject("Torch").transform;
                t.SetParent(parent, false);
                t.position = new Vector3(x, 0, HalfZ - 0.9f);
                PS2Spawn.Mesh(t, "Pole", LowPolyMesh.Cylinder(0.07f, 2.3f, 6), Vector3.zero, wood);
                PS2Spawn.Mesh(t, "Bowl", LowPolyMesh.Cylinder(0.22f, 0.18f, 6), new Vector3(0, 2.3f, 0), iron);
                var fl = new GameObject("Flame").transform;
                fl.SetParent(t, false);
                fl.localPosition = new Vector3(0, 2.45f, 0);
                PS2Spawn.Mesh(fl, "F", LowPolyMesh.Box(new Vector3(0.24f, 0.4f, 0.24f), new Vector3(0, 0.2f, 0)), Vector3.zero, flame, Quaternion.Euler(0, 45f, 0));
                var l = new GameObject("TorchLight").AddComponent<Light>();
                l.transform.SetParent(t, false);
                l.transform.localPosition = new Vector3(0, 2.8f, -0.5f);
                l.type = LightType.Point; l.range = 7f; l.intensity = 1.6f; l.color = new Color(1f, 0.55f, 0.22f);
                l.shadows = LightShadows.None;
                var tf = t.gameObject.AddComponent<TorchFlicker>();
                tf.Light = l; tf.Flame = fl; tf.BaseIntensity = l.intensity;
                LowPolyMesh.BakeAmbient(t.gameObject, AmbientSky, AmbientGround);
            }
        }

        /// <param name="inTown">The ludus inside the town: its north gate opens onto the town by itself (no arena gate, no east door).</param>
        internal static void BuildYard(Transform parent, bool inTown = false)
        {
            Material sand = PS2Materials.Get("sand", PS2Textures.Sand, Color.white);
            Material stone = PS2Materials.Get("stone", PS2Textures.Stone, Color.white);
            Material stoneDark = PS2Materials.Get("stone_dark", PS2Textures.Stone, new Color(0.72f, 0.66f, 0.6f));
            Material plaster = PS2Materials.Get("plaster", PS2Textures.Stone, new Color(0.95f, 0.88f, 0.76f));
            Material wood = PS2Materials.Get("wood", PS2Textures.Wood, Color.white);
            Material darkWood = PS2Materials.Get("wood_dark", PS2Textures.Wood, new Color(0.6f, 0.5f, 0.42f));
            Material roof = PS2Materials.Get("roof", PS2Textures.Stone, new Color(0.75f, 0.38f, 0.26f));
            Material banner = PS2Materials.Get("banner", PS2Textures.Cloth, new Color(0.62f, 0.1f, 0.08f));
            Material straw = PS2Materials.Get("straw", PS2Textures.Cloth, new Color(0.95f, 0.8f, 0.42f));
            Material dark = PS2Materials.Flat(new Color(0.08f, 0.06f, 0.05f), 1f);

            var yard = new GameObject("Yard").transform;
            yard.SetParent(parent, false);

            // Floor: subdivided grid, darker near the walls (baked AO in vertex colours).
            var fb = new MeshBuilder();
            fb.AddGrid(HalfX * 2f, HalfZ * 2f, 26, 22, 0f, Color.white, 1f / 3.5f);
            Mesh floor = fb.Build("YardFloor");
            var v = floor.vertices; var c = floor.colors;
            for (int i = 0; i < v.Length; i++)
            {
                float edge = Mathf.Min(HalfX - Mathf.Abs(v[i].x), HalfZ - Mathf.Abs(v[i].z));
                float ao = Mathf.Lerp(0.6f, 1f, Mathf.SmoothStep(0f, 2.5f, edge));
                c[i] = new Color(ao, ao, ao, 1f);
            }
            floor.colors = c;
            PS2Spawn.Mesh(yard, "Floor", floor, inTown ? Vector3.up * 0.012f : Vector3.zero, sand);

            // Perimeter walls (plaster with a stone base), north wall split by the gate.
            void Wall(string n, Vector3 center, Vector3 size)
            {
                float h = size.y;
                PS2Spawn.Box(yard, n, size, center + Vector3.up * (h * 0.5f), plaster, null, 1f / 3f);
                PS2Spawn.Box(yard, n + "_Base", new Vector3(size.x + 0.12f, Mathf.Min(0.9f, h), size.z + 0.12f), center + Vector3.up * (Mathf.Min(0.9f, h) * 0.5f), stoneDark, null, 1f / 3f);
                PS2Spawn.Box(yard, n + "_Cap", new Vector3(size.x + 0.25f, 0.25f, size.z + 0.25f), center + Vector3.up * (h + 0.12f), roof);
                PS2Spawn.BoxCollider(yard, center + Vector3.up * 2f, new Vector3(size.x, 4f, size.z));
            }
            float sideLen = HalfX - GateHalf;
            Wall("WallN_L", new Vector3(-(GateHalf + sideLen * 0.5f), 0, HalfZ + WallT * 0.5f), new Vector3(sideLen, WallH, WallT));
            Wall("WallN_R", new Vector3(GateHalf + sideLen * 0.5f, 0, HalfZ + WallT * 0.5f), new Vector3(sideLen, WallH, WallT));
            // South side is a low parapet: the follow camera sits south of the player and must see over it.
            Wall("WallS", new Vector3(0, 0, -HalfZ - WallT * 0.5f), new Vector3(HalfX * 2f + WallT * 2f, 1.1f, WallT));
            Wall("WallW", new Vector3(-HalfX - WallT * 0.5f, 0, 0), new Vector3(WallT, WallH, HalfZ * 2f));
            Wall("WallE", new Vector3(HalfX + WallT * 0.5f, 0, 0), new Vector3(WallT, WallH, HalfZ * 2f));

            // Gate to the arena: stone frame, lintel with a red banner, heavy wooden doors.
            var gate = new GameObject("ArenaGate").transform;
            gate.SetParent(yard, false);
            gate.localPosition = new Vector3(0, 0, HalfZ + WallT * 0.5f);
            PS2Spawn.Box(gate, "JambL", new Vector3(0.6f, WallH + 0.8f, 1.4f), new Vector3(-GateHalf - 0.3f, (WallH + 0.8f) * 0.5f, 0), stone);
            PS2Spawn.Box(gate, "JambR", new Vector3(0.6f, WallH + 0.8f, 1.4f), new Vector3(GateHalf + 0.3f, (WallH + 0.8f) * 0.5f, 0), stone);
            PS2Spawn.Box(gate, "Lintel", new Vector3(GateHalf * 2f + 1.8f, 1.2f, 1.4f), new Vector3(0, WallH + 0.2f, 0), stone);
            PS2Spawn.Box(gate, "Banner", new Vector3(GateHalf * 2f - 0.4f, 0.8f, 0.05f), new Vector3(0, WallH + 0.2f, -0.73f), banner);
            PS2Spawn.Box(gate, "Fill", new Vector3(GateHalf * 2f, WallH - 3.6f, 0.6f), new Vector3(0, 3.6f + (WallH - 3.6f) * 0.5f, 0), plaster);
            if (inTown)
            {
                // two leaves on hinges that swing open when someone walks up to the gate (push it open: no E, no loading)
                var doors = gate.gameObject.AddComponent<LudusGateDoors>();
                var iron = PS2Materials.Get("iron", PS2Textures.Metal, new Color(0.45f, 0.42f, 0.4f));
                for (int side = -1; side <= 1; side += 2)
                {
                    var hinge = new GameObject(side < 0 ? "HingeL" : "HingeR").transform;
                    hinge.SetParent(gate, false);
                    hinge.localPosition = new Vector3(side * GateHalf, 0, 0.1f);
                    float leafX = -side * (GateHalf - 0.05f) * 0.5f;
                    PS2Spawn.Box(hinge, "Leaf", new Vector3(GateHalf - 0.05f, 3.6f, 0.25f), new Vector3(leafX, 1.8f, 0), darkWood, null, 0.8f);
                    for (int i = -1; i <= 1; i += 2)
                        PS2Spawn.Box(hinge, "Band", new Vector3(GateHalf - 0.1f, 0.12f, 0.3f), new Vector3(leafX, 1.8f + i * 1.1f, 0f), iron);
                    if (side < 0) doors.Left = hinge; else doors.Right = hinge;
                }
                // the frame only: the doorway itself stays open to walk through
                PS2Spawn.BoxCollider(gate, new Vector3(-GateHalf - 0.3f, WallH * 0.5f, 0), new Vector3(0.6f, WallH, 1.4f));
                PS2Spawn.BoxCollider(gate, new Vector3(GateHalf + 0.3f, WallH * 0.5f, 0), new Vector3(0.6f, WallH, 1.4f));
                PS2Spawn.BoxCollider(gate, new Vector3(0, 3.6f + (WallH - 3.6f) * 0.5f, 0), new Vector3(GateHalf * 2f, WallH - 3.6f, 1.4f));
            }
            else
            {
                PS2Spawn.Box(gate, "DoorL", new Vector3(GateHalf - 0.05f, 3.6f, 0.25f), new Vector3(-GateHalf * 0.5f, 1.8f, 0.1f), darkWood, null, 0.8f);
                PS2Spawn.Box(gate, "DoorR", new Vector3(GateHalf - 0.05f, 3.6f, 0.25f), new Vector3(GateHalf * 0.5f, 1.8f, 0.1f), darkWood, null, 0.8f);
                for (int i = -1; i <= 1; i += 2)
                    PS2Spawn.Box(gate, "Band", new Vector3(GateHalf * 2f, 0.12f, 0.3f), new Vector3(0, 1.8f + i * 1.1f, 0.0f),
                        PS2Materials.Get("iron", PS2Textures.Metal, new Color(0.45f, 0.42f, 0.4f)));
                PS2Spawn.BoxCollider(gate, new Vector3(0, WallH * 0.5f, 0), new Vector3(GateHalf * 2f + 1.2f, WallH, 1.4f));
            }

            // East door to the town: stone frame, wooden door and a small sign (not in the town: the north gate opens onto it).
            var townDoor = new GameObject("TownDoor").transform;
            if (inTown) townDoor.gameObject.SetActive(false);
            townDoor.SetParent(yard, false);
            townDoor.localPosition = new Vector3(HalfX, 0, TownDoorZ);
            townDoor.localRotation = Quaternion.Euler(0, -90f, 0); // faces into the yard (-x)
            PS2Spawn.Box(townDoor, "JambL", new Vector3(0.35f, 3.1f, 0.5f), new Vector3(-1.05f, 1.55f, 0.1f), stone);
            PS2Spawn.Box(townDoor, "JambR", new Vector3(0.35f, 3.1f, 0.5f), new Vector3(1.05f, 1.55f, 0.1f), stone);
            PS2Spawn.Box(townDoor, "Lintel", new Vector3(2.5f, 0.4f, 0.5f), new Vector3(0, 3.2f, 0.1f), stone);
            PS2Spawn.Box(townDoor, "Door", new Vector3(1.75f, 2.95f, 0.12f), new Vector3(0, 1.48f, 0.05f), darkWood, null, 0.8f);
            PS2Spawn.Box(townDoor, "Sign", new Vector3(1.3f, 0.32f, 0.06f), new Vector3(0, 3.65f, 0.2f), PS2Materials.Get("sign_town", PS2Textures.Cloth, new Color(0.85f, 0.65f, 0.15f)));

            // Portico along the west wall: columns + tiled roof, barracks doors behind.
            var portico = new GameObject("Portico").transform;
            portico.SetParent(yard, false);
            const float px = -HalfX + 2.6f;
            for (int i = 0; i < 6; i++)
            {
                float z = -8.75f + i * 3.5f;
                PS2Spawn.Mesh(portico, "Column", LowPolyMesh.Cylinder(0.28f, 3.4f, 8, true, false, 1f, 1f / 3f), new Vector3(px, 0, z), stone);
                PS2Spawn.Box(portico, "Capital", new Vector3(0.75f, 0.2f, 0.75f), new Vector3(px, 3.45f, z), stone);
                PS2Spawn.BoxCollider(portico, new Vector3(px, 1.7f, z), new Vector3(0.6f, 3.4f, 0.6f));
                if (i < 5) PS2Spawn.Box(portico, "Door", new Vector3(0.1f, 2.4f, 1.3f), new Vector3(-HalfX + 0.02f, 1.2f, z + 1.75f), dark);
            }
            PS2Spawn.Box(portico, "Beam", new Vector3(0.5f, 0.35f, HalfZ * 2f), new Vector3(px, 3.7f, 0), wood);
            PS2Spawn.Box(portico, "Roof", new Vector3(3.2f, 0.25f, HalfZ * 2f), new Vector3(px - 1.3f, 4.1f, 0), roof, Quaternion.Euler(0, 0, 12f), 1f / 2f);

            // Props: weapon rack (west), straw bales + crates (east), water trough.
            var props = new GameObject("Props").transform;
            props.SetParent(yard, false);
            PS2Spawn.Box(props, "RackBack", new Vector3(3.2f, 1.6f, 0.15f), new Vector3(-8f, 0.8f, HalfZ - 0.35f), wood);
            PS2Spawn.Box(props, "RackBar", new Vector3(3.2f, 0.1f, 0.4f), new Vector3(-8f, 1.4f, HalfZ - 0.5f), wood);
            for (int i = 0; i < 6; i++)
                PS2Spawn.Box(props, "Rudis", new Vector3(0.1f, 1.0f, 0.06f), new Vector3(-9.2f + i * 0.48f, 0.9f, HalfZ - 0.6f), darkWood, Quaternion.Euler(8f, 0, 0));
            PS2Spawn.BoxCollider(props, new Vector3(-8f, 0.8f, HalfZ - 0.45f), new Vector3(3.2f, 1.6f, 0.6f));
            Vector3[] bales = { new Vector3(11.6f, 0.4f, 6.5f), new Vector3(11.6f, 0.4f, 8f), new Vector3(11.7f, 1.2f, 7.2f), new Vector3(10.2f, 0.4f, 9.4f) };
            foreach (var p in bales)
            {
                PS2Spawn.Box(props, "Bale", new Vector3(1.0f, 0.8f, 1.4f), p, straw, Quaternion.Euler(0, Random.Range(-10f, 10f), 0), 0.8f);
                PS2Spawn.BoxCollider(props, p, new Vector3(1.0f, 0.8f, 1.4f));
            }
            PS2Spawn.Box(props, "Crate", new Vector3(0.9f, 0.9f, 0.9f), new Vector3(11.8f, 0.45f, 2.5f), wood, Quaternion.Euler(0, 15f, 0), 1f);
            PS2Spawn.Box(props, "Crate2", new Vector3(0.7f, 0.7f, 0.7f), new Vector3(11.7f, 1.25f, 2.4f), wood, Quaternion.Euler(0, -10f, 0), 1f);
            PS2Spawn.BoxCollider(props, new Vector3(11.8f, 0.6f, 2.5f), new Vector3(1f, 1.2f, 1f));
            PS2Spawn.Box(props, "Trough", new Vector3(2.4f, 0.6f, 0.8f), new Vector3(-3.5f, 0.3f, HalfZ - 0.7f), stoneDark);
            PS2Spawn.Box(props, "Water", new Vector3(2.2f, 0.05f, 0.6f), new Vector3(-3.5f, 0.55f, HalfZ - 0.7f), PS2Materials.Flat(new Color(0.25f, 0.4f, 0.45f), 1f));
            PS2Spawn.BoxCollider(props, new Vector3(-3.5f, 0.3f, HalfZ - 0.7f), new Vector3(2.4f, 0.6f, 0.8f));

            LowPolyMesh.BakeAmbient(yard.gameObject, AmbientSky, AmbientGround);

            var floorCol = yard.gameObject.AddComponent<BoxCollider>();
            floorCol.center = new Vector3(0, -0.5f, 0);
            floorCol.size = new Vector3(HalfX * 2f + 4f, 1f, HalfZ * 2f + 4f);
        }

        /// <summary>Sand plain outside the walls and the colosseum on the horizon (it's where you're going).</summary>
        void BuildSurroundings(Transform parent)
        {
            Material sand = PS2Materials.Get("sand_far", PS2Textures.Sand, new Color(0.9f, 0.85f, 0.78f));
            Material stone = PS2Materials.Get("stone", PS2Textures.Stone, Color.white);
            Material roofs = PS2Materials.Get("roof", PS2Textures.Stone, new Color(0.75f, 0.38f, 0.26f));
            Material plaster = PS2Materials.Get("plaster", PS2Textures.Stone, new Color(0.95f, 0.88f, 0.76f));

            var far = new GameObject("Surroundings").transform;
            far.SetParent(parent, false);
            var gb = new MeshBuilder();
            gb.AddPolarDisc(160f, 6, 24, -0.02f, Color.white, 1f / 6f);
            PS2Spawn.Mesh(far, "Plain", gb.Build("Plain"), Vector3.zero, sand);

            // Colosseum: three arcade storeys (dark arch "holes" baked as vertex colour bands) + attic.
            var cb = new MeshBuilder();
            const float R = 34f;
            var dark = new Color(0.45f, 0.4f, 0.36f);
            float[] ys = { 0f, 6f, 12f, 18f, 22f };
            for (int s = 0; s < 4; s++)
            {
                cb.AddLathe(new[] { new MeshBuilder.Ring(R, ys[s], 1, 0), new MeshBuilder.Ring(R, ys[s + 1], 1, 0) },
                            48, Vector3.zero, s < 3 ? Color.white : new Color(0.9f, 0.88f, 0.84f), false, 48f, 0f);
            }
            for (int s = 0; s < 3; s++)
                for (int i = 0; i < 48; i++)
                {
                    float a = (i + 0.5f) / 48f * Mathf.PI * 2f;
                    Vector3 d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    cb.AddBox(d * (R + 0.05f) + Vector3.up * (ys[s] + 3f), new Vector3(2.2f, 3.8f, 0.1f), Quaternion.LookRotation(d), dark);
                }
            var colo = PS2Spawn.Mesh(far, "Colosseum", cb.Build("Colosseum"), new Vector3(10f, 0, 95f), stone);
            LowPolyMesh.BakeAmbient(colo, AmbientSky, AmbientGround);

            // A few city blocks between the ludus and the colosseum.
            for (int i = 0; i < 14; i++)
            {
                float x = Random.Range(-60f, 60f), z = Random.Range(22f, 60f);
                float w = Random.Range(4f, 9f), h = Random.Range(3f, 8f), d = Random.Range(4f, 9f);
                var house = PS2Spawn.Box(far, "House", new Vector3(w, h, d), new Vector3(x, h * 0.5f, z), plaster, Quaternion.Euler(0, Random.Range(-15f, 15f), 0), 1f / 3f);
                PS2Spawn.Box(house.transform, "Roof", new Vector3(w + 0.4f, 0.4f, d + 0.4f), new Vector3(0, h * 0.5f + 0.2f, 0), roofs);
                LowPolyMesh.BakeAmbient(house, AmbientSky, AmbientGround);
            }
        }

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
                c[i] = Color.Lerp(FogColor, SkyTop, Mathf.Pow(h, 0.55f));
            }
            m.colors = c;
            var mat = PS2Materials.Get("sky_ludus", PS2Textures.White, Color.white, 0f, 0f, null, CullMode.Off);
            PS2Spawn.Mesh(parent, "SkyDome", m, Vector3.zero, mat);
        }
    }
}
