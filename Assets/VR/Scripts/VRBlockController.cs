// DFU Quest3 VR — per-hand contextual combat (grip = whatever that hand holds).
//
// Semantics (Ross, 2026-09-24): each hand's grip speaks for what that hand holds.
//   RIGHT grip: weapon or fists -> attack. (Shields can never be in the right hand —
//               DFU equips shields LeftHand-only, verified ItemEquipTable.cs.)
//   LEFT grip:  left weapon    -> dual-wield attack (LEFT weapon's damage/skill/
//               enchantments apply via the active-hand flip trick below);
//               shield/empty  -> tap = timed guard window (IsBlocking);
//               2H weapon equipped (ItemHands.Both) -> both grips swing that weapon,
//               blocking impossible (classic-faithful: 2H can't block).
//
// Dual-wield damage — the flip trick (verified engine seams):
//   WeaponManager (~line 909): strikingWeapon = usingRightHand ? currentRightHandWeapon
//   : currentLeftHandWeapon — ALL damage/skill-tally/enchant attribution follows the
//   public `UsingRightHand` flag. So a left attack = UsingRightHand=false for the
//   swing, restored to true after. WM.Update (order 0) re-applies the hand config
//   (UpdateHands -> ApplyWeapon) every frame BEFORE our order-50 update, so setting
//   the flag here executes the attack with the left-hand config exactly one frame
//   after the flip — invisible to the player.
//
// Hook preserved: EnemyAttack.SendDamageToPlayer -> VRBlockController.TryBlock
// (class/field names unchanged so the upstream diff stays minimal).
//
// Grip ownership: VRBlockController is the ONLY reader of grip presses (the injector's
// grip branches were removed 2026-09-24) so edge semantics live in exactly one place.
using UnityEngine;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallConnect;

namespace DFUQuest3
{
    [DefaultExecutionOrder(50)]
    public class VRBlockController : MonoBehaviour
    {
        public static VRBlockController Instance { get; private set; }
        public static bool IsBlocking { get; private set; }

        /// <summary>True while a left-hand(dual-wield) swing owns the active-hand slot —
        /// consumers (e.g. VRWeaponRenderer) hide the right-hand visuals during it.</summary>
        public static bool IsLeftHandActive
        {
            get { return Instance != null && Instance.handState != HandState.RightActive; }
        }

        const float guardWindowSeconds = 1.2f;   // timed guard after a shield/empty-hand tap
        const float fatigueCostPerBlock = 2f;

        // Dual-wield flip state machine
        enum HandState { RightActive, LeftPending, LeftAttacking, RestorePending }
        HandState handState = HandState.RightActive;

        bool pendingRightAttack;   // right-grip queued while a left swing owned the hand
        float guardUntil;          // game-time bound of the guard window

        void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void Update()
        {
            var wm = GameManager.Instance ? GameManager.Instance.WeaponManager : null;

            // Pending dual-wield sequencing (runs AFTER WM.Update's ApplyWeapon at order 0)
            if (wm != null && wm.ScreenWeapon != null)
            {
                switch (handState)
                {
                    case HandState.LeftPending:
                        wm.UsingRightHand = false;  // WM applies left config on its next frame
                        handState = HandState.LeftAttacking;
                        Debug.Log("[DFUQuest3] DW flip -> LEFT hand active");
                        break;

                    case HandState.LeftAttacking:
                        if (!wm.Sheathed)
                        {
                            wm.VRTriggerAttack();
                            if (wm.ScreenWeapon.IsAttacking())
                            {
                                handState = HandState.RestorePending;
                                Debug.Log("[DFUQuest3] DW left attack issued");
                            }
                            // else: equip countdown / cooldown rejected the fire —
                            // stay in LeftAttacking and retry next frame (hand stays
                            // flipped left until the attack actually starts).
                        }
                        else
                        {
                            wm.UsingRightHand = true;   // sheathed mid-flip: abort cleanly
                            handState = HandState.RightActive;
                            Debug.Log("[DFUQuest3] DW aborted (sheathed)");
                        }
                        break;

                    case HandState.RestorePending:
                        // swing finished (or VRTriggerAttack rejected it) -> hand back right
                        if (!wm.ScreenWeapon.IsAttacking())
                        {
                            wm.UsingRightHand = true;
                            handState = HandState.RightActive;
                            Debug.Log("[DFUQuest3] DW flip back -> RIGHT hand active");
                        }
                        break;
                }

                // Queued right attack fires once the hand is back and idle
                if (pendingRightAttack && handState == HandState.RightActive
                    && !wm.ScreenWeapon.IsAttacking())
                {
                    pendingRightAttack = false;
                    if (!wm.Sheathed)
                    {
                        wm.VRTriggerAttack();
                        Debug.Log("[DFUQuest3] queued RIGHT attack fired");
                    }
                }
            }
            else
            {
                pendingRightAttack = false;
            }

            ReadGripEdges();
        }

