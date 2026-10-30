using System;
using System.IO;
using System.Reflection;
using ABI_RC.Core.InteractionSystem;
using ABI_RC.Systems.UI.UILib;
using ABI_RC.Systems.UI.UILib.UIObjects;
using ABI_RC.Systems.UI.UILib.UIObjects.Components;
using ABI_RC.Systems.UI.UILib.UIObjects.Objects;
using MelonLoader;
using UnityEngine;

namespace MP3toMP4
{
    // In-game settings page in the Quick Menu (Misc tab -> MP3toMP4), built with ChilloutVR's own UILib.
    // No extra mod needed. The color button opens CVR's built-in color picker (swatch + RGB sliders).
    internal static class QuickMenu
    {
        private const string ModName = "MP3toMP4";
        private const string IconName = "MP3toMP4-Icon";

        // Same order as the DisplayMode enum (Both, Icon, Visualizer).
        private static readonly string[] ModeLabels = { "Icon + Visualizer", "Icon only", "Visualizer only" };

        private static bool _created;
        private static Button _modeButton;
        private static SliderFloat _red, _green, _blue;

        internal static void Init()
        {
            if (QuickMenuAPI.MiscTabPage != null)
                Create();
            else
                QuickMenuAPI.OnMenuGenerated += OnMenuGenerated;
        }

        private static void OnMenuGenerated(CVR_MenuManager manager)
        {
            QuickMenuAPI.OnMenuGenerated -= OnMenuGenerated;
            Create();
        }

        private static void Create()
        {
            if (_created)
                return;
            _created = true;

            try
            {
                using (Stream icon = Assembly.GetExecutingAssembly().GetManifestResourceStream("MP3toMP4.mp3.png"))
                {
                    if (icon != null)
                        QuickMenuAPI.PrepareIcon(ModName, IconName, icon);
                }

                Category entry = QuickMenuAPI.MiscTabPage.AddCategory("MP3toMP4", ModName);
                Page page = entry.AddPage("MP3toMP4", IconName, "Settings for the audio-only video player overlay.", ModName);

                // General
                Category general = page.AddCategory("General", ModName);
                AddToggle(general, MP3toMP4Mod.Enabled, "Enabled", "Show the overlay for audio-only streams.");
                AddToggle(general, MP3toMP4Mod.ShowTitle, "Show Title", "Show the title or file name at the top.");
                AddToggle(general, MP3toMP4Mod.ShowProgress, "Show Progress", "Show the progress bar and time at the bottom.");
                AddToggle(general, MP3toMP4Mod.IconPulse, "Icon Pulse", "Icon and glow pulse with the bass.");
                AddToggle(general, MP3toMP4Mod.ReplaceThumbnail, "Replace Thumbnail", "Also take over when CVR shows its own thumbnail (e.g. YouTube audio-only).");

                // Opens a selection list instead of a text field.
                _modeButton = general.AddButton(ModeButtonText(), IconName, "Choose what is shown on the screen.");
                _modeButton.OnPress += OpenModeSelection;

                // Accent color: CVR's color picker plus the three sliders directly on the page.
                Category color = page.AddCategory("Accent Color", ModName);
                Button pick = color.AddButton("Pick Color", IconName, "Open the color picker.");
                pick.OnPress += OpenColorPicker;
                _red = AddIntSlider(color, MP3toMP4Mod.AccentR, "Red", "Red part of the bar color.", 0, 255, MP3toMP4Mod.DefaultAccent.r);
                _green = AddIntSlider(color, MP3toMP4Mod.AccentG, "Green", "Green part of the bar color.", 0, 255, MP3toMP4Mod.DefaultAccent.g);
                _blue = AddIntSlider(color, MP3toMP4Mod.AccentB, "Blue", "Blue part of the bar color.", 0, 255, MP3toMP4Mod.DefaultAccent.b);

                // Visualizer
                Category visualizer = page.AddCategory("Visualizer", ModName);
                AddIntSlider(visualizer, MP3toMP4Mod.BarCount, "Bar Count", "Number of visualizer bars.", 8, 128, 48);
                AddFloatSlider(visualizer, MP3toMP4Mod.Sensitivity, "Sensitivity", "How strongly the bars react.", 0.3f, 3f, 2, 1f);
                AddToggle(visualizer, MP3toMP4Mod.AutoGain, "Auto Gain", "Levels the volume so quiet players still look good.");

                // Advanced
                Category advanced = page.AddCategory("Advanced", ModName, true, true, true);
                AddIntSlider(advanced, MP3toMP4Mod.MaxFps, "Max FPS", "How often the visualizer is redrawn per second.", 5, 90, 30);
                AddIntSlider(advanced, MP3toMP4Mod.RenderWidth, "Render Width", "Internal resolution in pixels. Higher = sharper, but more CPU.", 256, 1920, 640);
                AddFloatSlider(advanced, MP3toMP4Mod.DetectDelay, "Detect Delay", "Seconds without a video frame before the mod takes over.", 0f, 5f, 1, 1f);

                MP3toMP4Mod.Logger.Msg("Quick Menu page added (Misc tab -> MP3toMP4).");
            }
            catch (Exception e)
            {
                MP3toMP4Mod.Logger.Error("Could not create the Quick Menu page: " + e);
            }
        }

