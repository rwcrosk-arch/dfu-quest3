// DFU Quest3 VR — additive VR trigger bridge (does NOT touch VRUIOverlay).
// VRUIOverlay.cs must remain byte-identical to the known-good baseline (any edit regresses
// controller ray tracking). This component is a separate, purely-additive trigger reader
// that sets InputManager.vrClickQueued on a rising trigger edge — the same flag the overlay's
// click path consumes. It reads the trigger from InputSystem XRController devices and legacy
// InputDevices every frame.

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
            bool trigger = ReadTrigger();
            if (trigger && !lastTrigger)
            {
                var im = InputManager.Instance;
                if (im != null)
                {
                    im.vrClickQueued = true;
                    Debug.Log("[DFUQuest3] TriggerBridge: click queued");
                }
            }
            lastTrigger = trigger;
        }

        bool ReadTrigger()
        {
            // 1) InputSystem XRController devices.
            foreach (var dev in UnityEngine.InputSystem.InputSystem.devices)
            {
                var xrCtrl = dev as UnityEngine.InputSystem.XR.XRController;
                if (xrCtrl == null) continue;
                if (xrCtrl.TryGetChildControl<UnityEngine.InputSystem.Controls.ButtonControl>("trigger") is var tc && tc != null && tc.ReadValue() > 0.5f)
                    return true;
            }
            // 2) Legacy InputDevices.
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
            return false;
        }
    }
}
