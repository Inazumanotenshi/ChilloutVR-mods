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
            int id = player.GetInstanceID();
            PlayerOverlay overlay;
            if (!Overlays.TryGetValue(id, out overlay))
            {
                overlay = new PlayerOverlay(player);
                Overlays[id] = overlay;
            }
            overlay.Update();
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
                Overlays[id].Dispose();
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
                if (!MP3toMP4Mod.Enabled.Value)
                    overlay.Deactivate(true);
            }
        }
    }

    internal sealed class PlayerOverlay
    {
        internal readonly CVRVideoPlayer Player;
        internal bool LayoutDirty = true;

        private bool _active;
        private float _audioOnlySince = -1f;

        private PixelCanvas _canvas;
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
            SetTapsEnabled(false);
            if (_canvas != null)
            {
                _canvas.Destroy();
                _canvas = null;
            }
        }

        internal void Update()
        {
            RenderTexture rt = Player.ProjectionTexture;
            IVideoPlayerPlayer vp = Player.VideoPlayer;
            if (rt == null || vp == null)
            {
                Deactivate(false);
                return;
            }

            IVideoPlayerInfo info = vp.Info;
            if (info == null)
            {
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
                _audioOnlySince = -1f;
                Deactivate(true);
                return;
            }

            IVideoMetaData meta = info.VideoMetaData;
            bool hasVideo;
            try
            {
                hasVideo = meta != null
                           && meta.GetVideoWidth() > 0
                           && meta.GetVideoHeight() > 0
                           && vp.GetVideoTexture() != null;
            }
            catch
            {
                // Player not ready yet -> when in doubt, leave it alone.
                hasVideo = true;
            }
            if (hasVideo)
            {
                // Real video: CVR already blitted the frame this update, stay out of the way.
                _audioOnlySince = -1f;
                Deactivate(false);
                return;
            }

            float now = Time.unscaledTime;
            if (!_active)
            {
                if (state != VideoPlayerUtils.PlayerState.Playing)
                    return;
                if (!MP3toMP4Mod.ReplaceThumbnail.Value && HasThumbnail(meta))
                    return;
                if (_audioOnlySince < 0f)
                    _audioOnlySince = now;
                if (now - _audioOnlySince < Mathf.Max(0f, MP3toMP4Mod.DetectDelay.Value))
                    return;
                Activate();
            }

            Render(rt, vp, info, meta, state == VideoPlayerUtils.PlayerState.Playing, now);
        }

        private void Activate()
        {
            _active = true;
            _spectrum.Reset();
            _nextTapScan = 0f;
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
            SetTapsEnabled(false);
            if (clear && Player != null)
            {
                RenderTexture rt = Player.ProjectionTexture;
                if (rt != null)
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

            if (now >= _nextDraw)
            {
                float dt = _lastDrawTime > 0f ? Mathf.Clamp(now - _lastDrawTime, 0.001f, 0.25f) : 1f / 30f;
                _lastDrawTime = now;
                _nextDraw = now + 1f / Mathf.Clamp(MP3toMP4Mod.MaxFps.Value, 5, 90);

                if (now >= _nextTapScan)
                {
                    _nextTapScan = now + 1f;
                    ScanTaps(vp);
                }

                bool hasAudio = playing && GatherSamples();
                _spectrum.Process(hasAudio ? _mix : null, _sampleRate, dt);

                if (now >= _nextTitleRefresh)
                {
                    _nextTitleRefresh = now + 2f;
                    _title = Font5x7.Sanitize(ResolveTitle(meta));
                }

                Draw(info, meta, now);
                _canvas.Upload();
            }

            Graphics.Blit(_canvas.Texture, rt);
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

            if (_canvas == null || _canvas.W != w || _canvas.H != h)
            {
                if (_canvas != null)
                    _canvas.Destroy();
                _canvas = new PixelCanvas(w, h);
                LayoutDirty = true;
            }

            if (LayoutDirty)
            {
                LayoutDirty = false;
                BuildLayout();
            }
        }

        private void BuildLayout()
        {
            int W = _canvas.W, H = _canvas.H;
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

            _showIcon = mode != DisplayMode.Visualizer && IconStore.Icon != null;
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

            // Pre-scale the icon once in high quality (slightly larger for pulsing).
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

        // Turns URL/HTML encoding back into plain text: "Mein%20Lied%20%C3%A4.mp3" -> "Mein Lied ä.mp3",
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
                if (src == null)
                    continue;
                GameObject go = src.gameObject;
                AudioTap tap = go.GetComponent<AudioTap>();
                if (tap == null)
                    tap = go.AddComponent<AudioTap>();
                if (!_taps.Contains(tap))
                    _taps.Add(tap);
            }

            SetTapsEnabled(true);
        }

        private void SetTapsEnabled(bool enabled)
        {
            foreach (AudioTap tap in _taps)
            {
                if (tap != null && tap.enabled != enabled)
                    tap.enabled = enabled;
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
                MP3toMP4Mod.Logger.Warning("Audio channels not found: " + e.Message);
            }

            if (result.Count == 0)
            {
                var children = new List<AudioSource>();
                Player.GetComponentsInChildren(true, children);
                foreach (AudioSource src in children)
                {
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
                if (tap == null || !tap.enabled)
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
