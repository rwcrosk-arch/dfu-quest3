// DFU Quest3 VR — locomotion + interaction shim.
// Map XR thumbsticks to DFU PlayerMotor input, and route XR
// trigger presses into DFU's PlayerActivate raycast.

using UnityEngine;
using UnityEngine.InputSystem;
using DaggerfallWorkshop.Game;

namespace DFUQuest3
{
    public class VRPlayerInput : MonoBehaviour
    {
        [Header("Input Actions (XR controller thumbsticks)")]
        public InputActionReference moveAction;   // left stick
        public InputActionReference turnAction;   // right stick (snap turn)
        public InputActionReference activateAction; // right trigger → PlayerActivate
        public InputActionReference jumpAction;   // A/X

        [Header("References")]
        public Transform headTransform; // XR camera
        public PlayerMotor motor;

        [Header("Tuning")]
        public float moveSpeed = 3f;
        public float snapTurnDegrees = 45f;

        bool lastActivate;
        bool lastJump;
        float turnCooldown;
        Transform rigTransform; // XR Origin — yaw-rotate this for snap turn

        void Start()
        {
            if (motor == null)
                motor = FindFirstObjectByType<PlayerMotor>();
            if (motor == null) { enabled = false; return; }
            rigTransform = transform; // expected to live on XR Origin under Player

            moveAction?.action.Enable();
            turnAction?.action.Enable();
            activateAction?.action.Enable();
            jumpAction?.action.Enable();
        }

        void Update()
        {
            if (motor == null) return;

            // --- Locomotion: head-relative movement from left stick ---
            Vector2 stick = moveAction?.action.ReadValue<Vector2>() ?? Vector2.zero;
            if (stick.sqrMagnitude > 0.0001f)
            {
                Vector3 fwd = headTransform != null ? headTransform.forward : transform.forward;
                fwd.y = 0; fwd.Normalize();
                Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
                Vector3 worldMove = (fwd * stick.y + right * stick.x) * moveSpeed;

                // Feed into DFU's movement via PlayerMotor's public input
                var cc = motor.GetComponent<CharacterController>();
                if (cc != null) cc.Move(worldMove * Time.deltaTime);
            }

            // --- Snap turn on right stick ---
            turnCooldown -= Time.deltaTime;
            if (turnCooldown <= 0f)
            {
                Vector2 turn = turnAction?.action.ReadValue<Vector2>() ?? Vector2.zero;
                if (Mathf.Abs(turn.x) > 0.6f)
                {
                    float dir = Mathf.Sign(turn.x);
                    rigTransform.RotateAround(
                        headTransform != null ? headTransform.position : transform.position,
                        Vector3.up, snapTurnDegrees * dir);
                    turnCooldown = 0.3f;
                }
            }

            // --- Activate (right trigger) → PlayerActivate raycast ---
            bool act = activateAction != null && activateAction.action.ReadValue<float>() > 0.5f;
            if (act && !lastActivate) TryActivate();
            lastActivate = act;

            // --- Jump ---
            bool jmp = jumpAction != null && jumpAction.action.ReadValue<float>() > 0.5f;
            if (jmp && !lastJump)
            {
                // DFU PlayerMotor has its own jump logic; for now nudge CharacterController
                // via PlayerMotor's fields if exposed. TODO: integrate with PlayerMotor.Jump().
            }
            lastJump = jmp;
        }

        void TryActivate()
        {
            var pActivate = FindFirstObjectByType<PlayerActivate>();
            if (pActivate == null) return;

            // DFU raycasts from Camera.main. XR camera is now MainCamera, so the
            // same code path works. Synthesize a center-ray activate by briefly
            // enabling DFU's interaction. In DFU, PlayerActivate polls InputManager
            // for the Activate key — inject via InputManager's Raise if available, or
            // directly call its private ActivateCenter via reflection as a stopgap.
            var m = typeof(PlayerActivate).GetMethod("ActivateCenter",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (m != null) m.Invoke(pActivate, null);
        }
    }
}
