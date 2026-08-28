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
        private string lastFollowError; // dedupe follow-failure logs

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
                // Rig-following is done in LateUpdate (position + yaw from the live
                // player). Do NOT SetParent here: DFU's char-creation PlayerObject is a
                // DIFFERENT object than the one spawned for gameplay (StartNewCharacter
                // creates a fresh Player), so a rig parented during char-creation gets
                // orphaned when that temp object is destroyed -> rigParent=none in
                // gameplay. LateUpdate re-asserts the follow every frame instead.
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

        // Make the rig (and thus camera/view) follow the live player every frame.
        // Game-scene only. Never relies on parenting persisting: DFU's char-creation
        // PlayerObject is a DIFFERENT object than the gameplay one (StartNewCharacter
        // creates a fresh Player and destroys the temp), so a rig parented during
        // char-creation gets orphaned -> rigParent=none in gameplay. Drive position and
        // YAW explicitly each frame instead. Yaw coupling preserved: stick-yaw rotates
        // PlayerObject (VRTriggerBridge), the rig copies that yaw, so the view turns as
        // before. Pitch stays on VRHeadPitch (child of the rig), untouched.
        void LateUpdate()
        {
            if (xrOrigin == null || !gameWired) return; // game-scene only
            try
            {
                var gm = DaggerfallWorkshop.Game.GameManager.Instance;
                var playerObj = gm != null ? gm.PlayerObject : null;
                if (playerObj == null) return;

                Transform rig = xrOrigin.transform;
                Transform p = playerObj.transform;

                // Keep the rig unparented (kill any stale half-parenting) and follow explicitly.
                if (rig.parent != null) rig.SetParent(null, true);

                // Position: player feet (HMD tracked local offset adds eye height).
                rig.position = p.position;
                // Rotation: yaw only, so rig/pitch-node/view turn with the player and
                // left-stick movement (Player local space) stays calibrated.
                rig.rotation = Quaternion.Euler(0f, p.eulerAngles.y, 0f);
            }
            catch (System.Exception ex)
            {
                if (ex.Message != lastFollowError)
                {
                    lastFollowError = ex.Message;
                    Debug.LogWarning("[DFUQuest3] Rig-follow skipped: " + ex.Message);
                }
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
            // as the tracked head. A dedicated camera-pitch node (VRHeadPitch) sits
            // between the rig and the camera so right-stick-Y pitch tilts ONLY the view,
            // never the rig/player movement frame (tilting the rig is what made both
            // sticks lose calibration after right-stick use).
            Transform pitch = xrOrigin.transform.Find("VRHeadPitch");
            if (pitch == null)
            {
                var pitchGo = new GameObject("VRHeadPitch");
                pitch = pitchGo.transform;
                pitch.SetParent(xrOrigin.transform, false);
            }
            if (dfCamera.transform.parent != pitch)
                dfCamera.transform.SetParent(pitch, true);
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

                // --- Keep only the PostProcessLayer disable (one-eye-white fix). ---
                // PPv2 (PostProcessing 2) + OpenXR Single-Pass-Instanced is a known-broken
                // combo: the game scene's MainCamera carries a PostProcessLayer (the startup
                // scene's doesn't). Under SPI the PPv2 blit goes to one eye and the other
                // gets the raw/un-cleared buffer -> one white eye. Disable it on the VR cam.
                //
                // NOTE: do NOT force a SolidColor clear or disable the other scene cameras
                // here. DFU renders the exterior sky via a SEPARATE SkyCamera (DaggerfallSky,
                // OnPostRender/DrawSky) — disabling it and forcing SolidColor black on the
                // main camera produces a pure-black sky. The 2019 reference build that
                // reached colored gameplay left CameraClearManager and all scene cameras
                // alone; do the same.
                var ppLayer = dfCamera.GetComponent<UnityEngine.Rendering.PostProcessing.PostProcessLayer>();
                if (ppLayer != null)
                    ppLayer.enabled = false;

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
