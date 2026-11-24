// AdvActionMenu - read an existing override JSON (overwrites or item_overrides)
// and take over icons and number settings into the generator.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Gen = AdvActionMenu.Unity.ActionMenuGenerator;

namespace AdvActionMenu.Unity.EditorTools
{
    // small JSON reader (objects, lists, strings, numbers, true/false/null), without Newtonsoft
    internal static class MiniJson
    {
        public static object Parse(string s)
        {
            var i = 0;
            var v = Value(s, ref i);
            return v;
        }

        static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) ++i; }

        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) throw new FormatException("unexpected end");
            switch (s[i])
            {
                case '{': return Obj(s, ref i);
                case '[': return Arr(s, ref i);
                case '"': return Str(s, ref i);
                case 't': i += 4; return true;
                case 'f': i += 5; return false;
                case 'n': i += 4; return null;
                default: return Num(s, ref i);
            }
        }

        static Dictionary<string, object> Obj(string s, ref int i)
        {
            var d = new Dictionary<string, object>();
            ++i; Ws(s, ref i);
            if (s[i] == '}') { ++i; return d; }
            while (true)
            {
                Ws(s, ref i);
                var k = Str(s, ref i);
                Ws(s, ref i);
                if (s[i] != ':') throw new FormatException("':' expected at " + i);
                ++i;
                d[k] = Value(s, ref i);
                Ws(s, ref i);
                if (s[i] == ',') { ++i; continue; }
                if (s[i] == '}') { ++i; return d; }
                throw new FormatException("',' or '}' expected at " + i);
            }
        }

        static List<object> Arr(string s, ref int i)
        {
            var l = new List<object>();
            ++i; Ws(s, ref i);
            if (s[i] == ']') { ++i; return l; }
            while (true)
            {
                l.Add(Value(s, ref i));
                Ws(s, ref i);
                if (s[i] == ',') { ++i; continue; }
                if (s[i] == ']') { ++i; return l; }
                throw new FormatException("',' or ']' expected at " + i);
            }
        }

        static string Str(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("string expected at " + i);
            ++i;
            var sb = new StringBuilder();
            while (s[i] != '"')
            {
                var c = s[i++];
                if (c != '\\') { sb.Append(c); continue; }
                var e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
            ++i;
            return sb.ToString();
        }

        static double Num(string s, ref int i)
        {
            var start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) ++i;
            return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }

    internal static class JsonImporter
    {
        struct Found
        {
            public string key;      // menu path, e.g. avatar/Clothes/Hat
            public string icon;     // data: URI, file path or built-in icon of the mod
            public string control;
            public float? min, max;
            public bool noLimits;
            public bool explicitControl; // from item_overrides: set on purpose
        }

        static float? F(object o) => o is double d ? (float)d : (float?)null;

        static List<Found> Collect(Dictionary<string, object> root)
        {
            var list = new List<Found>();

            // daky's format: whole menus
            if (root.TryGetValue("overwrites", out var ow) && ow is Dictionary<string, object> menus)
                foreach (var m in menus)
                    if (m.Value is List<object> items)
                        foreach (var o in items.OfType<Dictionary<string, object>>())
                        {
                            if (!(o.TryGetValue("name", out var n) && n is string name)) continue;
                            var f = new Found { key = m.Key + AMBuilder.Sep + name };
                            if (o.TryGetValue("icon", out var ic)) f.icon = ic as string;
                            if (o.TryGetValue("action", out var ao) && ao is Dictionary<string, object> a)
                            {
                                f.control = a.TryGetValue("control", out var c) ? c as string : null;
                                f.min = a.TryGetValue("min_value", out var mi) ? F(mi) : null;
                                f.max = a.TryGetValue("max_value", out var ma) ? F(ma) : null;
                            }
                            list.Add(f);
                        }

            // AdvActionMenu format: only changes
            if (root.TryGetValue("item_overrides", out var io) && io is Dictionary<string, object> imenus)
                foreach (var m in imenus)
                    if (m.Value is Dictionary<string, object> items)
                        foreach (var it in items)
                        {
                            if (!(it.Value is Dictionary<string, object> o)) continue;
                            var f = new Found { key = m.Key + AMBuilder.Sep + it.Key, explicitControl = true };
                            if (o.TryGetValue("icon", out var ic)) f.icon = ic as string;
                            if (o.TryGetValue("control", out var c)) f.control = c as string;
                            if (o.TryGetValue("min_value", out var mi)) f.min = F(mi);
                            if (o.TryGetValue("max_value", out var ma)) f.max = F(ma);
                            if (o.TryGetValue("no_limits", out var nl) && nl is bool b) f.noLimits = b;
                            list.Add(f);
                        }

            return list;
        }

        // icon bytes from a data: URI or from the mod's UIResources in the CVR folder
        static byte[] LoadIconBytes(string icon, string cvrPath)
        {
            if (string.IsNullOrEmpty(icon)) return null;
            const string marker = ";base64,";
            if (icon.StartsWith("data:image/"))
            {
                var p = icon.IndexOf(marker, StringComparison.Ordinal);
                return p < 0 ? null : Convert.FromBase64String(icon.Substring(p + marker.Length));
            }
            if (!icon.StartsWith("avataricons/") || string.IsNullOrEmpty(cvrPath)) return null; // skip the mod's built-in icons (SVG)
            foreach (var mod in new[] { "AdvActionMenu", "ActionMenu" })
            {
                var file = Path.Combine(cvrPath, "ChilloutVR_Data", "StreamingAssets", "Cohtml", "UIResources", mod,
                    icon.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(file)) return File.ReadAllBytes(file);
            }
            return null;
        }

        public static void Run(Gen gen, AMMenus menus)
        {
            var cvrPath = EditorPrefs.GetString(GeneratorEditor.CvrPathPref, "");
            var guid = GeneratorEditor.ResolveGuid(gen);
            var defaultFile = string.IsNullOrEmpty(cvrPath) || string.IsNullOrEmpty(guid) ? null
                : Path.Combine(cvrPath, "UserData", "ActionMenu", "AvatarOverrides", "for_" + guid + ".json");

            string file = null;
            if (defaultFile != null && File.Exists(defaultFile))
            {
                var choice = EditorUtility.DisplayDialogComplex("Import JSON",
                    "Import the override file of this avatar?\n" + defaultFile, "Import", "Cancel", "Choose other file");
                if (choice == 1) return;
                file = choice == 0 ? defaultFile : null;
            }
            if (file == null)
            {
                var dir = defaultFile != null ? Path.GetDirectoryName(defaultFile) : "";
                file = EditorUtility.OpenFilePanel("Import override JSON", dir, "json");
                if (string.IsNullOrEmpty(file)) return;
            }

            List<Found> found;
            try
            {
                found = Collect(MiniJson.Parse(File.ReadAllText(file)) as Dictionary<string, object> ?? new Dictionary<string, object>());
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Import JSON", "Could not read the file:\n" + e.Message, "OK");
                return;
            }

            // which entries are numbers (slider / Input Single)?
            var items = new Dictionary<string, AMItem>();
            foreach (var kv in menus)
                foreach (var it in kv.Value)
                    items[kv.Key + AMBuilder.Sep + it.name] = it;

            // save icons as PNG (identical images only once)
            var avatarName = Regex.Replace(gen.Avatar.name, @"[^A-Za-z0-9_\- ()]+", "_").Trim();
            var assetDir = "Assets/AdvActionMenu Icons/" + (avatarName.Length > 0 ? avatarName : "Avatar");
            var byHash = new Dictionary<string, string>(); // hash -> asset path
            var iconFor = new Dictionary<string, string>(); // entry -> asset path
            var skipped = 0;
            try
            {
                for (var n = 0; n < found.Count; ++n)
                {
                    var f = found[n];
                    if (f.icon == null) continue;
                    EditorUtility.DisplayProgressBar("Import JSON", f.key, (float)n / Math.Max(1, found.Count));
                    byte[] bytes;
                    try { bytes = LoadIconBytes(f.icon, cvrPath); } catch { bytes = null; }
                    if (bytes == null) { if (!f.icon.EndsWith(".svg")) ++skipped; continue; }

                    string hash;
                    using (var md5 = MD5.Create()) hash = BitConverter.ToString(md5.ComputeHash(bytes)).Replace("-", "");
                    if (!byHash.TryGetValue(hash, out var assetPath))
                    {
                        Directory.CreateDirectory(assetDir);
                        assetPath = assetDir + "/" + AMIcons.FileNameFor(f.key);
                        File.WriteAllBytes(assetPath, bytes);
                        byHash[hash] = assetPath;
                    }
                    iconFor[f.key] = assetPath;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.Refresh();
            foreach (var path in byHash.Values)
            {
                if (AssetImporter.GetAtPath(path) is TextureImporter ti)
                {
                    ti.alphaIsTransparency = true;
                    ti.mipmapEnabled = false;
                    ti.SaveAndReimport();
                }
            }

            Undo.RecordObject(gen, "Import JSON");
            int icons = 0, numbers = 0;
            foreach (var f in found)
            {
                if (iconFor.TryGetValue(f.key, out var assetPath))
                {
                    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                    if (tex != null) { gen.GetOrCreate(f.key).icon = tex; ++icons; }
                }

                if (!items.TryGetValue(f.key, out var item) || !item.IsNumeric) continue;
                var e = gen.GetOrCreate(f.key);
                var changed = false;
                if (f.control == "number") { e.control = Gen.NumberControl.NumberPad; changed = true; }
                else if (f.control == "radial" && f.explicitControl) { e.control = Gen.NumberControl.Slider; changed = true; }

                // don't take over automatically generated 0-1 limits from daky's format as a setting
                var isDefaultRange = f.min == 0f && f.max == 1f && !f.explicitControl && f.control == "radial";
                if ((f.min != null || f.max != null) && !isDefaultRange && !f.noLimits)
                {
                    e.useLimits = true;
                    e.min = f.min ?? 0f;
                    e.max = f.max ?? 1f;
                    changed = true;
                }
                if (changed) ++numbers;
            }
            gen.Cleanup();
            EditorUtility.SetDirty(gen);

            var msg = $"{icons} icon(s) and {numbers} number setting(s) imported from\n{file}";
            if (byHash.Count > 0) msg += $"\n\nIcons saved to:\n{assetDir}";
            if (skipped > 0) msg += $"\n\n{skipped} icon(s) could not be loaded (file not found).";
            EditorUtility.DisplayDialog("Import JSON", msg, "OK");
        }
    }
}
