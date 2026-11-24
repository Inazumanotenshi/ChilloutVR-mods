using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using ABI.CCK.Components;
using ABI_RC.VideoPlayer.Scripts;
using ABI_RC.VideoPlayer.Scripts.Players;
using UnityEngine;

namespace MP3toMP4
{
    internal static class OverlayManager
    {
        private static readonly Dictionary<int, PlayerOverlay> Overlays = new Dictionary<int, PlayerOverlay>();
        private static readonly List<int> ToRemove = new List<int>();
        private static float _nextPurge;

        internal static void OnPlayerUpdate(CVRVideoPlayer player)
        {
            if (player == null)
                return;
            int id = player.GetInstanceID();
            PlayerOverlay overlay;
            if (!Overlays.TryGetValue(id, out overlay))
            {
                overlay = new PlayerOverlay(player);
                Overlays[id] = overlay;
            }
            if (overlay.Faulted)
                return;

            // Error brake per video player: an error never reaches the game. After a few errors the
            // overlay switches itself off for this player; the video player itself keeps working normally.
            try
            {
                overlay.Update();
            }
            catch (Exception e)
            {
                overlay.OnError(e);
            }
        }

        internal static void Tick()
        {
            if (Time.unscaledTime < _nextPurge)
                return;
            _nextPurge = Time.unscaledTime + 5f;

            ToRemove.Clear();
            foreach (var pair in Overlays)
            {
                if (pair.Value.Player == null)
                    ToRemove.Add(pair.Key);
            }
            foreach (int id in ToRemove)
            {
                try
                {
                    Overlays[id].Dispose();
                }
                catch
                {
                    // the player is gone anyway
                }
                Overlays.Remove(id);
                ThumbnailUrls.Remove(id);
            }
        }

        private static readonly Dictionary<int, string> ThumbnailUrls = new Dictionary<int, string>();

        internal static void OnThumbnailShown(CVRVideoPlayer player)
        {
            ThumbnailUrls[player.GetInstanceID()] = UrlKey(player);
        }

        // The URL set by the user (synced), otherwise the player's URL.
        internal static string UrlKey(CVRVideoPlayer player)
        {
            try
            {
                string url = player.CurrentVideoUrl;
                if (!string.IsNullOrEmpty(url))
                    return url;
                IVideoPlayerPlayer vp = player.VideoPlayer;
                if (vp != null && vp.Info != null && vp.Info.VideoMetaData != null)
                    return vp.Info.VideoMetaData.GetUrl() ?? "";
            }
            catch
            {
                // ignore
            }
            return "";
        }

        internal static string ThumbnailUrlFor(CVRVideoPlayer player)
        {
            string url;
            return ThumbnailUrls.TryGetValue(player.GetInstanceID(), out url) ? url : null;
        }

        internal static void SettingsChanged()
        {
            foreach (var overlay in Overlays.Values)
            {
                overlay.LayoutDirty = true;
                overlay.CheckRenderer();
                if (!MP3toMP4Mod.Enabled.Value)
                    overlay.SafeDeactivate(true);
            }
        }
    }

    internal sealed class PlayerOverlay
    {
        internal readonly CVRVideoPlayer Player;
        internal bool LayoutDirty = true;

        // Set after repeated errors: the overlay stays off for this video player until the world is reloaded.
        internal bool Faulted;
        private int _errorCount;
        private const int MaxErrors = 3;

        // Limits so the mod never attaches to more than a handful of audio sources.
        private const int MaxTaps = 16;
        private const int MaxFallbackSources = 8;
        private bool _channelWarningLogged;

        // Timing (debug mode only): measures how long drawing takes and logs it every few seconds.
        // The frame rate is never changed automatically, only by the Max FPS setting.
        private const int WarmupDraws = 15;
        private const float TimingLogInterval = 5f;
        private readonly System.Diagnostics.Stopwatch _drawWatch = new System.Diagnostics.Stopwatch();
        private int _drawCount;
        private double _timingSum;
        private double _timingMax;
        private int _timingSamples;
        private float _nextTimingLog;

        private string _lastDebug;
        private bool _debugAudioLogged;

        private bool _active;
        private float _activeSince;
        private bool _hadAudio;
        private float _audioOnlySince = -1f;

        // Drawing: on the GPU when possible (fast), otherwise on the CPU (old path, fallback).
        private bool _gpuDecided;
        private bool _useGpu;
        private GpuCanvas _gpu;
        private PixelCanvas _canvas;
        private int _cw, _ch;
        private Texture2D _iconTex;
        private readonly TextTexture _titleTex = new TextTexture();
        private readonly TextTexture _timeTex = new TextTexture();
        private readonly Spectrum _spectrum = new Spectrum();
        private readonly List<AudioTap> _taps = new List<AudioTap>();
        private readonly List<AudioSource> _sourceScratch = new List<AudioSource>();
        private readonly float[] _mix = new float[Spectrum.FftSize];
        private readonly float[] _tmp = new float[Spectrum.FftSize];
        private int _sampleRate = 48000;

        private float _nextTapScan;
        private float _nextDraw;
        private float _lastDrawTime;
        private float _nextTitleRefresh;
        private string _title = "";

