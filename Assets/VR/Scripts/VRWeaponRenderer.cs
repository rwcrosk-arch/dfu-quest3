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
//
// Per-hand combat visuals (2026-09-24, layer 3): a SECOND quad renders the LEFT hand.
//   - Left weapon idle: the engine's pre-cached left atlas (WeaponManager feeds
//     UpdateLeftHandGfxCache for its SwitchHand feature) via FPSWeapon's VR accessors;
//     UV mirrored (negative width) like FPSWeapon.OnGUI's FlipHorizontal convention.
//   - Left weapon LIVE during a dual-wield swing: during VRBlockController's flip
//     window, ScreenWeapon IS the left weapon mid-animation — mirror CurrentWeaponTexture/
//     CurrentAnimRect (the engine already presents it flipped) while the right quad hides.
//   - Shield: static item image from ItemHelper.GetItemImage on its own quad; when the
//     guard window is active the shield nudges up/forward as the raised-guard cue.
// Right quad: unchanged semantics; the old block guard-offset retired (guard belongs
// to the left/shield hand now).

using UnityEngine;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Utility;

namespace DFUQuest3
{
    public class VRWeaponRenderer : MonoBehaviour
    {
        public float quadWidth = 0.4f;      // weapon quad size in meters (tune on-device)
        public float quadHeight = 0.6f;
        public Vector3 localOffset = new Vector3(0f, 0.1f, 0.3f); // up/forward from grip
        public MCPPoseBridge poseBridge;

        // Left-hand presentation tuning (on-device):
        public Vector3 leftLocalOffset = new Vector3(0f, 0.1f, 0.25f);
        public float metersPerPixel = 0.0035f;   // sprite px -> world meters (weapon/shield quads)
        public Vector3 guardRaiseOffset = new Vector3(0f, 0.06f, 0.08f); // shield guard cue (rot-local)

        GameObject quad;          // right hand (active-hand mirror)
        Material mat;
        GameObject leftQuad;      // left hand: weapon idle/live OR shield
        Material leftMat;
        bool nreLogged;
        float diagTimer = 3f;

        // ---- 3D pack path (preferred; sprite quads stay as fallback) ----
        public VR3DWeaponPack pack;
        public bool rightModel3D = true;    // 3D models on the right hand (sprite fallback if off/unavailable)
        public bool leftModel3D = true;
        GameObject rightModel, leftModel;   // instantiated pack models (un-parented under this DDOL transform)
        int rightModelId, leftModelId;      // currently instantiated ids (0 = none yet)

        VR3DWeaponPack Pack
        {
            get
            {
                if (pack == null) pack = GetComponent<VR3DWeaponPack>();
                if (pack == null) pack = FindFirstObjectByType<VR3DWeaponPack>();
                return pack;
            }
        }

