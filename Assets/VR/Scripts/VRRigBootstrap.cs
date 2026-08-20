// DFU Quest3 VR — XR rig bootstrap.
// Spawns an XR Origin (XRI 3.x), disables DFU's mouse/camera input stack,
// and parents the XR camera at DFU's Player head position.
// Attach to a GameObject in DFU_VR.unity scene.

using UnityEngine;
using UnityEngine.XR;
using Unity.XR.CoreUtils;
using DaggerfallWorkshop.Game;

namespace DFUQuest3
{
    public class VRRigBootstrap : MonoBehaviour
    {
        [Tooltip("Auto-configured if left null.")]
        public XROrigin xrOrigin;
        public Camera dfuCamera;

        private PlayerMouseLook mouseLook;
        private bool wired;

        void Start()
        {
            // XRI 3.x: XROrigin (Unity.XR.CoreUtils) replaces the old XRRig.
            if (xrOrigin == null)
                xrOrigin = FindFirstObjectByType<XROrigin>();

            if (xrOrigin == null)
            {
                Debug.LogError("[DFUQuest3] No XROrigin in scene. Add XR Origin (VR) via GameObject > XR menu.");
                enabled = false;
                return;
            }

            // Locate DFU player rig
            var player = GameManager.Instance?.PlayerObject;
            if (player == null)
            {
                Debug.LogWarning("[DFUQuest3] GameManager.PlayerObject not ready yet; will retry in Update.");
                return;
            }
            WireToPlayer(player);
        }

        void Update()
        {
            if (wired) return;
            var player = GameManager.Instance?.PlayerObject;
            if (player != null) WireToPlayer(player);
        }

        void WireToPlayer(GameObject player)
        {
            // Strip desktop input
            mouseLook = player.GetComponent<PlayerMouseLook>() ?? player.GetComponentInChildren<PlayerMouseLook>();
            if (mouseLook != null) mouseLook.enabled = false;

            // Find DFU's camera and let XR rig take over pose
            dfuCamera = player.GetComponentInChildren<Camera>(includeInactive: true);
            if (dfuCamera != null)
            {
                dfuCamera.enabled = false; // XR camera renders instead
                dfuCamera.tag = "Untagged";
            }

            // Anchor XR Origin at player's head
            var headAnchor = dfuCamera != null ? dfuCamera.transform : player.transform;
            xrOrigin.transform.SetParent(player.transform, worldPositionStays: false);
            xrOrigin.transform.localPosition = new Vector3(0, 1.6f, 0); // head height
            xrOrigin.transform.localRotation = Quaternion.identity;

            if (xrOrigin.Camera != null)
            {
                xrOrigin.Camera.transform.localPosition = Vector3.zero;
                xrOrigin.Camera.transform.localRotation = Quaternion.identity;
                Camera.main.tag = "Untagged";
                xrOrigin.Camera.tag = "MainCamera";

                // DFU reads camera via DaggerfallUnity singletone
                var du = FindFirstObjectByType<DaggerfallWorkshop.DaggerfallUnity>();
                if (du != null)
                {
                    // DaggerfallUnity does not expose a settable camera field publicly;
                    // most subsystems query Camera.main. Tagging the XR camera covers that.
                }
            }

            wired = true;
            Debug.Log("[DFUQuest3] VR rig wired. XR camera is now MainCamera; DFU mouse look disabled.");
        }
    }
}