        // Layout
        private Color32[] _background;
        private PmImage _iconPrepared;
        private float _iconCx, _iconCy, _iconSize, _iconDrawW;
        private float[] _glowMask;
        private int _glowX, _glowY, _glowW, _glowH;
        private int _barsX0, _barsX1, _barsY0, _barsMaxH, _reflectMaxH;
        private int _titleY, _textScale, _titleScale, _titleX0, _titleX1;
        private int _progY, _progX0, _progX1, _progThick, _timeY;
        private bool _showIcon, _showBars;
        private Color32 _accent, _accentLight, _textColor, _dimText;

        internal PlayerOverlay(CVRVideoPlayer player)
        {
            Player = player;
        }

        internal void Dispose()
        {
            SetTapsActive(false);
            if (_canvas != null)
            {
                _canvas.Destroy();
                _canvas = null;
            }
            if (_gpu != null)
            {
                _gpu.Destroy();
                _gpu = null;
            }
            _titleTex.Destroy();
            _timeTex.Destroy();
        }

        // Called when settings change: if Hardware Acceleration was switched, drop the old renderer.
        internal void CheckRenderer()
        {
            if (!_gpuDecided)
                return;
            bool wantGpu = MP3toMP4Mod.HardwareAcceleration.Value && GpuCanvas.Available;
            if (wantGpu == _useGpu)
                return;
            _gpuDecided = false;
            if (_gpu != null)
            {
                _gpu.Destroy();
                _gpu = null;
            }
            if (_canvas != null)
            {
                _canvas.Destroy();
                _canvas = null;
            }
        }

        internal void OnError(Exception e)
        {
            _errorCount++;
            string name = SafePlayerName();
            if (_errorCount == 1)
                MP3toMP4Mod.Logger.Warning("Error in overlay for video player '" + name + "' (the game is not affected): " + e);

            if (_errorCount >= MaxErrors)
            {
                Faulted = true;
                SafeDeactivate(true);
                MP3toMP4Mod.Logger.Warning("Overlay switched off for video player '" + name + "' after " + _errorCount +
                                           " errors. The video player itself keeps working; reloading the world resets this.");
            }
        }

        // Deactivate that can never throw (used from error handling and settings changes).
        internal void SafeDeactivate(bool clear)
        {
            try
            {
                Deactivate(clear);
            }
            catch
            {
                _active = false;
            }
        }

        private string SafePlayerName()
        {
            try
            {
                return Player != null ? Player.gameObject.name : "?";
            }
            catch
            {
                return "?";
            }
        }

        internal void Update()
        {
            RenderTexture rt = Player.ProjectionTexture;
            IVideoPlayerPlayer vp = Player.VideoPlayer;
            if (rt == null || vp == null)
            {
                Debug(rt == null ? "no screen texture (ProjectionTexture) set by the world" : "no video player backend yet");
                Deactivate(false);
                return;
            }

            IVideoPlayerInfo info = vp.Info;
            if (info == null)
            {
                Debug("video player has no info yet");
                Deactivate(true);
                return;
            }

            VideoPlayerUtils.PlayerState state = info.GetState();
            bool running = state == VideoPlayerUtils.PlayerState.Playing
                           || state == VideoPlayerUtils.PlayerState.Paused
                           || state == VideoPlayerUtils.PlayerState.Buffering
                           || state == VideoPlayerUtils.PlayerState.Seeking;
            if (!running)
            {
                Debug("state " + state + ", nothing to do");
                _audioOnlySince = -1f;
                Deactivate(true);
                return;
            }

            IVideoMetaData meta = info.VideoMetaData;
            bool hasVideo;
            int width = 0, height = 0;
            bool hasTexture = false;
            try
            {
                if (meta != null)
                {
                    width = meta.GetVideoWidth();
                    height = meta.GetVideoHeight();
                }
                hasTexture = vp.GetVideoTexture() != null;
                hasVideo = meta != null && width > 0 && height > 0 && hasTexture;
            }
            catch (Exception e)
            {
                // Player not ready yet -> when in doubt, leave it alone.
                hasVideo = true;
                Debug("state " + state + ", could not read the video size (" + e.GetType().Name + "), leaving it alone");
            }
            if (hasVideo)
            {
                // Real video: CVR already blitted the frame this update, stay out of the way.
                Debug("state " + state + ", real video " + width + "x" + height + ", not taking over");
                _audioOnlySince = -1f;
                Deactivate(false);
                return;
            }

            float now = Time.unscaledTime;
            if (!_active)
            {
                if (state != VideoPlayerUtils.PlayerState.Playing)
                {
                    Debug("state " + state + ", no picture (" + width + "x" + height + ", texture " + (hasTexture ? "yes" : "no") + "), waiting for Playing");
                    return;
                }
                if (!MP3toMP4Mod.ReplaceThumbnail.Value && HasThumbnail(meta))
                {
                    Debug("CVR shows its own thumbnail and Replace Thumbnail is off, not taking over");
                    return;
                }
                if (_audioOnlySince < 0f)
                    _audioOnlySince = now;
                if (now - _audioOnlySince < Mathf.Max(0f, MP3toMP4Mod.DetectDelay.Value))
                {
                    Debug("playing without picture (" + width + "x" + height + ", texture " + (hasTexture ? "yes" : "no") + "), waiting for Detect Delay");
                    return;
                }
                Activate();
                Debug("overlay active");
            }

            Render(rt, vp, info, meta, state == VideoPlayerUtils.PlayerState.Playing, now);
        }

