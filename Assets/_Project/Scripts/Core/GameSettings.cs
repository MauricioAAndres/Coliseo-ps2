using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gladiator.Core
{
    /// <summary>
    /// Player options (sound and graphics) saved in PlayerPrefs and applied on start-up and whenever they change.
    /// The pause menu edits them; PS2RenderSetup listens to <see cref="Changed"/> for the PS2 look options.
    /// </summary>
    public static class GameSettings
    {
        const string Prefix = "Gladiator.Settings.";

        // ---- sound (0..1)
        public static float MasterVolume = 1f, MusicVolume = 0.8f, SfxVolume = 1f;

        // ---- graphics
        public static bool Fullscreen = true;
        /// <summary>Index into <see cref="Resolutions"/> (-1 = the desktop resolution).</summary>
        public static int ResolutionIndex = -1;
        public static readonly int[] FpsCaps = { 30, 60, 120, 0 }; // 0 = no limit
        public static int FpsCapIndex = 1;
        public static bool VSync = true;
        public static bool PostProcessing;
        public static bool VertexSnapping;
        /// <summary>Internal render height: 448 (PS2), 720, or 0 = the window's own height.</summary>
        public static readonly int[] RenderHeights = { 448, 720, 0 };
        public static int RenderHeightIndex;

        /// <summary>0 = PS2 (the original look), 1 = PS3 (native resolution, anti-aliasing, per-pixel light, real shadows).</summary>
        public static int GraphicsPreset;
        public static bool PS3 => GraphicsPreset == 1;

        // ---- camera
        /// <summary>0 = classic high camera, 1 = over the shoulder (souls style, mouse / right stick turns it).</summary>
        public static int CameraMode;
        public static bool ShoulderCamera => CameraMode == 1;
        public static float CameraSensitivity = 1f;
        public static bool InvertY;

        public static event Action Changed;
        static bool loaded;

        public static int RenderHeight => RenderHeights[Mathf.Clamp(RenderHeightIndex, 0, RenderHeights.Length - 1)];

        /// <summary>Distinct window sizes the monitor supports, largest first.</summary>
        public static List<Vector2Int> Resolutions
        {
            get
            {
                var list = new List<Vector2Int>();
                foreach (var r in Screen.resolutions)
                {
                    var v = new Vector2Int(r.width, r.height);
                    if (v.y >= 480 && !list.Contains(v)) list.Add(v);
                }
                list.Sort((a, b) => (b.x * b.y).CompareTo(a.x * a.y));
                if (list.Count == 0) list.Add(new Vector2Int(Screen.width, Screen.height));
                return list;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            Load();
            Apply(false);
        }

        public static void Load()
        {
            loaded = true;
            MasterVolume = PlayerPrefs.GetFloat(Prefix + "Master", 1f);
            MusicVolume = PlayerPrefs.GetFloat(Prefix + "Music", 0.8f);
            SfxVolume = PlayerPrefs.GetFloat(Prefix + "Sfx", 1f);
            Fullscreen = PlayerPrefs.GetInt(Prefix + "Fullscreen", Screen.fullScreen ? 1 : 0) == 1;
            ResolutionIndex = PlayerPrefs.GetInt(Prefix + "Resolution", -1);
            FpsCapIndex = PlayerPrefs.GetInt(Prefix + "FpsCap", 1);
            VSync = PlayerPrefs.GetInt(Prefix + "VSync", 1) == 1;
            PostProcessing = PlayerPrefs.GetInt(Prefix + "Post", 0) == 1;
            VertexSnapping = PlayerPrefs.GetInt(Prefix + "Snap", 0) == 1;
            RenderHeightIndex = PlayerPrefs.GetInt(Prefix + "RenderHeight", 0);
            GraphicsPreset = PlayerPrefs.GetInt(Prefix + "Preset", 0);
            CameraMode = PlayerPrefs.GetInt(Prefix + "CameraMode", 0);
            CameraSensitivity = PlayerPrefs.GetFloat(Prefix + "CameraSens", 1f);
            InvertY = PlayerPrefs.GetInt(Prefix + "InvertY", 0) == 1;
        }

        public static void Save()
        {
            PlayerPrefs.SetFloat(Prefix + "Master", MasterVolume);
            PlayerPrefs.SetFloat(Prefix + "Music", MusicVolume);
            PlayerPrefs.SetFloat(Prefix + "Sfx", SfxVolume);
            PlayerPrefs.SetInt(Prefix + "Fullscreen", Fullscreen ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "Resolution", ResolutionIndex);
            PlayerPrefs.SetInt(Prefix + "FpsCap", FpsCapIndex);
            PlayerPrefs.SetInt(Prefix + "VSync", VSync ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "Post", PostProcessing ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "Snap", VertexSnapping ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "RenderHeight", RenderHeightIndex);
            PlayerPrefs.SetInt(Prefix + "Preset", GraphicsPreset);
            PlayerPrefs.SetInt(Prefix + "CameraMode", CameraMode);
            PlayerPrefs.SetFloat(Prefix + "CameraSens", CameraSensitivity);
            PlayerPrefs.SetInt(Prefix + "InvertY", InvertY ? 1 : 0);
            PlayerPrefs.Save();
        }

        /// <summary>Applies everything. Window mode/size only when asked (it re-creates the swap chain).</summary>
        public static void Apply(bool window)
        {
            if (!loaded) Load();
            AudioListener.volume = Mathf.Clamp01(MasterVolume);
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            int cap = FpsCaps[Mathf.Clamp(FpsCapIndex, 0, FpsCaps.Length - 1)];
            Application.targetFrameRate = cap > 0 ? cap : -1;
            if (window && !Application.isEditor)
            {
                var res = Resolutions;
                Vector2Int size = ResolutionIndex >= 0 && ResolutionIndex < res.Count
                    ? res[ResolutionIndex]
                    : new Vector2Int(Screen.currentResolution.width, Screen.currentResolution.height);
                Screen.SetResolution(size.x, size.y, Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
            }
            ApplyPreset();
            Changed?.Invoke();
        }

        static readonly int QualityId = Shader.PropertyToID("_PS2_Quality");

        /// <summary>
        /// PS2 / PS3 look: shader quality (per-vertex vs per-pixel light with real shadows and a little shine),
        /// sun shadows, shadow distance, MSAA and anisotropic filtering. Scenes call it again once their sun exists.
        /// </summary>
        public static void ApplyPreset()
        {
            bool hi = PS3;
            Shader.SetGlobalFloat(QualityId, hi ? 1f : 0f);
            QualitySettings.anisotropicFiltering = hi ? AnisotropicFiltering.ForceEnable : AnisotropicFiltering.Disable;
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.shadowDistance = hi ? 40f : 0f;
                urp.shadowCascadeCount = hi ? 2 : 1;
                urp.msaaSampleCount = hi ? 4 : 1;
            }
            var sun = RenderSettings.sun;
            if (sun != null)
            {
                sun.shadows = hi ? LightShadows.Soft : LightShadows.None;
                sun.shadowStrength = 0.8f;
                sun.shadowBias = 0.05f;
                sun.shadowNormalBias = 0.4f;
            }
        }

        /// <summary>F1 / F2 hotkeys keep the saved options in sync.</summary>
        public static void SetPs2Look(bool post, bool snap)
        {
            PostProcessing = post;
            VertexSnapping = snap;
            Save();
            Changed?.Invoke();
        }
    }
}
