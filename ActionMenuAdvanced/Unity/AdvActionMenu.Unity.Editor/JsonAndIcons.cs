// AdvActionMenu - JSON-Ausgabe (ohne Newtonsoft, kulturunabhaengige Zahlen) und Icon-Kodierung

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace AdvActionMenu.Unity.EditorTools
{
    // Aenderungen an einem einzelnen Eintrag (AdvActionMenu-Format "item_overrides")
    internal class AMItemOverride
    {
        public string icon;
        public string control;
        public float? min_value;
        public float? max_value;
        public bool no_limits;

        public bool IsEmpty => icon == null && control == null && min_value == null && max_value == null && !no_limits;
    }

    internal static class AMJson
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Str(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder("\"");
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        static string Num(float f) => f.ToString("R", Inv);

        static string Val(object o)
        {
            switch (o)
            {
                case null: return "null";
                case float f: return Num(f);
                case double d: return d.ToString("R", Inv);
                case int i: return i.ToString(Inv);
                case bool b: return b ? "true" : "false";
                case string s: return Str(s);
                default: return Convert.ToString(o, Inv);
            }
        }

        static string Action(AMAction a)
        {
            var p = new List<string>();
            void Add(string k, string v) => p.Add(Str(k) + ": " + v);

            if (a.type != null) Add("type", Str(a.type));
            if (a.menu != null) Add("menu", Str(a.menu));
            if (a.control != null) Add("control", Str(a.control));
            if (a.parameter != null) Add("parameter", Str(a.parameter));
            if (a.value != null) Add("value", Val(a.value));
            if (a.min_value.HasValue) Add("min_value", Num(a.min_value.Value));
            if (a.max_value.HasValue) Add("max_value", Num(a.max_value.Value));
            if (a.default_value != null) Add("default_value", Val(a.default_value));
            if (a.min_value_x.HasValue) Add("min_value_x", Num(a.min_value_x.Value));
            if (a.max_value_x.HasValue) Add("max_value_x", Num(a.max_value_x.Value));
            if (a.default_value_x.HasValue) Add("default_value_x", Num(a.default_value_x.Value));
            if (a.min_value_y.HasValue) Add("min_value_y", Num(a.min_value_y.Value));
            if (a.max_value_y.HasValue) Add("max_value_y", Num(a.max_value_y.Value));
            if (a.default_value_y.HasValue) Add("default_value_y", Num(a.default_value_y.Value));
            if (a.exclusive_option.HasValue) Add("exclusive_option", a.exclusive_option.Value ? "true" : "false");
            if (a.duration.HasValue) Add("duration", Num(a.duration.Value));
            return "{ " + string.Join(", ", p) + " }";
        }

        // daky's Format: ganze Menues ersetzen
        public static string Overwrites(List<KeyValuePair<string, List<AMItem>>> menus)
        {
            var sb = new StringBuilder();
            sb.Append("{\n    \"overwrites\": {\n");
            for (var mi = 0; mi < menus.Count; ++mi)
            {
                var kv = menus[mi];
                sb.Append("        ").Append(Str(kv.Key)).Append(": [\n");
                for (var ii = 0; ii < kv.Value.Count; ++ii)
                {
                    var it = kv.Value[ii];
                    sb.Append("            {\n");
                    sb.Append("                \"name\": ").Append(Str(it.name));
                    if (it.icon != null) sb.Append(",\n                \"icon\": ").Append(Str(it.icon));
                    if (it.enabled.HasValue) sb.Append(",\n                \"enabled\": ").Append(it.enabled.Value ? "true" : "false");
                    sb.Append(",\n                \"action\": ").Append(Action(it.action ?? new AMAction()));
                    sb.Append("\n            }").Append(ii < kv.Value.Count - 1 ? ",\n" : "\n");
                }
                sb.Append("        ]").Append(mi < menus.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("    }\n}\n");
            return sb.ToString();
        }

        // AdvActionMenu-Format: nur Aenderungen pro Eintrag, Menue -> Eintrag -> Aenderungen
        public static string ItemOverrides(Dictionary<string, Dictionary<string, AMItemOverride>> menus)
        {
            var sb = new StringBuilder();
            sb.Append("{\n    \"item_overrides\": {\n");
            var mlist = menus.Where(kv => kv.Value.Count > 0).ToList();
            for (var mi = 0; mi < mlist.Count; ++mi)
            {
                var kv = mlist[mi];
                sb.Append("        ").Append(Str(kv.Key)).Append(": {\n");
                var ilist = kv.Value.ToList();
                for (var ii = 0; ii < ilist.Count; ++ii)
                {
                    var o = ilist[ii].Value;
                    var p = new List<string>();
                    if (o.icon != null) p.Add("\"icon\": " + Str(o.icon));
                    if (o.control != null) p.Add("\"control\": " + Str(o.control));
                    if (o.no_limits) p.Add("\"no_limits\": true");
                    if (o.min_value.HasValue) p.Add("\"min_value\": " + Num(o.min_value.Value));
                    if (o.max_value.HasValue) p.Add("\"max_value\": " + Num(o.max_value.Value));
                    sb.Append("            ").Append(Str(ilist[ii].Key)).Append(": { ")
                      .Append(string.Join(", ", p)).Append(" }")
                      .Append(ii < ilist.Count - 1 ? ",\n" : "\n");
                }
                sb.Append("        }").Append(mi < mlist.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("    }\n}\n");
            return sb.ToString();
        }
    }

    // Skaliert per GPU, funktioniert auch mit nicht-lesbaren Texturen
    internal static class AMIcons
    {
        public static byte[] EncodePng(Texture2D src, int maxSize)
        {
            int w = src.width, h = src.height;
            var scale = Mathf.Min(1f, (float)maxSize / Mathf.Max(w, h));
            var tw = Mathf.Max(1, Mathf.RoundToInt(w * scale));
            var th = Mathf.Max(1, Mathf.RoundToInt(h * scale));

            var prevActive = RenderTexture.active;
            RenderTexture current = null;
            Texture currentTex = src;
            int cw = w, ch = h;
            try
            {
                // in Halbschritten verkleinern, damit grosse Texturen nicht flimmern
                while (cw >= tw * 2 && ch >= th * 2)
                {
                    cw /= 2; ch /= 2;
                    var next = RenderTexture.GetTemporary(cw, ch, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    next.filterMode = FilterMode.Bilinear;
                    Graphics.Blit(currentTex, next);
                    if (current != null) RenderTexture.ReleaseTemporary(current);
                    current = next;
                    currentTex = next;
                }

                var final = RenderTexture.GetTemporary(tw, th, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                final.filterMode = FilterMode.Bilinear;
                Graphics.Blit(currentTex, final);
                if (current != null) RenderTexture.ReleaseTemporary(current);
                current = final;

                RenderTexture.active = final;
                var tex = new Texture2D(tw, th, TextureFormat.RGBA32, false, false);
                tex.ReadPixels(new Rect(0, 0, tw, th), 0, 0);
                tex.Apply();
                var png = tex.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(tex);
                return png;
            }
            finally
            {
                RenderTexture.active = prevActive;
                if (current != null) RenderTexture.ReleaseTemporary(current);
            }
        }

        public static string FileNameFor(string key)
        {
            var safe = Regex.Replace(key, @"[^A-Za-z0-9_\-]+", "_").Trim('_');
            if (safe.Length > 60) safe = safe.Substring(0, 60);
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(key));
                var hex = BitConverter.ToString(hash, 0, 4).Replace("-", "").ToLowerInvariant();
                return safe + "_" + hex + ".png";
            }
        }
    }
}
