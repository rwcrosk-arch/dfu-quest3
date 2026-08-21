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
                // Enable everything needed for Quest 3 controllers: the touch interaction
                // profiles (plus/pro), the Meta/Oculus Quest feature extensions.
                if (name.Contains("OculusTouchControllerProfile") ||
                    name.Contains("MetaQuestTouchPlusControllerProfile") ||
                    name.Contains("MetaQuestTouchProControllerProfile") ||
                    name.Contains("MetaQuestFeature") ||
                    name.Contains("OculusQuestFeature"))
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
    }
}
