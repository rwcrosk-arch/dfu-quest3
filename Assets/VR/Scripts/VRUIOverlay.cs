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
        // Render target resolution. The UI quad is only 1.8m wide in VR, so a 700x525
        // target renders identically sharp on the headset at a fraction of the GPU cost of
        // 1600x1200 (which saturated the GPU and starved the audio DSP thread -> slow sound).
        public int renderWidth = 720;
        public int renderHeight = 540;

        Transform cameraTransform;
        GameObject panelGO;
        UserInterfaceRenderTarget uiTarget;
        DaggerfallUI dfUI;
        bool wired;
        bool lastTrigger;
        bool panelAnchored;
        GameObject reticleGO;
        LineRenderer rayLine;
        public MCPPoseBridge poseBridge; // real controller pose from on-device MCP server
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
            // Anchor to the PLAYER's head, not the camera/rig. The XROrigin is
            // DontDestroyOnLoad from the startup scene and can end up at world origin
            // (rigParent=none) while the player spawns 40m away — the camera (child of
            // the rig) then sits at origin too, and a panel anchored to it is invisible.
            // GameManager.PlayerObject is the ground truth of where the player is.
            Transform anchor = null;
            var gm = DaggerfallWorkshop.Game.GameManager.Instance;
            if (gm != null && gm.PlayerObject != null)
                anchor = gm.PlayerObject.transform;
            else if (gm != null && gm.MainCamera != null)
                anchor = gm.MainCamera.transform;
            else if (Camera.main != null)
                anchor = Camera.main.transform;
            if (anchor != null) cameraTransform = anchor;
            if (panelGO == null) return;

            // The render target shows DFU's ENTIRE UI stack (menu, in-game HUD, message
            // boxes, intro text). Keep the quad visible in-game too — hiding it removed
            // the in-game UI: the intro box never showed and a pending modal froze the
            // game (weather inits then stalls, no input). The ray drives the cursor so
            // the user can click the intro box's Continue button and HUD elements.

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

            // Visible ray from the head to the reticle (so the raycast is obvious).
            var rayGO = new GameObject("DFU VR Ray");
            rayLine = rayGO.AddComponent<LineRenderer>();
            rayLine.positionCount = 2;
            rayLine.startWidth = 0.004f;
            rayLine.endWidth = 0.001f;
            rayLine.startColor = new Color(1f, 0.5f, 0f, 0.9f);
            rayLine.endColor = new Color(1f, 1f, 0.3f, 0.9f);
            rayLine.material = new Material(Shader.Find("Unlit/Color"));
            rayLine.material.color = Color.white;
            rayLine.useWorldSpace = true;
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

            // === MCP pose bridge FIRST (the ONLY path that reads real controller pose) ===
            // Unity 6 + OpenXR reports controller pose as zeros to InputDevices/InputSystem,
            // but the on-device MCP server reads it correctly. Use that when valid.
            if (poseBridge != null && poseBridge.controllerValid)
            {
                origin = poseBridge.controllerPosition;
                dir = poseBridge.controllerRotation * Vector3.forward;
                hasRay = true;
            }

            // Try Input System XRController first (Unity 6 + OpenXR path).
            if (!hasRay)
            foreach (var dev in UnityEngine.InputSystem.InputSystem.devices)
            {
                var xrCtrl = dev as UnityEngine.InputSystem.XR.XRController;
                if (xrCtrl == null) continue;
                var posCtrl = xrCtrl.TryGetChildControl<UnityEngine.InputSystem.Controls.Vector3Control>("devicePosition");
                var rotCtrl = xrCtrl.TryGetChildControl<UnityEngine.InputSystem.Controls.QuaternionControl>("deviceRotation");
                if (posCtrl == null) continue;
                Vector3 cp = posCtrl.ReadValue();
                Quaternion cr = rotCtrl != null ? rotCtrl.ReadValue() : Quaternion.identity;
                // Only adopt the controller if its pose is actually valid. On Unity 6 + OpenXR
                // the controller may be listed but report a ZERO pose — adopting it would
                // lock the ray at origin. Sanity-check before overriding head-gaze.
                if (cp.sqrMagnitude < 0.001f || float.IsNaN(cr.x) || float.IsNaN(cr.y) || float.IsNaN(cr.z) || float.IsNaN(cr.w))
                    continue;
                if (xrCtrl.TryGetChildControl<UnityEngine.InputSystem.Controls.AxisControl>("trigger") is var tc && tc != null)
                    trigger = tc.ReadValue() > 0.5f;
                origin = cp;
                dir = cr * Vector3.forward;
                hasRay = true;
                break;
            }

            // Fallback: legacy InputDevices (same zero-pose guard).
            if (!hasRay)
            {
                var devices = new System.Collections.Generic.List<UnityEngine.XR.InputDevice>();
                InputDevices.GetDevices(devices);
                foreach (var d in devices)
                {
                    if ((d.characteristics & InputDeviceCharacteristics.Controller) != 0)
                    {
                        if (d.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out float t) && t > 0.5f) trigger = true;
                        if (d.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out Vector3 cp) &&
                            d.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out Quaternion cr))
                        {
                            if (cp.sqrMagnitude < 0.001f) continue; // zero pose = dead controller, skip
                            origin = cp;
                            dir = cr * Vector3.forward;
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
                // DIAG: which camera/rig are we tracking, and where are they?
                string camDiag = "camTransform=" + (cameraTransform != null ? cameraTransform.position.ToString() : "null");
                var gmDiag = DaggerfallWorkshop.Game.GameManager.Instance;
                if (gmDiag != null && gmDiag.MainCamera != null)
                    camDiag += " gmMainCam=" + gmDiag.MainCamera.transform.position;
                var rigDiag = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
                if (rigDiag != null)
                    camDiag += " rig=" + rigDiag.transform.position + " rigParent=" + (rigDiag.transform.parent != null ? rigDiag.transform.parent.name : "none");
                string mcpInfo = (poseBridge != null) ?
                    ("valid=" + poseBridge.controllerValid + " pos=" + poseBridge.controllerPosition + " rot=" + poseBridge.controllerRotation) :
                    "no-bridge";
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
                Debug.Log($"[DFUQuest3] legacyDevices=[{devList}] | rayOrigin={origin} rayDir={dir} hasRay={hasRay} uv={lastUv} trig={trigger} | panelPos={panelStr} | {camDiag} | MCP={mcpInfo} | trigVals=[{trigVals}] | INPUTSYSTEM({isCount}): {isDev}");
            }

            // If no controller ray, use head-gaze from the OpenXR head-tracking device
            // (reliable — the head device works via legacy, unlike controllers).
            if (!hasRay)
            {
                var hDevices = new System.Collections.Generic.List<UnityEngine.XR.InputDevice>();
                InputDevices.GetDevices(hDevices);
                foreach (var d in hDevices)
                {
                    if ((d.characteristics & InputDeviceCharacteristics.HeadMounted) != 0 &&
                        d.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out Vector3 hp) &&
                        d.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out Quaternion hr))
                    {
                        origin = hp;
                        dir = hr * Vector3.forward;
                        hasRay = true;
                        break;
                    }
                }
            }

            // Last resort: camera transform.
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
                // Draw the visible ray from head to the hit point.
                if (rayLine != null)
                {
                    rayLine.SetPosition(0, origin);
                    rayLine.SetPosition(1, new Ray(origin, dir).GetPoint(dist));
                    rayLine.enabled = true;
                }
            }
            else if (reticleGO != null && poseBridge != null && poseBridge.controllerValid)
            {
                // Controller is active but the ray misses the panel — clamp the reticle to
                // the nearest panel edge so it never vanishes while pointing. Find the
                // intersection of the ray direction with the panel plane, projected.
                var clampPlane = new Plane(-panelGO.transform.forward, panelGO.transform.position);
                if (clampPlane.Raycast(new Ray(origin, dir), out float pd))
                {
                    Vector3 hit = new Ray(origin, dir).GetPoint(pd);
                    // Clamp hit to the panel's local bounds.
                    Vector3 local = panelGO.transform.InverseTransformPoint(hit);
                    local.x = Mathf.Clamp(local.x, -0.5f * width, 0.5f * width);
                    local.y = Mathf.Clamp(local.y, -0.5f * height, 0.5f * height);
                    local.z = 0f;
                    Vector3 clamped = panelGO.transform.TransformPoint(local);
                    reticleGO.transform.position = clamped;
                    reticleGO.transform.rotation = panelGO.transform.rotation;
                    reticleGO.SetActive(true);
                    if (rayLine != null)
                    {
                        rayLine.SetPosition(0, origin);
                        rayLine.SetPosition(1, clamped);
                        rayLine.enabled = true;
                    }
                }
                else
                {
                    reticleGO.SetActive(false);
                    if (rayLine != null) rayLine.enabled = false;
                }
            }
            else if (reticleGO != null)
            {
                // Always show the reticle at a fixed distance along the aim ray so the
                // user always sees where they're aiming, even when the ray misses the
                // panel (e.g. panel stranded far away). This makes the action-button
                // location intuitive in gameplay.
                reticleGO.SetActive(true);
                reticleGO.transform.position = origin + dir.normalized * 2.0f;
                reticleGO.transform.rotation = Quaternion.identity;
                if (rayLine != null)
                {
                    rayLine.SetPosition(0, origin);
                    rayLine.SetPosition(1, origin + dir.normalized * 2.0f);
                    rayLine.enabled = true;
                }
            }

            // Drive DFU's cursor to the panel position.
            if (dfUI != null && wired)
            {
                // lastUv: u=0 left->1 right, v=0 TOP of panel ->1 BOTTOM (v = 0.5 - local.y).
                // DFU's CustomMousePosition is TOP-origin (y=0 at top), so the screen Y must
                // be v * Screen.height (NOT (1-v)*Screen.height, which flips it vertically and
                // makes "aim at a button" click its mirror across the horizontal midline).
                Vector2 screen = new Vector2(lastUv.x * Screen.width, lastUv.y * Screen.height);
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
