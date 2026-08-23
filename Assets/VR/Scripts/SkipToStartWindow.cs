// DFU Quest3 VR — skip the intro/title menu and go straight to the New Game / Load Game
// screen (DaggerfallStartWindow). This is the screen the title menu's "New Game" and
// "Load Game" buttons lead to. Bypassing the title menu lets VR testing start directly
// at the character-creation / load flow.
//
// Purely additive: a new file, self-wiring, touches no existing VR script.

using UnityEngine;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;

namespace DFUQuest3
{
    public class SkipToStartWindow : MonoBehaviour
    {
        bool pushed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Autostart()
        {
            var go = new GameObject("DFUQuest3 SkipToStartWindow");
            go.AddComponent<SkipToStartWindow>();
            DontDestroyOnLoad(go);
            Debug.Log("[DFUQuest3] SkipToStartWindow auto-wired");
        }

        void Update()
        {
            if (pushed) return;
            var ui = DaggerfallUI.UIManager;
            if (ui == null) return;
            if (ui.TopWindow == null) return;

            // Push the New Game / Load Game screen (same as the title menu's buttons).
            ui.PushWindow(UIWindowFactory.GetInstance(UIWindowType.Start, ui));
            pushed = true;
            Debug.Log("[DFUQuest3] Pushed StartWindow (New Game / Load Game) — skipped title menu");
        }
    }
}
