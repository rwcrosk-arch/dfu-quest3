// DFU Quest3 VR — renders the DFU first-person weapon as a world-space quad anchored
// to the right controller, WITHOUT disabling FPSWeapon.OnGUI.
// Rationale (vs reverted 8c8387e): the previous attempt set a static
// FPSWeapon.SuppressOnGUIDraw flag from OnEnable. Because VRSceneSetup wires this
// component at BOOT (startup scene), the flag was global and disabled the ONLY live
// weapon draw while the 3D quad path was untested (Update silently no-ops until the
// player/WeaponManager exist). One regression in the quad path = weapon permanently
// invisible, with no draw anywhere. It also risked double-draw or panel interference.
// This version keeps FPSWeapon.OnGUI fully enabled (it remains the animation-state
// machine: atlas load, frame timing, WeaponStates) and ADDS the 3D quad as an
// additional visual. The weapon will still also draw onto the 2D UI panel (DFU's own
// render-target capture); that is acceptable and removes the regression risk.

using UnityEngine;
using DaggerfallWorkshop.Game;

namespace DFUQuest3
{
    public class VRWeaponRenderer : MonoBehaviour
    {
        public float quadWidth = 0.4f;      // weapon quad size in meters (tune on-device)
        public float quadHeight = 0.6f;
        public Vector3 localOffset = new Vector3(0f, 0.1f, 0.3f); // up/forward from grip
        public MCPPoseBridge poseBridge;

        GameObject quad;
        Material mat;
        bool nreLogged;
        float diagTimer = 3f;

        void BuildQuad()
        {
            quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "DFU VR Weapon";
            quad.transform.localScale = new Vector3(quadWidth, quadHeight, 1f);
            var col = quad.GetComponent<Collider>();
            if (col) Destroy(col);
            // Never pass a possibly-null shader to new Material() — it throws
            // ArgumentNullException (param "shader") when Shader.Find returns null.
            // On device the CGPROGRAM chroma-key shader may fail to compile (built-in
            // pipeline path on this OpenXR/Vulkan build) even though it's in
            // AlwaysIncludedShaders, so find first, then construct.
            Shader s = Shader.Find("DFUQuest3/VRUIChromaKey");
            if (s == null || !s.isSupported) { Debug.LogWarning("[DFUQuest3] VRUIChromaKey shader unavailable — using Unlit/Transparent Cutout fallback"); s = Shader.Find("Unlit/Transparent Cutout"); }
            if (s == null || !s.isSupported) s = Shader.Find("Unlit/Texture");
            if (s == null || !s.isSupported) { BuildFallbackColorMaterial(); return; }
            mat = new Material(s);
            quad.GetComponent<Renderer>().sharedMaterial = mat;
            quad.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            quad.GetComponent<Renderer>().receiveShadows = false;
            quad.SetActive(false);
            DontDestroyOnLoad(quad);
        }

        // Last-resort material when even built-in unlit shaders are stripped:
        // a Sprites/Default material so at least SOMETHING renders.
        void BuildFallbackColorMaterial()
        {
            var spriteShader = Shader.Find("Sprites/Default");
            mat = spriteShader != null ? new Material(spriteShader)
                                       : new Material(Shader.Find("Hidden/Internal-Colored"));
            quad.GetComponent<Renderer>().sharedMaterial = mat;
        }

        void Update()
        {
            try
            {
                UpdateWeaponQuad();
            }
            catch (System.Exception e)
            {
                if (!nreLogged)
                {
                    nreLogged = true;
                    Debug.LogError("[DFUQuest3] VRWeaponRenderer.Update exception: " + e);
                }
            }
        }

        void UpdateWeaponQuad()
        {
            if (quad == null) BuildQuad();
            var gm = GameManager.Instance;
            if (gm == null || quad == null) return;

            FPSWeapon w = null;
            WeaponManager wm = null;
            try { wm = gm.WeaponManager; if (wm != null) w = wm.ScreenWeapon; } catch { }

            bool visible = w != null && wm != null && !wm.Sheathed && w.ShowWeapon
                           && w.WeaponType != DaggerfallWorkshop.WeaponTypes.None
                           && w.CurrentWeaponTexture != null
                           && !GameManager.IsGamePaused;
            quad.SetActive(visible);
            // Periodic diagnostic (every ~3s) so we capture the REAL gameplay state, not
            // a one-shot paused/menu frame. Logs both visible and invisible states.
            diagTimer -= Time.unscaledDeltaTime;
            if (diagTimer <= 0f)
            {
                diagTimer = 3f;
                Debug.Log("[DFUQuest3] VRWeapon quad " + (visible ? "VISIBLE" : "INVISIBLE") +
                    ": w=" + (w != null) + " wm=" + (wm != null) +
                    " sheathed=" + (wm != null ? wm.Sheathed : -1) +
                    " showWeapon=" + (w != null ? w.ShowWeapon : -1) +
                    " type=" + (w != null ? w.WeaponType.ToString() : "null") +
                    " tex=" + (w != null && w.CurrentWeaponTexture != null ? w.CurrentWeaponTexture.width + "x" + w.CurrentWeaponTexture.height : "null") +
                    " paused=" + GameManager.IsGamePaused +
                    " mat=" + (mat != null ? mat.shader.name : "null") +
                    " quadActive=" + (quad != null ? quad.activeSelf : false));
            }
            if (!visible) return;

            if (mat.mainTexture != w.CurrentWeaponTexture) mat.mainTexture = w.CurrentWeaponTexture;
            Rect r = w.CurrentAnimRect;
            mat.mainTextureOffset = new Vector2(r.x, r.y);
            mat.mainTextureScale = new Vector2(r.width, r.height);

            // Anchor to the controller: tracking space -> world via player feet + rig yaw
            // (same pattern VRUIOverlay.HandlePointer uses). Never parent under the
            // XROrigin — the rig moves with the player's tracked space and would
            // double-apply the tracking offset.
            Vector3 anchor = gm.PlayerObject ? gm.PlayerObject.transform.position : Vector3.zero;
            var rig = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
            Quaternion rigYaw = rig ? Quaternion.Euler(0, rig.transform.eulerAngles.y, 0) : Quaternion.identity;

            Vector3 pos; Quaternion rot;
            if (poseBridge != null && poseBridge.controllerValid)
            {
                pos = anchor + rigYaw * poseBridge.controllerPosition;
                rot = rigYaw * poseBridge.controllerRotation;
            }
            else
            {
                float yaw = gm.PlayerObject ? gm.PlayerObject.transform.eulerAngles.y : 0f;
                pos = anchor + Quaternion.Euler(0, yaw, 0) * new Vector3(0.25f, 1.2f, 0.5f);
                rot = Quaternion.Euler(0, yaw, 0);
            }
            pos += rot * localOffset;

            // Billboard toward the camera (weapon sprite is 2D — keep it readable).
            Camera cam = gm.MainCamera != null ? gm.MainCamera : Camera.main;
            if (cam != null)
            {
                Vector3 look = pos - cam.transform.position; look.y = 0;
                if (look.sqrMagnitude > 0.0001f)
                    rot = Quaternion.LookRotation(look.normalized) * Quaternion.Euler(0, 180f, 0);
            }
            quad.transform.SetPositionAndRotation(pos, rot);
        }
    }
}
