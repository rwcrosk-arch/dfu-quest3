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
        void Awake()
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
            camGO.AddComponent<UnityEngine.XR.Interaction.Toolkit.XRCameraController>();

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

            DontDestroyOnLoad(originGO);

            Debug.Log("[DFUQuest3] XR rig instantiated at boot.");
        }
    }
}
