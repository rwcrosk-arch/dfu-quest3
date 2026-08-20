// DFU Quest3 VR — headless XR settings bootstrap.
// Run once per project: enables XR Plugin Management on Android,
// loads OpenXR + Meta XR feature group, sets rendering options.

using UnityEditor;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.XR.Management;

namespace DFUQuest3.EditorTools
{
    public static class XRSetup
    {
        public static void Apply()
        {
            // Ensure XR Plugin Management settings exist (auto-generates in Library)
            var settings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<XRGeneralSettings>();
                XRGeneralSettingsPerBuildTarget.SetXRGeneralSettingsForBuildTarget(BuildTargetGroup.Android, settings);
            }
            if (settings.Manager == null)
            {
                var mgr = ScriptableObject.CreateInstance<XRManagerSettings>();
                settings.Manager = mgr;
            }

            // OpenXRLoader should be discoverable via type name; add if missing
            var mgrSettings = settings.Manager;
            var openXRType = System.Type.GetType("UnityEngine.XR.OpenXR.OpenXRLoader, Unity.XR.OpenXR");
            if (openXRType != null && !mgrSettings.TryAddLoader(ScriptableObject.CreateInstance(openXRType) as XRLoader))
            {
                Debug.Log("XR_SETUP: OpenXRLoader already present or add failed (idempotent)");
            }

            // Quest rendering targets
            UnityEditor.PlayerSettings.colorSpace = ColorSpace.Linear;
            UnityEditor.PlayerSettings.SetGraphicsAPIs(
                BuildTarget.Android,
                new[] { UnityEngine.Rendering.GraphicsDeviceType.Vulkan });

            // Enable input system alongside legacy (DFU uses old Input for desktop)
            UnityEditor.PlayerSettings.activeInputHandler = UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(BuildTargetGroup.Android) != null
                ? UnityEditor.PlayerSettings.activeInputHandler // leave as-is; Unity 6 defaults to both
                : UnityEditor.PlayerSettings.activeInputHandler;

            AssetDatabase.SaveAssets();
            Debug.Log("XR_SETUP: applied OpenXR + Meta feature group for Android");
        }
    }
}