        private void Activate()
        {
            _active = true;
            _activeSince = Time.unscaledTime;
            _hadAudio = false;
            _debugAudioLogged = false;
            _drawCount = 0;
            _timingSum = 0;
            _timingMax = 0;
            _timingSamples = 0;
            _nextTimingLog = 0f;
            _spectrum.Reset();
            // Attach to the audio sources only after playback has been running stable for a moment.
            _nextTapScan = _activeSince + 0.5f;
            _nextDraw = 0f;
            _lastDrawTime = 0f;
            _nextTitleRefresh = 0f;
            _sampleRate = AudioSettings.outputSampleRate;
            MP3toMP4Mod.Logger.Msg("Audio-only stream detected, showing overlay on video player '" + Player.gameObject.name + "'.");
        }

        internal void Deactivate(bool clear)
        {
            if (!_active)
                return;
            _active = false;
            _spectrum.Reset();
            SetTapsActive(false);
            if (clear && Player != null)
            {
                RenderTexture rt = Player.ProjectionTexture;
                if (rt != null && rt.IsCreated())
                    Graphics.Blit(Texture2D.blackTexture, rt);
            }
        }

        // Did CVR show a thumbnail for exactly this URL (YouTube etc. in audio-only mode)?
        private bool HasThumbnail(IVideoMetaData meta)
        {
            if (meta == null)
                return false;
            string shownFor = OverlayManager.ThumbnailUrlFor(Player);
            if (string.IsNullOrEmpty(shownFor))
                return false;
            return shownFor == OverlayManager.UrlKey(Player);
        }

        // ---------------------------------------------------------------- Rendering

        private void Render(RenderTexture rt, IVideoPlayerPlayer vp, IVideoPlayerInfo info, IVideoMetaData meta, bool playing, float now)
        {
            EnsureCanvas(rt);

            // The world's screen texture only exists on the GPU once something was drawn into it. If a world only
            // ever plays audio (e.g. an MP3 on autoplay), CVR never draws into it, so create it ourselves.
            if (!rt.IsCreated())
            {
                try
                {
                    rt.Create();
                }
                catch (Exception e)
                {
                    Debug("could not create the screen texture (" + e.GetType().Name + "), waiting");
                    return;
                }
                if (!rt.IsCreated())
                {
                    Debug("screen texture (ProjectionTexture) not created, waiting");
                    return;
                }
                Debug("screen texture (ProjectionTexture) was not created yet, created it");
            }

            if (now >= _nextDraw)
            {
                _drawWatch.Reset();
                _drawWatch.Start();

                float dt = _lastDrawTime > 0f ? Mathf.Clamp(now - _lastDrawTime, 0.001f, 0.25f) : 1f / 30f;
                _lastDrawTime = now;
                int fps = Mathf.Clamp(MP3toMP4Mod.MaxFps.Value, 5, 90);
                _nextDraw = now + 1f / fps;

                // Looking for audio sources is rare: only while no sound arrives, and then at most every 2 seconds.
                if (now >= _nextTapScan)
                {
                    _nextTapScan = now + (_hadAudio ? 10f : 2f);
                    if (!_hadAudio)
                        ScanTaps(vp);
                }

                bool hasAudio = playing && GatherSamples();
                if (hasAudio)
                {
                    _hadAudio = true;
                    if (!_debugAudioLogged && MP3toMP4Mod.DebugLog.Value)
                    {
                        _debugAudioLogged = true;
                        MP3toMP4Mod.Logger.Msg("[Debug] '" + SafePlayerName() + "': sound arrives, visualizer is fed");
                    }
                }
                _spectrum.Process(hasAudio ? _mix : null, _sampleRate, dt);

                if (now >= _nextTitleRefresh)
                {
                    _nextTitleRefresh = now + 2f;
                    _title = Font5x7.Sanitize(ResolveTitle(meta));
                }

                if (_useGpu)
                {
                    DrawGpu(info, meta, now);
                }
                else
                {
                    Draw(info, meta, now);
                    _canvas.Upload();
                }

                _drawWatch.Stop();
                LogTiming(_drawWatch.Elapsed.TotalMilliseconds, now, fps);
            }

            Graphics.Blit(_useGpu ? (Texture)_gpu.Target : _canvas.Texture, rt);
        }

