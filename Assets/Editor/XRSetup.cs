// DFU Quest3 VR — headless XR Plugin Management bootstrap (persistent sub-assets).
// Creates XRGeneralSettings + XRManagerSettings + OpenXR loader as saved
// sub-assets of the container, so the loader list actually persists.

using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Management;

namespace DFUQuest3.EditorTools
{
    public static class XRSetup
    {
        const string AssetPath = "Assets/XR/XRGeneralSettingsPerBuildTarget.asset";
        // XR Management expects the per-build-target settings registered here so the
        // build processor / loaders can find them. Missing this registration = flaky
        // loader inclusion (controllers not created). Ported from old project's ConfigureXR.
        const string k_SettingsKey = "com.unity.xr.management.loader_settings";

        public static void Apply()
        {
            var edAsm = Assembly.Load("Unity.XR.Management.Editor");
            var containerType = edAsm.GetType("UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget");
            var asset = AssetDatabase.LoadAssetAtPath(AssetPath, containerType);
            if (asset == null || containerType == null) { Debug.LogError("XR_SETUP: container/asset not found"); return; }

            // CRITICAL: register in EditorBuildSettings so the XR build processor finds it.
            // (No idempotency check — AddConfigObject is safe to call repeatedly.)
            EditorBuildSettings.AddConfigObject(k_SettingsKey, (UnityEngine.Object)asset, true);
            Debug.Log("XR_SETUP: registered settings in EditorBuildSettings");

            var openXrType = Type.GetType("UnityEngine.XR.OpenXR.OpenXRLoader, Unity.XR.OpenXR");
            if (openXrType == null) { Debug.LogError("XR_SETUP: OpenXRLoader type not found"); return; }

            // Remove any stale existing sub-assets to rebuild cleanly
            var subAssets = AssetDatabase.LoadAllAssetsAtPath(AssetPath)
                .Where(x => x != null && x != asset && (x is XRGeneralSettings || x is XRManagerSettings || x.GetType() == openXrType)).ToArray();
            foreach (var sa in subAssets) AssetDatabase.RemoveObjectFromAsset(sa);

            // Create persistent sub-assets
            var mgr = ScriptableObject.CreateInstance<XRManagerSettings>();
            mgr.name = "Android Providers";
            AssetDatabase.AddObjectToAsset(mgr, asset);

            var loader = ScriptableObject.CreateInstance(openXrType) as XRLoader;
            loader.name = "OpenXR Loader";
            AssetDatabase.AddObjectToAsset(loader, asset);
            mgr.TryAddLoader(loader);
            Debug.Log("XR_SETUP: manager loaders after add=" + mgr.activeLoaders.Count);

            var general = ScriptableObject.CreateInstance<XRGeneralSettings>();
            general.name = "Android Settings";
            general.Manager = mgr;
            AssetDatabase.AddObjectToAsset(general, asset);

            // Assign to Android key
            var setSettings = containerType.GetMethod("SetSettingsForBuildTarget", BindingFlags.Public | BindingFlags.Instance);
            setSettings.Invoke(asset, new object[] { BuildTargetGroup.Android, general });

            EditorUtility.SetDirty(mgr); EditorUtility.SetDirty(general); EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Verify
            var reloaded = AssetDatabase.LoadAssetAtPath(AssetPath, containerType);
            var reloadedGeneral = containerType.GetMethod("SettingsForBuildTarget", BindingFlags.Public | BindingFlags.Instance)
                .Invoke(reloaded, new object[] { BuildTargetGroup.Android }) as XRGeneralSettings;
            var loaders = reloadedGeneral != null && reloadedGeneral.Manager != null ? reloadedGeneral.Manager.activeLoaders.ToList() : null;
            Debug.Log("XR_SETUP: done. persisted loaders=" + (loaders == null ? "null" : loaders.Count.ToString()));
        }
    }
}
