// DFU Quest3 VR — additive VR trigger bridge (does NOT touch VRUIOverlay).
// VRUIOverlay.cs must remain byte-identical to the known-good baseline (any edit regresses
// controller ray tracking). This component is a separate, purely-additive trigger reader
// that sets InputManager.vrClickQueued on a rising trigger edge — the same flag the overlay's
// click path consumes. Reads the trigger from InputSystem XRController (analog axis),
// OVRInput, and legacy InputDevices. Each source is guarded so one failure never aborts the rest.

using UnityEngine;
using UnityEngine.XR;
using UnityEngine.InputSystem;
using Unity.XR.CoreUtils;
using DaggerfallWorkshop.Game;

namespace DFUQuest3
{
    public class VRTriggerBridge : MonoBehaviour
    {
        bool lastTrigger;
        // Cycle state for the left-grip menu cycler: opens a different DFU window per
        // press (CharacterSheet -> Inventory -> QuestJournal -> AutoMap -> TravelMap -> Rest).
        int menuCycleIndex = -1;
        bool lastGripLeftHeld;
        // Cycle through the non-face-button DFU windows. Face buttons already open
        // Spells (A) and Inventory (B), so the cycler covers the rest.
        static readonly InputManager.Actions[] MenuCycle =
        {
            InputManager.Actions.CharacterSheet,
            InputManager.Actions.LogBook,  // opens the quest journal
            InputManager.Actions.AutoMap,
            InputManager.Actions.TravelMap,
            InputManager.Actions.Rest,
        };

        // Self-wire at runtime so VRSceneSetup.cs (and VRUIOverlay.cs) stay untouched.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Autostart()
        {
            var go = new GameObject("DFUQuest3 VRTriggerBridge");
            go.AddComponent<VRTriggerBridge>();
            DontDestroyOnLoad(go);

            // Disable DFU's legacy joystick path. On Android the legacy Input.GetAxis
            // ("Axis1" etc.) maps to the XR controller sticks, so DFU's joystick movement
            // AND camera-look read the same sticks as our VR code, fighting it: right-stick
            // Y turned the camera (slow yaw), left-stick strafe got double-driven. Turning
            // EnableController off makes our VR code the sole stick reader. We implement
            // right-stick-Y pitch ourselves.
            try
            {
                var im = InputManager.Instance;
                if (im != null) im.EnableController = false;
                DaggerfallWorkshop.DaggerfallUnity.Settings.EnableController = false;
                Debug.Log("[DFUQuest3] VRTriggerBridge: legacy joystick (EnableController) disabled.");
            }
            catch (System.Exception e)
            {
                Debug.Log("[DFUQuest3] VRTriggerBridge: could not disable EnableController: " + e.Message);
            }
        }

        void Update()
        {
            // Decisive OVRInput probe: is OVRInput initialized, and does it read the trigger?
            hb -= Time.unscaledDeltaTime;
            if (hb <= 0f)
            {
                hb = 2f;
                string ovrState = "n/a";
                bool ovrTrig = false;
                try { ovrState = OVRInput.IsControllerConnected(OVRInput.Controller.RTouch) ? "RTouchConnected" : "RTouchDisconnected"; }
                catch { ovrState = "OVRInput-not-init"; }
                try { ovrTrig = OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger); }
                catch { }
                Debug.Log("[DFUQuest3] OVRInput probe: " + ovrState + " trigger=" + ovrTrig);
            }
            bool trigger = ReadTrigger();
            if (trigger && !lastTrigger)
            {
                // Set the click flag ONCE on the rising edge. InputManager clears it in
                // LateUpdate (frame-sticky), so every component in the UI tree sees the
                // same click on the same frame — no need to hold it across frames. Holding
                // it for 3 frames re-asserted the flag on 3 consecutive frames, which
                // forced 3 presses per trigger pull.
                var im = InputManager.Instance;
                if (im != null)
                {
                    im.vrClickQueued = true;
                    // Action button: in gameplay (PlayerMotor present) the trigger also
                    // fires ActivateCenterObject so it opens doors / uses objects, not just
                    // clicks the menu cursor. (In the menu the click path handles buttons.)
                    try
                    {
                        var gm = DaggerfallWorkshop.Game.GameManager.Instance;
                        if (gm != null && gm.PlayerMotor != null)
                        {
                            im.AddAction(InputManager.Actions.ActivateCenterObject);
                            Debug.Log("[DFUQuest3] TriggerBridge: ActivateCenterObject");
                        }
                    }
                    catch { }
                    Debug.Log("[DFUQuest3] TriggerBridge: click queued");
                }
            }
            lastTrigger = trigger;