        // Debug mode only: every few seconds log the average and maximum drawing time.
        // The first draws after the overlay starts are skipped (code compilation, icon loading, first upload).
        private void LogTiming(double ms, float now, int fps)
        {
            _drawCount++;
            if (!MP3toMP4Mod.DebugLog.Value || _drawCount <= WarmupDraws)
                return;

            _timingSum += ms;
            _timingSamples++;
            if (ms > _timingMax)
                _timingMax = ms;

            if (_nextTimingLog <= 0f)
                _nextTimingLog = now + TimingLogInterval;
            if (now < _nextTimingLog)
                return;

            double avg = _timingSum / Math.Max(1, _timingSamples);
            MP3toMP4Mod.Logger.Msg("[Debug] '" + SafePlayerName() + "': drawing takes " + avg.ToString("0.00") + " ms on average (max " +
                                   _timingMax.ToString("0.00") + " ms) at " + fps + " FPS, about " + (avg * fps / 1000.0 * 100.0).ToString("0.0") +
                                   "% of one CPU core");
            _timingSum = 0;
            _timingMax = 0;
            _timingSamples = 0;
            _nextTimingLog = now + TimingLogInterval;
        }

        private void EnsureCanvas(RenderTexture rt)
        {
            int w = Mathf.Clamp(MP3toMP4Mod.RenderWidth.Value, 256, 1920);
            int h = Mathf.Max(64, Mathf.RoundToInt(w * (rt.height / (float)Mathf.Max(1, rt.width))));
            if (h > 2048)
            {
                w = Mathf.Max(64, Mathf.RoundToInt(w * 2048f / h));
                h = 2048;
            }

            if (!_gpuDecided)
            {
                _gpuDecided = true;
                _useGpu = MP3toMP4Mod.HardwareAcceleration.Value && GpuCanvas.Available;
                if (MP3toMP4Mod.DebugLog.Value)
                    MP3toMP4Mod.Logger.Msg("[Debug] '" + SafePlayerName() + "': drawing on the " + (_useGpu ? "GPU" : "CPU (fallback)"));
            }

            if (_useGpu)
            {
                if (_gpu == null || _gpu.W != w || _gpu.H != h || _gpu.Target == null)
                {
                    if (_gpu != null)
                        _gpu.Destroy();
                    _gpu = new GpuCanvas(w, h);
                    LayoutDirty = true;
                }
            }
            else if (_canvas == null || _canvas.W != w || _canvas.H != h)
            {
                if (_canvas != null)
                    _canvas.Destroy();
                _canvas = new PixelCanvas(w, h);
                LayoutDirty = true;
            }
            _cw = w;
            _ch = h;

            if (LayoutDirty)
            {
                LayoutDirty = false;
                BuildLayout();
            }
        }

