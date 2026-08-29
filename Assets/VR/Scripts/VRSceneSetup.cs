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
            // Build XR Origin (the camera is DFU's existing MainCamera, wired by VRRigBootstrap)
            var originGO = new GameObject("XR Origin (VR)");
            var origin = originGO.AddComponent<XROrigin>();

            // Controllers (simple tracked pose — XRI 3.x actions via defaults)
            var leftGO = new GameObject("Left Controller");
            leftGO.transform.SetParent(originGO.transform, false);
            leftGO.AddComponent<UnityEngine.XR.Interaction.Toolkit.XRController>();

            var rightGO = new GameObject("Right Controller");
            rightGO.transform.SetParent(originGO.transform, false);
            rightGO.AddComponent<UnityEngine.XR.Interaction.Toolkit.XRController>();

            // Bootstrap + input (camera wired to DFU's MainCamera inside VRRigBootstrap)
            var setup = originGO.AddComponent<VRRigBootstrap>();
            setup.xrOrigin = origin;

            // MCP pose bridge — reads the REAL controller pose from the on-device MCP
            // server (Unity 6 + OpenXR reports controller pose as zeros to app code).
            var mcpBridge = originGO.AddComponent<MCPPoseBridge>();

            // VR UI overlay — renders DFU's IMGUI menu into a world-space quad.
            // Uses Camera.main (DFU's camera) as the head transform.
            var overlayGO = new GameObject("DFU VR UI Overlay");
            var overlay = overlayGO.AddComponent<VRUIOverlay>();
            overlay.poseBridge = mcpBridge;
            overlayGO.transform.SetParent(originGO.transform, false);

            DontDestroyOnLoad(originGO);

            Debug.Log("[DFUQuest3] XR rig instantiated at boot (DFU camera augment).");
        }
    }
}
