using UnityEditor;
using UnityEngine;
public class AndroidBuildTest {
    public static void Probe() {
        Debug.Log("BUILD_TARGET_PROBE: " + BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android).ToString());
    }
}