        private void BuildLayout()
        {
            int W = _cw, H = _ch;
            float s = Mathf.Min(H / 360f, W / 640f);
            DisplayMode mode = MP3toMP4Mod.CurrentMode;
            bool showTitle = MP3toMP4Mod.ShowTitle.Value;
            bool showProgress = MP3toMP4Mod.ShowProgress.Value;

            _accent = MP3toMP4Mod.Accent;
            _accentLight = Color32.Lerp(_accent, new Color32(255, 255, 255, 255), 0.55f);
            _textColor = new Color32(235, 235, 240, 255);
            _dimText = new Color32(160, 160, 172, 255);

            _textScale = Mathf.Max(1, Mathf.RoundToInt(2f * s));
            _titleScale = Mathf.Max(1, Mathf.RoundToInt(3f * s));
            int textH = 7 * _textScale;

            _iconTex = _useGpu ? IconStore.Texture : null;
            _showIcon = mode != DisplayMode.Visualizer && (_useGpu ? _iconTex != null : IconStore.Icon != null);
            _showBars = mode != DisplayMode.Icon;

            // Title at the top (leave one row for descenders)
            _titleY = H - Mathf.RoundToInt(0.05f * H) - 7 * _titleScale;
            _titleX0 = Mathf.RoundToInt(0.06f * W);
            _titleX1 = W - _titleX0;

            // Progress at the bottom
            _timeY = Mathf.RoundToInt(0.025f * H);
            _progY = _timeY + textH + Mathf.RoundToInt(6 * s);
            _progThick = Mathf.Max(2, Mathf.RoundToInt(3 * s));
            _progX0 = Mathf.RoundToInt(0.12f * W);
            _progX1 = W - _progX0;

            int bottom = showProgress ? _progY + _progThick + Mathf.RoundToInt(0.05f * H) : Mathf.RoundToInt(0.08f * H);
            int top = showTitle ? _titleY - _titleScale - Mathf.RoundToInt(0.04f * H) : H - Mathf.RoundToInt(0.06f * H);

            _barsX0 = Mathf.RoundToInt(0.06f * W);
            _barsX1 = W - _barsX0;
            _barsY0 = bottom;
            _reflectMaxH = Mathf.Max(0, bottom - (showProgress ? _progY + _progThick + 2 : 0));

            if (mode == DisplayMode.Both)
            {
                int avail = top - bottom;
                _barsMaxH = Mathf.RoundToInt(avail * 0.33f);
                float iconTop = top;
                float iconBottom = bottom + _barsMaxH + 0.03f * H;
                _iconSize = Mathf.Max(8f, (iconTop - iconBottom));
                _iconCy = (iconTop + iconBottom) * 0.5f;
            }
            else if (mode == DisplayMode.Icon)
            {
                _barsMaxH = 0;
                _iconSize = Mathf.Max(8f, (top - bottom) * 0.92f);
                _iconCy = (top + bottom) * 0.5f;
            }
            else
            {
                _barsMaxH = Mathf.Max(8, top - bottom);
                _iconSize = 0f;
                _iconCy = (top + bottom) * 0.5f;
            }
            _iconCx = W * 0.5f;

            // GPU: the texture has mipmaps, only the drawn width is needed. Text textures are rebuilt on demand.
            if (_useGpu)
            {
                if (_showIcon)
                    _iconDrawW = _iconTex.width >= _iconTex.height ? _iconSize : _iconSize * _iconTex.width / _iconTex.height;
                _titleTex.Text = null;
                _timeTex.Text = null;
                return;
            }

            // CPU fallback: pre-scale the icon once in high quality (slightly larger for pulsing).
            _iconPrepared = null;
            if (_showIcon)
            {
                PmImage icon = IconStore.Icon;
                float maxDim = _iconSize * 1.06f;
                int iw, ih;
                if (icon.W >= icon.H)
                {
                    iw = Mathf.RoundToInt(maxDim);
                    ih = Mathf.Max(1, Mathf.RoundToInt(maxDim * icon.H / icon.W));
                }
                else
                {
                    ih = Mathf.RoundToInt(maxDim);
                    iw = Mathf.Max(1, Mathf.RoundToInt(maxDim * icon.W / icon.H));
                }
                _iconPrepared = icon.Resize(iw, ih);
                // The longer side should be _iconSize; DrawImage expects the width.
                _iconDrawW = icon.W >= icon.H ? _iconSize : _iconSize * icon.W / icon.H;
            }

            // Background: dark gradient
            _background = new Color32[W * H];
            var bgTop = new Color32(26, 23, 34, 255);
            var bgBottom = new Color32(9, 9, 13, 255);
            for (int y = 0; y < H; y++)
            {
                Color32 c = Color32.Lerp(bgBottom, bgTop, y / (float)(H - 1));
                int row = y * W;
                for (int x = 0; x < W; x++)
                    _background[row + x] = c;
            }

            // Glow behind the icon or the bars
            float glowCy = _showIcon ? _iconCy : _barsY0 + _barsMaxH * 0.4f;
            float radius = Mathf.Max(16f, (_showIcon ? _iconSize * 0.9f : _barsMaxH * 0.9f));
            _glowW = Mathf.CeilToInt(radius * 2f);
            _glowH = _glowW;
            _glowX = Mathf.RoundToInt(_iconCx - radius);
            _glowY = Mathf.RoundToInt(glowCy - radius);
            _glowMask = new float[_glowW * _glowH];
            for (int y = 0; y < _glowH; y++)
            {
                for (int x = 0; x < _glowW; x++)
                {
                    float dx = (x + 0.5f - radius) / radius;
                    float dy = (y + 0.5f - radius) / radius;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float k = Mathf.Clamp01(1f - d);
                    _glowMask[y * _glowW + x] = k * k;
                }
            }
        }

