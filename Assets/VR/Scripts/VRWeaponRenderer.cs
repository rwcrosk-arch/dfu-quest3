// DFU Quest3 VR — renders the DFU first-person weapon as a world-space quad anchored
// to the right controller, instead of FPSWeapon.OnGUI (screen-space, invisible in VR).
// FPSWeapon remains the animation-state machine (atlas load, frame timing, WeaponStates);
// this component mirrors its current frame texture/rect in 3D. Damage/attack logic is
// untouched (WeaponManager drives it from SwingWeapon).

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
        bool suppressSet;

        void OnEnable()
        {
            FPSWeapon.SuppressOnGUIDraw = true; // VR: stop the screen-space draw
            suppressSet = true;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDisable()
        {
            if (suppressSet) FPSWeapon.SuppressOnGUIDraw = false;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        void OnDestroy()
        {
            if (suppressSet) FPSWeapon.SuppressOnGUIDraw = false;
        }

        void OnSceneLoaded(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m)
        {
            if (quad == null) BuildQuad();
        }

        void BuildQuad()
        {
            quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "DFU VR Weapon";
            quad.transform.localScale = new Vector3(quadWidth, quadHeight, 1f);
            var col = quad.GetComponent<Collider>();
            if (col) Destroy(col);
            // DFU weapon atlases have a black background keyed out; reuse the chroma-key
            // shader (already in AlwaysIncludedShaders so it survives the build).
            mat = new Material(Shader.Find("DFUQuest3/VRUIChromaKey"));
            if (mat == null || mat.shader == null)
                mat = new Material(Shader.Find("Unlit/Transparent Cutout"));
            quad.GetComponent<Renderer>().sharedMaterial = mat;
            quad.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            quad.GetComponent<Renderer>().receiveShadows = false;
            quad.SetActive(false);
            DontDestroyOnLoad(quad);
        }

        void Update()
        {
            if (quad == null) BuildQuad();
            var gm = GameManager.Instance;
            if (gm == null || quad == null) return;

            FPSWeapon w = null;
            try { if (gm.WeaponManager != null) w = gm.WeaponManager.ScreenWeapon; } catch { }
            bool visible = w != null && !gm.WeaponManager.Sheathed && w.ShowWeapon
                           && w.WeaponType != DaggerfallWorkshop.WeaponTypes.None && w.CurrentWeaponTexture != null
                           && !GameManager.IsGamePaused;
            quad.SetActive(visible);
            if (!visible) return;

            // Sync the texture + current anim frame (same pair FPSWeapon.OnGUI draws).
            if (mat.mainTexture != w.CurrentWeaponTexture) mat.mainTexture = w.CurrentWeaponTexture;
            Rect r = w.CurrentAnimRect;
            // Apply the frame UV rect (handles negative width for mirrored frames).
            mat.mainTextureOffset = new Vector2(r.x, r.y);
            mat.mainTextureScale = new Vector2(r.width, r.height);

            // Anchor to the controller (tracking space -> world via player feet + rig yaw,
            // same transform pattern VRUIOverlay.HandlePointer uses).
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
                // Fallback: float at classic lower-right view position relative to the player.
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
                    rot = Quaternion.LookRotation(look.normalized) * Quaternion.Euler(0, 180f, 0); // quad faces -Z
            }
            quad.transform.SetPositionAndRotation(pos, rot);
        }
    }
}