        /// <summary>
        /// Attach/position the pack model for one hand. Returns false to let the sprite
        /// quad path proceed (no pack, no prefab for this id, or load failure).
        /// Model ids derive from the EQUIPPED ITEM (not ScreenWeapon) so the right-hand
        /// model keeps its identity during the dual-wield flip window.
        /// Note v1: pack models hold their idle pose during swings (static meshes, no
        /// rig) — swing feedback comes from the controller motion itself.
        /// </summary>
        bool UpdateHandModel(bool rightHand, DaggerfallUnityItem item, Vector3 fallbackOffset,
            Vector3 localOffset, out Vector3 pos, out Quaternion rot)
        {
            var p3d = Pack;
            if (p3d == null || !p3d.PackLoaded || item == null)
            {
                pos = default(Vector3); rot = default(Quaternion); return false;
            }

            int id = 0;
            if (item.IsShield)
            {
                var shieldMetal = DaggerfallUnity.Instance.ItemHelper.ConvertItemMaterialToAPIMetalType(item);
                id = VR3DWeaponPack.ShieldPrefabId(shieldMetal, VR3DWeaponPack.ShieldTypeIndex(item.shortName));
            }
            else if (item.ItemGroup == ItemGroups.Weapons)
            {
                // Classic fine-grained type (Dagger=113..Long_Bow=130) — the pack's index
                // space. (The atlas-level converter collapses 8 blades into LongBlade.)
                var w = (Weapons)item.TemplateIndex;
                if (w == Weapons.Short_Bow || w == Weapons.Long_Bow || w == Weapons.Arrow)
                {
                    pos = default(Vector3); rot = default(Quaternion);
                    return false;   // no bow models in pack v1 (crossbow prefab exists but no mechanic yet)
                }
                var metal = DaggerfallUnity.Instance.ItemHelper.ConvertItemMaterialToAPIMetalType(item);
                id = VR3DWeaponPack.WeaponPrefabId(metal, w);
            }
            if (id == 0) { pos = default(Vector3); rot = default(Quaternion); return false; }

            GameObject model = rightHand ? rightModel : leftModel;
            int currentId = rightHand ? rightModelId : leftModelId;
            if (model == null || currentId != id)
            {
                if (model != null) Destroy(model);
                model = p3d.CreateHandModel(id, "3D " + (rightHand ? "R" : "L") + " " + id);
                if (model != null)
                {
                    model.transform.SetParent(transform, false);
                    DontDestroyOnLoad(model);   // survive scene loads like the renderer itself
                    // Auto-scale: longest bounds axis -> classic length for the class
                    var rends = model.GetComponentsInChildren<Renderer>();
                    if (rends != null && rends.Length > 0)
                    {
                        Bounds b = rends[0].bounds;
                        foreach (var r2 in rends) b.Encapsulate(r2.bounds);
                        float longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                        float target = p3d.TargetLength((Weapons)item.TemplateIndex);
                        float scale = target / Mathf.Max(0.01f, longest);
                        model.transform.localScale = Vector3.one * scale;
                        Debug.Log("[DFUQuest3] 3D model attached id=" + id +
                            " bounds=" + b.size.ToString("F3") + " scale=" + scale.ToString("F3"));
                    }
                }
                if (rightHand) { rightModel = model; rightModelId = id; }
                else { leftModel = model; leftModelId = id; }
                if (model == null) { pos = default(Vector3); rot = default(Quaternion); return false; }
            }

            TrackingToWorld(poseBridge, !rightHand, poseBridge != null &&
                (rightHand ? poseBridge.controllerValid : poseBridge.leftControllerValid),
                rightHand ? poseBridge.controllerPosition : poseBridge.leftControllerPosition,
                rightHand ? poseBridge.controllerRotation : poseBridge.leftControllerRotation,
                fallbackOffset, out pos, out rot);
            pos += rot * (rightHand ? localOffset : leftLocalOffset);

            // Grip orientation (Ross, device test 2026-10-03: "should be oriented as if
            // held in a hand — hilt down, blade up"): pack models aim the blade along
            // +Z (sprite-era "toward the enemy"); in a fist the blade must RISE. A -90°
            // X rotation maps mesh +Z onto controller-local +Y (up through the aim
            // pose), so flicking the wrist sweeps the blade like a real weapon. Pack
            // origin sits at the pommel — lift ~5cm so the handle lands in the fist.
            // Shields keep the aim-forward face (their Z face is the threat side).
            bool isShieldModel = id >= 132000;
            if (!isShieldModel)
            {
                rot *= Quaternion.Euler(-90f, 0f, 0f);
                pos += rot * new Vector3(0f, 0.05f, 0f);
            }
            return true;
        }

        // Shield image cache (item identity -> texture + px size)
        class ShieldArt { public Texture2D tex; public Vector2 sizePx; }
        readonly Dictionary<DaggerfallUnityItem, ShieldArt> shieldArtCache = new Dictionary<DaggerfallUnityItem, ShieldArt>();