        private void Draw(IVideoPlayerInfo info, IVideoMetaData meta, float now)
        {
            PixelCanvas c = _canvas;
            c.CopyFrom(_background);

            float bass = MP3toMP4Mod.IconPulse.Value ? Mathf.Clamp01(_spectrum.Bass) : 0f;
            c.AddGlow(_glowMask, _glowX, _glowY, _glowW, _glowH, _accent, 0.10f + 0.40f * bass);

            // Icon
            if (_showIcon && _iconPrepared != null)
            {
                float scale = 1f + 0.06f * bass;
                c.DrawImage(_iconPrepared, _iconCx, _iconCy, _iconDrawW * scale);
            }

            // Bars
            if (_showBars && _barsMaxH > 0)
            {
                float[] bands = _spectrum.Bands;
                float[] peaks = _spectrum.Peaks;
                int n = bands.Length;
                if (n > 0)
                {
                    float slot = (_barsX1 - _barsX0) / (float)n;
                    int barW = Mathf.Max(1, Mathf.FloorToInt(slot * 0.68f));
                    int capH = Mathf.Max(1, Mathf.RoundToInt(_barsMaxH / 60f));
                    int minH = Mathf.Max(1, capH);
                    for (int i = 0; i < n; i++)
                    {
                        int x = _barsX0 + Mathf.RoundToInt(i * slot + (slot - barW) * 0.5f);
                        int h = Mathf.Max(minH, Mathf.RoundToInt(bands[i] * _barsMaxH));
                        c.FillRectGradient(x, _barsY0, barW, h, _accent, _accentLight, _barsY0, _barsMaxH);

                        int peakY = _barsY0 + Mathf.RoundToInt(peaks[i] * _barsMaxH) + capH;
                        if (peaks[i] > 0.02f)
                            c.FillRect(x, peakY, barW, capH, _accentLight, 0.9f);

                        // Reflection below
                        if (_reflectMaxH > 2)
                        {
                            int rh = Mathf.Min(_reflectMaxH - 2, Mathf.RoundToInt(h * 0.3f));
                            if (rh > 0)
                                c.FillRect(x, _barsY0 - 2 - rh, barW, rh, _accent, 0.22f);
                        }
                    }
                }
            }

            // Title
            if (MP3toMP4Mod.ShowTitle.Value)
            {
                string title = string.IsNullOrEmpty(_title) ? "Audio" : _title;
                int tw = PixelCanvas.TextWidth(title, _titleScale);
                int areaW = _titleX1 - _titleX0;
                if (tw <= areaW)
                {
                    c.DrawText(title, _titleX0 + (areaW - tw) / 2, _titleY, _titleScale, _textColor, _titleX0, _titleX1);
                }
                else
                {
                    // Scrolling text
                    int gap = 30 * _titleScale;
                    int period = tw + gap;
                    int offset = Mathf.FloorToInt(now * 20f * _titleScale) % period;
                    int x = _titleX0 - offset;
                    c.DrawText(title, x, _titleY, _titleScale, _textColor, _titleX0, _titleX1);
                    c.DrawText(title, x + period, _titleY, _titleScale, _textColor, _titleX0, _titleX1);
                }
            }

            // Progress + time
            if (MP3toMP4Mod.ShowProgress.Value)
            {
                double time = 0, duration = 0;
                bool live = false;
                try
                {
                    time = info.Time;
                    if (meta != null)
                    {
                        duration = meta.GetDuration();
                        live = meta.IsLivestream;
                    }
                }
                catch
                {
                    // some players throw while loading
                }

                bool hasDuration = !live && duration > 0.5 && !double.IsInfinity(duration) && !double.IsNaN(duration);
                c.FillRect(_progX0, _progY, _progX1 - _progX0, _progThick, _accent, 0.25f);
                if (hasDuration)
                {
                    float p = Mathf.Clamp01((float)(time / duration));
                    c.FillRect(_progX0, _progY, Mathf.RoundToInt((_progX1 - _progX0) * p), _progThick, _accentLight);
                }

                string timeText = live ? "LIVE  " + FormatTime(time) : hasDuration ? FormatTime(time) + " / " + FormatTime(duration) : FormatTime(time);
                int tw = PixelCanvas.TextWidth(timeText, _textScale);
                c.DrawText(timeText, (c.W - tw) / 2, _timeY, _textScale, _dimText, 0, c.W);
            }
        }

