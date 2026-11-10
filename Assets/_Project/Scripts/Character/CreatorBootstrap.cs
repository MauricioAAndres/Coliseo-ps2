using System.Collections.Generic;
using Gladiator.Core;
using Gladiator.PS2Style;
using Gladiator.UI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Gladiator.Character
{
    /// <summary>
    /// Character creator scene (first scene of the game): a torch-lit room under the ludus with the gladiator on
    /// a turning stone plinth, the creator menu on the left. Every edit rebuilds the rig + mesh. "Finish" stores
    /// the look in the player profile (and PlayerPrefs) and opens the ludus.
    /// </summary>
    public class CreatorBootstrap : MonoBehaviour
    {
        [Header("Shaders (assigned by the scene builder so they're included in builds)")]
        public Shader PS2LitShader;
        public Shader PS2PostShader;

        public const string NextScene = "Town"; // the ludus is inside the town
        static readonly Color FogColor = new Color(0.12f, 0.08f, 0.06f);
        static readonly Color AmbientSky = new Color(0.46f, 0.4f, 0.4f);
        static readonly Color AmbientGround = new Color(0.3f, 0.2f, 0.14f);
        const float PlinthTop = 0.3f;

        public CharacterAppearance Look { get; private set; }
        public HumanoidRig Rig { get; private set; }
        public CharacterCreatorMenu Menu { get; private set; }
        public Camera Cam { get; private set; }
        public float Yaw { get; set; } = 20f;

        /// <summary>Animation preview on the plinth (MotionBricks clips played in place).</summary>
        public static readonly string[] PreviewNames =
            { "REPOSO", "ANDAR", "CORRER", "LATERAL", "ATRAS", "GUARDIA", "ATAQUE LIGERO", "ATAQUE FUERTE", "BLOQUEO", "IMPACTO", "MUERTE",
              "ESQUIVAR", "PASO ATRAS", "SALTAR", "CAER", "ATERRIZAR", "AGACHARSE", "AGACHADO ANDAR", "ESCALAR", "TREPAR", "PATADA", "ATURDIDO" };
        static readonly Vector3?[] PreviewVelocities =
            { null, new Vector3(0, 0, 1.4f), new Vector3(0, 0, 4.4f), new Vector3(3.6f, 0, 0), new Vector3(0, 0, -3f),
              null, null, null, null, null, null,
              null, null, null, null, null, null, new Vector3(0, 0, 1.1f), null, null, null, null };
        static readonly string[] PreviewActions =
            { null, null, null, null, null, "guard", "sword_light", "sword_heavy", "block", "stagger", "death",
              "dodge", "dodge_back", "jump", "fall", "land", "crouch", "crouch", "vault", "climb", "kick", "stunned" };
        Transform previewSocket;
        Combat.Weapon previewSword;
        public int PreviewAnim { get; private set; }

        Transform preview, previewBody, plinth;
        HumanoidPoser poser;
        int focusTab;
        bool dirty;
        Vector3 camTarget;
        float camDist;
        bool camSnapped;

        void Awake()
        {
            Time.timeScale = 1f;
            var gm = GameManager.Ensure();
            PS2Materials.Init(PS2LitShader);
            SetupRenderSettings();
            Look = gm.Profile.Appearance.Clone();

            var env = new GameObject("Environment").transform;
            BuildRoom(env);
            Cam = BuildCamera();

            preview = new GameObject("Preview").transform;
            preview.position = new Vector3(0, PlinthTop, 0);
            previewBody = new GameObject("Body").transform;
            previewBody.SetParent(preview, false);
            poser = preview.gameObject.AddComponent<HumanoidPoser>();
            Rebuild();

            var canvasGo = new GameObject("CreatorHUD", typeof(RectTransform));
            var root = PS2UI.CreateCanvas(canvasGo, Cam);
            Menu = CharacterCreatorMenu.Create(root, Look);
            Menu.Changed += () => dirty = true;
            Menu.TabChanged += t => focusTab = t;
            Menu.Randomize += RandomizeLook;
            Menu.Finished += Finish;
            Rebuild();

            if (GameManager.AutoTest) gameObject.AddComponent<CreatorAutoTest>().Init(this);
        }

        /// <summary>Armour shown on the plinth (none by default: armour is bought from the mercator). Autotest only.</summary>
        public ArmorSet PreviewArmor;

        public void Rebuild()
        {
            if (Rig != null)
            {
                if (Rig.Renderer != null) Destroy(Rig.Renderer.sharedMesh);
                Destroy(Rig.gameObject);
            }
            Rig = HumanoidBuilder.Build(previewBody, Look, AmbientSky, AmbientGround, PreviewArmor ?? ArmorSet.Empty);
            poser.Init(Rig);
            Gladiator.PS2Style.PlanarShadow.Attach(preview, Rig.Renderer);
            ApplyPreview();
            if (Menu != null) Menu.Refresh();
            if (Menu != null)
                UpdateInfo();
            dirty = false;
        }

        void UpdateInfo()
        {
            if (Menu == null || Rig == null) return;
            Menu.SetInfo(Rig.Triangles + " tris   SOMA 30   " + Look.HeightMeters.ToString("0.00").Replace('.', ',') + " m   ANIM: " +
                         PreviewNames[PreviewAnim] + (poser.UsingClips ? "" : " (procedural)"));
        }

        public bool UsingClips => poser != null && poser.UsingClips;
        public string ActionPlaying => poser != null ? poser.ActionPlaying : null;

        /// <summary>Locomotion previews set a velocity; combat previews put a gladius in the hand and play the clip.</summary>
        void ApplyPreview()
        {
            poser.PreviewVelocity = PreviewVelocities[PreviewAnim];
            string action = PreviewActions[PreviewAnim];
            poser.PreviewAction = action;
            if (action != null && previewSword == null)
            {
                previewSocket = new GameObject("PreviewSocket").transform;
                previewSocket.SetParent(preview, false);
                previewSword = Combat.WeaponFactory.Create(Combat.WeaponLibrary.Gladius);
                previewSword.AttachTo(null, previewSocket);
            }
            if (previewSword != null) previewSword.gameObject.SetActive(action != null);
            poser.WeaponSocket = action != null ? previewSocket : null;
        }

        public void SetPreviewAnim(int i)
        {
            PreviewAnim = (i % PreviewNames.Length + PreviewNames.Length) % PreviewNames.Length;
            ApplyPreview();
            UpdateInfo();
        }

        public void RandomizeLook()
        {
            var r = CharacterAppearance.Random(new System.Random(System.Environment.TickCount ^ Random.Range(0, int.MaxValue)));
            CopyInto(r, Look);
            Rebuild();
        }

        /// <summary>Copies all fields into the instance the menu edits (the menu keeps its reference).</summary>
        public static void CopyInto(CharacterAppearance src, CharacterAppearance dst)
        {
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(src), dst);
        }

        public void Finish()
        {
            var gm = GameManager.Ensure();
            gm.Profile.Appearance = Look.Clone();
            gm.Profile.Name = Look.Name;
            if (!GameManager.AutoTest) Look.Save();
            Debug.Log("[CREATOR] finished: " + JsonUtility.ToJson(Look));
            SceneManager.LoadScene(NextScene);
        }

        void Update()
        {
            if (dirty) Rebuild();
            if (Menu != null && !Menu.Confirming)
            {
                Yaw += GameInput.PreviewTurn(Time.unscaledDeltaTime);
                if (GameInput.PreviewAnimPressed) SetPreviewAnim(PreviewAnim + 1);
            }
            preview.rotation = Quaternion.Euler(0, Yaw, 0);
            plinth.rotation = preview.rotation;
        }

        void LateUpdate()
        {
            // Frame the whole body, or the head on the CABEZA tab. The character sits right of centre (menu on the left).
            var m = Rig.Metrics;
            bool head = focusTab == 1;
            Vector3 focus = head ? new Vector3(0, PlinthTop + m.ChinY + m.HeadH * 0.25f, 0) : new Vector3(0, PlinthTop + m.H * 0.53f, 0);
            float dist = head ? 1.55f : 1.5f + m.H * 1.2f;
            float k = camSnapped ? 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime) : 1f;
            camSnapped = true;
            camTarget = Vector3.Lerp(camTarget, focus, k);
            camDist = Mathf.Lerp(camDist, dist, k);
            // Camera on +Z looking at -Z; shifting it along +X puts the character on the right of the screen.
            Vector3 side = new Vector3(camDist * 0.26f, 0, 0);
            Vector3 camPos = camTarget + side + new Vector3(0, camDist * (head ? 0.06f : 0.14f), camDist);
            Cam.transform.position = camPos;
            Cam.transform.rotation = Quaternion.LookRotation(camTarget + side - camPos);
        }

        // ------------------------------------------------------------------ scene
        void SetupRenderSettings()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = FogColor;
            RenderSettings.fogStartDistance = 7f;
            RenderSettings.fogEndDistance = 20f;
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
            cam.fieldOfView = 38f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 60f;
            cam.allowMSAA = false;
            cam.allowHDR = false;
            var ps2 = go.AddComponent<PS2RenderSetup>();
            ps2.PostShader = PS2PostShader;
            return cam;
        }

        void BuildRoom(Transform parent)
        {
            Material stone = PS2Materials.Get("stone", PS2Textures.Stone, Color.white);
            Material darkStone = PS2Materials.Get("stone_dark", PS2Textures.Stone, new Color(0.62f, 0.56f, 0.52f));
            Material sand = PS2Materials.Get("sand", PS2Textures.Sand, Color.white);
            Material wood = PS2Materials.Get("wood", PS2Textures.Wood, Color.white);
            Material iron = PS2Materials.Get("iron", PS2Textures.Metal, new Color(0.45f, 0.42f, 0.4f));
            Material flame = PS2Materials.Flat(new Color(1f, 0.62f, 0.15f), 0.2f);
            Material red = PS2Materials.Get("cloth_banner", PS2Textures.Cloth, new Color(0.62f, 0.1f, 0.08f));

            var floor = new MeshBuilder();
            floor.AddPolarDisc(9f, 6, 24, 0f, Color.white, 0.35f);
            PS2Spawn.Mesh(parent, "Floor", floor.Build("CreatorFloor"), Vector3.zero, sand);

            // Semicircular stone wall behind the plinth (the camera looks at -Z).
            var wall = new MeshBuilder();
            var prof = new List<MeshBuilder.Ring> { new MeshBuilder.Ring(7f, 0f, 1f, 0f), new MeshBuilder.Ring(7f, 3f, 1f, 0f), new MeshBuilder.Ring(7f, 6f, 1f, 0f) };
            wall.AddLathe(prof, 24, Vector3.zero, Color.white, true, 6f, 0.5f);
            PS2Spawn.Mesh(parent, "Wall", wall.Build("CreatorWall"), Vector3.zero, stone);

            // Plinth (turns with the character).
            plinth = new GameObject("Plinth").transform;
            plinth.SetParent(parent, false);
            PS2Spawn.Mesh(plinth, "Base", LowPolyMesh.Cylinder(1.0f, PlinthTop - 0.08f, 12), Vector3.zero, darkStone);
            PS2Spawn.Mesh(plinth, "Top", LowPolyMesh.Cylinder(0.92f, 0.08f, 12), new Vector3(0, PlinthTop - 0.08f, 0), stone);

            // Columns, banners and torches around the back.
            for (int i = 0; i < 5; i++)
            {
                float a = Mathf.Lerp(200f, 340f, i / 4f) * Mathf.Deg2Rad;
                Vector3 p = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 6.3f;
                PS2Spawn.Mesh(parent, "Column", LowPolyMesh.Cylinder(0.32f, 5.5f, 8), p, stone);
                if (i % 2 == 1)
                {
                    Vector3 bp = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 6.85f;
                    PS2Spawn.Box(parent, "Banner", new Vector3(1.1f, 2.6f, 0.04f), bp + Vector3.up * 3.2f, red, Quaternion.LookRotation(-bp.normalized));
                }
            }
            foreach (float x in new[] { -2.6f, 2.6f })
            {
                var t = new GameObject("Torch").transform;
                t.SetParent(parent, false);
                t.position = new Vector3(x, 0, -2.2f);
                PS2Spawn.Mesh(t, "Pole", LowPolyMesh.Cylinder(0.06f, 1.9f, 6), Vector3.zero, wood);
                PS2Spawn.Mesh(t, "Bowl", LowPolyMesh.Cylinder(0.2f, 0.16f, 6), new Vector3(0, 1.9f, 0), iron);
                PS2Spawn.Mesh(t, "F", LowPolyMesh.Box(new Vector3(0.22f, 0.36f, 0.22f), new Vector3(0, 0.18f, 0)), new Vector3(0, 2.04f, 0), flame, Quaternion.Euler(0, 45f, 0));
                var l = new GameObject("TorchLight").AddComponent<Light>();
                l.transform.SetParent(t, false);
                l.transform.localPosition = new Vector3(0, 2.3f, 0.3f);
                l.type = LightType.Point;
                l.color = new Color(1f, 0.6f, 0.3f);
                l.range = 7f;
                l.intensity = 1.6f;
                l.shadows = LightShadows.None;
                var tf = t.gameObject.AddComponent<Arena.TorchFlicker>();
                tf.Light = l;
                tf.BaseIntensity = 1.6f;
            }

            var key = new GameObject("Key").AddComponent<Light>();
            key.transform.SetParent(parent, false);
            key.transform.rotation = Quaternion.Euler(30f, 200f, 0f);
            key.type = LightType.Directional;
            key.color = new Color(1f, 0.9f, 0.78f);
            key.intensity = 0.9f;
            key.shadows = LightShadows.None;
            RenderSettings.sun = key;

            LowPolyMesh.BakeAmbient(parent.gameObject, AmbientSky, AmbientGround);
        }
    }
}
