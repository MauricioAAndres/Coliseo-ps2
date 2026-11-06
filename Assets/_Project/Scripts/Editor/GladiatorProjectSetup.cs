using System;
using System.IO;
using Gladiator.Arena;
using Gladiator.Character;
using Gladiator.Training;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gladiator.EditorTools
{
    /// <summary>
    /// One-click project configuration for the PS2 look (menu "Gladiator") and batch-mode entry points:
    ///   Unity.exe -batchmode -projectPath ... -executeMethod Gladiator.EditorTools.GladiatorProjectSetup.BatchSetup
    ///   Unity.exe -batchmode -projectPath ... -executeMethod Gladiator.EditorTools.GladiatorProjectSetup.BatchBuild
    /// </summary>
    public static class GladiatorProjectSetup
    {
        const string Root = "Assets/_Project";
        const string SettingsDir = Root + "/Settings";
        const string ScenesDir = Root + "/Scenes";
        public const string ArenaScenePath = ScenesDir + "/Arena.unity";
        public const string TrainingScenePath = ScenesDir + "/Training.unity";
        public const string CreatorScenePath = ScenesDir + "/Creator.unity";
        const string LitShaderPath = Root + "/Shaders/PS2_Lit.shader";
        const string PostShaderPath = Root + "/Shaders/PS2_Post.shader";

        [MenuItem("Gladiator/1. Configurar proyecto (URP + estilo PS2)")]
        public static void ConfigureProject()
        {
            EnsureFolder(SettingsDir);
            EnsureFolder(ScenesDir);

            // --- URP asset: Forward path (needed for per-vertex additional lights), no MSAA, no HDR, no shadows.
            string rdPath = SettingsDir + "/PS2_Renderer.asset";
            var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rdPath);
            if (rd == null)
            {
                rd = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rd, rdPath);
            }
            rd.renderingMode = RenderingMode.Forward;
            EditorUtility.SetDirty(rd);

            string pPath = SettingsDir + "/PS2_URP.asset";
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pPath);
            if (asset == null)
            {
                asset = UniversalRenderPipelineAsset.Create(rd);
                AssetDatabase.CreateAsset(asset, pPath);
            }
            asset.msaaSampleCount = 1;
            asset.supportsHDR = false;
            asset.renderScale = 1f;
            asset.shadowDistance = 0f;
            asset.maxAdditionalLightsCount = 8;
            EditorUtility.SetDirty(asset);

            GraphicsSettings.defaultRenderPipeline = asset;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = asset;
                QualitySettings.antiAliasing = 0;
                QualitySettings.shadows = UnityEngine.ShadowQuality.Disable;
                QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;
                QualitySettings.vSyncCount = 1;
            }
            QualitySettings.SetQualityLevel(current, false);

            PlayerSettings.productName = "Coliseo PS2 Prototype";
            PlayerSettings.companyName = "GladiatorProto";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 896;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;

            AssetDatabase.SaveAssets();
            Debug.Log("[Gladiator] Project configured: URP Forward, MSAA off, HDR off, shadows off, PS2 materials.");
        }

        [MenuItem("Gladiator/2. Generar escena Arena")]
        public static void CreateArenaScene()
        {
            EnsureFolder(ScenesDir);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("ArenaBootstrap");
            var boot = go.AddComponent<ArenaBootstrap>();
            boot.PS2LitShader = AssetDatabase.LoadAssetAtPath<Shader>(LitShaderPath);
            boot.PS2PostShader = AssetDatabase.LoadAssetAtPath<Shader>(PostShaderPath);
            if (boot.PS2LitShader == null || boot.PS2PostShader == null)
                Debug.LogError("[Gladiator] PS2 shaders not found under " + Root + "/Shaders");
            EditorSceneManager.SaveScene(scene, ArenaScenePath);
            UpdateBuildScenes();
            Debug.Log("[Gladiator] Scene created: " + ArenaScenePath);
        }

        [MenuItem("Gladiator/3. Generar escena Training (ludus)")]
        public static void CreateTrainingScene()
        {
            EnsureFolder(ScenesDir);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("TrainingBootstrap");
            var boot = go.AddComponent<TrainingBootstrap>();
            boot.PS2LitShader = AssetDatabase.LoadAssetAtPath<Shader>(LitShaderPath);
            boot.PS2PostShader = AssetDatabase.LoadAssetAtPath<Shader>(PostShaderPath);
            if (boot.PS2LitShader == null || boot.PS2PostShader == null)
                Debug.LogError("[Gladiator] PS2 shaders not found under " + Root + "/Shaders");
            EditorSceneManager.SaveScene(scene, TrainingScenePath);
            UpdateBuildScenes();
            Debug.Log("[Gladiator] Scene created: " + TrainingScenePath);
        }

        [MenuItem("Gladiator/4. Generar escena Creador de personaje")]
        public static void CreateCreatorScene()
        {
            EnsureFolder(ScenesDir);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("CreatorBootstrap");
            var boot = go.AddComponent<CreatorBootstrap>();
            boot.PS2LitShader = AssetDatabase.LoadAssetAtPath<Shader>(LitShaderPath);
            boot.PS2PostShader = AssetDatabase.LoadAssetAtPath<Shader>(PostShaderPath);
            if (boot.PS2LitShader == null || boot.PS2PostShader == null)
                Debug.LogError("[Gladiator] PS2 shaders not found under " + Root + "/Shaders");
            EditorSceneManager.SaveScene(scene, CreatorScenePath);
            UpdateBuildScenes();
            Debug.Log("[Gladiator] Scene created: " + CreatorScenePath);
        }

        /// <summary>Creator first (the game starts by making the gladiator), then the ludus, then the arena.</summary>
        static void UpdateBuildScenes()
        {
            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>();
            if (File.Exists(CreatorScenePath)) list.Add(new EditorBuildSettingsScene(CreatorScenePath, true));
            if (File.Exists(TrainingScenePath)) list.Add(new EditorBuildSettingsScene(TrainingScenePath, true));
            if (File.Exists(ArenaScenePath)) list.Add(new EditorBuildSettingsScene(ArenaScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        [MenuItem("Gladiator/Configurar todo y abrir el Creador")]
        public static void SetupAll()
        {
            ConfigureProject();
            CreateArenaScene();
            CreateTrainingScene();
            CreateCreatorScene();
            EditorSceneManager.OpenScene(CreatorScenePath);
        }

        public static void BatchSetup()
        {
            try
            {
                ConfigureProject();
                CreateArenaScene();
                CreateTrainingScene();
                CreateCreatorScene();
                AssetDatabase.SaveAssets();
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>The PS3 look needs main-light shadows and soft shadows compiled into the build (the PS2 look keeps
        /// shadowDistance 0, so nothing is drawn with them unless the PS3 preset is on).</summary>
        static void EnsurePs3Support()
        {
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(SettingsDir + "/PS2_URP.asset");
            if (asset == null) return;
            var so = new SerializedObject(asset);
            void SetBool(string name, bool v) { var p = so.FindProperty(name); if (p != null) p.boolValue = v; }
            SetBool("m_MainLightShadowsSupported", true);
            SetBool("m_SoftShadowsSupported", true);
            SetBool("m_AnyShadowsSupported", true);
            // A shadow distance above 0 keeps the shadow shader variants in the build; the game sets it per look.
            var dist = so.FindProperty("m_ShadowDistance");
            if (dist != null) dist.floatValue = 40f;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
        }

        public static void BatchBuild()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (!File.Exists(CreatorScenePath)) CreateCreatorScene();
            EnsurePs3Support();
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { CreatorScenePath, TrainingScenePath, ArenaScenePath },
                locationPathName = Path.Combine(projectRoot, "Builds", "Win64", "ColiseoPS2.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };
            BuildReport report = BuildPipeline.BuildPlayer(opts);
            Debug.Log("[Gladiator] Build result: " + report.summary.result + " errors=" + report.summary.totalErrors);
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
