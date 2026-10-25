# MP3toMP4

ChilloutVR mod (MelonLoader): when the video player plays audio only (e.g. an MP3 from a private server,
a web radio stream or YouTube in audio-only mode), the screen normally stays black. This mod shows an
**MP3 icon**, an **audio visualizer** (calculated from the actual sound), the **title or file name** and
**progress/time** instead.

Local only: only people who have the mod see the overlay. Nothing changes for everyone else.

## Building

Requirement: .NET SDK 8 (or newer).

```
cd D:\Unity\ModDev\MP3toMP4
dotnet build -c Release
```

(or just double-click `build.bat`)

`MP3toMP4.dll` is written straight into `ChilloutVR\Mods`. If the game is installed elsewhere:
`dotnet build -c Release -p:CVRPATH="C:\Path\To\ChilloutVR"`.

`src\mp3.png` is **compiled into the DLL** as an embedded resource, so the DLL works without any extra files.
Optionally you can put your own image at `ChilloutVR\UserData\MP3toMP4\icon.png`; it takes priority.

## How it works

1. **Hook (`Main.cs`)**: Harmony postfix on `CVRVideoPlayer.Update()`. That is where CVR blits
   `VideoPlayer.GetVideoTexture()` into the world's `ProjectionTexture` every frame. Audio-only media has
   no texture (or a 0×0 video size), which is why the screen stays black otherwise.
2. **Detection (`PlayerOverlay.cs`)**: If the player is playing (`PlayerState.Playing`) for longer than
   `DetectDelay` seconds without a video frame, the mod takes over. As soon as a real picture arrives, it
   steps aside again. On stop or end, the screen is cleared to black.
3. **Audio capture (`AudioTap.cs`)**: AVPro and LibVLC both output sound through Unity AudioSources
   (the `Channels` of their `_audioManager`). A small component with `OnAudioFilterRead` is added to those
   GameObjects and reads the finished samples (without modifying them).
4. **Visualizer (`Spectrum.cs`)**: 2048-point FFT, logarithmic bands (40 Hz to 16 kHz), +3 dB/octave
   compensation, auto gain, smoothing and peak caps.
5. **Drawing (`PixelCanvas.cs`, `Font5x7.cs`)**: Rendered on the CPU into a small Texture2D (640 px wide by
   default, same aspect ratio as the world's screen), which is then blitted into the `ProjectionTexture`.
   No shaders are needed and there is no dependency on world assets.

## Settings (`UserData/MelonPreferences.cfg`, category `MP3toMP4`)

| Entry | Default | Meaning |
|---|---|---|
| `Enabled` | true | Master switch. |
| `DisplayMode` | Both | `Both` = icon + visualizer, `Icon` = icon only, `Visualizer` = bars only. |
| `ShowTitle` | true | Title/file name at the top (scrolls if too long). |
| `ShowProgress` | true | Progress bar and time at the bottom. |
| `BarCount` | 48 | Number of bars (8 to 128). |
| `Sensitivity` | 1.0 | How strongly the bars react. |
| `AutoGain` | true | Levels out quiet players. |
| `AccentColor` | #D96CC0 | Bar color (matches the icon). |
| `RenderWidth` | 640 | Internal resolution (256 to 1920). |
| `MaxFps` | 30 | Visualizer frame rate (5 to 90). |
| `DetectDelay` | 1.0 | Seconds without a picture before the mod takes over. |
| `ReplaceThumbnail` | false | Take over even when CVR shows its own thumbnail (e.g. YouTube audio-only). |
| `IconPulse` | true | Icon and glow pulse with the bass. |

## Known limitations

- The pixel font only supports ASCII (German umlauts become ae/oe/ue). Japanese and similar titles show as `?`.
- If the sound does not go through a Unity AudioSource for some reason, the bars stay flat; icon, title
  and time still work.
- The title comes from CVR (yt-dlp metadata). If there is none, the file name from the URL is used.
