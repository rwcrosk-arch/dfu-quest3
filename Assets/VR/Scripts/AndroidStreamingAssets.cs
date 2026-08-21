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
                !Directory.Exists(Path.Combine(r, "Fonts")) || !Directory.Exists(Path.Combine(r, "Tables")) ||
                !Directory.Exists(Path.Combine(r, "Text")) ||
                !File.Exists(Path.Combine(r, "Text", "MainMenu.txt")) ||
                !File.Exists(Path.Combine(r, "Tables", "Quests-GlobalVars.txt"));
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
                    // Copy quest tables (StreamingAssets/Tables) — needed for QuestMachine.
                    CopyDir("Tables", r);
                    // Copy Text databases (StreamingAssets/Text) — needed for TextManager.
                    CopyDir("Text", r);
                    Debug.Log("[DFUQuest3] Extracted StreamingAssets/Fonts+Tables+Text to " + r);
                }
                catch (System.Exception e)
                {
                    Debug.LogError("[DFUQuest3] StreamingAssets extraction failed: " + e);
                }
            }
#endif
        }

#if UNITY_ANDROID
        static void CopyDir(string dir, string root)
        {
            string destDir = Path.Combine(root, dir);
            Directory.CreateDirectory(destDir);
            if (dir == "Text")
            {
                // The actual menu/system text databases (Text/*.txt). These are what
                // TextManager enumerates and what the menu strings come from.
                string[] needed = {
                    "MainMenu.txt", "GameSettings.txt", "DialogShortcuts.txt", "ModSystem.txt"
                };
                foreach (var f in needed)
                {
                    var uwr = UnityWebRequest.Get(Application.streamingAssetsPath + "/" + dir + "/" + f);
                    uwr.SendWebRequest();
                    while (!uwr.isDone) { }
                    if (uwr.result == UnityWebRequest.Result.Success)
                        File.WriteAllBytes(Path.Combine(destDir, f), uwr.downloadHandler.data);
                    else
                        Debug.LogWarning("[DFUQuest3] Text file not found: " + f);
                    uwr.Dispose();
                }
            }
            else
            {
                // Tables: the well-known quest table filenames that DFU needs at startup.
                string[] needed = {
                    "Quests-GlobalVars.txt", "Quests-StaticMessages.txt", "Quests-Places.txt",
                    "Quests-Sounds.txt", "Quests-Items.txt", "Quests-Factions.txt",
                    "Quests-Foes.txt", "Quests-Diseases.txt", "Quests-Spells.txt"
                };
                foreach (var f in needed)
                {
                    var uwr = UnityWebRequest.Get(Application.streamingAssetsPath + "/" + dir + "/" + f);
                    uwr.SendWebRequest();
                    while (!uwr.isDone) { }
                    if (uwr.result == UnityWebRequest.Result.Success)
                        File.WriteAllBytes(Path.Combine(destDir, f), uwr.downloadHandler.data);
                    uwr.Dispose();
                }
            }
        }
#endif
    }
}
