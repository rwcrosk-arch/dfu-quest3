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
            // Re-extract unless ALL key files are present (dir existence alone is
            // insufficient — a prior partial run may have created empty dirs).
            bool need =
                !File.Exists(Path.Combine(r, "Quests", "_TUTOR__.txt")) ||
                !File.Exists(Path.Combine(r, "Tables", "QuestList-Classic.txt")) ||
                !File.Exists(Path.Combine(r, "Tables", "Quests-GlobalVars.txt")) ||
                !File.Exists(Path.Combine(r, "Factions", "FACTION.TXT")) ||
                !File.Exists(Path.Combine(r, "Text", "MainMenu.txt"));
            // Empty subfolders that System.IO enumerates during game start / world load
            // MUST exist on a real path or Directory.GetFiles throws. Create unconditionally.
            foreach (var d in new[] { "WorldData", "QuestPacks", "Factions", "SpellIcons",
                                      "Sound", "Movies", "Books", "Textures", "Docs", "Mods",
                                      "SoundFonts", "GameFiles", "Presets" })
                Directory.CreateDirectory(Path.Combine(r, d));

            if (!need)
                return;   // already fully extracted

            try
            {
                if (Directory.Exists(r)) Directory.Delete(r, true);
                Directory.CreateDirectory(r);
                int copied = 0, failed = 0;
                foreach (string rel in AndroidStreamingAssetsManifest.Files)
                {
                    string dest = Path.Combine(r, rel.Replace('/', Path.DirectorySeparatorChar));
                    var uwr = UnityWebRequest.Get(Application.streamingAssetsPath + "/" + rel);
                    uwr.SendWebRequest();
                    while (!uwr.isDone) { }
                    if (uwr.result == UnityWebRequest.Result.Success)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        File.WriteAllBytes(dest, uwr.downloadHandler.data);
                        copied++;
                    }
                    else
                    {
                        failed++;
                        if (failed <= 10) Debug.LogWarning("[DFUQuest3] extract fail: " + rel);
                    }
                    uwr.Dispose();
                }
                // Re-create the empty enumerable dirs after the delete (manifest only has
                // files that exist; the empties still need to be present for GetFiles).
                foreach (var d in new[] { "WorldData", "QuestPacks", "Factions", "SpellIcons",
                                          "Sound", "Movies", "Books", "Textures", "Docs", "Mods",
                                          "SoundFonts", "GameFiles", "Presets" })
                    Directory.CreateDirectory(Path.Combine(r, d));
                Debug.Log("[DFUQuest3] Extracted StreamingAssets: " + copied + " copied, " + failed + " failed -> " + r);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[DFUQuest3] StreamingAssets extraction failed: " + e);
            }
#endif
        }

#if UNITY_ANDROID
#endif
    }
}
