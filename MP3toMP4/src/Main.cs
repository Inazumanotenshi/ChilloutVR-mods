using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using ABI.CCK.Components;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(MP3toMP4.MP3toMP4Mod), "MP3toMP4", "0.2.0", "Inazumanotenshi", null)]
[assembly: MelonGame("ChilloutVR", "ChilloutVR")]
[assembly: MelonOptionalDependencies(new[] { "UIExpansionKit" })]

namespace MP3toMP4
{
    public enum DisplayMode
    {
        Both,
        Icon,
        Visualizer
    }

    public class MP3toMP4Mod : MelonMod
    {
        internal static MelonLogger.Instance Logger;

        private static MelonPreferences_Category _category;

        // Master switch.
        internal static MelonPreferences_Entry<bool> Enabled;
        // Both, Icon or Visualizer. An enum, so menus show a selection instead of a text field.
        internal static MelonPreferences_Entry<DisplayMode> Mode;
        // Show the title (or file name) at the top.
        internal static MelonPreferences_Entry<bool> ShowTitle;
        // Show progress bar + time at the bottom.
        internal static MelonPreferences_Entry<bool> ShowProgress;
        // Number of visualizer bars.
        internal static MelonPreferences_Entry<int> BarCount;
        // Visualizer sensitivity.
        internal static MelonPreferences_Entry<float> Sensitivity;
        // Automatically level the volume (quiet players still look good).
        internal static MelonPreferences_Entry<bool> AutoGain;
        // Accent color as RGB (0 - 255 each), like the color sliders in ChilloutVR.
        internal static MelonPreferences_Entry<int> AccentR;
        internal static MelonPreferences_Entry<int> AccentG;
        internal static MelonPreferences_Entry<int> AccentB;
        // Old hex setting (version 0.1.0), hidden; only read once to migrate to RGB.
        private static MelonPreferences_Entry<string> _legacyAccentHex;
        // Internal render width in pixels (height follows the world's screen aspect).
        internal static MelonPreferences_Entry<int> RenderWidth;
        // Max. visualizer frame rate.
        internal static MelonPreferences_Entry<int> MaxFps;
        // Seconds without a video frame before the mod takes over.
        internal static MelonPreferences_Entry<float> DetectDelay;
        // Take over even when CVR shows a thumbnail (e.g. YouTube audio-only).
        internal static MelonPreferences_Entry<bool> ReplaceThumbnail;
        // Icon and glow pulse with the bass.
        internal static MelonPreferences_Entry<bool> IconPulse;

        internal static DisplayMode CurrentMode = DisplayMode.Both;
        internal static readonly Color32 DefaultAccent = new Color32(217, 108, 192, 255);
        internal static Color32 Accent = DefaultAccent;

