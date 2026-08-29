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

            // Right primary (A) -> Sheath/unsheathe weapon. CALL THE MANAGER DIRECTLY, not
            // via AddAction(ReadyWeapon): WeaponManager checks ActionStarted(ReadyWeapon) at
            // default order (0), BEFORE this injector (order 100), so an injected action is
            // never seen as "started" on the same frame. Direct call bypasses that timing.
            // BUT: we must check that the WeaponManager exists AND isn't mid-attack (attack
            // overrides sheath state). The direct call is correct; timing is the fix.
            if (Pressed(VRActionBinder.AButtonAction))
            {
                var wm = DaggerfallWorkshop.Game.GameManager.Instance?.WeaponManager;
                if (wm != null)
                {
                    // ToggleSheath is safe to call even if attacking; WeaponManager.Update
                    // will reconcile the state on its next frame. The key fix is that we
                    // call it at order 100, AFTER InputManager has processed, so the
                    // Sheathed flag change persists into the next frame's Update.
                    wm.ToggleSheath();
                    var sw = wm.ScreenWeapon;
                    Debug.Log("[DFUQuest3] A -> ToggleSheath (direct, sheathed=" + wm.Sheathed +
                        ", weaponType=" + (sw != null ? sw.WeaponType.ToString() : "null") +
                        ", showWeapon=" + (sw != null ? sw.ShowWeapon : false) + ")");
                }
                else
                {
                    im.AddAction(InputManager.Actions.ReadyWeapon);
                    Debug.Log("[DFUQuest3] A -> ReadyWeapon (fallback, no WeaponManager)");
                }
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
            // Left trigger -> Recast/cast the last spell. CALL DIRECTLY: EntityEffectManager
            // checks ActionStarted(RecastSpell) at order 0 (before this injector), so an
            // injected action is never seen as "started". Replicate its recast logic
            // (line 257): need a last spell, not playing a cast anim, and a spellbook.
            if (Pressed(VRActionBinder.TriggerLeftAction))
            {
                var eem = DaggerfallWorkshop.Game.GameManager.Instance?.PlayerEffectManager;
                if (eem != null && eem.LastSpell != null &&
                    !DaggerfallWorkshop.Game.GameManager.Instance.PlayerSpellCasting.IsPlayingAnim)
                {
                    eem.SetReadySpell(eem.LastSpell);
                    Debug.Log("[DFUQuest3] LeftTrigger -> SetReadySpell (direct cast)");
                }
                else
                {
                    im.AddAction(InputManager.Actions.RecastSpell);
                    Debug.Log("[DFUQuest3] LeftTrigger -> RecastSpell (fallback)");
                }
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
                    // Open the pause options dialog (Save/Load/Settings/Controls) DIRECTLY.
                    // GameManager opens it via ActionComplete(Escape), which requires Escape
                    // in previousActions but NOT currentActions — a 2-frame press/release
                    // our injector can't satisfy with a single AddAction. Posting the message
                    // directly opens the window reliably.
                    DaggerfallWorkshop.Game.DaggerfallUI.PostMessage(
                        DaggerfallWorkshop.Game.DaggerfallUIMessages.dfuiOpenPauseOptionsDialog);
                    Debug.Log("[DFUQuest3] Menu -> PostMessage open pause options");
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
