// DFU Quest3 VR — VR UI overlay bridge.
// DFU's menu system is OnGUI/IMGUI, invisible in VR. DFU supports rendering its UI
// to a RenderTexture via DaggerfallUI.CustomRenderTarget. We create that target,
// display it on a world-space quad in front of the XR camera, and drive
// CustomMousePosition from the controller so the pointer can hover the menu.

using System;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.InputSystem;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;

namespace DFUQuest3
{
    public class VRUIOverlay : MonoBehaviour
    {
        [Header("Tuning")]
        public float distance = 2.0f;
        public float width = 1.8f;
        public float height = 1.35f;
        public int renderWidth = 1600;
        public int renderHeight = 1200;

        Transform cameraTransform;
        GameObject panelGO;
        UserInterfaceRenderTarget uiTarget;
        DaggerfallUI dfUI;
        bool wired;
        bool lastTrigger;
        Vector2 lastUv = new Vector2(0.5f, 0.5f);
        float diagTimer = 2f;

        public void Init(Transform cam)
        {
            cameraTransform = cam;
            enabled = true;
        }

        void OnEnable()
        {
            BuildPanel();
            Wire();
        }

        void Update()
        {
            if (!wired) Wire();
            if (cameraTransform == null || panelGO == null) return;

            // Keep panel floating in front of the head.
            Vector3 pos = cameraTransform.position + cameraTransform.forward * distance;
            panelGO.transform.position = pos;
            panelGO.transform.rotation = Quaternion.LookRotation(pos - cameraTransform.position);

            // Sync the quad texture to DFU's current render target every frame —
            // UserInterfaceRenderTarget.Update() recreates the texture, so a one-time
            // assignment goes stale (black box).
            if (uiTarget != null && uiTarget.TargetTexture != null)
            {
                var rend = panelGO.GetComponent<Renderer>();
                if (rend.sharedMaterial.mainTexture != uiTarget.TargetTexture)
                    rend.sharedMaterial.mainTexture = uiTarget.TargetTexture;
            }

            HandlePointer();
        }

        void BuildPanel()
        {
            if (panelGO != null) return;

            panelGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
            panelGO.name = "DFU VR UI Panel";
            panelGO.transform.localScale = new Vector3(width, height, 1f);

            var mat = new Material(Shader.Find("Unlit/Texture"));
            mat.mainTexture = CreateTargetTexture();
            var rend = panelGO.GetComponent<Renderer>();
            rend.sharedMaterial = mat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
        }

        RenderTexture CreateTargetTexture()
        {
            return new RenderTexture(renderWidth, renderHeight, 0, RenderTextureFormat.ARGB32);
        }

        void Wire()
        {
            dfUI = FindFirstObjectByType<DaggerfallUI>();
            if (dfUI == null) return;

            uiTarget = dfUI.GetComponent<UserInterfaceRenderTarget>();
            if (uiTarget == null)
                uiTarget = dfUI.gameObject.AddComponent<UserInterfaceRenderTarget>();
            uiTarget.CustomWidth = renderWidth;
            uiTarget.CustomHeight = renderHeight;

            // Force the private targetTexture to our RT.
            var f = typeof(UserInterfaceRenderTarget).GetField("targetTexture",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (f != null) f.SetValue(uiTarget, CreateTargetTexture());

            // Point the quad's material at DFU's UI render target.
            var rend = panelGO.GetComponent<Renderer>();
            rend.sharedMaterial.mainTexture = uiTarget.TargetTexture;

            dfUI.CustomRenderTarget = uiTarget;
            wired = true;
            Debug.Log("[DFUQuest3] VR UI overlay wired. CustomRenderTarget set.");
        }

        void HandlePointer()
        {
            bool trigger = false;
            Vector3 origin = Vector3.zero, dir = Vector3.forward;
            bool hasRay = false;

            // === PRIMARY: UnityEngine.XR.InputDevices (the OpenXR path) ===
            // OVRInput is dead under OpenXR (different input stack), so we must read
            // the controller via InputDevices which OpenXR fills with Quest Touch.
            var rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (rightHand.isValid)
            {
                if (rightHand.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out float t)) trigger = t > 0.5f;
                if (rightHand.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out origin) &&
                    rightHand.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out Quaternion r))
                {
                    dir = r * Vector3.forward;
                    hasRay = true;
                }
            }

            // Periodic diagnostics so we can see controller state on-device.
            diagTimer -= Time.unscaledDeltaTime;
            if (diagTimer <= 0f)
            {
                diagTimer = 2f;
                var rh = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
                rh.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out Vector3 rp);
                rh.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out Quaternion rr);
                Debug.Log($"[DFUQuest3] RH pos={rp} rot={rr.eulerAngles} valid={rh.isValid} | XRCam pos={cameraTransform?.position} fwd={cameraTransform?.forward}");
            }

            // If no controller pose, fall back to head pointer.
            if (!hasRay && cameraTransform != null)
            {
                origin = cameraTransform.position;
                dir = cameraTransform.forward;
            }

            // Raycast to panel -> UV.
            var plane = new Plane(-panelGO.transform.forward, panelGO.transform.position);
            if (plane.Raycast(new Ray(origin, dir), out float dist))
            {
                Vector3 local = panelGO.transform.InverseTransformPoint(plane.ClosestPointOnPlane(
                    new Ray(origin, dir).GetPoint(dist)));
                float u = Mathf.Clamp01(local.x + 0.5f);
                float v = Mathf.Clamp01(0.5f - local.y);
                lastUv = new Vector2(u, v);
            }

            // Drive DFU's cursor to the panel position.
            if (dfUI != null && wired)
            {
                Vector2 screen = new Vector2(lastUv.x * Screen.width, (1f - lastUv.y) * Screen.height);
                dfUI.CustomMousePosition = screen;
                if (trigger && !lastTrigger)
                {
                    // Synthesize a left-click so the menu responds to the trigger.
                    var im = DaggerfallWorkshop.Game.InputManager.Instance;
                    if (im != null) im.vrClickQueued = true;
                    Debug.Log("[DFUQuest3] Trigger press at uv=" + lastUv + " screen=" + screen);
                }
            }
            lastTrigger = trigger;
        }
    }
}
