using UnityEditor;
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
}
