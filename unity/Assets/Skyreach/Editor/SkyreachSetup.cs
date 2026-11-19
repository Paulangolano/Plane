using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Skyreach.EditorTools
{
    /// <summary>
    /// One-click project setup for Meta Quest, available from the "Skyreach" menu:
    ///   1. Install Quest Packages   (XR Plug-in Management + OpenXR)
    ///   2. Configure for Meta Quest (Android, IL2CPP/ARM64, Vulkan, ASTC, Linear, OpenXR)
    ///   3. Create Game Scene
    ///   4. Build APK / Build And Run on Quest
    /// </summary>
    public static class SkyreachSetup
    {
        const string ScenePath = "Assets/Skyreach/Scenes/Skyreach.unity";
        const string ShaderDir = "Assets/Skyreach/Shaders/";
        const string ApkPath = "Builds/SkyreachVR.apk";

        static AddAndRemoveRequest packageRequest;

        // ------------------------------------------------------------------ 1. packages
        [MenuItem("Skyreach/1. Install Quest Packages", priority = 1)]
        public static void InstallPackages()
        {
            packageRequest = Client.AddAndRemove(new[] { "com.unity.xr.management", "com.unity.xr.openxr" }, null);
            EditorApplication.update += WaitForPackages;
            Debug.Log("[Skyreach] Installing XR Plug-in Management and OpenXR… (Unity may ask to enable the new Input System and restart: choose Yes.)");
        }

        static void WaitForPackages()
        {
            if (packageRequest == null || !packageRequest.IsCompleted) return;
            EditorApplication.update -= WaitForPackages;
            if (packageRequest.Status == StatusCode.Success)
                Debug.Log("[Skyreach] XR packages installed. Next: Skyreach ▸ 2. Configure for Meta Quest.");
            else
                Debug.LogError("[Skyreach] Package install failed: " + packageRequest.Error?.message);
            packageRequest = null;
        }

        // ------------------------------------------------------------------ 2. project settings
        [MenuItem("Skyreach/2. Configure for Meta Quest", priority = 2)]
        public static void ConfigureForQuest()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

            var android = NamedBuildTarget.Android;
            PlayerSettings.companyName = "Skyreach";
            PlayerSettings.productName = "Skyreach VR";
            PlayerSettings.SetApplicationIdentifier(android, "com.skyreach.vrflightsim");
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;

            // Lean quality settings: no shadows (the game uses none), 4x MSAA for crisp edges in VR.
            QualitySettings.shadows = ShadowQuality.Disable;
            QualitySettings.antiAliasing = 4;
            QualitySettings.vSyncCount = 0;

            bool xrOk = XRConfigHook.Configure != null && XRConfigHook.Configure();
            AssetDatabase.SaveAssets();
            if (xrOk)
                Debug.Log("[Skyreach] Project configured for Meta Quest (OpenXR enabled). Next: Skyreach ▸ 3. Create Game Scene.");
            else
                Debug.LogWarning("[Skyreach] Player settings done, but OpenXR could not be configured automatically. " +
                                 "Run 'Skyreach ▸ 1. Install Quest Packages' first, or enable it by hand: Edit ▸ Project Settings ▸ " +
                                 "XR Plug-in Management ▸ Android tab ▸ tick OpenXR + 'Meta Quest feature group', then OpenXR ▸ " +
                                 "Interaction Profiles ▸ add 'Oculus Touch Controller Profile'.");
        }

        // ------------------------------------------------------------------ 3. scene
        [MenuItem("Skyreach/3. Create Game Scene", priority = 3)]
        public static void CreateScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("Skyreach");
            var gm = go.AddComponent<GameManager>();
            gm.LitShader = LoadShader("SkyreachLit.shader");
            gm.OceanShader = LoadShader("SkyreachOcean.shader");
            gm.SkyShader = LoadShader("SkyreachSky.shader");
            gm.UnlitShader = LoadShader("SkyreachUnlit.shader");
            gm.VignetteShader = LoadShader("SkyreachVignette.shader");

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("[Skyreach] Scene created at " + ScenePath + ". Press Play to test with the keyboard, or build to your Quest.");
        }

        static Shader LoadShader(string file)
        {
            var s = AssetDatabase.LoadAssetAtPath<Shader>(ShaderDir + file);
            if (!s) Debug.LogError("[Skyreach] Missing shader " + ShaderDir + file);
            return s;
        }

        // ------------------------------------------------------------------ 4. build
        [MenuItem("Skyreach/4. Build APK", priority = 20)]
        public static void BuildApk() => Build(BuildOptions.None);

        [MenuItem("Skyreach/5. Build And Run on Quest (USB)", priority = 21)]
        public static void BuildAndRun() => Build(BuildOptions.AutoRunPlayer);

        static void Build(BuildOptions options)
        {
            if (!File.Exists(ScenePath)) CreateScene();
            Directory.CreateDirectory("Builds");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = ApkPath,
                target = BuildTarget.Android,
                options = options,
            });
            if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                Debug.Log($"[Skyreach] Built {ApkPath} ({report.summary.totalSize / (1024 * 1024)} MB).");
                EditorUtility.RevealInFinder(ApkPath);
            }
            else Debug.LogError("[Skyreach] Build failed: " + report.summary.result);
        }

        [MenuItem("Skyreach/Set Up Everything (2 + 3)", priority = 40)]
        public static void SetUpEverything()
        {
            ConfigureForQuest();
            CreateScene();
        }
    }

    /// <summary>
    /// Filled in by the optional Skyreach.Editor.XR assembly, which only compiles once the
    /// XR packages are installed. Keeps this assembly compiling on a fresh project.
    /// </summary>
    public static class XRConfigHook
    {
        public static System.Func<bool> Configure;
    }
}
