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
        bool panelAnchored;
        GameObject reticleGO;
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
            if (cameraTransform == null) cameraTransform = Camera.main != null ? Camera.main.transform : null;
            if (panelGO == null) return;

            // Anchor the panel at a comfortable distance in front of the head, and
            // RE-anchor if the user gets far from it (so it's never stranded in the
            // distance). Old-fork stationary behavior, but always reachable.
            if (!panelAnchored || (cameraTransform != null &&
                Vector3.Distance(panelGO.transform.position, cameraTransform.position) > 4f))
            {
                if (cameraTransform == null) return; // wait for camera
                Vector3 pos = cameraTransform.position + cameraTransform.forward * distance;
                pos.y = cameraTransform.position.y; // keep at head height
                panelGO.transform.position = pos;
                panelGO.transform.rotation = Quaternion.LookRotation(pos - cameraTransform.position);
                panelAnchored = true;
                Debug.Log("[DFUQuest3] Menu panel anchored at " + panelGO.transform.position);
            }

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

        void BuildReticle()
        {
            if (reticleGO != null) return;
            reticleGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            reticleGO.name = "DFU VR Reticle";
            reticleGO.transform.localScale = new Vector3(0.012f, 0.012f, 0.012f);
            var mat = new Material(Shader.Find("Unlit/Color"));
            mat.color = new Color(1f, 1f, 0.4f, 1f); // bright yellow-green, clearly visible
            reticleGO.GetComponent<Renderer>().sharedMaterial = mat;
            reticleGO.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            reticleGO.GetComponent<Renderer>().receiveShadows = false;
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

            // === Head-gaze as the reliable pointer (head tracking is solid; the OpenXR
            // controller may not materialize to Unity app code on Unity 6). The controller
            // overrides when it surfaces; otherwise the head-gaze fallback below applies.

            // Try Input System XRController first (Unity 6 + OpenXR path).
            foreach (var dev in UnityEngine.InputSystem.InputSystem.devices)
            {
                var xrCtrl = dev as UnityEngine.InputSystem.XR.XRController;
                if (xrCtrl == null) continue;
                var posCtrl = xrCtrl.TryGetChildControl<UnityEngine.InputSystem.Controls.Vector3Control>("devicePosition");
                var rotCtrl = xrCtrl.TryGetChildControl<UnityEngine.InputSystem.Controls.QuaternionControl>("deviceRotation");
                if (posCtrl == null) continue;
                if (xrCtrl.TryGetChildControl<UnityEngine.InputSystem.Controls.ButtonControl>("trigger") is var tc && tc != null)
                    trigger = tc.ReadValue() > 0.5f;
                origin = posCtrl.ReadValue();
                if (rotCtrl != null) dir = rotCtrl.ReadValue() * Vector3.forward;
                hasRay = true;
                break;
            }

            // Fallback: legacy InputDevices.
            if (!hasRay)
            {
                var devices = new System.Collections.Generic.List<UnityEngine.XR.InputDevice>();
                InputDevices.GetDevices(devices);
                foreach (var d in devices)
                {
                    if ((d.characteristics & InputDeviceCharacteristics.Controller) != 0)
                    {
                        if (d.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out float t) && t > 0.5f) trigger = true;
                        if (d.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out origin) &&
                            d.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out Quaternion r))
                        {
                            dir = r * Vector3.forward;
                            hasRay = true;
                            break;
                        }
                    }
                }
            }

            // If no controller ray, head-gaze is applied below (existing fallback).

            // Periodic diagnostics so we can see controller state on-device.
            diagTimer -= Time.unscaledDeltaTime;
            if (diagTimer <= 0f)
            {
                diagTimer = 2f;
                // Legacy InputDevices list (the path controllers surfaced through before)
                var diagDevs = new System.Collections.Generic.List<UnityEngine.XR.InputDevice>();
                InputDevices.GetDevices(diagDevs);
                string devList = "";
                foreach (var dd in diagDevs)
                    devList += dd.name + "(" + dd.characteristics + ") ";
                // Log the ray source/dir and panel hit UV so we can calibrate aim headlessly.
                var panelStr = (panelGO != null) ? panelGO.transform.position.ToString() : "none";
                // Also read the raw trigger float value from the first controller
                string trigVals = "";
                foreach (var dd in diagDevs)
                {
                    if ((dd.characteristics & InputDeviceCharacteristics.Controller) != 0 &&
                        dd.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out float tv))
                        trigVals += dd.name + "=" + tv.ToString("0.00") + " ";
                }
                // Enumerate ALL InputSystem devices to see what's materialized
                string isDev = "";
                int isCount = 0;
                foreach (var dev in UnityEngine.InputSystem.InputSystem.devices)
                {
                    isCount++;
                    isDev += dev.name + "[" + dev.layout + "] ";
                }
                Debug.Log($"[DFUQuest3] legacyDevices=[{devList}] | rayOrigin={origin} rayDir={dir} hasRay={hasRay} uv={lastUv} trig={trigger} | panelPos={panelStr} | trigVals=[{trigVals}] | INPUTSYSTEM({isCount}): {isDev}");
            }

            // If no controller pose, fall back to head pointer.
            if (!hasRay && cameraTransform != null)
            {
                origin = cameraTransform.position;
                dir = cameraTransform.forward;
            }

            // Raycast to panel -> UV.
            BuildReticle();
            var plane = new Plane(-panelGO.transform.forward, panelGO.transform.position);
            if (plane.Raycast(new Ray(origin, dir), out float dist))
            {
                Vector3 local = panelGO.transform.InverseTransformPoint(plane.ClosestPointOnPlane(
                    new Ray(origin, dir).GetPoint(dist)));
                float u = Mathf.Clamp01(local.x + 0.5f);
                float v = Mathf.Clamp01(0.5f - local.y);
                lastUv = new Vector2(u, v);
                // Position the reticle exactly at the ray hit on the panel surface.
                if (reticleGO != null)
                {
                    reticleGO.transform.position = new Ray(origin, dir).GetPoint(dist);
                    reticleGO.transform.rotation = panelGO.transform.rotation;
                    reticleGO.SetActive(true);
                }
            }
            else if (reticleGO != null)
            {
                reticleGO.SetActive(false);
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
