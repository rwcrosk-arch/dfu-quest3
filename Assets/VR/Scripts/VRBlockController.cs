// DFU Quest3 VR — physical blocking (left grip hold).
//
// DFU has NO player block at all (verified: upstream FPSWeapon.cs / WeaponManager.cs
// contain zero block code; classic Daggerfall's hold-right-mouse block was never
// ported). This builds the mechanic VR-native: HOLD LEFT GRIP while a melee weapon is
// drawn = defensive stance. Incoming MELEE damage rolls a block chance; success plays
// a parry sound and negates the hit.
//
// Formula (classic-flavored): chance% = equippedWeaponSkill * 0.5, +15 if a shield is
// equipped, capped at 75%. Small fatigue cost per attempt. Melee only in v1 (arrows/
// spells untouched).
//
// Consumed by EnemyAttack.SendDamageToPlayer via VRBlockController.TryBlock().
using UnityEngine;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallConnect;
using DaggerfallWorkshop.Utility;

namespace DFUQuest3
{
    [DefaultExecutionOrder(50)]
    public class VRBlockController : MonoBehaviour
    {
        public static VRBlockController Instance { get; private set; }
        public static bool IsBlocking { get; private set; }

        const float fatigueCostPerBlock = 2f;

        void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void Update()
        {
            IsBlocking = ComputeBlocking();
        }

        bool ComputeBlocking()
        {
            // Left grip held
            var grip = VRActionBinder.GripLeftAction;
            if (grip == null || grip.ReadValue<float>() <= 0.5f)
                return false;

            var gm = GameManager.Instance;
            if (gm == null || gm.WeaponManager == null)
                return false;

            // Only while a weapon is actively shown (sheathed/holstered = no block)
            var wm = gm.WeaponManager;
            if (wm.ScreenWeapon == null || !wm.ScreenWeapon.ShowWeapon)
                return false;

            // No blocking while casting or with a bow readied (melee weapons only)
            if (gm.PlayerSpellCasting != null && gm.PlayerSpellCasting.IsPlayingAnim)
                return false;
            if (wm.ScreenWeapon.WeaponType == WeaponTypes.Bow)
                return false;

            return true;
        }

        /// <summary>
        /// Roll a block attempt against incoming melee damage.
        /// Returns true when the hit is fully blocked (parry sound + HUD feedback already fired).
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
                return false; // fists: no block in v1

            int skillId = (int)weapon.GetWeaponSkillID();
            int skillValue = player.Skills.GetLiveSkillValue((DFCareer.Skills)skillId);

            // Chance% = skill/2, +15 with a shield equipped, cap 75
            float chance = skillValue * 0.5f;
            var shield = player.ItemEquipTable.GetItem(EquipSlots.LeftHand);
            if (shield != null && shield.IsShield)
                chance += 15f;
            chance = Mathf.Clamp(chance, 0f, 75f);

            if (Random.Range(0f, 100f) >= chance)
                return false;

            // Blocked: parry sound + feedback + fatigue cost
            var dfui = DaggerfallUI.Instance;
            if (dfui != null && dfui.DaggerfallAudioSource != null)
            {
                var clip = (SoundClips)Random.Range((int)SoundClips.Parry1, (int)SoundClips.Parry3 + 1);
                dfui.DaggerfallAudioSource.PlayOneShot(clip);
            }
            DaggerfallUI.AddHUDText($"Blocked! ({chance:F0}% chance)");
            player.DecreaseFatigue((int)fatigueCostPerBlock);
            Debug.Log($"[DFUQuest3] BLOCK: incoming {damage} dmg negated (chance {chance:F0}%, skill {skillValue})");
            return true;
        }
    }
}