        public override void OnInitializeMelon()
        {
            Logger = LoggerInstance;

            _category = MelonPreferences.CreateCategory("MP3toMP4", "MP3 to MP4");
            Enabled = _category.CreateEntry("Enabled", true, "Enabled",
                "Shows an icon/visualizer in the video player for audio-only streams.", false, false, null);
            Mode = _category.CreateEntry("DisplayMode", DisplayMode.Both, "Display Mode",
                "Both = icon + visualizer, Icon = icon only, Visualizer = bars only.", false, false, null);
            ShowTitle = _category.CreateEntry("ShowTitle", true, "Show Title",
                "Show the title or file name at the top.", false, false, null);
            ShowProgress = _category.CreateEntry("ShowProgress", true, "Show Progress",
                "Show the progress bar and time at the bottom.", false, false, null);
            BarCount = _category.CreateEntry("BarCount", 48, "Bar Count",
                "Number of visualizer bars (8 - 128).", false, false, null);
            Sensitivity = _category.CreateEntry("Sensitivity", 1.0f, "Sensitivity",
                "How strongly the bars react (0.3 - 3.0).", false, false, null);
            AutoGain = _category.CreateEntry("AutoGain", true, "Auto Gain",
                "Levels the volume so quiet players still look good.", false, false, null);
            AccentR = _category.CreateEntry("AccentRed", (int)DefaultAccent.r, "Accent Red",
                "Red part of the bar color (0 - 255).", false, false, null);
            AccentG = _category.CreateEntry("AccentGreen", (int)DefaultAccent.g, "Accent Green",
                "Green part of the bar color (0 - 255).", false, false, null);
            AccentB = _category.CreateEntry("AccentBlue", (int)DefaultAccent.b, "Accent Blue",
                "Blue part of the bar color (0 - 255).", false, false, null);
            _legacyAccentHex = _category.CreateEntry("AccentColor", "", "Accent Color (old)",
                "Replaced by AccentRed/AccentGreen/AccentBlue.", true, true, null);
            RenderWidth = _category.CreateEntry("RenderWidth", 640, "Render Width",
                "Internal resolution in pixels (256 - 1920). Higher = sharper, but more CPU.", false, false, null);
            MaxFps = _category.CreateEntry("MaxFps", 30, "Max FPS",
                "How many times per second the visualizer is redrawn (5 - 90).", false, false, null);
            DetectDelay = _category.CreateEntry("DetectDelay", 1.0f, "Detect Delay",
                "Seconds without a video frame before the mod takes over.", false, false, null);
            ReplaceThumbnail = _category.CreateEntry("ReplaceThumbnail", false, "Replace Thumbnail",
                "Take over even when CVR shows a thumbnail (e.g. YouTube in audio-only mode).", false, false, null);
            IconPulse = _category.CreateEntry("IconPulse", true, "Icon Pulse",
                "Icon and glow pulse with the bass.", false, false, null);

            MigrateLegacyAccent();
            ParseSettings();

            Enabled.OnEntryValueChanged.Subscribe((oldValue, newValue) => OnSettingsChanged(), 0, false);
            Mode.OnEntryValueChanged.Subscribe((oldValue, newValue) => OnSettingsChanged(), 0, false);
            ShowTitle.OnEntryValueChanged.Subscribe((oldValue, newValue) => OnSettingsChanged(), 0, false);
            ShowProgress.OnEntryValueChanged.Subscribe((oldValue, newValue) => OnSettingsChanged(), 0, false);
            BarCount.OnEntryValueChanged.Subscribe((oldValue, newValue) => OnSettingsChanged(), 0, false);
            AccentR.OnEntryValueChanged.Subscribe((oldValue, newValue) => OnSettingsChanged(), 0, false);
            AccentG.OnEntryValueChanged.Subscribe((oldValue, newValue) => OnSettingsChanged(), 0, false);
            AccentB.OnEntryValueChanged.Subscribe((oldValue, newValue) => OnSettingsChanged(), 0, false);
            RenderWidth.OnEntryValueChanged.Subscribe((oldValue, newValue) => OnSettingsChanged(), 0, false);
            IconPulse.OnEntryValueChanged.Subscribe((oldValue, newValue) => OnSettingsChanged(), 0, false);
            // These are read live every frame; they only need to be saved.
            Sensitivity.OnEntryValueChanged.Subscribe((oldValue, newValue) => RequestSave(), 0, false);
            AutoGain.OnEntryValueChanged.Subscribe((oldValue, newValue) => RequestSave(), 0, false);
            MaxFps.OnEntryValueChanged.Subscribe((oldValue, newValue) => RequestSave(), 0, false);
            DetectDelay.OnEntryValueChanged.Subscribe((oldValue, newValue) => RequestSave(), 0, false);
            ReplaceThumbnail.OnEntryValueChanged.Subscribe((oldValue, newValue) => RequestSave(), 0, false);

            // Quick Menu page via ChilloutVR's built-in UILib (no extra mod needed).
            QuickMenu.Init();

            // Sliders/dropdown in UI Expansion Kit's "Mod Settings", if installed.
            if (IsAssemblyLoaded("UIExpansionKit"))
                RegisterUix();

            Logger.Msg("MP3toMP4 loaded.");
        }

