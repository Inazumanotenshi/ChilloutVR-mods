using System;
using System.Globalization;
using ABI.CCK.Components;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(MP3toMP4.MP3toMP4Mod), "MP3toMP4", "0.1.0", "Inazumanotenshi", null)]
[assembly: MelonGame("ChilloutVR", "ChilloutVR")]

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
        // "Both", "Icon" or "Visualizer".
        internal static MelonPreferences_Entry<string> Mode;
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
        // Accent color as hex (#RRGGBB).
        internal static MelonPreferences_Entry<string> AccentColor;
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
        internal static Color32 Accent = new Color32(217, 108, 192, 255);

        public override void OnInitializeMelon()
        {
            Logger = LoggerInstance;

            _category = MelonPreferences.CreateCategory("MP3toMP4", "MP3 to MP4");
            Enabled = _category.CreateEntry("Enabled", true, "Enabled",
                "Shows an icon/visualizer in the video player for audio-only streams.", false, false, null);
            Mode = _category.CreateEntry("DisplayMode", "Both", "Display Mode",
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
            AccentColor = _category.CreateEntry("AccentColor", "#D96CC0", "Accent Color",
                "Bar color as hex (#RRGGBB).", false, false, null);
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

            ParseSettings();

            Enabled.OnEntryValueChanged.Subscribe((oldValue, newValue) => OnSettingsChanged(), 0, false);
            Mode.OnEntryValueChanged.Subscribe((oldValue, newValue) => OnSettingsChanged(), 0, false);
            ShowTitle.OnEntryValueChanged.Subscribe((oldValue, newValue) => OnSettingsChanged(), 0, false);
            ShowProgress.OnEntryValueChanged.Subscribe((oldValue, newValue) => OnSettingsChanged(), 0, false);
            BarCount.OnEntryValueChanged.Subscribe((oldValue, newValue) => OnSettingsChanged(), 0, false);
            AccentColor.OnEntryValueChanged.Subscribe((oldValue, newValue) => OnSettingsChanged(), 0, false);
            RenderWidth.OnEntryValueChanged.Subscribe((oldValue, newValue) => OnSettingsChanged(), 0, false);
            IconPulse.OnEntryValueChanged.Subscribe((oldValue, newValue) => OnSettingsChanged(), 0, false);

            Logger.Msg("MP3toMP4 loaded.");
        }

        public override void OnUpdate()
        {
            OverlayManager.Tick();
        }

        private static void OnSettingsChanged()
        {
            ParseSettings();
            OverlayManager.SettingsChanged();
        }

        private static void ParseSettings()
        {
            DisplayMode mode;
            if (Enum.TryParse(Mode.Value ?? "", true, out mode))
                CurrentMode = mode;
            else
                CurrentMode = DisplayMode.Both;

            Color32 accent;
            if (TryParseHex(AccentColor.Value, out accent))
                Accent = accent;
            else
                Accent = new Color32(217, 108, 192, 255);
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
