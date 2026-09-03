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

            // Make a SaveLoadManager exist in the startup scene so the title menu's
            // Load Game / save-list / switch-char work. DFU ships SaveLoadManager ONLY
            // in the game scene (verified: both upstream master and this fork's
            // DaggerfallUnityStartup.unity lack it). At menu time GameManager.Instance
            // auto-creates a bare GameManager whose get_SaveLoadManager does
            // FindObjectOfType<SaveLoadManager>() -> null -> THROWS, killing
            // DaggerfallUnitySaveGameWindow.OnPush before EnumerateSaves() could run:
            // the menu save list was always empty and switch-char dead, even though the
            // saves existed on disk (looked like "saves don't persist across restarts").
            // SaveLoadManager is a self-registering singleton (SetupSingleton in Start),
            // so a scene instance here makes GameManager.Instance.SaveLoadManager resolve
            // everywhere, gameplay and menus alike.
            if (FindObjectOfType<DaggerfallWorkshop.Game.Serialization.SaveLoadManager>() == null)
            {
                var slm = new GameObject("VR Startup SaveLoadManager");
                slm.AddComponent<DaggerfallWorkshop.Game.Serialization.SaveLoadManager>();
                Debug.Log("[DFUQuest3] VRStartGameBridge: spawned guarded SaveLoadManager in startup scene.");
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

                // Complete a menu-initiated save-game load: the save window deferred the
                // actual load because the LoadGame coroutine needs a live player (it
                // resolves PlayerDeath/PlayerMotor/PlayerEntity through PlayerObject,
                // which throws in menus). Wait until the player + world are ready, then
                // run the normal Load(key) path here in the game scene.
                if (DaggerfallWorkshop.Game.Serialization.SaveLoadManager.HasPendingMenuLoad
                    && GameManagerReady())
                {
                    Debug.Log("[DFUQuest3] VRStartGameBridge: completing deferred menu-time save load.");
                    DaggerfallWorkshop.Game.Serialization.SaveLoadManager.CompletePendingMenuLoad();
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
            DiagnosticRenderState();
        }

        // Empirically separate white-world causes: lighting vs texture vs shader vs sky.
        void DiagnosticRenderState()
        {
            try
            {
                var gm = GameManager.Instance;
                string lightStr = "lights=" + 0;
                var lights = UnityEngine.Object.FindObjectsOfType<Light>();
                lightStr = "lights=" + lights.Length;
                foreach (var l in lights)
                {
                    if (l != null && l.type == LightType.Directional)
                        lightStr += " | D:" + l.name + "(" + l.intensity.ToString("0.0") + ")";
                }
                string skyStr = "skyCam=null";
                var sky = UnityEngine.Object.FindObjectOfType<DaggerfallWorkshop.DaggerfallSky>();
                if (sky != null && sky.SkyCamera != null)
                    skyStr = "skyCam=" + sky.SkyCamera.name + " enabled=" + sky.SkyCamera.enabled;
                string matStr = "mat=null";
                Renderer r = null;
                var terrain = UnityEngine.Object.FindObjectOfType<DaggerfallWorkshop.DaggerfallTerrain>();
                if (terrain != null) r = terrain.GetComponent<Renderer>();
                if (r == null)
                {
                    foreach (var b in UnityEngine.Object.FindObjectsOfType<Renderer>())
                    {
                        if (b != null && b.sharedMaterial != null && b.sharedMaterial.shader != null &&
                            b.sharedMaterial.shader.name.StartsWith("Daggerfall/"))
                        { r = b; break; }
                    }
                }
                if (r != null && r.sharedMaterial != null)
                {
                    var m = r.sharedMaterial;
                    Texture t = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
                    Texture tarr = m.HasProperty("_TileTexArr") ? m.GetTexture("_TileTexArr") : null;
                    Texture tmap = m.HasProperty("_TilemapTex") ? m.GetTexture("_TilemapTex") : null;
                    string arrDepth = "null";
                    if (tarr is Texture2DArray tda) arrDepth = tda.depth.ToString();
                    matStr = "mat=" + r.name + " shader=" + m.shader.name +
                             " _MainTex=" + (t != null ? t.width + "x" + t.height : "null") +
                             " _TileTexArr=" + (tarr != null ? "array(" + arrDepth + ")" : "null") +
                             " _TilemapTex=" + (tmap != null ? tmap.width + "x" + tmap.height : "null") +
                             " color=" + (m.HasProperty("_Color") ? m.GetColor("_Color").ToString() : "n/a");
                }
                Debug.Log("[DFUQuest3] RENDERDIAG " + lightStr + " | " + skyStr + " | " + matStr);
            }
            catch (System.Exception e)
            {
                Debug.Log("[DFUQuest3] RENDERDIAG failed: " + e.Message);
            }
        }

        bool GameManagerReady()
        {
            var gm = GameManager.Instance;
            return gm != null && gm.PlayerEnterExit != null && gm.StreamingWorld != null;
        }
    }
}
