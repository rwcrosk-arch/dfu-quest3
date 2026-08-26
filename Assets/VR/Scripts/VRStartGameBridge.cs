// DFU Quest3 VR — StartGameBehaviour bridge.
// The VR build boots into the STARTUP scene (index 0), which has no StartGameBehaviour
// and no Player/GameManager world. The StartNewGameWizard requires a StartGameBehaviour
// to exist (it throws "Could not find StartGameBehaviour in scene" otherwise), so this
// shim spawns a guarded one in the startup scene so character creation can complete.
// When the wizard sets StartMethod = NewCharacter, this bridge stashes the CharacterDocument,
// loads the GAME scene (index 1), and hands the document to the real serialized
// StartGameBehaviour there, which then runs DFU's normal StartNewCharacter() with the
// world present. This is the DFU-native pattern (wizard sets fields, world does the start).
//
// Requires the guarded StartGameBehaviour (see StartGameBehaviour.cs Awake/Start/Update).

using UnityEngine;
using UnityEngine.SceneManagement;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Player;
using DaggerfallWorkshop.Game.Utility;

namespace DFUQuest3
{
    public class VRStartGameBridge : MonoBehaviour
    {
        static CharacterDocument pendingDoc;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Autostart()
        {
            var go = new GameObject("DFUQuest3 VRStartGameBridge");
            go.AddComponent<VRStartGameBridge>();
            DontDestroyOnLoad(go);

            // Make a guarded StartGameBehaviour exist in the startup scene so the
            // StartNewGameWizard can complete character creation.
            if (FindObjectOfType<StartGameBehaviour>() == null)
            {
                var sgb = new GameObject("VR Startup StartGameBehaviour");
                sgb.AddComponent<StartGameBehaviour>();
                Debug.Log("[DFUQuest3] VRStartGameBridge: spawned guarded StartGameBehaviour in startup scene.");
            }

            // Disable intro cinematics: the StreamingAssets/Movies folder is empty on
            // Android (jar: URI), so the wizard's ANIM*.VID would never finish and the
            // game start would never be triggered — leaving a black screen after
            // character creation. Skip straight to TriggerGame().
            if (DaggerfallWorkshop.Game.DaggerfallUI.Instance != null)
            {
                DaggerfallWorkshop.Game.DaggerfallUI.Instance.enableVideos = false;
                Debug.Log("[DFUQuest3] VRStartGameBridge: intro videos disabled (empty Movies folder on Android).");
            }
        }

        void Update()
        {
            if (SceneManager.GetActiveScene().buildIndex == 0)
            {
                // Startup scene: the wizard just set StartMethod = NewCharacter on the shim.
                // Load the game scene and hand off the character document.
                var sgb = FindObjectOfType<StartGameBehaviour>();
                if (sgb != null && sgb.StartMethod == StartGameBehaviour.StartMethods.NewCharacter)
                {
                    pendingDoc = sgb.CharacterDocument;
                    sgb.StartMethod = StartGameBehaviour.StartMethods.DoNothing;
                    Debug.Log("[DFUQuest3] VRStartGameBridge: NewCharacter requested — loading game scene.");
                    SceneManager.LoadScene(1);
                }
            }
            else
            {
                // Game scene: the real StartGameBehaviour Awake/Start ran normally (player exists).
                if (pendingDoc != null)
                {
                    var sgb = FindObjectOfType<StartGameBehaviour>();
                    if (sgb != null && GameManagerReady())
                    {
                        sgb.CharacterDocument = pendingDoc;
                        sgb.StartMethod = StartGameBehaviour.StartMethods.NewCharacter;
                        pendingDoc = null;
                        Debug.Log("[DFUQuest3] VRStartGameBridge: handed CharacterDocument to game-scene StartGameBehaviour.");
                        // Schedule a one-shot spawn diagnostic after the world settles.
                        diagTimer = 3f;
                    }
                }
                if (diagTimer > 0f)
                {
                    diagTimer -= Time.unscaledDeltaTime;
                    if (diagTimer <= 0f)
                        DiagnosticSpawn();
                }
            }
        }

        float diagTimer = 0f;

        // One-shot: log the player position vs the dungeon start/enter markers so we can
        // see whether the player is stranded at world origin (empty void) or correctly at
        // the dungeon spawn. This distinguishes a spawn-placement bug from a render bug.
        void DiagnosticSpawn()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;
            var player = gm.PlayerObject;
            var pee = gm.PlayerEnterExit;
            string p = player != null ? player.transform.position.ToString("F2") : "null";
            string inside = pee != null ? "insideDungeon=" + pee.IsPlayerInsideDungeon + " inside=" + pee.IsPlayerInside : "pee=null";
            string marker = "none";
            string enter = "none";
            if (pee != null && pee.Dungeon != null)
            {
                var d = pee.Dungeon;
                if (d.StartMarker != null) marker = d.StartMarker.transform.position.ToString("F2");
                if (d.EnterMarker != null) enter = d.EnterMarker.transform.position.ToString("F2");
            }
            Debug.Log($"[DFUQuest3] SPAWNDIAG player={p} {inside} | dungeonStart={marker} enter={enter} | StreamingWorld={(gm.StreamingWorld != null ? gm.StreamingWorld.name : "null")}");
        }

        bool GameManagerReady()
        {
            var gm = GameManager.Instance;
            return gm != null && gm.PlayerEnterExit != null && gm.StreamingWorld != null;
        }
    }
}
