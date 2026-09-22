// DFU Quest3 VR — shared activation-aim source.
//
// Ross's observation (verified via ACTDIAG): world interactions raycast from the HEAD
// (Camera.main), while the visible pointer/reticle follows the CONTROLLER. Result:
// pointing the controller at a corpse/door only interacts if the head is also facing
// it — the controller aim is ignored for gameplay activation. That mismatch affects
// every world interaction (loot, doors, NPCs, pickups).
//
// VRAim resolves ONE ray for all world activation: the right-hand controller when a
// valid pose exists (MCP bridge first — Unity 6 + OpenXR reports zero poses to app
// code; then InputSystem; then legacy InputDevices, all zero-pose guarded), else the
// head gaze. Tracking-space controller poses are transformed to world space exactly
// as VRUIOverlay.HandlePointer does (feet-level anchor + rig yaw).
//
// VRUIOverlay keeps its own inline pointer chain for now (working panel code is not
// touched this round); consolidation can follow after verification.
using UnityEngine;
using UnityEngine.XR;
using DaggerfallWorkshop.Game;

namespace DFUQuest3
{
    public static class VRAim
    {
        /// <summary>True when the last TryGetRay came from a controller rather than head gaze.</summary>
        public static bool LastRayFromController { get; private set; }

        /// <summary>
        /// Resolve the world-space activation ray: controller pose when valid, else head gaze.
        /// </summary>
        public static bool TryGetRay(out Ray ray)
        {
            // Tracking-space controller pose -> world space needs the rig's feet anchor
            // and yaw (same transform as VRUIOverlay.HandlePointer).
            Vector3 rayAnchor = Vector3.zero;
            Quaternion rigYaw = Quaternion.identity;
            var origin = Object.FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (origin != null)
            {
                rigYaw = Quaternion.Euler(0f, origin.transform.eulerAngles.y, 0f);
                rayAnchor = origin.transform.position;
            }
            var gm = GameManager.Instance;
            if (gm != null)
            {
                try
                {
                    if (gm.PlayerMotor != null && gm.PlayerObject != null)
                        rayAnchor = gm.PlayerObject.transform.position; // feet level
                }
                catch { }
            }
            if (rayAnchor == Vector3.zero && origin != null)
                rayAnchor = origin.transform.position;

            // 1) MCP pose bridge — the only path that reads real controller pose on
            //    Unity 6 + OpenXR (Unity reports zeros to InputDevices/InputSystem).
            var bridge = Object.FindFirstObjectByType<MCPPoseBridge>();
            if (bridge != null && bridge.controllerValid)
            {
                ray = new Ray(
                    rayAnchor + rigYaw * bridge.controllerPosition,
                    rigYaw * (bridge.controllerRotation * Vector3.forward));
                LastRayFromController = true;
                return true;
            }

            // 2) Input System XRController (zero-pose guarded).
            foreach (var dev in UnityEngine.InputSystem.InputSystem.devices)
            {
                var xrCtrl = dev as UnityEngine.InputSystem.XR.XRController;
                if (xrCtrl == null) continue;
                var posCtrl = xrCtrl.TryGetChildControl<UnityEngine.InputSystem.Controls.Vector3Control>("devicePosition");
                var rotCtrl = xrCtrl.TryGetChildControl<UnityEngine.InputSystem.Controls.QuaternionControl>("deviceRotation");
                if (posCtrl == null) continue;
                Vector3 cp = posCtrl.ReadValue();
                Quaternion cr = rotCtrl != null ? rotCtrl.ReadValue() : Quaternion.identity;
                if (cp.sqrMagnitude < 0.001f || float.IsNaN(cr.x) || float.IsNaN(cr.y) || float.IsNaN(cr.z) || float.IsNaN(cr.w))
                    continue;
                ray = new Ray(rayAnchor + rigYaw * cp, rigYaw * (cr * Vector3.forward));
                LastRayFromController = true;
                return true;
            }

            // 3) Legacy InputDevices (same zero-pose guard).
            var devices = new System.Collections.Generic.List<InputDevice>();
            InputDevices.GetDevices(devices);
            foreach (var d in devices)
            {
                if ((d.characteristics & InputDeviceCharacteristics.Controller) == 0)
                    continue;
                if (d.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 cp) &&
                    d.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion cr))
                {
                    if (cp.sqrMagnitude < 0.001f) continue; // zero pose = dead controller
                    ray = new Ray(rayAnchor + rigYaw * cp, rigYaw * (cr * Vector3.forward));
                    LastRayFromController = true;
                    return true;
                }
            }

            // 4) Head gaze fallback.
            var cam = Camera.main;
            if (cam != null)
            {
                ray = new Ray(cam.transform.position, cam.transform.forward);
                LastRayFromController = false;
                return true;
            }

            ray = default;
            return false;
        }
    }
}