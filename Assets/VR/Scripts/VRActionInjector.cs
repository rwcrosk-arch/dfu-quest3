// DFU Quest3 VR — additive button-action injector.
// Runs at DefaultExecutionOrder(100), AFTER InputManager.Update (default 0) clears
// currentActions at its start. This lets ActionStarted/HasAction consumers (ReadyWeapon,
// SwingWeapon, Jump, Run) actually see the injected actions on the press frame.
//
// Split from VRTriggerBridge on purpose: VRTriggerBridge stays at default order so its
// vrClickQueued flag is set BEFORE the UI reads it (menu clicks keep working). The two
// concerns have conflicting execution-order requirements, so they live in separate files.

using UnityEngine;
using UnityEngine.InputSystem;
using DaggerfallWorkshop.Game;

namespace DFUQuest3
{
    [DefaultExecutionOrder(100)]
    public class VRActionInjector : MonoBehaviour
    {
        // Manual run (sprint) toggle — right stick click flips this; while on, Run is held.
        bool runToggled;
        // Menu cycle state: opens a different DFU window per press.
        int menuCycleIndex = -1;
        // Cycle through the DFU windows. B opens the magic menu (spell book) directly, so
        // the cycler covers the rest including Inventory.
        static readonly InputManager.Actions[] MenuCycle =
        {
            InputManager.Actions.CharacterSheet,
            InputManager.Actions.Inventory,
            InputManager.Actions.LogBook,  // opens the quest journal
            InputManager.Actions.AutoMap,
            InputManager.Actions.TravelMap,
            InputManager.Actions.Rest,
        };

        // Self-wire at runtime.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Autostart()
        {
            var go = new GameObject("DFUQuest3 VRActionInjector");
            go.AddComponent<VRActionInjector>();
            DontDestroyOnLoad(go);
        }

        void Update()
        {
            var im = InputManager.Instance;
            if (im == null) return;

            // Right primary (A) -> Sheath/unsheathe weapon (ReadyWeapon toggles).
            if (Pressed(VRActionBinder.AButtonAction))
            {
                im.AddAction(InputManager.Actions.ReadyWeapon);
                Debug.Log("[DFUQuest3] A -> ReadyWeapon (sheath/unsheathe)");
            }
            // Right secondary (B) -> Magic menu (spell book). Inventory is in the cycler.
            if (Pressed(VRActionBinder.BButtonAction))
            {
                im.AddAction(InputManager.Actions.CastSpell);
                Debug.Log("[DFUQuest3] B -> Magic menu (spell book)");
            }
            // Left secondary (Y) -> cycle menus.
            if (Pressed(VRActionBinder.YButtonAction))
            {
                menuCycleIndex = (menuCycleIndex + 1) % MenuCycle.Length;
                InputManager.Actions act = MenuCycle[menuCycleIndex];
                im.AddAction(act);
                Debug.Log("[DFUQuest3] Y -> menu cycle: " + act);
            }
            // Left grip -> left-hand weapon/shield use (SwingWeapon).
            if (Pressed(VRActionBinder.GripLeftAction))
            {
                im.AddAction(InputManager.Actions.SwingWeapon);
                Debug.Log("[DFUQuest3] LeftGrip -> SwingWeapon (left hand)");
            }
            // X button (left primary) -> Jump (HELD, like the spacebar). DFU's AcrobatMotor
            // checks HasAction(Jump) continuously + requires GroundedTime >= 0.1f.
            if (Held(VRActionBinder.XButtonAction))
            {
                im.AddAction(InputManager.Actions.Jump);
            }
            // Left trigger -> Recast spell (actual cast). CastSpell opens the spell book (on B).
            if (Pressed(VRActionBinder.TriggerLeftAction))
            {
                im.AddAction(InputManager.Actions.RecastSpell);
                Debug.Log("[DFUQuest3] LeftTrigger -> RecastSpell (cast)");
            }
            // Left thumbstick click -> Crouch.
            if (Pressed(VRActionBinder.StickClickLeftAction))
            {
                im.AddAction(InputManager.Actions.Crouch);
                Debug.Log("[DFUQuest3] LeftStickClick -> Crouch");
            }
            // Right thumbstick click -> Toggle run (sprint).
            if (Pressed(VRActionBinder.StickClickRightAction))
            {
                runToggled = !runToggled;
                Debug.Log("[DFUQuest3] RightStickClick -> run " + (runToggled ? "ON" : "OFF"));
            }
            if (runToggled)
            {
                im.AddAction(InputManager.Actions.Run);
            }
            // Right grip -> right-hand weapon/shield use (SwingWeapon).
            if (Pressed(VRActionBinder.GripRightAction))
            {
                im.AddAction(InputManager.Actions.SwingWeapon);
                Debug.Log("[DFUQuest3] RightGrip -> SwingWeapon (right hand)");
            }
            // Menu button -> CONTEXT-AWARE. If a window is open (WindowCount>0, i.e. not
            // just the HUD), it acts as BACK/EXIT (close the top window). If no window is
            // open, it opens the pause options dialog (Save/Load/Settings/Controls).
            bool windowOpen = false;
            try
            {
                var uiMgr = DaggerfallWorkshop.Game.DaggerfallUI.UIManager;
                if (uiMgr != null && uiMgr.WindowCount > 0)
                    windowOpen = true;
            }
            catch { }
            if (Pressed(VRActionBinder.MenuButtonAction))
            {
                if (windowOpen)
                {
                    im.vrEscapeQueuedFrame = Time.frameCount + 1;
                    Debug.Log("[DFUQuest3] Menu -> back (close window)");
                }
                else
                {
                    im.AddAction(InputManager.Actions.Escape);
                    im.vrEscapeQueuedFrame = Time.frameCount + 1;
                    Debug.Log("[DFUQuest3] Menu -> open pause options");
                }
            }
        }

        // Edge-triggered button press (fires once on rising edge).
        bool Pressed(InputAction a)
        {
            if (a == null || !a.enabled) return false;
            try { return a.WasPressedThisFrame(); }
            catch { return false; }
        }

        // Continuous button hold.
        bool Held(InputAction a)
        {
            if (a == null || !a.enabled) return false;
            try { return a.IsPressed(); }
            catch { return false; }
        }
    }
}
