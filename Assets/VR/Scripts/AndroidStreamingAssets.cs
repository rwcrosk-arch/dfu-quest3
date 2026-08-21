// DFU Quest3 VR — Android StreamingAssets extraction helper.
// On Android, Application.streamingAssetsPath is a jar: URI that System.IO
// cannot enumerate (Directory.Exists==false, GetFiles throws). DFU reads
// Fonts/Text/BIOGs etc. through System.IO, so on Android we extract the
// bundled assets/ to persistentDataPath synchronously BEFORE any scene
// Awake runs, and point DFU's folder reads there.

using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace DFUQuest3
{
    public static class AndroidStreamingAssets
    {
        const string ExtractRel = "streaming_assets_extracted";
        static string root;

        /// <summary>A real, enumerable filesystem path to StreamingAssets content.</summary>
        public static string Resolve()
        {
#if UNITY_ANDROID
            if (root == null) root = Application.persistentDataPath + "/" + ExtractRel;
            return root;
#else
            return Application.streamingAssetsPath;
#endif
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ExtractAtBoot()
        {
#if UNITY_ANDROID
            string r = Resolve();
            bool need = !Directory.Exists(Path.Combine(r, "Fonts"));
            if (need)
            {
                try
                {
                    if (Directory.Exists(r)) Directory.Delete(r, true);
                    Directory.CreateDirectory(r);
                    // Copy the critical font files (font loading is synchronous at Awake).
                    for (int i = 0; i < 5; i++)
                    {
                        string name = "FONT" + i.ToString("D4") + ".FNT";
                        string dest = Path.Combine(Path.Combine(r, "Fonts"), name);
                        Directory.CreateDirectory(Path.Combine(r, "Fonts"));
                        var uwr = UnityWebRequest.Get(Application.streamingAssetsPath + "/Fonts/" + name);
                        uwr.SendWebRequest();
                        while (!uwr.isDone) { }
                        if (uwr.result == UnityWebRequest.Result.Success)
                            File.WriteAllBytes(dest, uwr.downloadHandler.data);
                        else
                            Debug.LogError("[DFUQuest3] Could not extract font " + name + ": " + uwr.error);
                        uwr.Dispose();
                    }
                    Debug.Log("[DFUQuest3] Extracted StreamingAssets/Fonts to " + r);
                }
                catch (System.Exception e)
                {
                    Debug.LogError("[DFUQuest3] StreamingAssets extraction failed: " + e);
                }
            }
#endif
        }
    }
}
