// DFU Quest3 VR — XR rig bootstrap (augment-DFU approach).
// Does NOT create a competing camera. Instead it finds DFU's existing MainCamera,
// adds head tracking (TrackedPoseDriver) to it, and parent it under the XROrigin.
// This preserves DFU's Camera.main lookups (PlayerMouseLook, PlayerActivate, etc.)
// which broke when a separate "Main Camera (XR)" replaced DFU's camera.

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
        public Camera dfuCamera;

        private bool wired;

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
            Wire();
        }

        void Update()
        {
            if (wired) return;
            Wire();
        }

        void Wire()
        {
            // DFU's main camera stays the single render camera.
            dfuCamera = Camera.main;
            if (dfuCamera == null)
            {
                Debug.LogWarning("[DFUQuest3] Camera.main not ready; retrying.");
                return;
            }

            // Drive DFU's camera from the headset (TrackedPoseDriver) instead of
            // creating a second camera. This keeps Camera.main == DFU's camera so
            // PlayerMouseLook / PlayerActivate / GameManager lookups all still work.
            var tpd = dfuCamera.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
            if (tpd == null)
                tpd = dfuCamera.gameObject.AddComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
            tpd.positionInput = new InputActionProperty(
                new InputAction("Position", InputActionType.Value, "<XRHMD>/centerEyePosition"));
            tpd.rotationInput = new InputActionProperty(
                new InputAction("Rotation", InputActionType.Value, "<XRHMD>/centerEyeRotation"));
            tpd.trackingType = UnityEngine.InputSystem.XR.TrackedPoseDriver.TrackingType.RotationAndPosition;
            tpd.updateType = UnityEngine.InputSystem.XR.TrackedPoseDriver.UpdateType.BeforeRender;

            // Keep DFU's mouse-look component but neutralize it so it doesn't fight
            // the XR pose (it would override Camera rotation from mouse input).
            var ml = dfuCamera.GetComponent<PlayerMouseLook>();
            if (ml != null) ml.enabled = false;

            // Parent the camera under the XROrigin so the rig's floor offset applies.
            // But keep DFU's camera as the visual root of the world.
            if (dfuCamera.transform.parent != xrOrigin.transform)
                dfuCamera.transform.SetParent(xrOrigin.transform, true);

            // Let the XROrigin know which camera is the tracked head.
            if (xrOrigin.Camera != dfuCamera)
                xrOrigin.Camera = dfuCamera;

            wired = true;
            Debug.Log("[DFUQuest3] DFU camera wired to head tracking (single-camera, augment approach).");
        }
    }
}
