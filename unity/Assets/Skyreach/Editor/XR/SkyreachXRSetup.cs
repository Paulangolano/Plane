using System.IO;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

namespace Skyreach.EditorTools
{
    /// <summary>
    /// Turns on OpenXR for Android with the Meta Quest feature and Touch controller profile.
    /// This assembly only compiles when com.unity.xr.management and com.unity.xr.openxr are installed.
    /// </summary>
    [InitializeOnLoad]
    public static class SkyreachXRSetup
    {
        static SkyreachXRSetup()
        {
            XRConfigHook.Configure = Configure;
        }

        public static bool Configure()
        {
            const BuildTargetGroup group = BuildTargetGroup.Android;

            // XR Plug-in Management settings asset (created if this is a fresh project).
            EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget);
            if (perTarget == null)
            {
                Directory.CreateDirectory("Assets/XR");
                perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
            }
            if (!perTarget.HasSettingsForBuildTarget(group)) perTarget.CreateDefaultSettingsForBuildTarget(group);
            if (!perTarget.HasManagerSettingsForBuildTarget(group)) perTarget.CreateDefaultManagerSettingsForBuildTarget(group);

            var general = perTarget.SettingsForBuildTarget(group);
            general.InitManagerOnStart = true;
            bool assigned = XRPackageMetadataStore.AssignLoader(general.Manager, "UnityEngine.XR.OpenXR.OpenXRLoader", group);
            EditorUtility.SetDirty(general);
            EditorUtility.SetDirty(perTarget);

            // OpenXR features: Meta Quest support + Touch controllers, single-pass instanced stereo.
            var openxr = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (openxr == null)
            {
                Debug.LogWarning("[Skyreach] OpenXR settings for Android not created yet. Open Edit ▸ Project Settings ▸ " +
                                 "XR Plug-in Management ▸ OpenXR once, then run Configure again.");
                return false;
            }
            int enabled = 0;
            foreach (var feature in openxr.GetFeatures())
            {
                string n = feature.GetType().Name;
                if (n == "MetaQuestFeature" || n == "OculusQuestFeature" || n == "OculusTouchControllerProfile" || n == "MetaQuestTouchPlusControllerProfile")
                {
                    feature.enabled = true;
                    enabled++;
                    EditorUtility.SetDirty(feature);
                }
            }
            openxr.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
            EditorUtility.SetDirty(openxr);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Skyreach] OpenXR loader assigned: {assigned}, Quest features enabled: {enabled}.");
            return enabled > 0;
        }
    }
}
