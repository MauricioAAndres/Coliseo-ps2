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

        /// <summary>The town of this PC: the seed is made once and kept (-townseed N overrides it, for tests).</summary>
        public static int CurrentSeed()
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

            Layout = TownLayout.Generate(CurrentSeed());
            var env = new GameObject("Environment").transform;
            BuildLighting(env);
            var builder = new TownBuilder(Layout, env);
            builder.Build();
            Buildings = builder.BuildingCount;
            BuildSky(env);
            Camera cam = BuildCamera();

            Vector3 spawn = Layout.LudusGate + Layout.LudusFacing * 3.5f + Vector3.up * 0.05f; // clear of the ludus wall (camera)
            Player = CharacterFactory.Create(gm.Profile.Name, gm.Profile.Appearance, spawn, Quaternion.LookRotation(Layout.LudusFacing), 0, gm.Profile.ArmorSet);
            Player.Equip(WeaponFactory.Create(WeaponLibrary.Rudis));
            Player.DisarmResistance = 1f;
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
            Hud.SetHint("WASD mover   E interactuar   puerta del ludus: volver   puerta de la arena: elegir combate   ESC menu");
            gameObject.AddComponent<TownManager>().Init(this, pc);
            Hud.ShowMessage("EL PUEBLO", 2.5f);
            Debug.Log($"[TOWN] seed={Layout.Seed} rot={Layout.RotationDeg} mirrored={Layout.Mirrored} lots={Layout.Lots.Count} places={Layout.Places.Count} buildings={Buildings}");
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

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
        PlayerController pc;
        bool leaving;

        public void Init(TownBootstrap t, PlayerController controller) { town = t; pc = controller; }

        void Update()
        {
            GameInput.UpdateDeviceMode();
            if (town == null || leaving) return;
            if (ArenaGateMenu.Instance != null && ArenaGateMenu.Instance.IsOpen) { town.Hud.SetPrompt(""); return; }
            string key = GameInput.UsingGamepad ? "(A) " : "[" + InputBindings.Short(GameAction.Interact) + "] ";
            Vector3 p = town.Player.transform.position;
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
