using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using ActionMenu;
using UnityEngine;

namespace MP3toMP4
{
    // Own submenu in AdvActionMenu (Action Menu -> Mods -> MP3toMP4), built with its mod API (ActionMenuMod.Lib).
    // Only created when AdvActionMenu is installed. Every entry has its own icon (embedded PNGs).
    internal sealed class ActionMenuIntegration : ActionMenuMod.Lib
    {
        private static ActionMenuIntegration _instance;

        internal static void Register()
        {
            if (_instance == null)
                _instance = new ActionMenuIntegration();
        }

        protected override string modName => "MP3toMP4";

        protected override string modIcon => ModIconUri;

        // If building the menu ever fails, AdvActionMenu gets an empty list instead of an exception.
        protected override List<MenuItem> modMenuItems()
        {
            try
            {
                return BuildMainItems();
            }
            catch (Exception e)
            {
                MP3toMP4Mod.Logger.Warning("Could not build the Action Menu entry: " + e.Message);
                return new List<MenuItem>();
            }
        }

        // Menu layout:
        //   Enabled
        //   Display Mode  > Icon + Visualizer / Icon only / Visualizer only
        //   Appearance    > Accent Color > Red / Green / Blue / Reset Color
        //                   Show Title / Show Progress / Icon Pulse
        //   Visualizer    > Sensitivity / Bar Count (number pad) / Auto Gain
        //   Advanced      > Max FPS (number pad) / Hardware Acceleration / Detect Delay / Replace Thumbnail / Debug Log
        private List<MenuItem> BuildMainItems()
        {
            return new List<MenuItem>
            {
                Toggle("Enabled", v => Set(MP3toMP4Mod.Enabled, v), MP3toMP4Mod.Enabled.Value, Icons.Power),
                Menu("Display Mode", ModeItems, Icons.Layout),
                Menu("Appearance", AppearanceItems, Icons.Eye),
                Menu("Visualizer", VisualizerItems, Icons.Bars),
                Menu("Advanced", AdvancedItems, Icons.Settings),
            };
        }

        private List<MenuItem> ModeItems()
        {
            DisplayMode mode = MP3toMP4Mod.Mode.Value;
            return new List<MenuItem>
            {
                Button("Icon + Visualizer", () => SetMode(DisplayMode.Both), mode == DisplayMode.Both, Icons.Both, true),
                Button("Icon only", () => SetMode(DisplayMode.Icon), mode == DisplayMode.Icon, ModIconUri, true),
                Button("Visualizer only", () => SetMode(DisplayMode.Visualizer), mode == DisplayMode.Visualizer, Icons.Bars, true),
            };
        }

        private List<MenuItem> AppearanceItems()
        {
            return new List<MenuItem>
            {
                Menu("Accent Color", ColorItems, Icons.Palette),
                Toggle("Show Title", v => Set(MP3toMP4Mod.ShowTitle, v), MP3toMP4Mod.ShowTitle.Value, Icons.Title),
                Toggle("Show Progress", v => Set(MP3toMP4Mod.ShowProgress, v), MP3toMP4Mod.ShowProgress.Value, Icons.Progress),
                Toggle("Icon Pulse", v => Set(MP3toMP4Mod.IconPulse, v), MP3toMP4Mod.IconPulse.Value, Icons.Pulse),
            };
        }

        private List<MenuItem> ColorItems()
        {
            return new List<MenuItem>
            {
                Radial("Red", v => SetInt(MP3toMP4Mod.AccentR, v, 0, 255), Icons.Red, 0f, 255f, MP3toMP4Mod.AccentR.Value),
                Radial("Green", v => SetInt(MP3toMP4Mod.AccentG, v, 0, 255), Icons.Green, 0f, 255f, MP3toMP4Mod.AccentG.Value),
                Radial("Blue", v => SetInt(MP3toMP4Mod.AccentB, v, 0, 255), Icons.Blue, 0f, 255f, MP3toMP4Mod.AccentB.Value),
                Button("Reset Color", () => MP3toMP4Mod.Safe(ResetColor, "Reset Color"), null, Icons.Reset, false),
            };
        }

        private List<MenuItem> VisualizerItems()
        {
            return new List<MenuItem>
            {
                Radial("Sensitivity", v => SetFloat(MP3toMP4Mod.Sensitivity, v, 0.3f, 3f), Icons.Gauge, 0.3f, 3f, MP3toMP4Mod.Sensitivity.Value),
                Number("Bar Count", v => SetInt(MP3toMP4Mod.BarCount, v, 8, 128), Icons.BarCount, 8f, 128f, MP3toMP4Mod.BarCount.Value),
                Toggle("Auto Gain", v => Set(MP3toMP4Mod.AutoGain, v), MP3toMP4Mod.AutoGain.Value, Icons.Gain),
            };
        }