        // Same picture as Draw(), but drawn by the GPU: a few dozen quads instead of ~230,000 CPU pixels.
        private void DrawGpu(IVideoPlayerInfo info, IVideoMetaData meta, float now)
        {
            GpuCanvas g = _gpu;
            if (g == null || !g.Begin())
                return;
            try
            {
                int W = g.W, H = g.H;

                // Background: dark gradient
                g.Gradient(0, 0, W, H, g.Col(new Color32(9, 9, 13, 255)), g.Col(new Color32(26, 23, 34, 255)));

                float bass = MP3toMP4Mod.IconPulse.Value ? Mathf.Clamp01(_spectrum.Bass) : 0f;

                // Glow behind the icon or the bars
                float glowCy = _showIcon ? _iconCy : _barsY0 + _barsMaxH * 0.4f;
                float radius = Mathf.Max(16f, _showIcon ? _iconSize * 0.9f : _barsMaxH * 0.9f);
                g.Glow(_iconCx, glowCy, radius, g.Col(_accent, Mathf.Clamp01((0.10f + 0.40f * bass) * 1.2f)));

                // Icon (pulses with the bass)
                if (_showIcon && _iconTex != null)
                {
                    float scale = 1f + 0.06f * bass;
                    float w = _iconDrawW * scale;
                    float h = w * _iconTex.height / Mathf.Max(1, _iconTex.width);
                    g.Image(_iconTex, _iconCx, _iconCy, w, h, Color.white);
                }

                // Bars
                if (_showBars && _barsMaxH > 0)
                {
                    float[] bands = _spectrum.Bands;
                    float[] peaks = _spectrum.Peaks;
                    int n = bands.Length;
                    if (n > 0)
                    {
                        float slot = (_barsX1 - _barsX0) / (float)n;
                        int barW = Mathf.Max(1, Mathf.FloorToInt(slot * 0.68f));
                        int capH = Mathf.Max(1, Mathf.RoundToInt(_barsMaxH / 60f));
                        Color bottom = g.Col(_accent);
                        Color cap = g.Col(_accentLight, 0.9f);
                        Color reflection = g.Col(_accent, 0.22f);
                        for (int i = 0; i < n; i++)
                        {
                            int x = _barsX0 + Mathf.RoundToInt(i * slot + (slot - barW) * 0.5f);
                            int h = Mathf.Max(capH, Mathf.RoundToInt(bands[i] * _barsMaxH));
                            // Color depends on the absolute height, like the CPU version.
                            Color top = g.Col(Color32.Lerp(_accent, _accentLight, Mathf.Clamp01(h / (float)_barsMaxH)));
                            g.Gradient(x, _barsY0, barW, h, bottom, top);

                            if (peaks[i] > 0.02f)
                                g.Rect(x, _barsY0 + Mathf.RoundToInt(peaks[i] * _barsMaxH) + capH, barW, capH, cap);

                            if (_reflectMaxH > 2)
                            {
                                int rh = Mathf.Min(_reflectMaxH - 2, Mathf.RoundToInt(h * 0.3f));
                                if (rh > 0)
                                    g.Rect(x, _barsY0 - 2 - rh, barW, rh, reflection);
                            }
                        }
                    }
                }

                // Title (texture rebuilt only when the text changes; scrolling happens via texture coordinates)
                if (MP3toMP4Mod.ShowTitle.Value)
                {
                    string title = string.IsNullOrEmpty(_title) ? "Audio" : _title;
                    _titleTex.Set(title, 30);
                    int sc = _titleScale;
                    int textW = _titleTex.TextPixels * sc;
                    int totalW = _titleTex.TotalPixels * sc;
                    int areaW = _titleX1 - _titleX0;
                    float y = _titleY - sc;
                    float h = 8 * sc;
                    Color color = g.Col(_textColor);
                    if (textW <= areaW)
                    {
                        g.ImageUV(_titleTex.Texture, _titleX0 + (areaW - textW) / 2, y, textW, h,
                                  0f, _titleTex.TextPixels / (float)_titleTex.TotalPixels, color);
                    }
                    else
                    {
                        int offset = Mathf.FloorToInt(now * 20f * sc) % Mathf.Max(1, totalW);
                        // Snap to whole font pixels so the pixel font stays crisp.
                        offset -= offset % sc;
                        float u0 = offset / (float)totalW;
                        g.ImageUV(_titleTex.Texture, _titleX0, y, areaW, h, u0, u0 + areaW / (float)totalW, color);
                    }
                }

                // Progress + time
                if (MP3toMP4Mod.ShowProgress.Value)
                {
                    double time = 0, duration = 0;
                    bool live = false;
                    try
                    {
                        time = info.Time;
                        if (meta != null)
                        {
                            duration = meta.GetDuration();
                            live = meta.IsLivestream;
                        }
                    }
                    catch
                    {
                        // some players throw while loading
                    }

                    bool hasDuration = !live && duration > 0.5 && !double.IsInfinity(duration) && !double.IsNaN(duration);
                    g.Rect(_progX0, _progY, _progX1 - _progX0, _progThick, g.Col(_accent, 0.25f));
                    if (hasDuration)
                    {
                        float p = Mathf.Clamp01((float)(time / duration));
                        g.Rect(_progX0, _progY, Mathf.RoundToInt((_progX1 - _progX0) * p), _progThick, g.Col(_accentLight));
                    }

                    string timeText = live ? "LIVE  " + FormatTime(time) : hasDuration ? FormatTime(time) + " / " + FormatTime(duration) : FormatTime(time);
                    _timeTex.Set(timeText, 0);
                    int ts = _textScale;
                    int tw = _timeTex.TextPixels * ts;
                    g.ImageUV(_timeTex.Texture, (W - tw) / 2, _timeY - ts, tw, 8 * ts, 0f, 1f, g.Col(_dimText));
                }
            }
            finally
            {
                g.End();
            }
        }

        private static string FormatTime(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
                seconds = 0;
            long total = (long)seconds;
            long h = total / 3600, m = (total / 60) % 60, sec = total % 60;
            if (h > 0)
                return h + ":" + m.ToString("00") + ":" + sec.ToString("00");
            return m + ":" + sec.ToString("00");
        }

        private string ResolveTitle(IVideoMetaData meta)
        {
            string title = null;
            string url = null;
            try
            {
                if (meta != null)
                {
                    title = meta.GetTitle();
                    url = meta.GetUrl();
                }
            }
            catch
            {
                // ignore
            }

            if (!string.IsNullOrEmpty(title) && !LooksLikeUrl(title))
                return DecodeText(title);

            if (string.IsNullOrEmpty(url))
            {
                try
                {
                    url = Player.CurrentVideoUrl;
                }
                catch
                {
                    url = null;
                }
            }
            if (string.IsNullOrEmpty(url))
                url = title;
            return FileNameFromUrl(url);
        }

        // Turns URL/HTML encoding back into plain text: "My%20Song%20%C3%A4.mp3" -> "My Song ä.mp3",
        // "Rock &amp; Roll" -> "Rock & Roll". Also handles double encoding like "%2520".
        private static string DecodeText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            for (int pass = 0; pass < 3 && HasPercentEscape(text); pass++)
            {
                string decoded;
                try
                {
                    decoded = Uri.UnescapeDataString(text);
                }
                catch
                {
                    break; // invalid escaping, keep what we have
                }
                if (decoded == text)
                    break;
                text = decoded;
            }

            if (text.IndexOf('&') >= 0 && text.IndexOf(';') > 0)
            {
                try
                {
                    text = System.Net.WebUtility.HtmlDecode(text);
                }
                catch
                {
                    // keep text
                }
            }

            return text.Trim();
        }

        private static bool HasPercentEscape(string s)
        {
            for (int i = 0; i + 2 < s.Length; i++)
            {
                if (s[i] == '%' && IsHex(s[i + 1]) && IsHex(s[i + 2]))
                    return true;
            }
            return false;
        }

