// DFU Quest3 VR — comfort settings page for the in-game effects/settings window.
// Exposes right-stick vertical look (pitch) options: disabled by default (comfort —
// head tracking already covers vertical look, and stick pitch tilts the horizon, the
// most nauseogenic motion class in VR), snap-pitch quantization, smooth-pitch speed,
// and a pitch clamp that ALWAYS applies (also fixes the historical unclamped-pitch bug).
//
// Evidence base: DFU_VR_RESEARCH_COMFORT.md (Meta locomotion guidance, discrete-motion
// sickness data). Implementation pattern copied from AntialiasingConfigPage.
//
// Settings keys live under [VR] in settings.ini (SettingsManager), persisted across
// sessions and switchable in real time (Meta guidance: options reachable in gameplay).

using UnityEngine;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;

namespace DaggerfallWorkshop.Game.UserInterfaceWindows
{
    public class VRComfortConfigPage : GameEffectConfigPage
    {
        const string key = "vrcomfort";

        Checkbox verticalLookCheckbox;
        HorizontalSlider pitchModeSlider;
        HorizontalSlider pitchSpeedSlider;
        HorizontalSlider pitchLimitSlider;

        public override string Key => key;

        public override string Title => "VR Comfort";

        public override void Setup(Panel parent)
        {
            Vector2 pos = settingsStartPos;

            // About this page
            AddTipPanel(parent, "VR comfort options. Right-stick vertical look is OFF by default: head tracking already lets you look up and down, and stick pitch tilts the horizon, a common source of motion sickness in VR.");

            // Master checkbox: vertical look enabled (OFF = comfort default)
            verticalLookCheckbox = AddCheckbox(parent, "Right-stick vertical look", ref pos);
            verticalLookCheckbox.OnToggleState += VerticalLookCheckbox_OnToggleState;

            // Pitch mode: Snap 15 / Snap 30 / Smooth (no "Off" entry — the master
            // checkbox above is the single source of truth for on/off)
            string[] pitchModes = new string[] { "Snap 15", "Snap 30", "Smooth" };
            pitchModeSlider = AddSlider(parent, "Pitch mode", pitchModes.Length, ref pos);
            pitchModeSlider.OnScroll += PitchModeSlider_OnScroll;
            pitchModeSlider.SetIndicator(pitchModes, DaggerfallUnity.Settings.VRPitchMode);
            StyleIndicator(pitchModeSlider);

            // Smooth pitch speed (degrees/sec) — low default per Meta guidance
            string[] pitchSpeeds = new string[] { "Slow", "Medium", "Fast", "Very Fast" };
            pitchSpeedSlider = AddSlider(parent, "Smooth pitch speed", pitchSpeeds.Length, ref pos);
            pitchSpeedSlider.OnScroll += PitchSpeedSlider_OnScroll;
            pitchSpeedSlider.SetIndicator(pitchSpeeds, SpeedIndex(DaggerfallUnity.Settings.VRPitchSpeed));
            StyleIndicator(pitchSpeedSlider);

            // Pitch clamp (degrees) — applies in every mode (bugfix: pitch was unclamped)
            string[] pitchLimits = new string[] { "30", "45", "60", "75", "90" };
            pitchLimitSlider = AddSlider(parent, "Pitch limit", pitchLimits.Length, ref pos);
            pitchLimitSlider.OnScroll += PitchLimitSlider_OnScroll;
            pitchLimitSlider.SetIndicator(pitchLimits, LimitIndex(DaggerfallUnity.Settings.VRPitchLimit));
            StyleIndicator(pitchLimitSlider);
        }

        // Mapping helpers between settings degrees and slider indices.
        static readonly int[] speedValues = { 30, 60, 90, 120 };   // degrees per second
        static readonly int[] limitValues = { 30, 45, 60, 75, 90 };

        static int SpeedIndex(int degPerSec)
        {
            for (int i = 0; i < speedValues.Length; i++)
                if (speedValues[i] == degPerSec) return i;
            return 0;
        }

        static int LimitIndex(int degrees)
        {
            for (int i = 0; i < limitValues.Length; i++)
                if (limitValues[i] == degrees) return i;
            return 2; // 60
        }

        public override void ReadSettings()
        {
            verticalLookCheckbox.IsChecked = DaggerfallUnity.Settings.VRVerticalLookEnabled;
            // Slider index = VRPitchMode - 1 (stored mode 1=Snap15, 2=Snap30, 3=Smooth;
            // 0=Off is expressed by the checkbox, not the slider).
            pitchModeSlider.ScrollIndex = Mathf.Clamp(DaggerfallUnity.Settings.VRPitchMode - 1, 0, 2);
            pitchSpeedSlider.ScrollIndex = SpeedIndex(DaggerfallUnity.Settings.VRPitchSpeed);
            pitchLimitSlider.ScrollIndex = LimitIndex(DaggerfallUnity.Settings.VRPitchLimit);
        }

        public override void DeploySettings()
        {
            // SettingsManager properties are already updated by the handlers; persistence
            // happens via GameEffectsConfigWindow.OnPop -> Settings.SaveSettings().
            // Runtime consumers (VRTriggerBridge) read the live properties, so changes
            // apply immediately — Meta guidance: comfort options switchable in real time.
        }

        public override void SetDefaults()
        {
            // Comfort-friendly defaults: vertical look OFF, everything else conservative.
            DaggerfallUnity.Settings.VRVerticalLookEnabled = false;
            DaggerfallUnity.Settings.VRPitchMode = 0;
            DaggerfallUnity.Settings.VRPitchSpeed = 30;
            DaggerfallUnity.Settings.VRPitchLimit = 60;
        }

        private void VerticalLookCheckbox_OnToggleState()
        {
            DaggerfallUnity.Settings.VRVerticalLookEnabled = verticalLookCheckbox.IsChecked;
        }

        private void PitchModeSlider_OnScroll()
        {
            // Slider index 0/1/2 -> stored mode 1/2/3 (Snap 15/Snap 30/Smooth).
            DaggerfallUnity.Settings.VRPitchMode = Mathf.Clamp(pitchModeSlider.ScrollIndex + 1, 1, 3);
        }

        private void PitchSpeedSlider_OnScroll()
        {
            DaggerfallUnity.Settings.VRPitchSpeed = speedValues[Mathf.Clamp(pitchSpeedSlider.ScrollIndex, 0, speedValues.Length - 1)];
        }

        private void PitchLimitSlider_OnScroll()
        {
            DaggerfallUnity.Settings.VRPitchLimit = limitValues[Mathf.Clamp(pitchLimitSlider.ScrollIndex, 0, limitValues.Length - 1)];
        }
    }
}