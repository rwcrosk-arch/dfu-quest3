// DFU Quest3 VR — additive Input System action binder.
// ROOT CAUSE (verified by orchestrator kimi-k3): the project runs Input System
// (activeInputHandler:2) but the Input Actions asset has ZERO actions/bindings, so the
// OpenXR InputSystem driver creates the controller/hand devices but never submits the
// action set + bindings to the runtime -> no input VALUES ever update (devices appear,
// TryGetFeatureValue returns 0). This builds the action asset in code and enables it.
// Purely additive — does NOT touch VRUIOverlay.cs.

using UnityEngine;
using UnityEngine.InputSystem;

namespace DFUQuest3
{
    public class VRActionBinder
    {
        public static InputAction TriggerAction { get; private set; }
        public static InputAction PinchAction { get; private set; }
        public static InputAction MoveAction { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Autostart()
        {
            BuildAndEnable();
        }

        static void BuildAndEnable()
        {
            var asset = new InputActionAsset();
            var map = new InputActionMap("VR");
            asset.AddActionMap(map);

            // Trigger (controller right trigger) — bind to the InputSystem layout names
            // actually registered by the OpenXR plugin (seen in device list):
            //   MetaQuestTouchPlusControllerOpenXR  and generic XRController
            var trig = map.AddAction("Trigger", InputActionType.Button);
            trig.AddBinding("<MetaQuestTouchPlusControllerOpenXR>{RightHand}/trigger");
            trig.AddBinding("<XRController>{RightHand}/trigger");

            // Pinch (hand tracking select) — MetaAimHand + generic XRHand layouts
            var pinch = map.AddAction("Pinch", InputActionType.Button);
            pinch.AddBinding("<MetaAimHand>{RightHand}/select");
            pinch.AddBinding("<XRHand>{RightHand}/select");

            // Move (left thumbstick) — drives DFU locomotion via InputManager.vrMoveStick.
            var move = map.AddAction("Move", InputActionType.Value);
            move.AddBinding("<MetaQuestTouchPlusControllerOpenXR>{LeftHand}/thumbstick");
            move.AddBinding("<XRController>{LeftHand}/thumbstick");

            TriggerAction = trig;
            PinchAction = pinch;
            MoveAction = move;
            map.Enable();

            Debug.Log("[DFUQuest3] VRActionBinder: built + enabled VR action map");
        }
    }
}
