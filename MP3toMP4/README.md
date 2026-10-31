# MP3toMP4

When a ChilloutVR video player plays **audio only** (for example an MP3 link from a private server, a web radio
stream or YouTube in audio-only mode), the screen normally stays black and nobody can tell what is playing.
MP3toMP4 shows the following on the video player screen instead:

- an **MP3 icon**
- a **live audio visualizer**, calculated from the actual sound
- the **title** or file name (URL encoding like `%20` is turned back into plain text)
- a **progress bar** and the playback **time**

As soon as a real video is playing, the mod steps aside and the video is shown as usual.

**Local only:** only you see the overlay. Nothing changes for other players, the world or the server.
The mod makes **no network requests** and collects or sends **no data**.

## Requirements

- ChilloutVR with **MelonLoader** (tested with MelonLoader 0.7.3).
- No other mods, programs, avatar setup or hardware needed.
- Optional: **UI Expansion Kit**. If installed, the settings also show up in **Main Menu → Mod Settings**
  with a dropdown and sliders. Without it, everything still works through the Quick Menu.

## Installation

1. Download `MP3toMP4.dll`.
2. Put it into `ChilloutVR\Mods`.
3. Start the game.

That's it. The mod is a **single DLL**: the MP3 icon is embedded in it, so no extra files are needed.

## Usage

Play any audio-only URL in a video player. After about one second without a video picture, the overlay appears.
When playback stops or ends, the screen is cleared to black.

### Settings

All settings can be changed in game and apply immediately:

- **Quick Menu → Misc tab → MP3toMP4**: toggles, sliders, a selection list for the display mode and a
  **Pick Color** button that opens ChilloutVR's own color picker.
- **Main Menu → Mod Settings** (only with UI Expansion Kit): `Display Mode` is a dropdown, all numbers are sliders.

Changes are saved automatically to `UserData/MelonPreferences.cfg`, category `MP3toMP4`:

| Entry | Default | Meaning |
|---|---|---|
| `Enabled` | true | Master switch. |
| `DisplayMode` | Both | `Both` = icon + visualizer, `Icon` = icon only, `Visualizer` = bars only. |
| `ShowTitle` | true | Title/file name at the top (scrolls if too long). |
| `ShowProgress` | true | Progress bar and time at the bottom. |
| `BarCount` | 48 | Number of bars (8 to 128). |
| `Sensitivity` | 1.0 | How strongly the bars react. |
| `AutoGain` | true | Levels out quiet players. |
| `AccentRed` / `AccentGreen` / `AccentBlue` | 217 / 108 / 192 | Bar color as RGB, 0 to 255 each (default matches the icon). |
| `RenderWidth` | 640 | Internal resolution (256 to 1920). Higher = sharper, but more CPU. |
| `MaxFps` | 30 | Visualizer frame rate (5 to 90). |
| `DetectDelay` | 1.0 | Seconds without a picture before the mod takes over. |
| `ReplaceThumbnail` | false | Take over even when ChilloutVR shows its own thumbnail (e.g. YouTube audio-only). |
| `IconPulse` | true | Icon and glow pulse with the bass. |

### Custom icon (optional)

To use your own image instead of the built-in MP3 icon, save a PNG as `ChilloutVR\UserData\MP3toMP4\icon.png`
and restart the game. A square image with a transparent background works best.

## Known limitations

- The on-screen font only supports basic Latin characters (ASCII). German umlauts are shown as ae/oe/ue;
  titles in other scripts (e.g. Japanese) appear as `?`.
- If a video player outputs its sound without a Unity AudioSource, the bars stay flat; icon, title and time still work.
- The title comes from ChilloutVR (yt-dlp metadata). If there is none, the file name from the URL is used.

## How it works

1. **Hook (`Main.cs`)**: Harmony postfix on `CVRVideoPlayer.Update()`. That is where ChilloutVR copies the video
   picture into the world's screen texture every frame. Audio-only media has no picture, so the screen stays black.
2. **Detection (`PlayerOverlay.cs`)**: If the player is playing for longer than `DetectDelay` seconds without a
   video picture, the mod takes over. As soon as a real picture arrives, it steps aside again.
3. **Audio capture (`AudioTap.cs`)**: Both video player backends (AVPro and LibVLC) output sound through Unity
   AudioSources. A small component reads the finished samples from them (without modifying them).
4. **Visualizer (`Spectrum.cs`)**: 2048-point FFT, logarithmic bands (40 Hz to 16 kHz), auto gain, smoothing and peak caps.
5. **Drawing (`PixelCanvas.cs`, `Font5x7.cs`)**: Rendered on the CPU into a small texture that is copied onto the
   video player screen. No shaders and no dependency on world assets.
6. **Menus (`QuickMenu.cs`, `UixSettings.cs`)**: Quick Menu page built with ChilloutVR's own built-in menu system,
   plus slider ranges for UI Expansion Kit's "Mod Settings" page when UI Expansion Kit is installed.

## Building from source

Requirement: .NET SDK 8 (or newer).

```
dotnet build -c Release
```

`MP3toMP4.dll` is written straight into `ChilloutVR\Mods`. If the game is installed somewhere other than
`D:\SteamLibrary\steamapps\common\ChilloutVR`:
`dotnet build -c Release -p:CVRPATH="C:\Path\To\ChilloutVR"`.

The project references the game's own DLLs, MelonLoader and UI Expansion Kit (for the optional settings integration).

## Credits

- The 5×7 pixel font in `Font5x7.cs` is based on the classic `glcdfont` from the
  [Adafruit GFX Library](https://github.com/adafruit/Adafruit-GFX-Library) (BSD license).
