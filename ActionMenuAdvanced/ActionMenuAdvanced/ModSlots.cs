// AdvActionMenu - mod slots
// Puts up to 10 settings (MelonPreferences) of any other mod into the Action Menu.
// They are picked in the CVR Quick Menu (Misc tab -> AdvActionMenu) and show up in the wheel under "Mods",
// every mod with its own entry. Mods that already add themselves through the API get
// the slot settings added to their existing submenu.

using MelonLoader;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace ActionMenu
{
    using static Daky.Dakytils;

    internal class ModSlot
    {
        public string? category; // identifier of the MelonPreferences category (= mod)
        public string? entry;    // identifier of the setting
        public string? control;  // toggle, options, radial, number
        public float? min;
        public float? max;

        [JsonIgnore] public bool IsSet => !string.IsNullOrEmpty(category) && !string.IsNullOrEmpty(entry);
    }

    internal static class ModSlots
    {
        public const int Count = 10;
        public const string ParamPrefix = "modslot:";
        private const string MenuPrefix = "modslots";
        private const string OwnCategory = "AdvActionMenu";
        private static readonly string iconFolder = @"UserData\AdvActionMenu\ModIcons";

        internal static MelonPreferences_Entry<string>? pref;
        internal static List<ModSlot> slots = new();
        private static readonly Dictionary<string, string?> iconCache = new();

        internal static readonly Dictionary<string, string> ControlNames = new()
        {
            ["toggle"] = "Toggle",
            ["options"] = "Options",
            ["radial"] = "Slider",
            ["number"] = "Number pad",
        };

        public static void Init(MelonPreferences_Category cat)
        {
            pref = cat.CreateEntry("mod_slots", "", "Mod slots",
                description: "Settings of other mods shown in the Action Menu (edit them in the Quick Menu: Misc tab -> AdvActionMenu)",
                is_hidden: true);
            Load();
        }

        private static void Load()
        {
            try
            {
                slots = string.IsNullOrWhiteSpace(pref?.Value) ? new() : JsonConvert.DeserializeObject<List<ModSlot>>(pref!.Value) ?? new();
            }
            catch (Exception e)
            {
                ActionMenuMod.Log.Warning($"mod_slots could not be read, starting empty: {e.Message}");
                slots = new();
            }
            while (slots.Count < Count) slots.Add(new ModSlot());
            if (slots.Count > Count) slots.RemoveRange(Count, slots.Count - Count);
        }

        public static void Save()
        {
            if (pref == null) return;
            pref.Value = JsonConvert.SerializeObject(slots, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
        }

        // ----- which mods and settings can be used -----

        public static List<MelonPreferences_Category> Categories() =>
            MelonPreferences.Categories
                .Where(c => c != null && !c.IsHidden && c.Identifier != OwnCategory && Entries(c).Count > 0)
                .OrderBy(c => c.DisplayName ?? c.Identifier, StringComparer.OrdinalIgnoreCase)
                .ToList();

        public static List<MelonPreferences_Entry> Entries(MelonPreferences_Category c) =>
            c.Entries.Where(e => e != null && !e.IsHidden && ControlsFor(e).Count > 0).ToList();

        public static MelonPreferences_Category? FindCategory(string? id) =>
            id == null ? null : MelonPreferences.Categories.FirstOrDefault(c => c.Identifier == id);

        public static MelonPreferences_Entry? FindEntry(string? cat, string? entry) =>
            FindCategory(cat)?.Entries.FirstOrDefault(e => e.Identifier == entry);

        public static string Name(MelonPreferences_Category c) => string.IsNullOrEmpty(c.DisplayName) ? c.Identifier : c.DisplayName;
        public static string Name(MelonPreferences_Entry e) => string.IsNullOrEmpty(e.DisplayName) ? e.Identifier : e.DisplayName;

        private static bool IsNumeric(Type t) => t == typeof(float) || t == typeof(double) || t == typeof(int) || t == typeof(long);

        public static List<string> ControlsFor(MelonPreferences_Entry e)
        {
            var t = e.GetReflectedType();
            if (t == typeof(bool)) return new() { "toggle" };
            if (t.IsEnum) return new() { "options" };
            if (IsNumeric(t)) return Range(e) != null ? new() { "radial", "number" } : new() { "number", "radial" };
            return new();
        }

        // range from the mod's validator (MelonLoader ValueRange<T>), via reflection for all ML versions
        public static (float min, float max)? Range(MelonPreferences_Entry e)
        {
            try
            {
                var v = e.Validator;
                if (v == null) return null;
                var t = v.GetType();
                if (!t.Name.StartsWith("ValueRange")) return null;
                var min = t.GetProperty("MinValue")?.GetValue(v);
                var max = t.GetProperty("MaxValue")?.GetValue(v);
                if (min == null || max == null) return null;
                return (Convert.ToSingle(min, CultureInfo.InvariantCulture), Convert.ToSingle(max, CultureInfo.InvariantCulture));
            }
            catch
            {
                return null;
            }
        }

        public static (float min, float max) SliderRange(ModSlot s, MelonPreferences_Entry e)
        {
            var r = Range(e);
            return (s.min ?? r?.min ?? 0f, s.max ?? r?.max ?? 1f);
        }

        // ----- menus in the wheel -----

        public static void AddToMenus(Menus menus)
        {
            var mods = ActionMenuMod.Lib.ModsMainMenu(menus);
            foreach (var group in slots.Where(s => s.IsSet).GroupBy(s => s.category))
            {
                var cat = FindCategory(group.Key);
                if (cat == null) continue; // mod is not installed (anymore)

                var items = new List<MenuItem>();
                foreach (var s in group)
                {
                    var e = cat.Entries.FirstOrDefault(x => x.Identifier == s.entry);
                    if (e == null) continue;
                    var item = BuildItem(menus, cat, e, s);
                    if (item != null) items.Add(item.Value);
                }
                if (items.Count == 0) continue;

                // does the mod already add itself (same name)? Then add to that menu instead of a second entry
                var name = Name(cat);
                var existing = mods.FirstOrDefault(m => m.name == name && m.action.type == "menu" && m.action.menu != null);
                if (existing.action.menu != null)
                {
                    menus.GetWithDefault(existing.action.menu).AddRange(items);
                    continue;
                }

                var key = ActionMenuMod.Path(MenuPrefix, cat.Identifier);
                menus[key] = items;
                mods.Add(new MenuItem
                {
                    name = name,
                    icon = IconFor(cat),
                    action = new ItemAction { type = "menu", menu = key },
                });
            }
        }

        private static MenuItem? BuildItem(Menus menus, MelonPreferences_Category cat, MelonPreferences_Entry e, ModSlot s)
        {
            var controls = ControlsFor(e);
            if (controls.Count == 0) return null;
            var control = s.control != null && controls.Contains(s.control) ? s.control : controls[0];
            var param = ParamPrefix + cat.Identifier + "|" + e.Identifier;
            var value = e.BoxedValue;

            switch (control)
            {
                case "toggle":
                    return new MenuItem
                    {
                        name = Name(e),
                        enabled = value is bool b && b,
                        action = new ItemAction { type = "set melon preference", control = "toggle", parameter = param, toggle = true },
                    };

                case "options":
                {
                    var t = e.GetReflectedType();
                    var key = ActionMenuMod.Path(MenuPrefix, cat.Identifier, e.Identifier);
                    menus[key] = Enum.GetNames(t).Select(n => new MenuItem
                    {
                        name = n,
                        enabled = value?.ToString() == n,
                        action = new ItemAction
                        {
                            type = "set melon preference", control = "toggle", parameter = param,
                            value = n, toggle = true, exclusive_option = true,
                        },
                    }).ToList();
                    return new MenuItem { name = Name(e), action = new ItemAction { type = "menu", menu = key } };
                }

                case "radial":
                {
                    var (min, max) = SliderRange(s, e);
                    return new MenuItem
                    {
                        name = Name(e),
                        action = new ItemAction
                        {
                            type = "set melon preference", control = "radial", parameter = param,
                            default_value = ToFloat(value), min_value = min, max_value = max,
                        },
                    };
                }

                case "number":
                    return new MenuItem
                    {
                        name = Name(e),
                        action = new ItemAction
                        {
                            type = "set melon preference", control = "number", parameter = param,
                            default_value = ToFloat(value), min_value = s.min, max_value = s.max,
                            control_locked = true, // don't turn it back into a slider
                        },
                    };
            }
            return null;
        }

        private static float ToFloat(object? v)
        {
            try { return v == null ? 0f : Convert.ToSingle(v, CultureInfo.InvariantCulture); }
            catch { return 0f; }
        }

        // set a value from the wheel, parameter = "modslot:<category>|<setting>"
        public static void SetValue(string parameter, string value)
        {
            var rest = parameter.Substring(ParamPrefix.Length);
            var sep = rest.IndexOf('|');
            if (sep < 0) return;
            var e = FindEntry(rest.Substring(0, sep), rest.Substring(sep + 1));
            if (e == null)
            {
                ActionMenuMod.Log.Warning($"Mod setting {rest} not found");
                return;
            }

            try
            {
                var t = e.GetReflectedType();
                var inv = CultureInfo.InvariantCulture;
                if (t == typeof(bool))
                {
                    if (float.TryParse(value, NumberStyles.Float, inv, out var f)) e.BoxedValue = f != 0;
                }
                else if (t.IsEnum)
                {
                    if (!string.IsNullOrEmpty(value) && value != "0" && Enum.GetNames(t).Contains(value))
                        e.BoxedValue = Enum.Parse(t, value);
                }
                else if (float.TryParse(value, NumberStyles.Float, inv, out var f))
                {
                    if (t == typeof(float)) e.BoxedValue = f;
                    else if (t == typeof(double)) e.BoxedValue = (double)f;
                    else if (t == typeof(int)) e.BoxedValue = (int)Math.Round(f);
                    else if (t == typeof(long)) e.BoxedValue = (long)Math.Round(f);
                }
            }
            catch (Exception ex)
            {
                ActionMenuMod.Log.Warning($"Could not set mod setting {rest}: {ex.Message}");
                return;
            }
            if (ActionMenuMod.DebugMode) ActionMenuMod.Log.Msg($"Mod setting {rest} = {e.BoxedValue}");

            // Many mods (e.g. CVRGoesBrrr) only apply changes in OnPreferencesSaved.
            // So save like UI Expansion Kit does, but batched (sliders send many values while dragging).
            saveAt = UnityEngine.Time.unscaledTime + 0.4f;
        }

        private static float saveAt = -1f;

        // call every frame
        public static void FlushSave()
        {
            if (saveAt < 0f || UnityEngine.Time.unscaledTime < saveAt) return;
            saveAt = -1f;
            try
            {
                MelonPreferences.Save();
            }
            catch (Exception ex)
            {
                ActionMenuMod.Log.Warning($"Saving preferences after a mod setting change failed: {ex.Message}");
            }
        }

        // ----- sync values: if the mod changes its setting itself (or through another menu), the wheel shows it -----

        private static readonly Dictionary<string, string> lastValues = new();

        public static void PollValues(Action<ItemAction> send)
        {
            foreach (var s in slots)
            {
                if (!s.IsSet) continue;
                var e = FindEntry(s.category, s.entry);
                if (e == null) continue;
                var param = ParamPrefix + s.category + "|" + s.entry;
                object? v;
                try { v = e.BoxedValue; } catch { continue; }
                var repr = Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
                if (lastValues.TryGetValue(param, out var old) && old == repr) continue;
                lastValues[param] = repr;

                var t = e.GetReflectedType();
                var update = new ItemAction { type = "set melon preference", parameter = param };
                if (t == typeof(bool)) update.value = v is bool b && b;
                else if (t.IsEnum) update.value = v?.ToString();
                else update.default_value = ToFloat(v);
                send(update);
            }
        }

        // ----- icons: UserData\AdvActionMenu\ModIcons\<mod name>.svg/.png, otherwise the puzzle piece -----

        private static string? IconFor(MelonPreferences_Category cat)
        {
            if (!iconCache.TryGetValue(cat.Identifier, out var icon))
            {
                icon = null;
                try
                {
                    foreach (var name in new[] { Name(cat), cat.Identifier })
                    {
                        var safe = string.Concat(name.Split(Path.GetInvalidFileNameChars()));
                        foreach (var (ext, mime) in new[] { (".svg", "image/svg+xml"), (".png", "image/png") })
                        {
                            var file = Path.Combine(iconFolder, safe + ext);
                            if (!File.Exists(file)) continue;
                            icon = $"data:{mime};base64," + Convert.ToBase64String(File.ReadAllBytes(file));
                            break;
                        }
                        if (icon != null) break;
                    }
                }
                catch (Exception e)
                {
                    ActionMenuMod.Log.Warning($"Mod icon for {cat.Identifier} could not be read: {e.Message}");
                }
                iconCache[cat.Identifier] = icon;
            }
            return icon;
        }

        public static void EnsureIconFolder()
        {
            try { Directory.CreateDirectory(iconFolder); } catch { }
        }

        // Quick Menu page: in its own class so a CVR version without UILib doesn't break the whole mod
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void SetupQuickMenu()
        {
            try
            {
                ModSlotsPage.Setup();
            }
            catch (Exception e)
            {
                ActionMenuMod.Log.Warning($"Quick Menu page for mod slots not available: {e.Message}");
            }
        }
    }
}