        private List<MenuItem> AdvancedItems()
        {
            return new List<MenuItem>
            {
                Number("Max FPS", v => SetInt(MP3toMP4Mod.MaxFps, v, 5, 90), Icons.Fps, 5f, 90f, MP3toMP4Mod.MaxFps.Value),
                Toggle("Hardware Acceleration", v => Set(MP3toMP4Mod.HardwareAcceleration, v), MP3toMP4Mod.HardwareAcceleration.Value, Icons.Chip),
                Radial("Detect Delay", v => SetFloat(MP3toMP4Mod.DetectDelay, v, 0f, 5f), Icons.Clock, 0f, 5f, MP3toMP4Mod.DetectDelay.Value),
                Toggle("Replace Thumbnail", v => Set(MP3toMP4Mod.ReplaceThumbnail, v), MP3toMP4Mod.ReplaceThumbnail.Value, Icons.Picture),
                Toggle("Debug Log", v => Set(MP3toMP4Mod.DebugLog, v), MP3toMP4Mod.DebugLog.Value, Icons.Bug),
            };
        }

        // Number input with AdvActionMenu's number pad: a radial item whose control is switched to "number"
        // (the same way AdvActionMenu itself turns avatar Input Singles into number pads).
        private MenuItem Number(string label, Action<float> callback, string icon, float min, float max, float value)
        {
            MenuItem item = Radial(label, callback, icon, min, max, value);
            ItemAction action = item.action;
            action.control = "number";
            item.action = action;
            return item;
        }

        private static void ResetColor()
        {
            MP3toMP4Mod.AccentR.Value = MP3toMP4Mod.DefaultAccent.r;
            MP3toMP4Mod.AccentG.Value = MP3toMP4Mod.DefaultAccent.g;
            MP3toMP4Mod.AccentB.Value = MP3toMP4Mod.DefaultAccent.b;
        }

        // All setters run through MP3toMP4Mod.Safe, so an error can never reach AdvActionMenu or the game.
        private static void SetMode(DisplayMode mode)
        {
            MP3toMP4Mod.Safe(() => MP3toMP4Mod.Mode.Value = mode, "Display Mode");
        }

        private static void Set(MelonLoader.MelonPreferences_Entry<bool> entry, bool value)
        {
            MP3toMP4Mod.Safe(() =>
            {
                if (entry.Value != value)
                    entry.Value = value;
            }, entry.Identifier);
        }

        private static void SetInt(MelonLoader.MelonPreferences_Entry<int> entry, float value, int min, int max)
        {
            MP3toMP4Mod.Safe(() =>
            {
                if (float.IsNaN(value))
                    return;
                int rounded = Mathf.Clamp(Mathf.RoundToInt(value), min, max);
                if (entry.Value != rounded)
                    entry.Value = rounded;
            }, entry.Identifier);
        }

        private static void SetFloat(MelonLoader.MelonPreferences_Entry<float> entry, float value, float min, float max)
        {
            MP3toMP4Mod.Safe(() =>
            {
                if (float.IsNaN(value))
                    return;
                float clamped = Mathf.Clamp(value, min, max);
                if (Mathf.Abs(entry.Value - clamped) > 0.0001f)
                    entry.Value = clamped;
            }, entry.Identifier);
        }

        // The embedded mp3.png as data URI (same picture as on the video player screen).
        private static string _modIconUri;

        private static string ModIconUri
        {
            get
            {
                if (_modIconUri != null)
                    return _modIconUri;
                try
                {
                    using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("MP3toMP4.mp3.png"))
                    {
                        if (s != null)
                        {
                            var ms = new MemoryStream();
                            s.CopyTo(ms);
                            _modIconUri = "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
                        }
                    }
                }
                catch
                {
                    // fall back to the drawn icon below
                }
                if (_modIconUri == null)
                    _modIconUri = Icons.Bars;
                return _modIconUri;
            }
        }

        // White line icons (128x128 PNG), compiled into the DLL as embedded resources (src/icons/*.png).
        // Deliberately PNG and not SVG: SVG data URIs ("image/svg+xml") get mangled on the way into
        // ChilloutVR's menu engine (Cohtml) and made the game crash. PNG data URIs are handled fine.
        private static class Icons
        {
            internal static readonly string Power = Png("power");
            internal static readonly string Layout = Png("layout");
            internal static readonly string Both = Png("both");
            internal static readonly string Bars = Png("bars");
            internal static readonly string Palette = Png("palette");
            internal static readonly string Title = Png("title");
            internal static readonly string Progress = Png("progress");
            internal static readonly string Pulse = Png("pulse");
            internal static readonly string Picture = Png("picture");
            internal static readonly string Gauge = Png("gauge");
            internal static readonly string BarCount = Png("barcount");
            internal static readonly string Gain = Png("gain");
            internal static readonly string Reset = Png("reset");
            internal static readonly string Red = Png("dot_red");
            internal static readonly string Green = Png("dot_green");
            internal static readonly string Blue = Png("dot_blue");
            internal static readonly string Eye = Png("eye");
            internal static readonly string Settings = Png("settings");
            internal static readonly string Fps = Png("fps");
            internal static readonly string Chip = Png("chip");
            internal static readonly string Clock = Png("clock");
            internal static readonly string Bug = Png("bug");

            // Returns a PNG data URI, or null if the resource is missing (the menu then shows no icon).
            private static string Png(string name)
            {
                try
                {
                    using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("MP3toMP4.icons." + name + ".png"))
                    {
                        if (s == null)
                            return null;
                        var ms = new MemoryStream();
                        s.CopyTo(ms);
                        return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
                    }
                }
                catch
                {
                    return null;
                }
            }
        }
    }
}
