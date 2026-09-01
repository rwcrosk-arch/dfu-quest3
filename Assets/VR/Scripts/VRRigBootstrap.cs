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
using System.Collections;
using System.Reflection;

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
        private bool followLogged;      // one-shot follow diagnostic
        private float followHeartbeat = 2f;
        private bool ppv2DeployQueued;    // defer the PPv2 redeploy out of the stereo pass

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

                // One-shot diagnostic: confirm the follow is actually running and moving
                // the rig to the player (the on-device diag showed rig at origin in
                // gameplay, so we need to see if this LateUpdate fires at all).
                if (!followLogged)
                {
                    followLogged = true;
                    Debug.Log($"[DFUQuest3] Rig-follow active: gameWired={gameWired} player={p.position} rigBefore={rig.position}");
                }

                // Keep the rig unparented (kill any stale half-parenting) and follow explicitly.
                if (rig.parent != null) rig.SetParent(null, true);

                // Position: player feet (HMD tracked local offset adds eye height).
                rig.position = p.position;
                // Rotation: yaw only, so rig/pitch-node/view turn with the player and
                // left-stick movement (Player local space) stays calibrated.
                rig.rotation = Quaternion.Euler(0f, p.eulerAngles.y, 0f);

                // Gameplay heartbeat: prove the rig STAYS at the player (the old on-device
                // rig=(0,0,0) reads were all char-creation, pre-gameWired).
                followHeartbeat -= Time.unscaledDeltaTime;
                if (followHeartbeat <= 0f)
                {
                    followHeartbeat = 2f;
                    Debug.Log($"[DFUQuest3] Rig-follow heartbeat: rig={rig.position} player={p.position} camWorld={xrOrigin.Camera.transform.position}");
                }
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

                // --- Fix ambient/brightness (dark world) ---
                // DFU is lit by RenderSettings.ambientLight driven by PlayerAmbientLight,
                // which depends on GameManager.MainCamera and SunlightManager.DaylightScale.
                // StartGameBehaviour.DeployCoreGameEffectSettings + SunlightManager run at
                // game start against the STALE startup camera (MainCameraObject was still the
                // DDOL startup camera), so the ambient pipeline computed the NIGHT value
                // (~0.25) instead of daytime (~0.9) and the PostProcessLayer was never found
                // on the wrong camera. Re-pointing MainCameraObject above is too late for the
                // already-cached PostProcessLayer field. Re-resolve it and re-push the core
                // effect settings now that the real camera is set, so AA/AO/Bloom and the
                // ambient/daylight rebind to the actual game camera.
                var sgb = FindFirstObjectByType<DaggerfallWorkshop.Game.Utility.StartGameBehaviour>();
                if (sgb != null && !ppv2DeployQueued)
                {
                    ppv2DeployQueued = true;
                    // Do NOT DeployCoreGameEffectSettings here. Wire() runs from Update()
                    // mid-frame inside the XR multi-pass stereo pass; calling it right now
                    // re-inits PPv2 mid-pass and corrupts per-eye render state (broken
                    // stereo: AO lands on one eye, the world-space VRKeyboard label overlay
                    // drops from one eye -> blank letters on the save screen). Opening the
                    // in-game effects menu fixes everything because it forces the SAME
                    // deploy + a PostProcessLayer bounce, but in script phase (between
                    // frames) where PPv2 rebuilds per-eye cleanly. Reproduce that here:
                    // defer the deploy + layer bounce a couple frames out of the pass.
                    StartCoroutine(DeferPpv2Redeploy());
                }

                // PostProcessLayer stays ENABLED. It was briefly disabled as part of the
                // one-eye-white fix (PPv2 + SPI is broken), but the SPI culprit was the
                // SolidColor-black force + camera suppression in a0875fb, NOT this layer —
                // all camera suppression was reverted in eb48810 and the white-eye resolved
                // with the OpenXR MULTI-PASS switch (dfc6349), where PPv2 renders per-eye
                // correctly. Keeping the layer disabled stripped DFU's ColorBoost grading
                // (StartGameBehaviour.DeployCoreGameEffectSettings -> ColorBoost) plus any
                // AA on the camera, which is the change that DARKENED gameplay. The 2019
                // reference build reached bright, colored gameplay with PPv2 left enabled.
                // If a PPv2 blit problem ever resurfaces, disable a single effect (TAA/AA)
                // on the layer, NEVER ppLayer.enabled (the whole layer).

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

        // Defer the PPv2 core-effect redeploy + a PostProcessLayer bounce a couple of
        // frames AFTER the game camera wire, so it runs in script phase (between frames)
        // instead of mid-XR-stereo-pass. This reproduces the "entering the effects menu"
        // fix automatically. Touches NO runtime materials/textures (that split the eyes).
        IEnumerator DeferPpv2Redeploy()
        {
            // Let the current frame finish (camera wire settles), then one more frame.
            yield return null;   // end of first frame after Wire
            yield return null;   // one clean frame in between

            var sgb = FindFirstObjectByType<DaggerfallWorkshop.Game.Utility.StartGameBehaviour>();
            if (sgb == null || dfCamera == null)
            {
                Debug.LogWarning("[DFUQuest3] Deferred PPv2 redeploy skipped (sgb/camera null).");
                ppv2DeployQueued = false;
                yield break;
            }

            try
            {
                // Force StartGameBehaviour to re-resolve its cached postProcessLayer,
                // then re-push AA/AO/Bloom. Runs between frames -> PPv2 rebuilds cleanly.
                var postField = typeof(DaggerfallWorkshop.Game.Utility.StartGameBehaviour)
                    .GetField("postProcessLayer", BindingFlags.NonPublic | BindingFlags.Instance);
                if (postField != null) postField.SetValue(sgb, null);

                sgb.DeployCoreGameEffectSettings(
                    DaggerfallWorkshop.CoreGameEffectSettingsGroups.Antialiasing |
                    DaggerfallWorkshop.CoreGameEffectSettingsGroups.AmbientOcclusion |
                    DaggerfallWorkshop.CoreGameEffectSettingsGroups.Bloom);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[DFUQuest3] Deferred PPv2 redeploy error: " + e.Message);
            }

            // Bounce the PostProcessLayer off/on to force a clean per-eye PPv2 re-init
            // (the same thing the effects-settings menu does when it re-applies).
            // Yields must stay OUTSIDE the try (CS1626: can't yield in a try w/ catch).
            var pp = dfCamera.GetComponent<UnityEngine.Rendering.PostProcessing.PostProcessLayer>();
            if (pp != null)
            {
                pp.enabled = false;
                yield return null;       // one frame with the layer off (clean reset)
                pp.enabled = true;
            }
            else
            {
                yield return null;       // no layer to bounce; still settle a frame
            }

            Debug.Log("[DFUQuest3] Deferred PPv2 redeploy + layer bounce complete (stereo/keyboard fix).");
            ppv2DeployQueued = false;
        }
    }
}