        private static bool IsHex(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }

        private static bool LooksLikeUrl(string s)
        {
            return s.IndexOf("://", StringComparison.Ordinal) >= 0;
        }

        private static string FileNameFromUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return "";
            string u = url;
            int cut = u.IndexOfAny(new[] { '?', '#' });
            if (cut >= 0)
                u = u.Substring(0, cut);
            u = u.TrimEnd('/', '\\');
            int slash = Math.Max(u.LastIndexOf('/'), u.LastIndexOf('\\'));
            string name = slash >= 0 ? u.Substring(slash + 1) : u;
            name = DecodeText(name.Replace('+', ' '));
            if (string.IsNullOrEmpty(name))
            {
                try
                {
                    name = new Uri(url).Host;
                }
                catch
                {
                    name = url;
                }
            }
            return name;
        }

        // ---------------------------------------------------------------- Audio

        private void ScanTaps(IVideoPlayerPlayer vp)
        {
            _sourceScratch.Clear();
            CollectAudioSources(vp, _sourceScratch);

            for (int i = _taps.Count - 1; i >= 0; i--)
            {
                if (_taps[i] == null)
                    _taps.RemoveAt(i);
            }

            foreach (AudioSource src in _sourceScratch)
            {
                if (_taps.Count >= MaxTaps)
                    break;
                if (src == null)
                    continue;
                GameObject go = src.gameObject;
                // Attached once per audio source and never removed or toggled afterwards.
                AudioTap tap = go.GetComponent<AudioTap>();
                if (tap == null)
                    tap = go.AddComponent<AudioTap>();
                if (tap != null && !_taps.Contains(tap))
                    _taps.Add(tap);
            }

            SetTapsActive(true);

            if (MP3toMP4Mod.DebugLog.Value)
                MP3toMP4Mod.Logger.Msg("[Debug] '" + SafePlayerName() + "': " + _sourceScratch.Count + " audio source(s) found, listening to " + _taps.Count);
        }

        // Debug output, only when the setting is on and only when the reason changes (no log spam).
        private void Debug(string reason)
        {
            if (!MP3toMP4Mod.DebugLog.Value || reason == _lastDebug)
                return;
            _lastDebug = reason;
            MP3toMP4Mod.Logger.Msg("[Debug] '" + SafePlayerName() + "': " + reason);
        }

        // Only flips a flag the audio thread reads; the components themselves are never enabled/disabled.
        private void SetTapsActive(bool active)
        {
            foreach (AudioTap tap in _taps)
            {
                if (tap != null)
                    tap.Active = active;
            }
        }

        private void CollectAudioSources(IVideoPlayerPlayer vp, List<AudioSource> result)
        {
            // AvProPlayer and LibVLCPlayer both have a private field "_audioManager" with a
            // public "Channels" list whose entries each have an "AudioSource".
            try
            {
                FieldInfo field = vp.GetType().GetField("_audioManager", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                Component manager = field != null ? field.GetValue(vp) as Component : null;
                if (manager != null)
                {
                    PropertyInfo channelsProp = manager.GetType().GetProperty("Channels", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    IEnumerable channels = channelsProp != null ? channelsProp.GetValue(manager, null) as IEnumerable : null;
                    if (channels != null)
                    {
                        foreach (object channel in channels)
                        {
                            if (channel == null)
                                continue;
                            PropertyInfo srcProp = channel.GetType().GetProperty("AudioSource", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            AudioSource src = srcProp != null ? srcProp.GetValue(channel, null) as AudioSource : null;
                            if (src != null && !result.Contains(src))
                                result.Add(src);
                        }
                    }

                    if (result.Count == 0)
                    {
                        var children = new List<AudioSource>();
                        manager.GetComponentsInChildren(true, children);
                        foreach (AudioSource src in children)
                        {
                            if (src != null && !result.Contains(src))
                                result.Add(src);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                if (!_channelWarningLogged)
                {
                    _channelWarningLogged = true;
                    MP3toMP4Mod.Logger.Warning("Audio channels not found, the bars may stay flat: " + e.Message);
                }
            }

            // Fallback: only a few audio sources directly under the video player, never the whole world.
            if (result.Count == 0)
            {
                var children = new List<AudioSource>();
                Player.GetComponentsInChildren(true, children);
                foreach (AudioSource src in children)
                {
                    if (result.Count >= MaxFallbackSources)
                        break;
                    if (src != null && !result.Contains(src))
                        result.Add(src);
                }
            }
        }

        private bool GatherSamples()
        {
            Array.Clear(_mix, 0, _mix.Length);
            int used = 0;
            foreach (AudioTap tap in _taps)
            {
                if (tap == null || !tap.Active)
                    continue;
                if (!tap.CopyLatest(_tmp, 0.3))
                    continue;
                for (int i = 0; i < _mix.Length; i++)
                    _mix[i] += _tmp[i];
                used++;
            }
            if (used == 0)
                return false;
            if (used > 1)
            {
                float inv = 1f / used;
                for (int i = 0; i < _mix.Length; i++)
                    _mix[i] *= inv;
            }
            return true;
        }
    }
}
