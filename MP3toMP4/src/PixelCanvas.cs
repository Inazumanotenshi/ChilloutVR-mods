using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace MP3toMP4
{
    // Image with premultiplied alpha (rgb * a / 255). Row 0 = bottom (like Unity).
    internal sealed class PmImage
    {
        internal readonly int W;
        internal readonly int H;
        internal readonly Color32[] Px;

        internal PmImage(int w, int h, Color32[] px)
        {
            W = w;
            H = h;
            Px = px;
        }

        internal static PmImage FromPng(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!ImageConversion.LoadImage(tex, bytes, false))
                    return null;
                var px = tex.GetPixels32();
                for (int i = 0; i < px.Length; i++)
                {
                    Color32 c = px[i];
                    int a = c.a;
                    px[i] = new Color32((byte)(c.r * a / 255), (byte)(c.g * a / 255), (byte)(c.b * a / 255), c.a);
                }
                return new PmImage(tex.width, tex.height, px);
            }
            finally
            {
                UnityEngine.Object.Destroy(tex);
            }
        }

        // Area averaging (good quality when downscaling). Once per layout.
        internal PmImage Resize(int dw, int dh)
        {
            dw = Math.Max(1, dw);
            dh = Math.Max(1, dh);
            var dst = new Color32[dw * dh];
            float sx = W / (float)dw;
            float sy = H / (float)dh;
            for (int y = 0; y < dh; y++)
            {
                int y0 = (int)(y * sy);
                int y1 = Math.Max(y0 + 1, Math.Min(H, (int)((y + 1) * sy)));
                for (int x = 0; x < dw; x++)
                {
                    int x0 = (int)(x * sx);
                    int x1 = Math.Max(x0 + 1, Math.Min(W, (int)((x + 1) * sx)));
                    int r = 0, g = 0, b = 0, a = 0, n = 0;
                    for (int yy = y0; yy < y1; yy++)
                    {
                        int row = yy * W;
                        for (int xx = x0; xx < x1; xx++)
                        {
                            Color32 c = Px[row + xx];
                            r += c.r; g += c.g; b += c.b; a += c.a;
                            n++;
                        }
                    }
                    dst[y * dw + x] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), (byte)(a / n));
                }
            }
            return new PmImage(dw, dh, dst);
        }
    }

    internal static class IconStore
    {
        private static bool _loaded;
        private static PmImage _icon;

        // Custom icon possible: ChilloutVR/UserData/MP3toMP4/icon.png. Otherwise the embedded mp3.png.
        internal static PmImage Icon
        {
            get
            {
                if (!_loaded)
                {
                    _loaded = true;
                    _icon = Load();
                }
                return _icon;
            }
        }

        private static PmImage Load()
        {
            try
            {
                string gameDir = Path.GetDirectoryName(Application.dataPath);
                string custom = Path.Combine(Path.Combine(Path.Combine(gameDir, "UserData"), "MP3toMP4"), "icon.png");
                if (File.Exists(custom))
                {
                    var img = PmImage.FromPng(File.ReadAllBytes(custom));
                    if (img != null)
                    {
                        MP3toMP4Mod.Logger.Msg("Loaded custom icon: " + custom);
                        return img;
                    }
                    MP3toMP4Mod.Logger.Warning("Could not read custom icon, using the built-in one: " + custom);
                }
            }
            catch (Exception e)
            {
                MP3toMP4Mod.Logger.Warning("Custom icon: " + e.Message);
            }

            try
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("MP3toMP4.mp3.png"))
                {
                    if (s == null)
                    {
                        MP3toMP4Mod.Logger.Warning("Embedded mp3.png is missing from the DLL.");
                        return null;
                    }
                    var ms = new MemoryStream();
                    s.CopyTo(ms);
                    return PmImage.FromPng(ms.ToArray());
                }
            }
            catch (Exception e)
            {
                MP3toMP4Mod.Logger.Error("Could not load the embedded icon: " + e);
                return null;
            }
        }
    }

    // Simple CPU canvas that is uploaded as a Texture2D and blitted into the ProjectionTexture.
    internal sealed class PixelCanvas
    {
        internal readonly int W;
        internal readonly int H;
        internal readonly Color32[] Px;
        internal readonly Texture2D Texture;

        internal PixelCanvas(int w, int h)
        {
            W = w;
            H = h;
            Px = new Color32[w * h];
            Texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Texture.name = "MP3toMP4_Canvas";
            Texture.wrapMode = TextureWrapMode.Clamp;
            Texture.filterMode = FilterMode.Bilinear;
            Texture.hideFlags = HideFlags.HideAndDontSave;
        }

        internal void Destroy()
        {
            if (Texture != null)
                UnityEngine.Object.Destroy(Texture);
        }

        internal void Upload()
        {
            Texture.SetPixels32(Px);
            Texture.Apply(false, false);
        }

        internal void CopyFrom(Color32[] src)
        {
            Array.Copy(src, Px, Px.Length);
        }

        // a: 0..256
        private static Color32 Blend(Color32 dst, Color32 src, int a)
        {
            int ia = 256 - a;
            return new Color32(
                (byte)((src.r * a + dst.r * ia) >> 8),
                (byte)((src.g * a + dst.g * ia) >> 8),
                (byte)((src.b * a + dst.b * ia) >> 8),
                255);
        }

        internal void FillRect(int x, int y, int w, int h, Color32 c, float alpha = 1f)
        {
            int x0 = Math.Max(0, x), y0 = Math.Max(0, y);
            int x1 = Math.Min(W, x + w), y1 = Math.Min(H, y + h);
            if (x0 >= x1 || y0 >= y1)
                return;
            int a = Mathf.Clamp(Mathf.RoundToInt(alpha * 256f), 0, 256);
            if (a == 0)
                return;
            c.a = 255;
            for (int yy = y0; yy < y1; yy++)
            {
                int row = yy * W;
                if (a >= 256)
                {
                    for (int xx = x0; xx < x1; xx++)
                        Px[row + xx] = c;
                }
                else
                {
                    for (int xx = x0; xx < x1; xx++)
                        Px[row + xx] = Blend(Px[row + xx], c, a);
                }
            }
        }

        // Vertical gradient. The color depends on the absolute height (gradY0..gradY0+gradH),
        // so all bars look consistent.
        internal void FillRectGradient(int x, int y, int w, int h, Color32 bottom, Color32 top, int gradY0, int gradH, float alpha = 1f)
        {
            int x0 = Math.Max(0, x), y0 = Math.Max(0, y);
            int x1 = Math.Min(W, x + w), y1 = Math.Min(H, y + h);
            if (x0 >= x1 || y0 >= y1)
                return;
            int a = Mathf.Clamp(Mathf.RoundToInt(alpha * 256f), 0, 256);
            if (a == 0)
                return;
            float inv = 1f / Math.Max(1, gradH);
            for (int yy = y0; yy < y1; yy++)
            {
                float t = Mathf.Clamp01((yy - gradY0) * inv);
                Color32 c = Color32.Lerp(bottom, top, t);
                c.a = 255;
                int row = yy * W;
                if (a >= 256)
                {
                    for (int xx = x0; xx < x1; xx++)
                        Px[row + xx] = c;
                }
                else
                {
                    for (int xx = x0; xx < x1; xx++)
                        Px[row + xx] = Blend(Px[row + xx], c, a);
                }
            }
        }

        // Draws a premultiplied image centered at (cx, cy) with width dw (keeps aspect ratio).
        internal void DrawImage(PmImage img, float cx, float cy, float dw)
        {
            if (img == null || dw < 1f)
                return;
            float dh = dw * img.H / img.W;
            float left = cx - dw * 0.5f;
            float bottom = cy - dh * 0.5f;
            int x0 = Math.Max(0, (int)Math.Floor(left)), x1 = Math.Min(W, (int)Math.Ceiling(left + dw));
            int y0 = Math.Max(0, (int)Math.Floor(bottom)), y1 = Math.Min(H, (int)Math.Ceiling(bottom + dh));
            float sx = img.W / dw;
            float sy = img.H / dh;
            int iw = img.W, ih = img.H;
            Color32[] src = img.Px;

            for (int y = y0; y < y1; y++)
            {
                float v = (y + 0.5f - bottom) * sy - 0.5f;
                if (v < -0.5f || v > ih - 0.5f)
                    continue;
                int vy = (int)Math.Floor(v);
                float fy = v - vy;
                int ya = Mathf.Clamp(vy, 0, ih - 1), yb = Mathf.Clamp(vy + 1, 0, ih - 1);
                int rowA = ya * iw, rowB = yb * iw;
                int row = y * W;

                for (int x = x0; x < x1; x++)
                {
                    float u = (x + 0.5f - left) * sx - 0.5f;
                    if (u < -0.5f || u > iw - 0.5f)
                        continue;
                    int ux = (int)Math.Floor(u);
                    float fx = u - ux;
                    int xa = Mathf.Clamp(ux, 0, iw - 1), xb = Mathf.Clamp(ux + 1, 0, iw - 1);

                    Color32 c00 = src[rowA + xa], c10 = src[rowA + xb], c01 = src[rowB + xa], c11 = src[rowB + xb];
                    float w00 = (1 - fx) * (1 - fy), w10 = fx * (1 - fy), w01 = (1 - fx) * fy, w11 = fx * fy;
                    float a = c00.a * w00 + c10.a * w10 + c01.a * w01 + c11.a * w11;
                    if (a < 0.5f)
                        continue;
                    float r = c00.r * w00 + c10.r * w10 + c01.r * w01 + c11.r * w11;
                    float g = c00.g * w00 + c10.g * w10 + c01.g * w01 + c11.g * w11;
                    float b = c00.b * w00 + c10.b * w10 + c01.b * w01 + c11.b * w11;
                    float ia = 1f - a / 255f;
                    Color32 d = Px[row + x];
                    Px[row + x] = new Color32(
                        (byte)Mathf.Min(255f, r + d.r * ia),
                        (byte)Mathf.Min(255f, g + d.g * ia),
                        (byte)Mathf.Min(255f, b + d.b * ia),
                        255);
                }
            }
        }

        // Additive glow using a precomputed mask (0..1) in the rect (mx, my, mw, mh).
        internal void AddGlow(float[] mask, int mx, int my, int mw, int mh, Color32 color, float intensity)
        {
            if (mask == null || intensity <= 0.001f)
                return;
            for (int y = 0; y < mh; y++)
            {
                int py = my + y;
                if (py < 0 || py >= H)
                    continue;
                int row = py * W;
                int mrow = y * mw;
                for (int x = 0; x < mw; x++)
                {
                    int px = mx + x;
                    if (px < 0 || px >= W)
                        continue;
                    float k = mask[mrow + x] * intensity;
                    if (k <= 0.002f)
                        continue;
                    Color32 d = Px[row + px];
                    Px[row + px] = new Color32(
                        (byte)Mathf.Min(255f, d.r + color.r * k),
                        (byte)Mathf.Min(255f, d.g + color.g * k),
                        (byte)Mathf.Min(255f, d.b + color.b * k),
                        255);
                }
            }
        }

        // Text in 5x7 pixel font. (x, y) = bottom-left corner. Horizontal clipping to [clipX0, clipX1).
        internal void DrawText(string text, int x, int y, int scale, Color32 color, int clipX0, int clipX1)
        {
            if (string.IsNullOrEmpty(text))
                return;
            clipX0 = Math.Max(0, clipX0);
            clipX1 = Math.Min(W, clipX1);
            color.a = 255;
            int adv = Font5x7.Advance * scale;
            for (int i = 0; i < text.Length; i++)
            {
                int cx = x + i * adv;
                if (cx >= clipX1)
                    break;
                if (cx + adv <= clipX0)
                    continue;
                int glyph = Font5x7.Index(text[i]);
                if (glyph < 0)
                    continue;
                for (int col = 0; col < 5; col++)
                {
                    byte bits = Font5x7.Data[glyph * 5 + col];
                    if (bits == 0)
                        continue;
                    for (int r = 0; r < 8; r++)
                    {
                        if ((bits & (1 << r)) == 0)
                            continue;
                        // Bit 0 = top row, bit 7 = descender below y; our y grows upwards.
                        int py = y + (6 - r) * scale;
                        int px = cx + col * scale;
                        for (int sy = 0; sy < scale; sy++)
                        {
                            int yy = py + sy;
                            if (yy < 0 || yy >= H)
                                continue;
                            int row = yy * W;
                            for (int sx = 0; sx < scale; sx++)
                            {
                                int xx = px + sx;
                                if (xx < clipX0 || xx >= clipX1)
                                    continue;
                                Px[row + xx] = color;
                            }
                        }
                    }
                }
            }
        }

        internal static int TextWidth(string text, int scale)
        {
            if (string.IsNullOrEmpty(text))
                return 0;
            return text.Length * Font5x7.Advance * scale - scale;
        }
    }
}
