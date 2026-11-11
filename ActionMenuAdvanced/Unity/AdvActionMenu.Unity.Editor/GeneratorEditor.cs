// AdvActionMenu - Inspector und Generator fuer die Override-JSON

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Gen = AdvActionMenu.Unity.ActionMenuGenerator;

namespace AdvActionMenu.Unity.EditorTools
{
    [CustomEditor(typeof(Gen))]
    public class GeneratorEditor : Editor
    {
        internal const string CvrPathPref = "AdvActionMenu.CVRPath";
        static readonly Dictionary<string, bool> Foldouts = new Dictionary<string, bool>();
        static readonly string[] TargetLabels = { "Action Menu (daky)", "AdvActionMenu" };

        AMMenus _menus;
        GameObject _menusFor;
        Gen.TargetMod _menusTarget;
        bool _showSettings;

        Gen G => (Gen)target;
        bool Advanced => G.target == Gen.TargetMod.AdvActionMenu;

        // ------------------------------------------------------------------
        public override void OnInspectorGUI()
        {
            var gen = G;
            serializedObject.Update();

            EditorGUILayout.LabelField("Target mod", EditorStyles.boldLabel);
            var t = GUILayout.Toolbar((int)gen.target, TargetLabels, GUILayout.Height(24));
            if (t != (int)gen.target)
            {
                Undo.RecordObject(gen, "Action Menu target");
                gen.target = (Gen.TargetMod)t;
                EditorUtility.SetDirty(gen);
            }
            EditorGUILayout.HelpBox(Advanced
                ? "AdvActionMenu: only your changes are written (icons, control, limits). New entries you add to the avatar later show up in the menu automatically."
                : "daky's Action Menu: whole menus are replaced. Regenerate the JSON after adding or renaming entries in the avatar.",
                MessageType.None);

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("avatar"), new GUIContent("Avatar", "Empty = the object this component is on"));
            serializedObject.ApplyModifiedProperties();

            var avatarGo = gen.Avatar;
            if (AMBuilder.FindCvrAvatar(avatarGo) == null)
            {
                EditorGUILayout.HelpBox("No CVRAvatar found. Put this component on your avatar, or drag the avatar into the field above.", MessageType.Warning);
                return;
            }

            if (_menus == null || _menusFor != avatarGo || _menusTarget != gen.target)
                Rebuild();

            DrawGuid();
            DrawSettings();
            DrawImportOld();

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Menu", EditorStyles.boldLabel);
                if (GUILayout.Button("Expand all", EditorStyles.miniButtonLeft, GUILayout.Width(80))) SetAllFoldouts(true);
                if (GUILayout.Button("Collapse", EditorStyles.miniButtonMid, GUILayout.Width(70))) SetAllFoldouts(false);
                if (GUILayout.Button("Reload", EditorStyles.miniButtonRight, GUILayout.Width(60))) Rebuild();
            }

            if (_menus.Count == 0)
            {
                EditorGUILayout.HelpBox("No Advanced Avatar Settings found. Create them in the CVRAvatar component and press Reload.", MessageType.Info);
            }
            else
            {
                DrawMenu(AMBuilder.RootMenu, 0, new HashSet<string>());
                foreach (var (key, item) in AMBuilder.RootExtras(true, true))
                    DrawRow(key, item, item.name + "  (added by the mod)", false, 0);
            }