        // Kept in its own method so UI Expansion Kit types are only resolved when it is installed.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RegisterUix()
        {
            try
            {
                UixSettings.Register();
            }
            catch (Exception e)
            {
                Logger.Warning("Could not register UI Expansion Kit slider ranges: " + e.Message);
            }
        }

        private static bool IsAssemblyLoaded(string name)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name == name)
                    return true;
            }
            return false;
        }

        public override void OnUpdate()
        {
            OverlayManager.Tick();

            // Save changes made in the menu a moment after the last slider movement.
            if (_saveAt > 0f && Time.unscaledTime >= _saveAt)
            {
                _saveAt = 0f;
                _category.SaveToFile(false);
            }
        }

        private static float _saveAt;

        internal static void RequestSave()
        {
            _saveAt = Time.unscaledTime + 2f;
        }

        private static void OnSettingsChanged()
        {
            ParseSettings();
            OverlayManager.SettingsChanged();
            RequestSave();
        }

        private static void MigrateLegacyAccent()
        {
            Color32 old;
            if (TryParseHex(_legacyAccentHex.Value, out old))
            {
                AccentR.Value = old.r;
                AccentG.Value = old.g;
                AccentB.Value = old.b;
                Logger.Msg("Migrated old accent color to RGB: " + old.r + ", " + old.g + ", " + old.b);
            }

            // Remove the old text entry so it doesn't show up as a text field in settings menus.
            _category.DeleteEntry("AccentColor");
            _legacyAccentHex = null;
            _category.SaveToFile(false);
        }

        private static void ParseSettings()
        {
            CurrentMode = Enum.IsDefined(typeof(DisplayMode), Mode.Value) ? Mode.Value : DisplayMode.Both;

            Accent = new Color32(
                (byte)Mathf.Clamp(AccentR.Value, 0, 255),
                (byte)Mathf.Clamp(AccentG.Value, 0, 255),
                (byte)Mathf.Clamp(AccentB.Value, 0, 255),
                255);
        }

        private static bool TryParseHex(string hex, out Color32 color)
        {
            color = default(Color32);
            if (string.IsNullOrEmpty(hex))
                return false;
            hex = hex.Trim().TrimStart('#');
            if (hex.Length != 6)
                return false;
            int value;
            if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
                return false;
            color = new Color32((byte)((value >> 16) & 0xFF), (byte)((value >> 8) & 0xFF), (byte)(value & 0xFF), 255);
            return true;
        }
    }

    // CVRVideoPlayer.Update() blits GetVideoTexture() into the ProjectionTexture every frame.
    // Audio-only media has no picture, so we draw our own into it afterwards.
    [HarmonyPatch(typeof(CVRVideoPlayer), "Update")]
    internal static class CVRVideoPlayerUpdatePatch
    {
        private static bool _errorLogged;

        private static void Postfix(CVRVideoPlayer __instance)
        {
            if (!MP3toMP4Mod.Enabled.Value)
                return;
            try
            {
                OverlayManager.OnPlayerUpdate(__instance);
            }
            catch (Exception e)
            {
                if (_errorLogged)
                    return;
                _errorLogged = true;
                MP3toMP4Mod.Logger.Error("Error in video player overlay (logged only once): " + e);
            }
        }
    }

    // CVR shows its own thumbnail for some audio-only streams (e.g. YouTube).
    // Remember which URL that happened for, so we don't cover it by default.
    [HarmonyPatch(typeof(CVRVideoPlayer), "FetchAndDisplayThumbnail")]
    internal static class CVRVideoPlayerThumbnailPatch
    {
        private static void Postfix(CVRVideoPlayer __instance)
        {
            try
            {
                OverlayManager.OnThumbnailShown(__instance);
            }
            catch
            {
                // convenience feature only
            }
        }
    }
}
