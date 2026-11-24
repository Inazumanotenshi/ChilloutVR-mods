using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace MP3toMP4
{
    // Draws the overlay on the GPU: rectangles, gradients and images as simple quads with Unity's
    // built-in UI shader into a small RenderTexture. The CPU only sends a few dozen vertices per frame.
    internal sealed class GpuCanvas
    {
        internal readonly int W;
        internal readonly int H;
        internal readonly RenderTexture Target;

        private static Material _material;
        private static bool _materialTried;
        private static Texture2D _white;
        private static Texture2D _glow;

        private RenderTexture _previous;
        private Texture _currentTexture;
        private bool _inQuads;
        private bool _linear;

        // True when the GPU path can be used (shader found). Otherwise the CPU fallback draws.
        internal static bool Available
        {
            get
            {
                EnsureMaterial();
                return _material != null;
            }
        }

        private static void EnsureMaterial()
        {
            if (_materialTried)
                return;
            _materialTried = true;
            try
            {
                Shader shader = Shader.Find("UI/Default");
                if (shader == null)
                {
                    MP3toMP4Mod.Logger.Warning("UI/Default shader not found, drawing on the CPU instead (slower).");
                    return;
                }
                _material = new Material(shader) { name = "MP3toMP4_Overlay", hideFlags = HideFlags.HideAndDontSave };
                // No depth buffer and no stencil in our texture: always draw.
                if (_material.HasProperty("unity_GUIZTestMode"))
                    _material.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
                _material.color = Color.white;

                _white = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "MP3toMP4_White", hideFlags = HideFlags.HideAndDontSave };
                _white.SetPixels32(new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) });
                _white.Apply(false, true);

                _glow = BuildGlowTexture(128);
            }
            catch (Exception e)
            {
                MP3toMP4Mod.Logger.Warning("GPU drawing not available, drawing on the CPU instead (slower): " + e.Message);
                _material = null;
            }
        }

        // White circle with alpha (1 - d)^2 towards the edge, tinted at draw time.
        private static Texture2D BuildGlowTexture(int size)
        {
            var px = new Color32[size * size];
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - r) / r;
                    float dy = (y + 0.5f - r) / r;
                    float k = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(k * k * 255f));
                }
            }
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "MP3toMP4_Glow",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        internal GpuCanvas(int w, int h)
        {
            W = w;
            H = h;
            Target = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "MP3toMP4_Target",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                useMipMap = false
            };
            Target.Create();
        }

        internal void Destroy()
        {
            if (Target != null)
            {
                Target.Release();
                UnityEngine.Object.Destroy(Target);
            }
        }

        // Starts drawing into Target. Always pair with End() (use try/finally).
        internal bool Begin()
        {
            if (_material == null || Target == null)
                return false;
            if (!Target.IsCreated())
                Target.Create();

            _linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            _previous = RenderTexture.active;
            RenderTexture.active = Target;
            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, W, 0f, H);
            GL.Clear(false, true, Color.black);
            _currentTexture = null;
            _inQuads = false;
            return true;
        }

        internal void End()
        {
            if (_inQuads)
            {
                GL.End();
                _inQuads = false;
            }
            GL.PopMatrix();
            RenderTexture.active = _previous;
            _previous = null;
            _currentTexture = null;
        }

        // Colors are given in sRGB (like before); in a linear project the shader expects linear values.
        internal Color Col(Color32 c, float alpha = 1f)
        {
            Color col = c;
            if (_linear)
                col = col.linear;
            col.a = Mathf.Clamp01(alpha);
            return col;
        }

        private void Use(Texture tex)
        {
            if (tex == null)
                tex = _white;
            if (_inQuads && tex == _currentTexture)
                return;
            if (_inQuads)
                GL.End();
            _material.mainTexture = tex;
            _material.SetPass(0);
            GL.Begin(GL.QUADS);
            _inQuads = true;
            _currentTexture = tex;
        }

        // One quad with a vertical color gradient (bottom -> top) and texture coordinates.
        private static void Quad(float x0, float y0, float x1, float y1, Color bottom, Color top, float u0, float v0, float u1, float v1)
        {
            GL.Color(bottom);
            GL.TexCoord2(u0, v0);
            GL.Vertex3(x0, y0, 0f);
            GL.Color(bottom);
            GL.TexCoord2(u1, v0);
            GL.Vertex3(x1, y0, 0f);
            GL.Color(top);
            GL.TexCoord2(u1, v1);
            GL.Vertex3(x1, y1, 0f);
            GL.Color(top);
            GL.TexCoord2(u0, v1);
            GL.Vertex3(x0, y1, 0f);
        }

        internal void Rect(float x, float y, float w, float h, Color color)
        {
            if (w <= 0f || h <= 0f)
                return;
            Use(_white);
            Quad(x, y, x + w, y + h, color, color, 0f, 0f, 1f, 1f);
        }

        internal void Gradient(float x, float y, float w, float h, Color bottom, Color top)
        {
            if (w <= 0f || h <= 0f)
                return;
            Use(_white);
            Quad(x, y, x + w, y + h, bottom, top, 0f, 0f, 1f, 1f);
        }

        internal void Glow(float cx, float cy, float radius, Color color)
        {
            if (radius <= 0f || color.a <= 0.002f)
                return;
            Use(_glow);
            Quad(cx - radius, cy - radius, cx + radius, cy + radius, color, color, 0f, 0f, 1f, 1f);
        }

        internal void Image(Texture tex, float cx, float cy, float w, float h, Color tint)
        {
            if (tex == null || w <= 0f || h <= 0f)
                return;
            Use(tex);
            Quad(cx - w * 0.5f, cy - h * 0.5f, cx + w * 0.5f, cy + h * 0.5f, tint, tint, 0f, 0f, 1f, 1f);
        }

        // Draws part of a texture: u0..u1 may go beyond 1 when the texture repeats (scrolling text).
        internal void ImageUV(Texture tex, float x, float y, float w, float h, float u0, float u1, Color tint)
        {
            if (tex == null || w <= 0f || h <= 0f)
                return;
            Use(tex);
            Quad(x, y, x + w, y + h, tint, tint, u0, 0f, u1, 1f);
        }
    }

    // A line of 5x7 pixel text as a small white texture (tinted when drawn). Rebuilt only when the text changes.
    internal sealed class TextTexture
    {
        private const int Rows = 8; // 7 rows + 1 for descenders

        internal Texture2D Texture;
        internal string Text;
        internal int TextPixels;  // width of the text itself, in font pixels
        internal int TotalPixels; // width including the trailing gap (for scrolling)

        // Returns true when the texture was (re)built.
        internal bool Set(string text, int gapPixels)
        {
            if (text == null)
                text = "";
            if (Texture != null && text == Text)
                return false;

            Text = text;
            TextPixels = Math.Max(1, text.Length * Font5x7.Advance - 1);
            TotalPixels = TextPixels + Math.Max(0, gapPixels);

            var px = new Color32[TotalPixels * Rows];
            var on = new Color32(255, 255, 255, 255);
            for (int i = 0; i < text.Length; i++)
            {
                int glyph = Font5x7.Index(text[i]);
                if (glyph < 0)
                    continue;
                for (int col = 0; col < 5; col++)
                {
                    byte bits = Font5x7.Data[glyph * 5 + col];
                    if (bits == 0)
                        continue;
                    int x = i * Font5x7.Advance + col;
                    if (x >= TotalPixels)
                        continue;
                    for (int r = 0; r < 8; r++)
                    {
                        if ((bits & (1 << r)) == 0)
                            continue;
                        // Bit 0 = top row (texture row 7), bit 6 = baseline (row 1), bit 7 = descender (row 0).
                        int y = 7 - r;
                        px[y * TotalPixels + x] = on;
                    }
                }
            }

            if (Texture == null || Texture.width != TotalPixels)
            {
                Destroy();
                Texture = new Texture2D(TotalPixels, Rows, TextureFormat.RGBA32, false)
                {
                    name = "MP3toMP4_Text",
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Repeat
                };
            }
            Texture.SetPixels32(px);
            Texture.Apply(false, false);
            return true;
        }

        internal void Destroy()
        {
            if (Texture != null)
                UnityEngine.Object.Destroy(Texture);
            Texture = null;
        }
    }
}