        void ReadGripEdges()
        {
            // Guard window countdown first (game time — freezes while paused)
            bool now = Time.time < guardUntil && GuardAllowed();
            if (now != IsBlocking)
                Debug.Log($"[DFUQuest3] GUARD {(now ? "ON" : "OFF")} (window {(now ? guardUntil - Time.time : 0f):F1}s left)");
            IsBlocking = now;

            // Edge-detect grips — one reader, one semantic owner
            var gl = VRActionBinder.GripLeftAction;
            if (gl != null && gl.enabled)
            {
                bool pressed;
                try { pressed = gl.WasPressedThisFrame(); } catch { pressed = false; }
                if (pressed) OnLeftGripPressed();
            }
            var gr = VRActionBinder.GripRightAction;
            if (gr != null && gr.enabled)
            {
                bool pressed;
                try { pressed = gr.WasPressedThisFrame(); } catch { pressed = false; }
                if (pressed) OnRightGripPressed();
            }
        }

        // ---- per-hand context resolution ----

        static DaggerfallUnityItem LeftItem()
        {
            var pe = GameManager.Instance ? GameManager.Instance.PlayerEntity : null;
            return pe != null ? pe.ItemEquipTable.GetItem(EquipSlots.LeftHand) : null;
        }

        static DaggerfallUnityItem RightItem()
        {
            var pe = GameManager.Instance ? GameManager.Instance.PlayerEntity : null;
            return pe != null ? pe.ItemEquipTable.GetItem(EquipSlots.RightHand) : null;
        }

        static bool IsTwoHanded(DaggerfallUnityItem item)
        {
            return item != null && ItemEquipTable.GetItemHands(item) == ItemHands.Both;
        }

        bool GuardAllowed()
        {
            // Same gating family as the original stance: no guard while casting;
            // sheathed handled by ShowWeapon check at window-raising time — but a
            // mid-window sheath should not keep guarding either.
            var wm = GameManager.Instance ? GameManager.Instance.WeaponManager : null;
            if (wm == null || wm.ScreenWeapon == null || !wm.ScreenWeapon.ShowWeapon)
                return false;
            var gm = GameManager.Instance;
            if (gm.PlayerSpellCasting != null && gm.PlayerSpellCasting.IsPlayingAnim)
                return false;
            // Bow active hand: no guard (classic + our v1 melee-only rule)
            if (wm.ScreenWeapon.WeaponType == WeaponTypes.Bow)
                return false;
            return true;
        }

        bool CanActNow(WeaponManager wm)
        {
            return wm != null && wm.ScreenWeapon != null
                && wm.ScreenWeapon.ShowWeapon
                && !wm.ScreenWeapon.IsAttacking()
                && !GameManager.IsGamePaused;
        }

        // ---- grip entry points ----

        public void OnRightGripPressed()
        {
            var wm = GameManager.Instance ? GameManager.Instance.WeaponManager : null;
            if (wm == null || wm.ScreenWeapon == null) return;

            // mid left-swing: queue for when the hand returns
            if (handState != HandState.RightActive)
            {
                pendingRightAttack = true;
                Debug.Log("[DFUQuest3] R-grip queued (left swing owns the hand)");
                return;
            }
            if (!CanActNow(wm)) return;

            // Right hand: weapon or fists -> attack. 2H lives here too (it is the
            // right-hand item). VRTriggerAttack carries sheath/cooldown/paralyzed/
            // climb guards mirroring the click-attack path.
            wm.VRTriggerAttack();
            Debug.Log("[DFUQuest3] R-grip -> attack (right item=" +
                (RightItem() != null ? RightItem().shortName : "fists") + ")");
        }

