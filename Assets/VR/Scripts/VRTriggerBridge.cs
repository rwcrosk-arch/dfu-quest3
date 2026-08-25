// DFU Quest3 VR — additive VR trigger bridge (does NOT touch VRUIOverlay).
// VRUIOverlay.cs must remain byte-identical to the known-good baseline (any edit regresses
// controller ray tracking). This component is a separate, purely-additive trigger reader
// that sets InputManager.vrClickQueued on a rising trigger edge — the same flag the overlay's
// click path consumes. Reads the trigger from InputSystem XRController (analog axis),
// OVRInput, and legacy InputDevices. Each source is guarded so one failure never aborts the rest.

using UnityEngine;
using UnityEngine.XR;
using UnityEngine.InputSystem;
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
                // Direct action: push the StartNewGameWizard window (same as the New Game
                // button's OnMouseClick). This bypasses the fragile single-shot
                // vrClickQueued + mouse-over mechanism that never lands on the button.
                TryDirectNewGame();
                var im = InputManager.Instance;
                if (im != null)
                {
                    im.vrClickQueued = true;
                    Debug.Log("[DFUQuest3] TriggerBridge: click queued");
                }
            }
            lastTrigger = trigger;
        }

        float hb = 0f;

        // Directly push the StartNewGameWizard window (same as the New Game button's
        // OnMouseClick handler). Bypasses the fragile mouse-over + single-shot flag path.
        void TryDirectNewGame()
        {
            try
            {
                var uiMgr = DaggerfallUI.UIManager;
                if (uiMgr == null) return;
                var top = uiMgr.TopWindow;
                if (top is DaggerfallWorkshop.Game.UserInterfaceWindows.DaggerfallStartWindow)
                {
                    uiMgr.PushWindow(
                        DaggerfallWorkshop.Game.UserInterfaceWindows.UIWindowFactory.GetInstance(
                            DaggerfallWorkshop.Game.UserInterfaceWindows.UIWindowType.StartNewGameWizard,
                            uiMgr));
                    Debug.Log("[DFUQuest3] TriggerBridge: pushed StartNewGameWizard directly");
                }
            }
            catch (System.Exception e)
            {
                Debug.Log("[DFUQuest3] TriggerBridge: direct newgame failed: " + e.Message);
            }
        }

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
