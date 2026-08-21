// DFU Quest3 VR — scene setup. Adds XR Origin + our bridge to the DFU
// startup scene hierarchy at play time, so we don't need to edit DFU's
// serialized scenes (keeps upstream diffs clean).

using UnityEngine;
using UnityEngine.XR;
using UnityEngine.InputSystem;
using Unity.XR.CoreUtils;
using DaggerfallWorkshop.Game;

namespace DFUQuest3
{
    [DefaultExecutionOrder(-5000)]
    public class VRSceneSetup : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Autostart()
        {
            // Always create the setup object; it polls for XR readiness and only
            // builds the rig once OpenXR is actually active (avoids the race where
            // XRSettings.isDeviceActive is false at scene load).
            var go = new GameObject("DFUQuest3 VRSceneSetup");
            go.AddComponent<VRSceneSetup>();
        }

        void Awake()
        {
            // Defer rig construction until XR is active.
            StartCoroutine(WaitForXR());
        }

        System.Collections.IEnumerator WaitForXR()
        {
            // Poll until an XR device/loader is active (OpenXR initialized).
            float timeout = 30f;
            while (!XRSettings.isDeviceActive && timeout > 0f)
            {
                timeout -= Time.unscaledDeltaTime;
                yield return null;
            }
            if (!XRSettings.isDeviceActive)
            {
                Debug.Log("[DFUQuest3] XR never became active; skipping VR rig (flat build).");
                Destroy(gameObject);
                yield break;
            }
            BuildRig();
        }

        void BuildRig()
        {
            // Input System must be active for XRI 3.x actions
#if ENABLE_INPUT_SYSTEM
            // already enabled via PlayerSettings.activeInputHandler
#endif
            var gm = GameManager.Instance;
            if (gm == null) { Debug.LogWarning("[DFUQuest3] No GameManager yet"); return; }

            // Build XR Origin
            var originGO = new GameObject("XR Origin (VR)");
            var origin = originGO.AddComponent<XROrigin>();

            // XR camera
            var camGO = new GameObject("Main Camera (XR)");
            camGO.transform.SetParent(originGO.transform, false);
            camGO.tag = "MainCamera";
            var cam = camGO.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 2000f;
            cam.clearFlags = CameraClearFlags.Skybox;
            origin.Camera = cam;
            camGO.AddComponent<AudioListener>();

            // Head tracking: TrackedPoseDriver (Input System) drives the camera from the headset.
            // Without this, the view is pinned to the world and head rotation does nothing.
            var tpd = camGO.AddComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
            tpd.positionInput = new UnityEngine.InputSystem.InputActionProperty(
                new UnityEngine.InputSystem.InputAction("Position", UnityEngine.InputSystem.InputActionType.Value, null, null,
                    null, null, null, new UnityEngine.InputSystem.InputBinding { path = "<XRHMD>/centerEyePosition" }));
            tpd.rotationInput = new UnityEngine.InputSystem.InputActionProperty(
                new UnityEngine.InputSystem.InputAction("Rotation", UnityEngine.InputSystem.InputActionType.Value, null, null,
                    null, null, null, new UnityEngine.InputSystem.InputBinding { path = "<XRHMD>/centerEyeRotation" }));
            tpd.trackingType = UnityEngine.InputSystem.XR.TrackingType.RotationAndPosition;
            tpd.updateType = UnityEngine.InputSystem.XR.TrackedPoseDriver.UpdateType.BeforeRender;

            // Controllers (simple tracked pose — XRI 3.x actions via defaults)
            var leftGO = new GameObject("Left Controller");
            leftGO.transform.SetParent(originGO.transform, false);
            leftGO.AddComponent<UnityEngine.XR.Interaction.Toolkit.XRController>();

            var rightGO = new GameObject("Right Controller");
            rightGO.transform.SetParent(originGO.transform, false);
            rightGO.AddComponent<UnityEngine.XR.Interaction.Toolkit.XRController>();

            // Bootstrap + input
            var setup = originGO.AddComponent<VRRigBootstrap>();
            setup.xrOrigin = origin;

            var input = originGO.AddComponent<VRPlayerInput>();
            input.headTransform = cam.transform;

            // VR UI overlay — renders DFU's IMGUI menu into a world-space quad.
            var overlayGO = new GameObject("DFU VR UI Overlay");
            var overlay = overlayGO.AddComponent<VRUIOverlay>();
            overlay.Init(cam.transform);
            overlayGO.transform.SetParent(originGO.transform, false);

            DontDestroyOnLoad(originGO);

            Debug.Log("[DFUQuest3] XR rig instantiated at boot.");
        }
    }
}
