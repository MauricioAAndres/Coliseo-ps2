using Gladiator.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Gladiator.PS2Style
{
    /// <summary>
    /// Renders the game camera into a low-resolution RenderTexture (448 lines, like NTSC PS2 games)
    /// and presents it full-screen with point filtering through the PS2_Post shader
    /// (16-bit dithering, scanlines, slight chromatic aberration). Also drives global vertex snapping.
    /// Hotkeys: F1 post-process on/off, F2 vertex snapping on/off.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class PS2RenderSetup : MonoBehaviour
    {
        public static PS2RenderSetup Instance { get; private set; }

        [Range(224, 720)] public int VerticalResolution = 448;
        public Shader PostShader;
        [Tooltip("Off by default (F1 turns it on).")] public bool PostProcessing = false;
        [Tooltip("Off by default (F2 turns it on).")] public bool VertexSnapping = false;
        [Tooltip("Vertices snap to a grid of this many low-res pixels.")]
        [Range(1f, 4f)] public float SnapPixelSize = 1.5f;
        [Range(0f, 0.5f)] public float Scanlines = 0.10f;
        [Range(0f, 3f)] public float ChromaticAberration = 0.8f;
        public float ColorLevels = 32f;
        [Range(0f, 1f)] public float Dither = 1f;

        static readonly int SnapId = Shader.PropertyToID("_PS2_SnapRes");

        Camera cam;
        RenderTexture rt;
        RawImage output;
        Material postMat;
        int lastW = -1, lastH = -1;

        bool ps3;
        /// <summary>PS3 look: native resolution; PS2: the chosen internal height.</summary>
        static int EffectiveHeight => GameSettings.PS3 ? 0 : GameSettings.RenderHeight;

        public Camera GameCamera => cam;

        void Start() { GameSettings.ApplyPreset(); } // the scene's sun exists by now
        public RenderTexture Target => rt;

        void Awake()
        {
            Instance = this;
            cam = GetComponent<Camera>();
            // Options (PS2 filter, vertex snap, internal resolution) come from the saved settings.
            PostProcessing = GameSettings.PostProcessing;
            VertexSnapping = GameSettings.VertexSnapping;
            VerticalResolution = EffectiveHeight;
            ps3 = GameSettings.PS3;
            GameSettings.Changed += OnSettingsChanged;
            cam.allowMSAA = false;
            cam.allowHDR = false;
            if (PostShader == null) PostShader = Shader.Find("Gladiator/PS2_Post");
            if (PostShader != null) postMat = new Material(PostShader) { name = "PS2_Post (runtime)" };
            BuildOutput();
            Rebuild();
        }

        void BuildOutput()
        {
            var ocGo = new GameObject("PS2_OutputCamera");
            var oc = ocGo.AddComponent<Camera>();
            oc.cullingMask = 0;
            oc.clearFlags = CameraClearFlags.SolidColor;
            oc.backgroundColor = Color.black;
            oc.depth = cam.depth + 10;
            oc.allowMSAA = false;
            oc.allowHDR = false;
            oc.useOcclusionCulling = false;

            var cgo = new GameObject("PS2_OutputCanvas");
            var canvas = cgo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -100;

            var img = new GameObject("Frame", typeof(RectTransform));
            img.transform.SetParent(cgo.transform, false);
            var rtf = (RectTransform)img.transform;
            rtf.anchorMin = Vector2.zero; rtf.anchorMax = Vector2.one;
            rtf.offsetMin = Vector2.zero; rtf.offsetMax = Vector2.zero;
            output = img.AddComponent<RawImage>();
            output.raycastTarget = false;
        }

        void Rebuild()
        {
            lastW = Mathf.Max(1, Screen.width);
            lastH = Mathf.Max(1, Screen.height);
            int h = VerticalResolution > 0 ? VerticalResolution : lastH;
            int w = Mathf.Max(64, Mathf.RoundToInt(h * lastW / (float)lastH));

            if (rt != null)
            {
                cam.targetTexture = null;
                rt.Release();
                Destroy(rt);
            }
            rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32)
            {
                name = ps3 ? "PS3_Frame" : "PS2_LowRes",
                filterMode = ps3 ? FilterMode.Bilinear : FilterMode.Point,
                antiAliasing = ps3 ? 4 : 1,
                useMipMap = false,
                autoGenerateMips = false,
                wrapMode = TextureWrapMode.Clamp
            };
            rt.Create();
            uiCheck = 0f;
            cam.targetTexture = rt;
            output.texture = rt;
            Apply();
        }

        void Apply()
        {
            if (rt == null) return;
            Shader.SetGlobalVector(SnapId, new Vector4(rt.width / SnapPixelSize, rt.height / SnapPixelSize, VertexSnapping && !ps3 ? 1f : 0f, 0f));
            if (postMat != null)
            {
                postMat.SetVector("_SrcRes", new Vector4(rt.width, rt.height, 1f / rt.width, 1f / rt.height));
                postMat.SetFloat("_Scanline", Scanlines);
                postMat.SetFloat("_Chroma", ChromaticAberration);
                postMat.SetFloat("_ColorLevels", ColorLevels);
                postMat.SetFloat("_Dither", Dither);
            }
            output.material = PostProcessing && !ps3 ? postMat : null;
        }

        float uiCheck;

        /// <summary>The HUD canvases draw into the game frame: scale them with its height so they keep their size
        /// (448 lines = 1x) at 720, native or the PS3 look.</summary>
        void ScaleUi()
        {
            if (rt == null || Time.unscaledTime < uiCheck) return;
            uiCheck = Time.unscaledTime + 0.5f;
            float f = Mathf.Max(1f, rt.height / 448f);
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (c.worldCamera != cam || !c.isRootCanvas) continue;
                var sc = c.GetComponent<UnityEngine.UI.CanvasScaler>();
                if (sc != null && sc.uiScaleMode == UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize && !Mathf.Approximately(sc.scaleFactor, f))
                    sc.scaleFactor = f;
            }
        }

        void Update()
        {
            ScaleUi();
            if (Screen.width != lastW || Screen.height != lastH) Rebuild();
            if (GameInput.TogglePostPressed) GameSettings.SetPs2Look(!PostProcessing, VertexSnapping);
            if (GameInput.ToggleSnapPressed) GameSettings.SetPs2Look(PostProcessing, !VertexSnapping);
            Apply();
        }

        /// <summary>Ray from a screen-space position (window pixels) through the low-res camera.</summary>
        public Ray ScreenPointToRay(Vector2 screenPos)
        {
            return cam.ViewportPointToRay(new Vector3(screenPos.x / Mathf.Max(1, Screen.width), screenPos.y / Mathf.Max(1, Screen.height), 0f));
        }

        void OnSettingsChanged()
        {
            PostProcessing = GameSettings.PostProcessing;
            VertexSnapping = GameSettings.VertexSnapping;
            if (VerticalResolution != EffectiveHeight || ps3 != GameSettings.PS3)
            {
                VerticalResolution = EffectiveHeight;
                ps3 = GameSettings.PS3;
                if (output != null) Rebuild();
            }
        }

        void OnDestroy()
        {
            GameSettings.Changed -= OnSettingsChanged;
            if (Instance == this) Instance = null;
            Shader.SetGlobalVector(SnapId, Vector4.zero);
            if (cam != null) cam.targetTexture = null;
            if (rt != null) { rt.Release(); Destroy(rt); }
            if (postMat != null) Destroy(postMat);
        }
    }
}
