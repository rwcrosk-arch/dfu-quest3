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
        public static InputAction TurnAction { get; private set; }
        public static InputAction AButtonAction { get; private set; }   // right primary
        public static InputAction BButtonAction { get; private set; }   // right secondary
        public static InputAction XButtonAction { get; private set; }  // left primary
        public static InputAction YButtonAction { get; private set; }   // left secondary
        public static InputAction GripLeftAction { get; private set; }
        public static InputAction GripRightAction { get; private set; }
        public static InputAction TriggerLeftAction { get; private set; }
        public static InputAction StickClickLeftAction { get; private set; }
        public static InputAction MenuButtonAction { get; private set; }

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

            // Turn (right thumbstick X) — drives yaw rotation of the player rig.
            var turn = map.AddAction("Turn", InputActionType.Value);
            turn.AddBinding("<MetaQuestTouchPlusControllerOpenXR>{RightHand}/thumbstick");
            turn.AddBinding("<XRController>{RightHand}/thumbstick");

            // --- Buttons (PCVR DFU template, see VRInputBridge.cs) ---
            // Right primary (A) -> CastSpell
            var aBtn = map.AddAction("AButton", InputActionType.Button);
            aBtn.AddBinding("<MetaQuestTouchPlusControllerOpenXR>{RightHand}/primaryButton");
            aBtn.AddBinding("<XRController>{RightHand}/primaryButton");
            // Right secondary (B) -> Inventory
            var bBtn = map.AddAction("BButton", InputActionType.Button);
            bBtn.AddBinding("<MetaQuestTouchPlusControllerOpenXR>{RightHand}/secondaryButton");
            bBtn.AddBinding("<XRController>{RightHand}/secondaryButton");
            // Left primary (X) -> (reserved; PCVR uses it for nothing critical)
            var xBtn = map.AddAction("XButton", InputActionType.Button);
            xBtn.AddBinding("<MetaQuestTouchPlusControllerOpenXR>{LeftHand}/primaryButton");
            xBtn.AddBinding("<XRController>{LeftHand}/primaryButton");
            // Left secondary (Y) -> RecastSpell
            var yBtn = map.AddAction("YButton", InputActionType.Button);
            yBtn.AddBinding("<MetaQuestTouchPlusControllerOpenXR>{LeftHand}/secondaryButton");
            yBtn.AddBinding("<XRController>{LeftHand}/secondaryButton");
            // Left grip -> Jump (HELD, like the spacebar). Bind to the discrete button
            // control (gripPressed on MetaQuestTouchPlus, gripButton on generic XRController)
            // with the analog 'grip' axis as fallback — 'grip' alone is an AxisControl
            // (squeeze 0-1) that can plateau under the 0.5 press point and never fire as
            // a button. DFU's AcrobatMotor checks HasAction(Jump) continuously + requires
            // GroundedTime >= 0.1f, so hold the grip to jump.
            var gripL = map.AddAction("GripLeft", InputActionType.Button);
            gripL.AddBinding("<MetaQuestTouchPlusControllerOpenXR>{LeftHand}/gripPressed");
            gripL.AddBinding("<XRController>{LeftHand}/gripButton");
            gripL.AddBinding("<MetaQuestTouchPlusControllerOpenXR>{LeftHand}/grip");
            gripL.AddBinding("<XRController>{LeftHand}/grip");
            // Right grip -> Run (hold)
            var gripR = map.AddAction("GripRight", InputActionType.Button);
            gripR.AddBinding("<MetaQuestTouchPlusControllerOpenXR>{RightHand}/gripPressed");
            gripR.AddBinding("<XRController>{RightHand}/gripButton");
            gripR.AddBinding("<MetaQuestTouchPlusControllerOpenXR>{RightHand}/grip");
            gripR.AddBinding("<XRController>{RightHand}/grip");
            // Left trigger -> Crouch
            var trigL = map.AddAction("TriggerLeft", InputActionType.Button);
            trigL.AddBinding("<MetaQuestTouchPlusControllerOpenXR>{LeftHand}/trigger");
            trigL.AddBinding("<XRController>{LeftHand}/trigger");
            // Left thumbstick click -> AutoRun
            var stickL = map.AddAction("StickClickLeft", InputActionType.Button);
            stickL.AddBinding("<MetaQuestTouchPlusControllerOpenXR>{LeftHand}/thumbstickClick");
            stickL.AddBinding("<XRController>{LeftHand}/thumbstickClick");
            // Menu button -> Escape (pause)
            var menuBtn = map.AddAction("MenuButton", InputActionType.Button);
            menuBtn.AddBinding("<MetaQuestTouchPlusControllerOpenXR>{LeftHand}/menu");
            menuBtn.AddBinding("<XRController>{LeftHand}/menu");

            TriggerAction = trig;
            PinchAction = pinch;
            MoveAction = move;
            TurnAction = turn;
            AButtonAction = aBtn;
            BButtonAction = bBtn;
            XButtonAction = xBtn;
            YButtonAction = yBtn;
            GripLeftAction = gripL;
            GripRightAction = gripR;
            TriggerLeftAction = trigL;
            StickClickLeftAction = stickL;
            MenuButtonAction = menuBtn;
            map.Enable();

            Debug.Log("[DFUQuest3] VRActionBinder: built + enabled VR action map");
        }
    }
}
