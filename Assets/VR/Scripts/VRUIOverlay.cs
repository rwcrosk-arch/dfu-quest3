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

        void Start()
        {
            // DFU loads the game scene with SceneManager.LoadScene(Single): every
            // non-DDOL object (this quad, the reticle, the ray) is destroyed, and the
            // game scene's DaggerfallUI is a DIFFERENT instance. Re-arm anchoring and
            // rewiring so gameplay rebuilds the panel instead of Update() early-returning
            // forever on the destroyed (fake-null) quad.
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDestroy()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            panelAnchored = false;
            wired = false;
            dfUI = null;
            uiTarget = null;
            Debug.Log("[DFUQuest3] VRUIOverlay scene-loaded reset: will rebuild/re-anchor panel (scene=" + scene.name + ")");
        }

        // Resolved once via HMD pose; falls back to camera transform when tracking is unavailable.
        Vector3 headPosition;
        Quaternion headRotation;
        bool hasHeadPose;

        void Update()
        {
            if (!wired) Wire();

            // === Resolve the head pose FIRST (scene-independent, always correct) ===
            // The HMD's tracked pose is the single ground truth for where the user's
            // head is, in every scene (menu, character creation, gameplay). It is
            // unaffected by the XROrigin being at world origin or GameManager camera
            // ambiguity. Read it via legacy InputDevices (reliable on Unity 6 + OpenXR,
            // unlike controllers).
            hasHeadPose = false;
            headPosition = Vector3.zero;
            headRotation = Quaternion.identity;
            var hDevices = new System.Collections.Generic.List<UnityEngine.XR.InputDevice>();
            InputDevices.GetDevices(hDevices);
            foreach (var d in hDevices)
            {
                if ((d.characteristics & InputDeviceCharacteristics.HeadMounted) != 0 &&
                    d.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out Vector3 hp) &&
                    d.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out Quaternion hr))
                {
                    headPosition = hp;
                    headRotation = hr;
                    hasHeadPose = true;
                    break;
                }
            }

            // Keep cameraTransform as a fallback only (menu scene before HMD pose is
            // available, or tracking lost). Prefer GameManager.MainCamera (game scene)
            // over Camera.main (stale startup camera) when no HMD pose.
            var gm = DaggerfallWorkshop.Game.GameManager.Instance;
            Camera cam = null;
            if (gm != null && gm.MainCamera != null)
                cam = gm.MainCamera;
            else if (Camera.main != null)
                cam = Camera.main;
            if (cam != null) cameraTransform = cam.transform;

            if (panelGO == null)
            {
                // Quad was destroyed by the game-scene load (LoadScene Single). Rebuild
                // and re-anchor instead of silently returning every frame.
                BuildPanel();
                panelAnchored = false;
                if (panelGO == null) return;
            }

            // Anchor the panel at a comfortable distance in front of the head, and
            // RE-anchor if the user gets far from it.
            //
            // ROBUST ANCHOR (does not depend on rig/camera state, which is unreliable —
            // the rig-follow moves the rig to the player but the on-device diag still
            // reads rig=(0,0,0) cam=(origin) in gameplay, suggesting a second rig or a
            // reset):
            // - Gameplay: anchor to PlayerObject + eye-height offset. PlayerMotor only
            //   exists on the real playable player (NOT the char-creation temp), so its
            //   presence is the reliable gameplay discriminator (GameInProgress was false
            //   in gameplay, so that gate is unusable).
            // - Menu/char-creation: PlayerMotor absent, so anchor to the HMD pose / camera
            //   (tracking origin == world origin there, so those are correct).
            Transform playerT = null;
            if (gm != null)
            {
                try
                {
                    var pm = gm.PlayerMotor;
                    if (pm != null && gm.PlayerObject != null)
                        playerT = gm.PlayerObject.transform;
                }
                catch { }
            }

            Vector3 anchorOrigin;
            Vector3 anchorForward;
            if (playerT != null)
            {
                // Gameplay: player body + eye height. Player-forward is the facing.
                anchorOrigin = playerT.position + Vector3.up * 1.5f;
                anchorForward = playerT.forward;
                anchorForward.y = 0f;
                if (anchorForward.sqrMagnitude < 0.001f) anchorForward = Vector3.forward;
                anchorForward.Normalize();
            }
            else if (hasHeadPose)
            {
                anchorOrigin = headPosition;
                anchorForward = headRotation * Vector3.forward;
            }
            else if (cameraTransform != null)
            {
                anchorOrigin = cameraTransform.position;
                anchorForward = cameraTransform.forward;
            }
            else
            {
                return; // nothing to anchor to yet
            }

            // Smoothly follow the anchor each frame (no hard "pop" when the player moves
            // far). The panel glides toward its target position/rotation instead of
            // snapping when the >4m re-anchor threshold trips.
            Vector3 targetPos = anchorOrigin + anchorForward * distance;
            targetPos.y = anchorOrigin.y; // keep at head height
            Quaternion targetRot = Quaternion.LookRotation(targetPos - anchorOrigin);

            if (!panelAnchored)
            {
                // First placement: snap (no visible jump from a far-away start).
                panelGO.transform.position = targetPos;
                panelGO.transform.rotation = targetRot;
                panelAnchored = true;
                Debug.Log("[DFUQuest3] Menu panel anchored at " + panelGO.transform.position + " (player=" + (playerT != null) + ")");
            }
            else
            {
                // Smooth follow: lerp toward the target each frame. Fast enough to feel
                // responsive, slow enough to avoid jitter.
                float t = 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime);
                panelGO.transform.position = Vector3.Lerp(panelGO.transform.position, targetPos, t);
                panelGO.transform.rotation = Quaternion.Slerp(panelGO.transform.rotation, targetRot, t);
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

            // Remove the MeshCollider that CreatePrimitive adds by default — it makes the
            // panel a physical object that blocks the player's CharacterController movement.
            var col = panelGO.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.Destroy(col);

            var mat = new Material(Shader.Find("Unlit/Texture"));
            mat.mainTexture = CreateTargetTexture();
            var rend = panelGO.GetComponent<Renderer>();
            rend.sharedMaterial = mat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            DontDestroyOnLoad(panelGO); // survive DFU's LoadScene(Single) into the game scene
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
            DontDestroyOnLoad(reticleGO);

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
            DontDestroyOnLoad(rayGO);
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

            // The controller pose (MCP/InputSystem/legacy) is in XR TRACKING SPACE
            // (relative to the XROrigin), but the panel is anchored in WORLD space at the
            // player. In gameplay the rig is at the player (40m from tracking origin), so
            // a raw tracking-space ray never reaches the panel. Offset the controller ray
            // origin by the rig's world position to bring it into world space.
            Vector3 rigWorld = Vector3.zero;
            var rigRef = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (rigRef != null) rigWorld = rigRef.transform.position;

            // === Head-gaze as the reliable pointer (head tracking is solid; the OpenXR
            // controller may not materialize to Unity app code on Unity 6). The controller
            // overrides when it surfaces; otherwise the head-gaze fallback below applies.

            // === MCP pose bridge FIRST (the ONLY path that reads real controller pose) ===
            // Unity 6 + OpenXR reports controller pose as zeros to InputDevices/InputSystem,
            // but the on-device MCP server reads it correctly. Use that when valid.
            if (poseBridge != null && poseBridge.controllerValid)
            {
                origin = poseBridge.controllerPosition + rigWorld;
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
                origin = cp + rigWorld;
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
                            origin = cp + rigWorld;
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

            // If no controller ray, use head-gaze. In gameplay the camera/HMD pose is at
            // world origin while the panel is anchored to the player + eye height, so
            // point the gaze at the player's facing from eye height. Otherwise (menu/
            // char-creation) use the HMD/camera as before.
            if (!hasRay)
            {
                Transform playerT = null;
                var gm = DaggerfallWorkshop.Game.GameManager.Instance;
                if (gm != null)
                {
                    try
                    {
                        var pm = gm.PlayerMotor;
                        if (pm != null && gm.PlayerObject != null)
                            playerT = gm.PlayerObject.transform;
                    }
                    catch { }
                }
                if (playerT != null)
                {
                    origin = playerT.position + Vector3.up * 1.5f;
                    dir = playerT.forward;
                    dir.y = 0f;
                    if (dir.sqrMagnitude < 0.001f) dir = Vector3.forward;
                    dir.Normalize();
                    hasRay = true;
                }
                else if (cameraTransform != null)
                {
                    origin = cameraTransform.position;
                    dir = cameraTransform.forward;
                    hasRay = true;
                }
                else
                {
                    var hDevices = new System.Collections.Generic.List<UnityEngine.XR.InputDevice>();
                    InputDevices.GetDevices(hDevices);
                    foreach (var d in hDevices)
                    {
                        if ((d.characteristics & InputDeviceCharacteristics.HeadMounted) != 0 &&
                            d.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out Vector3 hp) &&
                            d.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out Quaternion hr))
                        {
                            origin = hp + rigWorld;
                            dir = hr * Vector3.forward;
                            hasRay = true;
                            break;
                        }
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
