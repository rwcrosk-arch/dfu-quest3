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

        // Self-wire at runtime so VRSceneSetup.cs (and VRUIOverlay.cs) stay untouched.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Autostart()
        {
            var go = new GameObject("DFUQuest3 VRTriggerBridge");
            go.AddComponent<VRTriggerBridge>();
            DontDestroyOnLoad(go);
            Debug.Log("[DFUQuest3] VRTriggerBridge auto-wired");
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
                // Left stick is 180° flipped (left->right, down->up) while the right stick
                // reads correctly. The left controller's thumbstick reports inverted axes on
                // this OpenXR/InputSystem layout. Negate both components so movement matches
                // stick direction. (Right stick/turn is unaffected — it reads correctly.)
                im2.vrMoveStick = new Vector2(-stick.x, -stick.y);

                // VR turn: read the right thumbstick X and rotate the player rig's yaw.
                // The rig (XROrigin) is parented under the Player object, so rotating it
                // turns the player. Smooth continuous turn from the right stick X.
                Vector2 turnStick = Vector2.zero;
                try
                {
                    if (VRActionBinder.TurnAction != null && VRActionBinder.TurnAction.enabled)
                        turnStick = VRActionBinder.TurnAction.ReadValue<Vector2>();
                }
                catch { }
                if (Mathf.Abs(turnStick.x) > 0.15f)
                {
                    var rig = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
                    if (rig != null)
                    {
                        float turnSpeed = 120f; // degrees per second
                        rig.transform.Rotate(0f, turnStick.x * turnSpeed * Time.unscaledDeltaTime, 0f, Space.World);
                    }
                }
            }
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