        public void OnLeftGripPressed()
        {
            var wm = GameManager.Instance ? GameManager.Instance.WeaponManager : null;
            if (wm == null || wm.ScreenWeapon == null) return;
            if (!CanActNow(wm)) return;

            // Bow drawn: no left-hand duty at all (bow needs both hands; classic-faithful).
            if (wm.ScreenWeapon.WeaponType == WeaponTypes.Bow)
            {
                Debug.Log("[DFUQuest3] L-grip ignored (bow drawn)");
                return;
            }

            var left = LeftItem();
            var right = RightItem();

            // 2H weapon: BOTH hands belong to it — either grip swings it, no blocking.
            if (IsTwoHanded(right))
            {
                wm.VRTriggerAttack();
                Debug.Log("[DFUQuest3] L-grip -> attack 2H weapon (" + right.shortName + ")");
                return;
            }

            if (left != null && !left.IsShield && left.ItemGroup == ItemGroups.Weapons)
            {
                // dual-wield: flip active hand to LEFT; attack fires next frame
                // (WM's order-0 Update applies the left config in between); hand
                // restores when the swing completes.
                if (handState != HandState.RightActive) return;   // already mid flip/swing
                handState = HandState.LeftPending;
                Debug.Log("[DFUQuest3] L-grip -> dual-wield attack (" + left.shortName + ")");
            }
            else if (left == null || left.IsShield)
            {
                // shield or empty left hand -> timed guard window
                guardUntil = Time.time + guardWindowSeconds;
                Debug.Log("[DFUQuest3] L-grip -> guard window " + guardWindowSeconds +
                    "s (left=" + (left != null ? left.shortName : "empty") + ")");
            }
            // anything else in the left slot (non-weapon non-shield): ignore
        }

        /// <summary>
        /// Roll a block attempt against incoming melee damage (hook unchanged).
        /// </summary>
        public bool TryBlock(int damage)
        {
            if (!IsBlocking || damage <= 0)
                return false;

            var gm = GameManager.Instance;
            var player = gm.PlayerEntity;
            if (player == null)
                return false;

            // Equipped right-hand weapon's skill (classic blocking used weapon skill;
            // Daggerfall has no Shield skill).
            var weapon = player.ItemEquipTable.GetItem(EquipSlots.RightHand);
            if (weapon == null)
            {
                // Fists: no block in v1 — but SAY so (both-outcome feedback rule; a
                // silent window reads as broken).
                DaggerfallUI.AddHUDText("No weapon to block with");
                Debug.Log("[DFUQuest3] BLOCK FAIL: no right-hand weapon (fists)");
                return false;
            }

            int skillId = (int)weapon.GetWeaponSkillID();
            int skillValue = player.Skills.GetLiveSkillValue((DFCareer.Skills)skillId);

            // Chance% = skill/2, +15 with a shield equipped, cap 75
            float chance = skillValue * 0.5f;
            var shield = player.ItemEquipTable.GetItem(EquipSlots.LeftHand);
            if (shield != null && shield.IsShield)
                chance += 15f;
            chance = Mathf.Clamp(chance, 0f, 75f);

            // Feedback for BOTH outcomes — a silent block attempt reads as "broken"
            // (Ross's bat test: rolls failed with zero feedback, system seemed dead).
            if (Random.Range(0f, 100f) >= chance)
            {
                // Failed block: soft miss-cue + HUD text with the chance so the player
                // learns what their skill provides. No fatigue cost on failure.
                var dfuiFail = DaggerfallUI.Instance;
                if (dfuiFail != null && dfuiFail.DaggerfallAudioSource != null)
                    dfuiFail.DaggerfallAudioSource.PlayOneShot(SoundClips.SwingHighPitch, 1f, 0.5f);
                DaggerfallUI.AddHUDText($"Block failed ({chance:F0}% chance)");
                Debug.Log($"[DFUQuest3] BLOCK FAIL: {damage} dmg taken (chance {chance:F0}%, skill {skillValue})");
                return false;
            }

            // Blocked: parry sound + feedback + fatigue cost
            var dfui = DaggerfallUI.Instance;
            if (dfui != null && dfui.DaggerfallAudioSource != null)
            {
                var clip = (SoundClips)Random.Range((int)SoundClips.Parry1, (int)SoundClips.Parry3 + 1);
                dfui.DaggerfallAudioSource.PlayOneShot(clip);
            }
            DaggerfallUI.AddHUDText($"Blocked! ({chance:F0}% chance)");
            player.DecreaseFatigue((int)fatigueCostPerBlock);
            Debug.Log($"[DFUQuest3] BLOCK SUCCESS: incoming {damage} dmg negated (chance {chance:F0}%, skill {skillValue})");
            return true;
        }
    }
}