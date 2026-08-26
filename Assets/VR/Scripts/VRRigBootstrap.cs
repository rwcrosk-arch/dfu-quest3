// DFU Quest3 VR — XR rig bootstrap (augment-DFU approach).
// Does NOT create a competing camera. Instead it finds DFU's existing camera(s):
//   - Startup/menu scene: DFU's startup camera (Camera.main) is wired to head tracking
//     so the IMGUI menu overlay follows the headset.
//   - Game scene: DFU's real player camera (the one carrying PlayerMouseLook) is
//     resolved and force-registered with GameManager (MainCameraObject + MainCamera)
//     so every DFU camera lookup (PlayerMouseLook, PlayerActivate, PostProcess, etc.)
//     points at the object that actually has those components.
// Without this re-resolution, GameManager.MainCameraObject returns the stale
// DontDestroyOnLoad startup camera ("Camera", tagged MainCamera, no PlayerMouseLook),
// and StartNewCharacter throws "could not find PlayerMouseLook on object Camera"
// before PauseGame(false), leaving the menu frozen over the game.

using UnityEngine;
using UnityEngine.XR;
using UnityEngine.InputSystem;
using Unity.XR.CoreUtils;
using DaggerfallWorkshop.Game;

namespace DFUQuest3
{
    public class VRRigBootstrap : MonoBehaviour
    {
        [Tooltip("Auto-configured if left null.")]
        public XROrigin xrOrigin;
        public Camera dfCamera;

        private bool menuWired;      // wired to startup/menu camera
        private bool gameWired;      // wired to the real PlayerMouseLook camera

        void Start()
        {
            if (xrOrigin == null)
                xrOrigin = FindFirstObjectByType<XROrigin>();
            if (xrOrigin == null)
            {
                Debug.LogError("[DFUQuest3] No XROrigin in scene.");
                enabled = false;
                return;
            }
        }

        void Update()
        {
            if (xrOrigin == null) return;

            // Prefer the real DFU game camera (has PlayerMouseLook). Resolve it by
            // component, not by Camera.main — Camera.main is shadowed by the startup
            // camera in the game scene.
            Camera gameCam = FindGameCamera<PlayerMouseLook>();
            if (gameCam != null)
            {
                if (!gameWired || dfCamera != gameCam)
                    Wire(gameCam, true);
                return;
            }

            // No game camera yet (startup/menu scene): wire Camera.main to the headset.
            if (!menuWired)
            {
                Camera menuCam = Camera.main;
                if (menuCam == null)
                {
                    Debug.LogWarning("[DFUQuest3] Camera.main not ready; retrying.");
                    return;
                }
                Wire(menuCam, false);
            }
        }

        Camera FindGameCamera<T>() where T : Component
        {
            // Scan all cameras for the one carrying the DFU player-look component.
            foreach (Camera c in Camera.allCameras)
            {
                if (c != null && c.GetComponent<T>() != null)
                    return c;
            }
            return null;
        }

        void Wire(Camera target, bool isGame)
        {
            dfCamera = target;

            // Drive the target camera from the headset (TrackedPoseDriver).
            var tpd = dfCamera.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
            if (tpd == null)
                tpd = dfCamera.gameObject.AddComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
            tpd.positionInput = new InputActionProperty(
                new InputAction("Position", InputActionType.Value, "<XRHMD>/centerEyePosition"));
            tpd.rotationInput = new InputActionProperty(
                new InputAction("Rotation", InputActionType.Value, "<XRHMD>/centerEyeRotation"));
            tpd.trackingType = UnityEngine.InputSystem.XR.TrackedPoseDriver.TrackingType.RotationAndPosition;
            tpd.updateType = UnityEngine.InputSystem.XR.TrackedPoseDriver.UpdateType.BeforeRender;

            // Keep DFU's mouse-look but neutralize it so it doesn't fight the XR pose.
            var ml = dfCamera.GetComponent<PlayerMouseLook>();
            if (ml != null) ml.enabled = false;

            // Parent under the XROrigin so the floor offset applies, and register it
            // as the tracked head.
            if (dfCamera.transform.parent != xrOrigin.transform)
                dfCamera.transform.SetParent(xrOrigin.transform, true);
            if (xrOrigin.Camera != dfCamera)
                xrOrigin.Camera = dfCamera;

            if (isGame)
            {
                // Force every GameManager camera lookup at the real game camera.
                var gm = DaggerfallWorkshop.Game.GameManager.Instance;
                if (gm != null)
                {
                    gm.MainCameraObject = dfCamera.gameObject;
                    gm.MainCamera = dfCamera;
                }
                gameWired = true;
                menuWired = false;
                Debug.Log($"[DFUQuest3] Game camera wired to head tracking + GameManager.MainCameraObject={dfCamera.name}.");
            }
            else
            {
                menuWired = true;
                Debug.Log("[DFUQuest3] Menu camera wired to head tracking.");
            }
        }
    }
}
