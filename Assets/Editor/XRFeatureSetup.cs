// DFU Quest3 VR — enable OpenXR controller interaction profiles via reflection.
// OpenXRPackageSettings is internal, so we reach it through reflection.

using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace DFUQuest3.EditorTools
{
    public static class XRFeatureSetup
    {
        public static void EnableTouchProfiles()
        {
            var asm = Assembly.Load("Unity.XR.OpenXR.Editor");
            if (asm == null) { Debug.LogError("[XRFEATURE] no Unity.XR.OpenXR.Editor asm"); return; }
            var pkgType = asm.GetType("UnityEditor.XR.OpenXR.OpenXRPackageSettings");
            if (pkgType == null) { Debug.LogError("[XRFEATURE] OpenXRPackageSettings type not found"); return; }

            var instanceProp = pkgType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            var instance = instanceProp?.GetValue(null);
            if (instance == null) { Debug.LogError("[XRFEATURE] OpenXRPackageSettings.Instance null"); return; }

            var getFeatures = pkgType.GetMethod("GetFeatures").MakeGenericMethod(typeof(UnityEngine.XR.OpenXR.Features.OpenXRFeature));
            var features = (System.Collections.IEnumerable)getFeatures.Invoke(instance, null);

            int enabled = 0;
            foreach (var entry in features)
            {
                // entry is (BuildTargetGroup, feature)
                var f = (UnityEngine.XR.OpenXR.Features.OpenXRFeature)entry.GetType().GetFields(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(x => x.FieldType == typeof(UnityEngine.XR.OpenXR.Features.OpenXRFeature))?.GetValue(entry);

                if (f == null)
                {
                    // tuple item2
                    f = (UnityEngine.XR.OpenXR.Features.OpenXRFeature)entry.GetType().GetProperty("Item2")?.GetValue(entry);
                }
                if (f == null) continue;
                string name = f.GetType().Name;
                // Disable the profiles that break binding: Meta Quest Touch Plus/Pro and
                // all Detached variants bind /detached_controller_meta + thumbrest/force
                // paths that require XR_META_detached_controllers (native feature we can't
                // bundle) -> xrSuggestInteractionProfileBindings fails -> no controllers.
                if (name.Contains("Detached") || name.Contains("MetaQuestTouchPlus") ||
                    name.Contains("MetaQuestTouchPro"))
                {
                    if (f.enabled)
                    {
                        f.enabled = false;
                        Debug.Log("[XRFEATURE] DISABLED " + name + " (unsupported bindings)");
                    }
                    continue;
                }
                // Disable the native Quest features (broke build).
                if (name.Contains("MetaQuestFeature") || name.Contains("OculusQuestFeature"))
                {
                    if (f.enabled)
                    {
                        f.enabled = false;
                        Debug.Log("[XRFEATURE] DISABLED " + name + " (native extension breaks build)");
                    }
                    continue;
                }
                // Enable ONLY the plain Oculus Touch profile — binds standard /input/* paths
                // that always work and register the Quest controllers.
                if (name.Contains("OculusTouchControllerProfile"))
                {
                    if (!f.enabled)
                    {
                        f.enabled = true;
                        enabled++;
                        Debug.Log("[XRFEATURE] Enabled " + name);
                    }
                    else
                    {
                        Debug.Log("[XRFEATURE] Already enabled " + name);
                    }
                }
            }
            Debug.Log("[XRFEATURE] enabled " + enabled + " profiles");

            EditorUtility.SetDirty((UnityEngine.Object)instance);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[XRFEATURE] done");
        }

        // Enables the Quest 3 target device on a MetaQuestFeature instance via reflection
        // (targetDevices + EnableTargetDevice are internal).
        static void EnableQuestDevice(UnityEngine.Object feature)
        {
            var t = feature.GetType();
            var targetDevicesField = t.GetField("targetDevices", BindingFlags.NonPublic | BindingFlags.Instance);
            var list = targetDevicesField?.GetValue(feature) as System.Collections.IList;
            if (list == null) { Debug.LogWarning("[XRFEATURE] no targetDevices on MetaQuestFeature"); return; }

            // EnableTargetDevice(manifestName, enabled) is internal — call via reflection.
            var enableMethod = t.GetMethod("EnableTargetDevice", BindingFlags.NonPublic | BindingFlags.Instance);
            if (enableMethod == null) { Debug.LogWarning("[XRFEATURE] no EnableTargetDevice method"); return; }

            // Enable Quest 3 ("eureka") and Quest 3S, Quest, Quest2 as fallback.
            foreach (var devName in new[] { "eureka", "quest3s", "quest", "quest2" })
            {
                enableMethod.Invoke(feature, new object[] { devName, true });
            }
            EditorUtility.SetDirty(feature);
            Debug.Log("[XRFEATURE] enabled Quest target devices on MetaQuestFeature");
        }
    }
}