            DrawOrphans();

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(_menus.Count == 0))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Preview", GUILayout.Height(32), GUILayout.Width(80)))
                    PreviewWindow.Open(gen);
                if (GUILayout.Button(new GUIContent("Import JSON", "Take icons and number settings from an existing override file"), GUILayout.Height(32), GUILayout.Width(100)))
                {
                    Rebuild();
                    JsonImporter.Run(gen, _menus);
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button("Generate JSON", GUILayout.Height(32)))
                    Generate();
            }
        }

        void Rebuild()
        {
            _menus = AMBuilder.Build(G.Avatar, Advanced);
            _menusFor = G.Avatar;
            _menusTarget = G.target;
        }

        void SetAllFoldouts(bool open)
        {
            foreach (var kv in _menus)
                foreach (var it in kv.Value)
                    if (it.action?.type == "menu")
                        Foldouts[kv.Key + AMBuilder.Sep + it.name] = open;
        }

        internal static string ResolveGuid(Gen gen)
        {
            if (!string.IsNullOrWhiteSpace(gen.avatarGuidOverride)) return gen.avatarGuidOverride.Trim();
            return AMBuilder.FindAvatarGuid(gen.Avatar);
        }

        void DrawGuid()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("avatarGuidOverride"), new GUIContent("Avatar ID (manual)", "Empty = ID from CVRAssetInfo"));
            serializedObject.ApplyModifiedProperties();

            var guid = ResolveGuid(G);
            if (string.IsNullOrEmpty(guid))
                EditorGUILayout.HelpBox("No avatar ID found. Upload the avatar once (the ID is then stored in CVRAssetInfo) or enter it above.", MessageType.Warning);
            else
                EditorGUILayout.LabelField("File", "for_" + guid + ".json", EditorStyles.miniLabel);
        }

        void DrawSettings()
        {
            _showSettings = EditorGUILayout.Foldout(_showSettings, "Settings", true);
            if (!_showSettings) return;

            using (new EditorGUI.IndentLevelScope())
            {
                serializedObject.Update();
                EditorGUILayout.PropertyField(serializedObject.FindProperty("iconMode"), new GUIContent("Icon storage"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("iconSize"), new GUIContent("Icon size (px)"));
                if (!Advanced)
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("keepEmotesLink"), new GUIContent("Keep Emotes link"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("keepProfilesLink"), new GUIContent("Keep Profiles link"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("writeAllMenus"), new GUIContent("Write all menus"));
                }
                serializedObject.ApplyModifiedProperties();

                using (new EditorGUILayout.HorizontalScope())
                {
                    var cvr = EditorPrefs.GetString(CvrPathPref, "");
                    var next = EditorGUILayout.TextField(new GUIContent("ChilloutVR folder", "Folder with ChilloutVR.exe"), cvr);
                    if (GUILayout.Button("…", GUILayout.Width(28)))
                    {
                        var picked = EditorUtility.OpenFolderPanel("Select ChilloutVR folder", cvr, "");
                        if (!string.IsNullOrEmpty(picked)) next = picked;
                    }
                    if (next != cvr) EditorPrefs.SetString(CvrPathPref, next);
                }
            }
        }

        // ----- Icons aus dem alten Generator (ActionMenuIcons.ActionMenuIconGenerator) uebernehmen -----
        void DrawImportOld()
        {
            var old = FindOldGenerator();
            if (old == null) return;
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Old Action Menu Icon Generator found in the scene", EditorStyles.wordWrappedMiniLabel);
                if (GUILayout.Button("Import icons", GUILayout.Width(100)))
                    ImportFromOld(old);
            }
        }

        Component FindOldGenerator()
        {
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("ActionMenuIcons.ActionMenuIconGenerator", false))
                .FirstOrDefault(x => x != null);
            if (type == null) return null;
            return UnityEngine.Object.FindObjectsOfType(type).OfType<Component>().FirstOrDefault();
        }

        void ImportFromOld(Component old)
        {
            const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            if (!(old.GetType().GetField("icons", bf)?.GetValue(old) is IEnumerable icons)) return;
            Undo.RecordObject(G, "Import icons");
            var count = 0;
            foreach (var e in icons)
            {
                if (e == null) continue;
                var path = e.GetType().GetField("path", bf)?.GetValue(e) as string;
                var icon = e.GetType().GetField("icon", bf)?.GetValue(e) as Texture2D;
                if (string.IsNullOrEmpty(path) || icon == null) continue;
                G.GetOrCreate(path).icon = icon;
                ++count;
            }
            EditorUtility.SetDirty(G);
            EditorUtility.DisplayDialog("AdvActionMenu", count + " icon(s) imported from the old generator.\nYou can remove the old component afterwards.", "OK");
        }

        // ------------------------------------------------------------------
        void DrawMenu(string menuKey, int depth, HashSet<string> visited)
        {
            if (!_menus.TryGetValue(menuKey, out var items)) return;
            if (!visited.Add(menuKey)) return;

            foreach (var item in items)
            {
                var key = menuKey + AMBuilder.Sep + item.name;
                var isMenu = item.action?.type == "menu";
                DrawRow(key, item, item.name, isMenu, depth);
                if (isMenu && Foldouts.TryGetValue(key, out var open) && open)
                    DrawMenu(item.action.menu, depth + 1, visited);
            }
        }

        static string KindLabel(AMItem item)
        {
            switch (item.action?.control)
            {
                case "toggle": return "toggle";
                case "impulse": return "impulse";
                case "radial": return "slider";
                case "number": return "number pad";
                case "joystick_2d": return "joystick 2D";
                case "input_vector_2d": return "input vector 2D";
                default: return null;
            }
        }

        void DrawRow(string key, AMItem item, string label, bool isMenu, int depth)
        {
            var gen = G;
            var entry = gen.Find(key);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(depth * 16);
                if (isMenu)
                {
                    Foldouts.TryGetValue(key, out var open);
                    Foldouts[key] = EditorGUILayout.Foldout(open, label, true);
                }
                else
                {
                    var kind = KindLabel(item);
                    EditorGUILayout.LabelField(kind != null ? label + "  (" + kind + ")" : label);
                }

                var cur = entry?.icon;
                var next = (Texture2D)EditorGUILayout.ObjectField(cur, typeof(Texture2D), false, GUILayout.Width(150));
                if (next != cur)
                {
                    Undo.RecordObject(gen, "Action Menu icon");
                    gen.SetIcon(key, next);
                    EditorUtility.SetDirty(gen);
                }
            }

            // Zahlen-Eintraege: Steuerungsart und Grenzen (nur AdvActionMenu)
            if (!Advanced || !item.IsNumeric) return;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(depth * 16 + 16);
                var e = entry ?? new Gen.Entry { path = key };
                EditorGUI.BeginChangeCheck();
                var control = (Gen.NumberControl)EditorGUILayout.EnumPopup(e.control, GUILayout.Width(95));
                var useLimits = EditorGUILayout.ToggleLeft("Limits", e.useLimits, GUILayout.Width(58));
                float min = e.min, max = e.max;
                using (new EditorGUI.DisabledScope(!useLimits))
                {
                    EditorGUIUtility.labelWidth = 28;
                    min = EditorGUILayout.FloatField("min", min, GUILayout.MinWidth(60));
                    max = EditorGUILayout.FloatField("max", max, GUILayout.MinWidth(60));
                    EditorGUIUtility.labelWidth = 0;
                }
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(gen, "Action Menu number settings");
                    var ent = gen.GetOrCreate(key);
                    ent.control = control;
                    ent.useLimits = useLimits;
                    ent.min = min;
                    ent.max = max;
                    gen.Cleanup();
                    EditorUtility.SetDirty(gen);
                }
            }
        }

        HashSet<string> AllKeys()
        {
            var keys = new HashSet<string>();
            foreach (var kv in _menus)
                foreach (var it in kv.Value)
                    keys.Add(kv.Key + AMBuilder.Sep + it.name);
            foreach (var (key, _) in AMBuilder.RootExtras(true, true))
                keys.Add(key);
            return keys;
        }

        void DrawOrphans()
        {
            var keys = AllKeys();
            var orphans = G.entries.Where(e => e != null && !keys.Contains(e.path)).ToList();
            if (orphans.Count == 0) return;

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                orphans.Count + " setting(s) belong to entries that no longer exist in the avatar (renamed or removed):\n" +
                string.Join("\n", orphans.Select(o => "• " + o.path)), MessageType.Warning);
            if (GUILayout.Button("Remove orphaned settings"))
            {
                Undo.RecordObject(G, "Remove orphaned settings");
                G.entries.RemoveAll(e => e == null || !keys.Contains(e.path));
                EditorUtility.SetDirty(G);
            }
        }

        // ------------------------------------------------------------------
        // Generieren
        // ------------------------------------------------------------------
        void Generate()
        {
            var gen = G;
            Rebuild(); // immer mit dem aktuellen Stand des Avatars

            var guid = ResolveGuid(gen);
            if (string.IsNullOrEmpty(guid))
            {
                EditorUtility.DisplayDialog("AdvActionMenu", "No avatar ID found (see the hint in the inspector).", "OK");
                return;
            }

            var cvrPath = EditorPrefs.GetString(CvrPathPref, "");
            var hasCvr = !string.IsNullOrEmpty(cvrPath) && File.Exists(Path.Combine(cvrPath, "ChilloutVR.exe"));
            if (gen.iconMode == Gen.IconMode.PngFiles && !hasCvr)
            {
                EditorUtility.DisplayDialog("AdvActionMenu", "PNG files need the ChilloutVR folder (with ChilloutVR.exe) in the settings.", "OK");
                return;
            }

            string jsonPath;
            if (hasCvr)
            {
                var dir = Path.Combine(cvrPath, "UserData", "ActionMenu", "AvatarOverrides");
                Directory.CreateDirectory(dir);
                jsonPath = Path.Combine(dir, "for_" + guid + ".json");
            }
            else
            {
                jsonPath = EditorUtility.SaveFilePanel("Save override JSON", "", "for_" + guid + ".json", "json");
                if (string.IsNullOrEmpty(jsonPath)) return;
            }

            // PNG-Modus: Dateien in den UIResources-Ordner der jeweiligen Mod
            string pngDir = null;
            if (gen.iconMode == Gen.IconMode.PngFiles)
            {
                pngDir = Path.Combine(cvrPath, "ChilloutVR_Data", "StreamingAssets", "Cohtml", "UIResources",
                    Advanced ? "AdvActionMenu" : "ActionMenu", "avataricons", guid);
                Directory.CreateDirectory(pngDir);
                foreach (var old in Directory.GetFiles(pngDir, "*.png")) File.Delete(old);
            }

            var cache = new Dictionary<Texture2D, byte[]>();
            var iconCount = 0;
            var total = Math.Max(1, gen.entries.Count(e => e?.icon != null));

            string IconFor(string key)
            {
                var tex = gen.GetIcon(key);
                if (tex == null) return null;
                EditorUtility.DisplayProgressBar("AdvActionMenu", key, Mathf.Clamp01((float)iconCount / total));
                if (!cache.TryGetValue(tex, out var png))
                {
                    png = AMIcons.EncodePng(tex, gen.iconSize);
                    cache[tex] = png;
                }
                ++iconCount;
                if (pngDir == null) return "data:image/png;base64," + Convert.ToBase64String(png);
                var file = AMIcons.FileNameFor(key);
                File.WriteAllBytes(Path.Combine(pngDir, file), png);
                return "avataricons/" + guid + "/" + file;
            }

            string json;
            int written;
            try
            {
                json = Advanced ? BuildItemOverrides(IconFor, out written) : BuildOverwrites(IconFor, out written);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (written == 0)
            {
                EditorUtility.DisplayDialog("AdvActionMenu", "Nothing to write yet: no icon or setting is assigned.", "OK");
                return;
            }

            File.WriteAllText(jsonPath, json, new UTF8Encoding(false));
            var msg = $"{iconCount} icon(s), {written} {(Advanced ? "entry change(s)" : "menu(s)")} written:\n{jsonPath}";
            if (pngDir != null) msg += $"\n\nPNG files:\n{pngDir}";
            Debug.Log("[AdvActionMenu] " + msg.Replace("\n", " "));
            if (EditorUtility.DisplayDialog("AdvActionMenu", msg, "Show in Explorer", "OK"))
                EditorUtility.RevealInFinder(jsonPath);
        }

        // AdvActionMenu: nur Aenderungen pro Eintrag
        string BuildItemOverrides(Func<string, string> iconFor, out int written)
        {
            var result = new Dictionary<string, Dictionary<string, AMItemOverride>>();
            var known = new Dictionary<string, AMItem>();
            foreach (var kv in _menus)
                foreach (var it in kv.Value)
                    known[kv.Key + AMBuilder.Sep + it.name] = it;
            foreach (var (key, extra) in AMBuilder.RootExtras(true, true))
                known[key] = extra;

            written = 0;
            foreach (var e in G.entries)
            {
                if (e == null || string.IsNullOrEmpty(e.path) || !known.TryGetValue(e.path, out var item)) continue;
                var o = new AMItemOverride { icon = iconFor(e.path) };
                if (item.IsNumeric)
                {
                    switch (e.control)
                    {
                        case Gen.NumberControl.Slider: o.control = "radial"; break;
                        case Gen.NumberControl.NumberPad: o.control = "number"; break;
                    }
                    var isNumberPad = e.control == Gen.NumberControl.NumberPad
                        || (e.control == Gen.NumberControl.Auto && item.action?.control == "number");
                    if (e.useLimits) { o.min_value = e.min; o.max_value = e.max; }
                    else if (isNumberPad && e.control != Gen.NumberControl.Auto) o.no_limits = true;
                }
                if (o.IsEmpty) continue;

                var i = e.path.LastIndexOf(AMBuilder.Sep);
                var menu = e.path.Substring(0, i);
                var name = e.path.Substring(i + 1);
                if (!result.TryGetValue(menu, out var dict)) result[menu] = dict = new Dictionary<string, AMItemOverride>();
                dict[name] = o;
                ++written;
            }
            return AMJson.ItemOverrides(result);
        }

        // daky's Action Menu: ganze Menues ersetzen (wie der alte Generator)
        string BuildOverwrites(Func<string, string> iconFor, out int written)
        {
            var gen = G;
            var output = new List<KeyValuePair<string, List<AMItem>>>();
            foreach (var kv in _menus)
            {
                var items = new List<AMItem>();
                var hasIcon = false;
                foreach (var it in kv.Value)
                {
                    var copy = it;
                    var icon = iconFor(kv.Key + AMBuilder.Sep + it.name);
                    if (icon != null) { copy.icon = icon; hasIcon = true; }
                    items.Add(copy);
                }

                if (kv.Key == AMBuilder.RootMenu)
                {
                    foreach (var (key, extra) in AMBuilder.RootExtras(gen.keepEmotesLink, gen.keepProfilesLink))
                    {
                        var copy = extra;
                        var icon = iconFor(key);
                        if (icon != null) { copy.icon = icon; hasIcon = true; }
                        items.Add(copy);
                    }
                }

                if (hasIcon || gen.writeAllMenus)
                    output.Add(new KeyValuePair<string, List<AMItem>>(kv.Key, items));
            }
            written = output.Count;
            return AMJson.Overwrites(output);
        }
    }
}