        GameObject BuildQuadObject(string name)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = name;
            var col = q.GetComponent<Collider>();
            if (col) Destroy(col);
            Shader s = Shader.Find("DFUQuest3/VRUIChromaKey");
            if (s == null || !s.isSupported)
            {
                Debug.LogError("[DFUQuest3] VRUIChromaKey shader NOT AVAILABLE — " +
                    "check that Assets/VR/Shaders/VRUIChromaKey.shader is in AlwaysIncludedShaders " +
                    "with fileID 4800000. Building debug-magenta fallback for " + name + ".");
                var unlit = Shader.Find("Unlit/Color");
                var m = new Material(unlit != null ? unlit : Shader.Find("Hidden/Internal-Colored"));
                m.color = Color.magenta;
                q.GetComponent<Renderer>().sharedMaterial = m;
            }
            else
            {
                q.GetComponent<Renderer>().sharedMaterial = new Material(s);
            }
            var r = q.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            q.SetActive(false);
            DontDestroyOnLoad(q);
            return q;
        }

        void Update()
        {
            try
            {
                UpdateRightQuad();
                UpdateLeftQuad();
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

        // Shared world-space transform: anchor at player feet, rotate through rig yaw,
        // never parent under XROrigin (double tracking offset). Mirrors VRUIOverlay.
        // Falls back to an eye-relative pose when the tracked pose is stale — the quad
        // follows the head's side (left/right) instead of freezing mid-air.
        void TrackingToWorld(MCPPoseBridge bridge, bool useLeft, bool poseValid,
            Vector3 posePos, Quaternion poseRot,
            Vector3 fallbackOffset, out Vector3 pos, out Quaternion rot)
        {
            var gm = GameManager.Instance;
            Vector3 anchor = gm.PlayerObject ? gm.PlayerObject.transform.position : Vector3.zero;
            var rig = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
            Quaternion rigYaw = rig ? Quaternion.Euler(0, rig.transform.eulerAngles.y, 0) : Quaternion.identity;

            bool fresh = poseValid && bridge != null && bridge.PoseFresh(useLeft);
            if (fresh)
            {
                pos = anchor + rigYaw * posePos;
                rot = rigYaw * poseRot;
            }
            else
            {
                float yaw = gm.PlayerObject ? gm.PlayerObject.transform.eulerAngles.y : 0f;
                pos = anchor + Quaternion.Euler(0, yaw, 0) * fallbackOffset;
                rot = Quaternion.Euler(0, yaw, 0);
            }
        }

        void FaceCamera(Vector3 pos, ref Quaternion rot)
        {
            var gm = GameManager.Instance;
            Camera cam = gm.MainCamera != null ? gm.MainCamera : Camera.main;
            if (cam != null)
            {
                Vector3 look = pos - cam.transform.position; look.y = 0;
                if (look.sqrMagnitude > 0.0001f)
                    rot = Quaternion.LookRotation(look.normalized) * Quaternion.Euler(0, 180f, 0);
            }
        }

        // ---------------- RIGHT hand (3D pack model first, sprite-mirror fallback) ----------------
        void UpdateRightQuad()
        {
            if (quad == null)
            {
                quad = BuildQuadObject("DFU VR Weapon");
                mat = quad.GetComponent<Renderer>().sharedMaterial;
            }
            var gm = GameManager.Instance;
            if (gm == null || quad == null) return;

            FPSWeapon w = null;
            WeaponManager wm = null;
            try { wm = gm.WeaponManager; if (wm != null) w = wm.ScreenWeapon; } catch { }

            // 3D pack model first — driven by the EQUIPPED right item (identity is stable
            // during the dual-wield flip, unlike ScreenWeapon which becomes the left weapon).
            DaggerfallUnityItem rightItem = null;
            var pe = gm.PlayerEntity;
            if (pe != null && pe.ItemEquipTable != null)
                rightItem = pe.ItemEquipTable.GetItem(EquipSlots.RightHand);

            bool modelShown = false;
            if (rightModel3D && rightItem != null)
            {
                Vector3 mpos; Quaternion mrot;
                modelShown = UpdateHandModel(true, rightItem,
                    new Vector3(0.25f, 1.2f, 0.5f), localOffset, out mpos, out mrot);
                if (modelShown)
                {
                    // Visibility family mirrors the sprite quad (sheath/spell-cast/pause hide it).
                    bool vis = wm != null && !wm.Sheathed && w != null && w.ShowWeapon
                        && !GameManager.IsGamePaused;
                    rightModel.SetActive(vis);
                    // 3D model orientation = controller pose (NO billboard — a real sword
                    // points where the hand points; Z+ mesh convention matches aim pose).
                    rightModel.transform.SetPositionAndRotation(mpos, mrot);
                    quad.SetActive(false);
                }
            }

            if (!modelShown)
            {
                // Sprite fallback path (unchanged semantics)
                bool leftOwnsHand = DFUQuest3.VRBlockController.IsLeftHandActive;
                bool visible = w != null && wm != null && !wm.Sheathed && w.ShowWeapon
                               && w.WeaponType != DaggerfallWorkshop.WeaponTypes.None
                               && w.CurrentWeaponTexture != null
                               && !GameManager.IsGamePaused
                               && !leftOwnsHand;
                quad.SetActive(visible);
                diagTimer -= Time.unscaledDeltaTime;
                if (diagTimer <= 0f)
                {
                    diagTimer = 3f;
                    Debug.Log("[DFUQuest3] VRWeapon quad R=" + (visible ? "VISIBLE" : "INVISIBLE") +
                        " L=" + (leftQuad != null && leftQuad.activeSelf ? "VISIBLE" : "INVISIBLE") +
                        ": sheathed=" + (wm != null ? wm.Sheathed : -1) +
                        " showWeapon=" + (w != null ? w.ShowWeapon : -1) +
                        " type=" + (w != null ? w.WeaponType.ToString() : "null") +
                        " paused=" + GameManager.IsGamePaused +
                        " leftHandActive=" + leftOwnsHand +
                        " blocking=" + DFUQuest3.VRBlockController.IsBlocking +
                        " mat=" + (mat != null ? mat.shader.name : "null"));
                }
                if (!visible) return;

                if (mat.mainTexture != w.CurrentWeaponTexture) mat.mainTexture = w.CurrentWeaponTexture;
                Rect r = w.CurrentAnimRect;
                mat.mainTextureOffset = new Vector2(r.x, r.y);
                mat.mainTextureScale = new Vector2(r.width, r.height);

                Vector2 sizePx = w.CurrentWeaponSizePx;
                if (sizePx.x > 1f && sizePx.y > 1f)
                    SetQuadSize(quad, Mathf.Max(0.08f, sizePx.x * metersPerPixel),
                        Mathf.Max(0.12f, sizePx.y * metersPerPixel));

                Vector3 pos; Quaternion rot;
                TrackingToWorld(poseBridge, false, poseBridge != null && poseBridge.controllerValid,
                    poseBridge != null ? poseBridge.controllerPosition : Vector3.zero,
                    poseBridge != null ? poseBridge.controllerRotation : Quaternion.identity,
                    new Vector3(0.25f, 1.2f, 0.5f), out pos, out rot);
                pos += rot * localOffset;

                FaceCamera(pos, ref rot);
                quad.transform.SetPositionAndRotation(pos, rot);
            }
        }

        // ---------------- LEFT hand (3D model first; sprite idle/live/shield fallback) ----------------
        void UpdateLeftQuad()
        {
            if (leftQuad == null)
            {
                leftQuad = BuildQuadObject("DFU VR Weapon (Left)");
                leftMat = leftQuad.GetComponent<Renderer>().sharedMaterial;
            }
            var gm = GameManager.Instance;
            if (gm == null || leftQuad == null) return;
            var wm = gm.WeaponManager;
            var w = wm != null ? wm.ScreenWeapon : null;
            if (wm == null || w == null || GameManager.IsGamePaused)
            {
                leftQuad.SetActive(false);
                return;
            }

            var pe = gm.PlayerEntity;
            DaggerfallUnityItem leftItem = null;
            if (pe != null && pe.ItemEquipTable != null)
                leftItem = pe.ItemEquipTable.GetItem(EquipSlots.LeftHand);

            bool leftOwnsHand = DFUQuest3.VRBlockController.IsLeftHandActive;

            // 3D pack model first (equipped-item identity; during a left swing the model
            // simply follows the hand — which is real swing feedback in itself, v1 note).
            if (leftModel3D && leftItem != null)
            {
                Vector3 mpos; Quaternion mrot;
                if (UpdateHandModel(false, leftItem,
                    new Vector3(-0.25f, 1.2f, 0.5f), leftLocalOffset, out mpos, out mrot))
                {
                    bool vis = leftOwnsHand || (!wm.Sheathed && w.ShowWeapon);
                    leftModel.SetActive(vis);
                    leftModel.transform.SetPositionAndRotation(mpos, mrot);
                    // Guard cue nudges the shield model up/forward while the window is open
                    if (leftItem.IsShield && DFUQuest3.VRBlockController.IsBlocking)
                        leftModel.transform.position += mrot * guardRaiseOffset;
                    leftQuad.SetActive(false);
                    return;
                }
            }

            // Priority 1: LIVE left-weapon swing (engine is animating the LEFT weapon now)
            if (leftOwnsHand && !wm.Sheathed && w.ShowWeapon
                && w.WeaponType != WeaponTypes.None && w.WeaponType != WeaponTypes.Bow
                && w.CurrentWeaponTexture != null)
            {
                if (leftMat.mainTexture != w.CurrentWeaponTexture) leftMat.mainTexture = w.CurrentWeaponTexture;
                Rect r = w.CurrentAnimRect;
                if (r.width < 0)
                {
                    // Engine already presents mirrored (FlipHorizontal convention) — use as-is
                    leftMat.mainTextureOffset = new Vector2(r.x, r.y);
                    leftMat.mainTextureScale = new Vector2(r.width, r.height);
                }
                else
                {
                    // Mirror to read as a LEFT-held swing (same convention as the idle pose)
                    leftMat.mainTextureOffset = new Vector2(r.x + r.width, r.y);
                    leftMat.mainTextureScale = new Vector2(-r.width, r.height);
                }
                SetQuadSize(leftQuad, quadWidth, quadHeight);

                Vector3 pos; Quaternion rot;
                TrackingToWorld(poseBridge, true, poseBridge != null && poseBridge.leftControllerValid,
                    poseBridge != null ? poseBridge.leftControllerPosition : Vector3.zero,
                    poseBridge != null ? poseBridge.leftControllerRotation : Quaternion.identity,
                    new Vector3(-0.25f, 1.2f, 0.5f), out pos, out rot);
                pos += rot * leftLocalOffset;
                FaceCamera(pos, ref rot);
                leftQuad.transform.SetPositionAndRotation(pos, rot);
                leftQuad.SetActive(true);
                return;
            }

            // Priority 2: idle left WEAPON (pre-cached off-hand atlas, mirrored presentation)
            if (leftItem != null && !leftItem.IsShield &&
                leftItem.ItemGroup == ItemGroups.Weapons && !wm.Sheathed)
            {
                Texture2D tex; Rect uv; Vector2 sizePx;
                if (w.TryGetLeftHandIdleVisual(out tex, out uv, out sizePx))
                {
                    if (leftMat.mainTexture != tex) leftMat.mainTexture = tex;
                    // Mirror horizontally (negative width), same convention FPSWeapon uses
                    // for FlipHorizontal presentation — a left-held weapon faces inward.
                    leftMat.mainTextureOffset = new Vector2(uv.x + uv.width, uv.y);
                    leftMat.mainTextureScale = new Vector2(-uv.width, uv.height);
                    float qw = Mathf.Max(0.12f, sizePx.x * metersPerPixel);
                    float qh = Mathf.Max(0.18f, sizePx.y * metersPerPixel);
                    SetQuadSize(leftQuad, qw, qh);

                    Vector3 pos; Quaternion rot;
                    TrackingToWorld(poseBridge, true, poseBridge != null && poseBridge.leftControllerValid,
                        poseBridge != null ? poseBridge.leftControllerPosition : Vector3.zero,
                        poseBridge != null ? poseBridge.leftControllerRotation : Quaternion.identity,
                        new Vector3(-0.25f, 1.2f, 0.5f), out pos, out rot);
                    pos += rot * leftLocalOffset;
                    FaceCamera(pos, ref rot);
                    leftQuad.transform.SetPositionAndRotation(pos, rot);
                    leftQuad.SetActive(true);
                    return;
                }
                // no cached atlas yet (left gfx cache fills ~1 frame after equip) — hide
                leftQuad.SetActive(false);
                return;
            }

            // Priority 3: SHIELD (static item image; guard cue raises it slightly)
            if (leftItem != null && leftItem.IsShield)
            {
                ShieldArt art = GetShieldArt(leftItem);
                if (art != null && art.tex != null)
                {
                    if (leftMat.mainTexture != art.tex) leftMat.mainTexture = art.tex;
                    leftMat.mainTextureOffset = new Vector2(0f, 0f);
                    leftMat.mainTextureScale = new Vector2(1f, 1f);
                    float qw = Mathf.Max(0.18f, art.sizePx.x * metersPerPixel);
                    float qh = Mathf.Max(0.24f, art.sizePx.y * metersPerPixel);
                    SetQuadSize(leftQuad, qw, qh);

                    Vector3 pos; Quaternion rot;
                    TrackingToWorld(poseBridge, true, poseBridge != null && poseBridge.leftControllerValid,
                        poseBridge != null ? poseBridge.leftControllerPosition : Vector3.zero,
                        poseBridge != null ? poseBridge.leftControllerRotation : Quaternion.identity,
                        new Vector3(-0.28f, 1.25f, 0.42f), out pos, out rot);
                    pos += rot * leftLocalOffset;
                    // Guard cue: nudge up/forward while the guard window is open
                    if (DFUQuest3.VRBlockController.IsBlocking)
                        pos += rot * guardRaiseOffset;
                    FaceCamera(pos, ref rot);
                    leftQuad.transform.SetPositionAndRotation(pos, rot);
                    leftQuad.SetActive(true);
                    return;
                }
                leftQuad.SetActive(false);
                return;
            }

            // Nothing held / sheathed: no left visual
            leftQuad.SetActive(false);
        }

        void SetQuadSize(GameObject q, float wMeters, float hMeters)
        {
            var s = q.transform.localScale;
            if (!Mathf.Approximately(s.x, wMeters) || !Mathf.Approximately(s.y, hMeters))
                q.transform.localScale = new Vector3(wMeters, hMeters, 1f);
        }

        ShieldArt GetShieldArt(DaggerfallUnityItem item)
        {
            ShieldArt art;
            if (shieldArtCache.TryGetValue(item, out art) && art != null && art.tex != null)
                return art;
            try
            {
                // Inventory (face-on) image reads best on a floating quad; the paper-doll
                // variant is the strapped-on side view.
                ImageData data = DaggerfallUnity.Instance.ItemHelper.GetItemImage(item);
                if (data.texture == null)
                    return null;
                art = new ShieldArt { tex = data.texture, sizePx = new Vector2(data.width, data.height) };
                // Cheap cache cap: items are stable per equip table; never grows beyond a few.
                if (shieldArtCache.Count > 8) shieldArtCache.Clear();
                shieldArtCache[item] = art;
                Debug.Log("[DFUQuest3] shield art cached: " + item.shortName +
                    " (" + data.width + "x" + data.height + ")");
                return art;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[DFUQuest3] shield art load failed: " + e.Message);
                return null;
            }
        }
    }
}