        private static void AddToggle(Category category, MelonPreferences_Entry<bool> entry, string name, string tooltip)
        {
            ToggleButton toggle = category.AddToggle(name, tooltip, entry.Value);
            toggle.OnValueUpdated += value =>
            {
                if (entry.Value != value)
                    entry.Value = value;
            };
        }

        private static SliderFloat AddIntSlider(Category category, MelonPreferences_Entry<int> entry, string name, string tooltip, int min, int max, int defaultValue)
        {
            SliderFloat slider = category.AddSlider(name, tooltip, entry.Value, min, max, 0, defaultValue, true);
            slider.OnValueUpdated += value =>
            {
                int rounded = Math.Max(min, Math.Min(max, (int)Math.Round(value)));
                if (entry.Value != rounded)
                    entry.Value = rounded;
            };
            return slider;
        }

        private static void AddFloatSlider(Category category, MelonPreferences_Entry<float> entry, string name, string tooltip, float min, float max, int decimals, float defaultValue)
        {
            SliderFloat slider = category.AddSlider(name, tooltip, entry.Value, min, max, decimals, defaultValue, true);
            slider.OnValueUpdated += value =>
            {
                float clamped = Math.Max(min, Math.Min(max, value));
                if (Math.Abs(entry.Value - clamped) > 0.0001f)
                    entry.Value = clamped;
            };
        }

        private static void OpenColorPicker()
        {
            Color current = MP3toMP4Mod.Accent;
            QuickMenuAPI.OpenColourPicker(current, (picked, hex) =>
            {
                int r = Mathf.Clamp(Mathf.RoundToInt(picked.r * 255f), 0, 255);
                int g = Mathf.Clamp(Mathf.RoundToInt(picked.g * 255f), 0, 255);
                int b = Mathf.Clamp(Mathf.RoundToInt(picked.b * 255f), 0, 255);
                MP3toMP4Mod.AccentR.Value = r;
                MP3toMP4Mod.AccentG.Value = g;
                MP3toMP4Mod.AccentB.Value = b;

                // Keep the sliders on our page in sync.
                if (_red != null) _red.SetSliderValue(r);
                if (_green != null) _green.SetSliderValue(g);
                if (_blue != null) _blue.SetSliderValue(b);
            }, true);
        }

        private static int CurrentModeIndex()
        {
            int index = (int)MP3toMP4Mod.Mode.Value;
            return index >= 0 && index < ModeLabels.Length ? index : 0;
        }

        private static string ModeButtonText()
        {
            return "Mode: " + ModeLabels[CurrentModeIndex()];
        }

        private static void OpenModeSelection()
        {
            var selection = new MultiSelection("Display Mode", ModeLabels, CurrentModeIndex());
            selection.OnOptionUpdated += index =>
            {
                if (index < 0 || index >= ModeLabels.Length)
                    return;
                MP3toMP4Mod.Mode.Value = (DisplayMode)index;
                if (_modeButton != null)
                    _modeButton.ButtonText = ModeButtonText();
            };
            QuickMenuAPI.OpenMultiSelect(selection);
        }
    }
}