            // VR locomotion: feed the left thumbstick into InputManager.vrMoveStick so
            // DFU's PlayerMotor (via InputManager.Horizontal/Vertical) moves the player.
            // Only when the game is not paused (menus use the stick for cursor too).
            var im2 = InputManager.Instance;
            if (im2 != null)
            {
                Vector2 stick = Vector2.zero;
                try
                {
                    if (VRActionBinder.MoveAction != null && VRActionBinder.MoveAction.enabled)
                        stick = VRActionBinder.MoveAction.ReadValue<Vector2>();
                }
                catch { }
                if (stick.sqrMagnitude < 0.0001f)
                {
                    // Fallback: read the LEFT XR controller thumbstick directly. Select by
                    // handedness (usages contains LeftHand), not the first controller in the
                    // list — the first is often the right controller, whose stick would
                    // otherwise feed movement (strafe) instead of turning.
                    try
                    {
                        foreach (var dev in UnityEngine.InputSystem.InputSystem.devices)
                        {
                            if (dev is UnityEngine.InputSystem.XR.XRController xrCtrl)
                            {
                                bool isLeft = false;
                                foreach (var u in xrCtrl.usages)
                                {
                                    if (u == UnityEngine.InputSystem.CommonUsages.LeftHand) { isLeft = true; break; }
                                }
                                if (!isLeft)
                                    continue;
                                var ts = xrCtrl.TryGetChildControl<UnityEngine.InputSystem.Controls.Vector2Control>("thumbstick");
                                if (ts != null) { stick = ts.ReadValue(); break; }
                            }
                        }
                    }
                    catch { }
                }
                // Left stick reads inverted on this layout. The previous 180° negate
                // (-stick.x, -stick.y) was compensating for the OLD rig-yaw setup; now that
                // yaw rotates the Player object, that negate is backwards and left stick
                // reads inverted. Feed the raw stick through.
                im2.vrMoveStick = new Vector2(stick.x, stick.y);

                // VR turn: read the right thumbstick and rotate the player rig.
                // X = yaw (turn left/right), Y = pitch (look up/down).
                //
                // CRITICAL (stick-calibration fix): yaw rotates the PLAYER object, NOT
                // the XROrigin. DFU computes movement in the Player's local space
                // (FrictionMotor: myTransform.TransformDirection), so "left-stick
                // forward" = the Player's facing. Rotating the rig (a child of the
                // Player) left the Player's facing frozen and decoupled from where you
                // look -> sticks lost calibration. Rotating the Player keeps the
                // movement frame synced to the view, exactly like vanilla PlayerMouseLook.
                //
                // Pitch rotates a dedicated VRHeadPitch node (created by VRRigBootstrap
                // between the rig and the camera). Tilting ONLY the camera view, never
                // the rig or Player, keeps the yaw/movement plane level.
                Vector2 turnStick = Vector2.zero;
                try
                {
                    if (VRActionBinder.TurnAction != null && VRActionBinder.TurnAction.enabled)
                        turnStick = VRActionBinder.TurnAction.ReadValue<Vector2>();
                }
                catch { }
                if (Mathf.Abs(turnStick.x) > 0.15f || Mathf.Abs(turnStick.y) > 0.15f)
                {
                    // Yaw target: the Player object. Fall back to the XROrigin if no
                    // Player yet (shouldn't happen in-game, but never throw in menu).
                    Transform yawTarget = null;
                    try
                    {
                        var gm = GameManager.Instance;
                        if (gm != null && gm.PlayerObject != null)
                            yawTarget = gm.PlayerObject.transform;
                    }
                    catch { }
                    if (yawTarget == null)
                    {
                        var rig = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
                        if (rig != null) yawTarget = rig.transform;
                    }

                    float turnSpeed = 120f; // degrees per second (yaw)
                    float pitchSpeed = 60f;  // degrees per second (pitch)

                    if (yawTarget != null)
                    {
                        // Yaw around world up.
                        yawTarget.Rotate(0f, turnStick.x * turnSpeed * Time.unscaledDeltaTime, 0f, Space.World);
                    }

                    // Pitch the camera-pitch node (look up/down). Create it on the rig if
                    // VRRigBootstrap hasn't yet (menu fallback path).
                    Transform pitch = null;
                    try
                    {
                        var rig = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
                        if (rig != null)
                        {
                            pitch = rig.transform.Find("VRHeadPitch");
                            if (pitch == null && rig.Camera != null)
                            {
                                var pitchGo = new GameObject("VRHeadPitch");
                                pitch = pitchGo.transform;
                                pitch.SetParent(rig.transform, false);
                                rig.Camera.transform.SetParent(pitch, true);
                            }
                        }
                    }
                    catch { }
                    if (pitch != null)
                    {
                        // Pitch around the pitch node's local right axis (look up/down).
                        pitch.Rotate(-turnStick.y * pitchSpeed * Time.unscaledDeltaTime, 0f, 0f, Space.Self);
                    }
                }

                // --- Buttons (PCVR DFU template) ---
                // Inject DFU actions via InputManager.AddAction so all of DFU's gameplay
                // logic (cast, inventory, jump, crouch, run, recast, autorun, pause) works
                // unmodified. Edge-triggered buttons fire once on the rising edge.
                var im3 = InputManager.Instance;
                if (im3 != null)
                {
                    // Right primary (A) -> CastSpell
                    if (Pressed(VRActionBinder.AButtonAction))
                    {
                        im3.AddAction(InputManager.Actions.CastSpell);
                        Debug.Log("[DFUQuest3] A -> CastSpell");
                    }
                    // Right secondary (B) -> Inventory
                    if (Pressed(VRActionBinder.BButtonAction))
                    {
                        im3.AddAction(InputManager.Actions.Inventory);
                        Debug.Log("[DFUQuest3] B -> Inventory");
                    }
                    // Left secondary (Y) -> RecastSpell
                    if (Pressed(VRActionBinder.YButtonAction))
                    {
                        im3.AddAction(InputManager.Actions.RecastSpell);
                        Debug.Log("[DFUQuest3] Y -> RecastSpell");
                    }
                    // Left grip -> cycle menus (press = next window). The left grip is
                    // free since Jump moved to X. Cycles CharacterSheet -> LogBook ->
                    // AutoMap -> TravelMap -> Rest (face buttons already open Spells A /
                    // Inventory B). Edge-triggered on the rising edge.
                    bool gripLeftHeld = Held(VRActionBinder.GripLeftAction);
                    if (gripLeftHeld && !lastGripLeftHeld)
                    {
                        menuCycleIndex = (menuCycleIndex + 1) % MenuCycle.Length;
                        InputManager.Actions act = MenuCycle[menuCycleIndex];
                        im3.AddAction(act);
                        Debug.Log("[DFUQuest3] LeftGrip -> menu cycle: " + act);
                    }
                    lastGripLeftHeld = gripLeftHeld;
                    // X button (left primary) -> Jump (HELD, like the spacebar). DFU's
                    // AcrobatMotor checks HasAction(Jump) continuously + requires
                    // GroundedTime >= 0.1f, so hold X to jump.
                    if (Held(VRActionBinder.XButtonAction))
                    {
                        im3.AddAction(InputManager.Actions.Jump);
                    }
                    // Left trigger -> Crouch
                    if (Pressed(VRActionBinder.TriggerLeftAction))
                    {
                        im3.AddAction(InputManager.Actions.Crouch);
                        Debug.Log("[DFUQuest3] LeftTrigger -> Crouch");
                    }
                    // Left thumbstick click -> AutoRun
                    if (Pressed(VRActionBinder.StickClickLeftAction))
                    {
                        im3.AddAction(InputManager.Actions.AutoRun);
                        Debug.Log("[DFUQuest3] LeftStickClick -> AutoRun");
                    }
                    // Menu button -> Escape (pause) + back (close windows). The action
                    // opens the pause menu; the vrEscapeQueuedFrame counter lets DFU
                    // windows (which close on GetBackButtonUp) be dismissed from VR.
                    // Delay visibility to NEXT frame so the same press that just OPENED
                    // the pause menu can't immediately close it; the NEXT press closes
                    // the window regardless of script execution order (frame counter
                    // survives LateUpdate).
                    if (Pressed(VRActionBinder.MenuButtonAction))
                    {
                        im3.AddAction(InputManager.Actions.Escape);
                        im3.vrEscapeQueuedFrame = Time.frameCount + 1;
                        Debug.Log("[DFUQuest3] Menu -> Escape + back");
                    }
                    // Right grip -> Run (hold, continuous)
                    if (Held(VRActionBinder.GripRightAction))
                    {
                        im3.AddAction(InputManager.Actions.Run);
                    }
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

        float hb = 0f;

        bool ReadTrigger()
        {
            // 0) NEW: the VRActionBinder action (the orchestrator-verified root-cause fix).
            //    The OpenXR InputSystem driver only submits state when an action set with
            //    bindings is enabled — the empty default asset never did, so devices
            //    registered but no values flowed. This action drives the real trigger.
            //    Use WasPressedThisFrame (edge) so it fires ONCE per press, not continuously.
            try
            {
                if (VRActionBinder.TriggerAction != null && VRActionBinder.TriggerAction.enabled)
                {
                    if (VRActionBinder.TriggerAction.WasPressedThisFrame())
                    {
                        Debug.Log("[DFUQuest3] TriggerBridge: VRActionBinder Trigger pressed");
                        return true;
                    }
                }
            }
            catch { }

            // 1) InputSystem XR devices — the trigger control is an ANALOG AxisControl
            //    (0-1), not a ButtonControl. Reading it as ButtonControl throws
            //    InvalidOperationException every frame, so read it as an axis.
            try
            {
                foreach (var dev in UnityEngine.InputSystem.InputSystem.devices)
                {
                    if (dev is UnityEngine.InputSystem.XR.XRController xrCtrl)
                    {
                        var axis = xrCtrl.TryGetChildControl<UnityEngine.InputSystem.Controls.AxisControl>("trigger");
                        if (axis != null && axis.ReadValue() > 0.5f)
                            return true;
                    }
                }
            }
            catch { }

            // 2) OVRInput (Meta SDK native) — may read the real trigger even on OpenXR
            //    now that the interaction profile is bound and the controller tracks.
            try
            {
                if (OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger))
                    return true;
            }
            catch { /* OVRInput may not be initialized */ }

            // 3) Legacy InputDevices.
            try
            {
                var devs = new System.Collections.Generic.List<UnityEngine.XR.InputDevice>();
                InputDevices.GetDevices(devs);
                foreach (var d in devs)
                {
                    if ((d.characteristics & InputDeviceCharacteristics.Controller) != 0)
                    {
                        if (d.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out float tf) && tf > 0.5f) return true;
                        if (d.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool tb) && tb) return true;
                    }
                }
            }
            catch { }
            return false;
        }
    }
}
