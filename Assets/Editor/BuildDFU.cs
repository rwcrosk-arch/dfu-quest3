using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
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
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { UnityEngine.Rendering.GraphicsDeviceType.Vulkan });
        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        Debug.Log("ANDROID_SETTINGS_PREPPED id=com.dfworkshop.dfuquest3");
    }
}
