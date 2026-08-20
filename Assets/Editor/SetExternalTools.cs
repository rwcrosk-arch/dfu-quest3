using UnityEditor;
using UnityEngine;
public class SetExternalTools {
    public static void Apply() {
        // Point Unity's Android external tools at our manually-installed SDK/JDK
        EditorPrefs.SetString("AndroidSdkRoot", System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile) + "/Android/Sdk");
        EditorPrefs.SetString("JdkPath", "/usr/lib/jvm/java-17-openjdk");
        EditorPrefs.SetBool("JdkUseEmbedded", false);
        EditorPrefs.SetBool("SdkUseEmbedded", false);
        // NDK will be wired once the background download lands
        var ndk = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile) + "/Android/Sdk/ndk/r27c";
        if (System.IO.Directory.Exists(ndk)) {
            EditorPrefs.SetString("AndroidNdkRoot", ndk);
            EditorPrefs.SetBool("NdkUseEmbedded", false);
        }
        Debug.Log("TOOLS_SET: sdk=" + EditorPrefs.GetString("AndroidSdkRoot") + " jdk=" + EditorPrefs.GetString("JdkPath") + " ndk=" + EditorPrefs.GetString("AndroidNdkRoot"));
    }
}
