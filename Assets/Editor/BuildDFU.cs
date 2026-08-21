using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.Build;
using UnityEngine;
using System.Linq;
public class BuildDFU {
    public static void BuildLinux() {
        var opts = new BuildPlayerOptions {
            scenes = new[] { "Assets/Scenes/DaggerfallUnityStartup.unity", "Assets/Scenes/DaggerfallUnityGame.unity" },
            locationPathName = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile) + "/dfu-builds/linux/DFU.x86_64",
            target = BuildTarget.StandaloneLinux64,
            options = BuildOptions.None
        };
        var report = BuildPipeline.BuildPlayer(opts);
        Debug.Log("BUILD_RESULT: " + report.summary.result + " errors=" + report.summary.totalErrors);
    }
    public static void BuildAndroid() {
        var nbt = NamedBuildTarget.FromBuildTargetGroup(BuildTargetGroup.Android);
        PlayerSettings.SetScriptingBackend(nbt, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        AssetDatabase.SaveAssets();
        Debug.Log("ACTIVE_TARGET: " + EditorUserBuildSettings.activeBuildTarget + " arch=" + PlayerSettings.Android.targetArchitectures + " backend=" + PlayerSettings.GetScriptingBackend(nbt));
        PrepAndroidSettings();
        AssetDatabase.SaveAssets();
        Debug.Log("AFTER_PREP arch=" + PlayerSettings.Android.targetArchitectures);
        var opts = new BuildPlayerOptions {
            scenes = new[] { "Assets/Scenes/DaggerfallUnityStartup.unity", "Assets/Scenes/DaggerfallUnityGame.unity" },
            locationPathName = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile) + "/dfu-builds/android/DFU.apk",
            target = BuildTarget.Android,
            options = BuildOptions.None
        };
        var report = BuildPipeline.BuildPlayer(opts);
        Debug.Log("BUILD_RESULT: " + report.summary.result + " errors=" + report.summary.totalErrors);
    }
    public static void PrepAndroidSettings() {
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.dfworkshop.dfuquest3");
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29; // Quest 3 baseline
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel35;
        // Meta Quest requires the Game Activity entry point on Unity 6 (OpenXR validation).
        PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.GameActivity;
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { UnityEngine.Rendering.GraphicsDeviceType.Vulkan });
        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        Debug.Log("ANDROID_SETTINGS_PREPPED id=com.dfworkshop.dfuquest3");
    }    public static void ProbeTools() {
        var t = typeof(UnityEditor.PlayerSettings).Assembly.GetType("UnityEditor.Android.AndroidExternalToolsSettings");
        if (t == null) { Debug.Log("PROBE: AndroidExternalToolsSettings type not found"); }
        else {
            foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static))
                Debug.Log("PROBE methods: " + m);
        }
        // Also dump what Unity thinks the NDK path is
        var bt = typeof(UnityEditor.PlayerSettings).Assembly.GetType("UnityEditor.Android.AndroidBuildTools");
        if (bt != null)
            foreach (var p2 in bt.GetProperties(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static))
                Debug.Log("BT: " + p2.Name + "=" + (p2.CanRead ? p2.GetValue(null) : "?"));
    }
    public static void BuildAddressablesContent()
    {
        // Build DFU's Addressables player content into the APK's assets/aaData
        var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) { Debug.LogError("ADDR: no AddressableAssetSettings found"); return; }
        // Addressables editor API lives in its own asmdef; use reflection to avoid
        // adding an explicit assembly reference to Assembly-CSharp-Editor.
        var asm = System.Reflection.Assembly.Load("Unity.Addressables.Editor");
        var t = asm.GetType("UnityEditor.AddressableAssets.Settings.AddressableAssetSettings");
        // Pick the parameterless BuildPlayerContent() overload (there are several).
        var m = t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                  .FirstOrDefault(x => x.Name == "BuildPlayerContent" && x.GetParameters().Length == 0);
        if (m != null) { m.Invoke(null, null); Debug.Log("ADDR: content built via reflection"); }
        else Debug.LogError("ADDR: BuildPlayerContent method not found");
        Debug.Log("ADDR: content built");
    }
}
