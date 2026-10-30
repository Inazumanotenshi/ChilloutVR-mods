using System;
using UIExpansionKit.API;

namespace MP3toMP4
{
    // UI Expansion Kit ("Mod Settings" in the main menu): register value ranges so numbers show up
    // as sliders instead of plain number fields. DisplayMode is an enum and becomes a dropdown there
    // automatically. Only used when UI Expansion Kit is installed.
    internal static class UixSettings
    {
        internal static void Register()
        {
            ExpansionKitApi.RegisterSettingsRange(MP3toMP4Mod.AccentR, (0d, 255d), 1d);
            ExpansionKitApi.RegisterSettingsRange(MP3toMP4Mod.AccentG, (0d, 255d), 1d);
            ExpansionKitApi.RegisterSettingsRange(MP3toMP4Mod.AccentB, (0d, 255d), 1d);
            ExpansionKitApi.RegisterSettingsRange(MP3toMP4Mod.BarCount, (8d, 128d), 1d);
            ExpansionKitApi.RegisterSettingsRange(MP3toMP4Mod.Sensitivity, (0.3d, 3d), 0.05d);
            ExpansionKitApi.RegisterSettingsRange(MP3toMP4Mod.RenderWidth, (256d, 1920d), 32d);
            ExpansionKitApi.RegisterSettingsRange(MP3toMP4Mod.MaxFps, (5d, 90d), 1d);
            ExpansionKitApi.RegisterSettingsRange(MP3toMP4Mod.DetectDelay, (0d, 5d), 0.1d);
        }
    }
}
