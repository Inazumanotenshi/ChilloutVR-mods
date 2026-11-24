// AdvActionMenu - page in the CVR Quick Menu (Misc tab) to fill the mod slots
// Uses the UILib built into CVR (ABI_RC.Systems.UI.UILib), no extra mod needed.

using ABI_RC.Systems.UI.UILib;
using ABI_RC.Systems.UI.UILib.UIObjects;
using ABI_RC.Systems.UI.UILib.UIObjects.Components;
using ABI_RC.Systems.UI.UILib.UIObjects.Objects;
using System;
using System.Globalization;
using System.Linq;

namespace ActionMenu
{
    internal static class ModSlotsPage
    {
        private const string ModName = "AdvActionMenu";
        private const string IconName = "AdvActionMenu";
        private static bool built;

        private class Row
        {
            public Category category = null!;
            public Button mod = null!, setting = null!, type = null!, min = null!, max = null!, range = null!, clear = null!;
        }
        private static Row[] rows = new Row[ModSlots.Count];

        public static void Setup()
        {
            try
            {
                var asm = typeof(ModSlotsPage).Assembly;
                var res = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("qm_icon.png"));
                if (res != null)
                    QuickMenuAPI.PrepareIcon(ModName, IconName, asm.GetManifestResourceStream(res));
            }
            catch (Exception e)
            {
                ActionMenuMod.Log.Warning($"Quick Menu icon could not be prepared: {e.Message}");
            }

            // only build once CVR creates the Quick Menu; later rebuilds (F5) are handled by UILib itself
            QuickMenuAPI.OnMenuRegenerate += _ => Build();
        }

        private static void Build()
        {
            if (built) return;
            built = true;
            try
            {
                var cat = QuickMenuAPI.MiscTabPage.AddCategory("AdvActionMenu", ModName, true, true, false);
                var page = cat.AddPage("Action Menu mod slots", IconName,
                    "Choose up to 10 settings of other mods to show in the Action Menu", ModName);
                page.MenuTitle = "AdvActionMenu";
                page.MenuSubtitle = "Settings of other mods in the Action Menu";

                var info = page.AddCategory("How it works", ModName, true, true, false);
                info.AddTextBlock("Pick a mod and one of its settings for each slot. They show up in the Action Menu under " +
                                  "\"Mods\", every mod with its own entry. Empty slots stay hidden. " +
                                  "Mod icons: put <mod name>.svg or .png into UserData\\AdvActionMenu\\ModIcons.");

                for (var i = 0; i < ModSlots.Count; ++i)
                {
                    var idx = i;
                    var c = page.AddCategory($"Slot {i + 1}", ModName, true, true, false);
                    var r = rows[i] = new Row { category = c };
                    r.mod = c.AddButton("Mod", "", "Choose the mod", ButtonStyle.TextOnly);
                    r.setting = c.AddButton("Setting", "", "Choose the setting of this mod", ButtonStyle.TextOnly);
                    r.type = c.AddButton("Type", "", "How it is shown in the Action Menu", ButtonStyle.TextOnly);
                    r.min = c.AddButton("Min", "", "Lowest value", ButtonStyle.TextOnly);
                    r.max = c.AddButton("Max", "", "Highest value", ButtonStyle.TextOnly);
                    r.range = c.AddButton("Reset range", "", "Use the range of the mod again", ButtonStyle.TextOnly);
                    r.clear = c.AddButton("Clear slot", "", "Remove this slot from the Action Menu", ButtonStyle.TextOnly);

                    r.mod.OnPress += () => PickMod(idx);
                    r.setting.OnPress += () => PickSetting(idx);
                    r.type.OnPress += () => PickType(idx);
                    r.min.OnPress += () => PickLimit(idx, true);
                    r.max.OnPress += () => PickLimit(idx, false);
                    r.range.OnPress += () => { var s = ModSlots.slots[idx]; s.min = s.max = null; Changed(idx); };
                    r.clear.OnPress += () => { ModSlots.slots[idx] = new ModSlot(); Changed(idx); };
                    Refresh(idx);
                }
            }
            catch (Exception e)
            {
                ActionMenuMod.Log.Error($"Building the Quick Menu page for mod slots failed: {e}");
            }
        }

        private static string Num(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        private static void Refresh(int i)
        {
            var r = rows[i];
            if (r == null) return;
            var s = ModSlots.slots[i];
            var cat = ModSlots.FindCategory(s.category);
            var e = cat == null ? null : ModSlots.FindEntry(s.category, s.entry);

            var modName = cat != null ? ModSlots.Name(cat) : s.category != null ? s.category + " (not installed)" : "-";
            var settingName = e != null ? ModSlots.Name(e) : s.entry ?? "-";
            r.category.CategoryName = s.IsSet ? $"Slot {i + 1}: {modName} / {settingName}" : $"Slot {i + 1}";

            r.mod.ButtonText = "Mod: " + modName;
            r.setting.ButtonText = "Setting: " + settingName;
            r.setting.Hidden = cat == null;

            var controls = e != null ? ModSlots.ControlsFor(e) : new();
            var control = s.control != null && controls.Contains(s.control) ? s.control : controls.FirstOrDefault();
            r.type.ButtonText = "Type: " + (control != null && ModSlots.ControlNames.TryGetValue(control, out var cn) ? cn : "-");
            r.type.Hidden = e == null || controls.Count < 2;

            var numeric = e != null && (control == "radial" || control == "number");
            r.min.Hidden = r.max.Hidden = !numeric;
            r.range.Hidden = !numeric || (s.min == null && s.max == null);
            if (numeric)
            {
                if (control == "radial")
                {
                    var (min, max) = ModSlots.SliderRange(s, e!);
                    r.min.ButtonText = "Min: " + Num(min);
                    r.max.ButtonText = "Max: " + Num(max);
                }
                else
                {
                    r.min.ButtonText = "Min: " + (s.min != null ? Num(s.min.Value) : "none");
                    r.max.ButtonText = "Max: " + (s.max != null ? Num(s.max.Value) : "none");
                }
            }
            r.clear.Hidden = s.category == null;
        }

        private static void Changed(int i)
        {
            ModSlots.Save();
            Refresh(i);
            ActionMenuMod.RefreshMenus();
        }

        private static void PickMod(int i)
        {
            var s = ModSlots.slots[i];
            var cats = ModSlots.Categories();
            var options = new[] { "- none -" }.Concat(cats.Select(c => ModSlots.Name(c))).ToArray();
            var selected = Math.Max(0, cats.FindIndex(c => c.Identifier == s.category) + 1);
            var ms = new MultiSelection($"Slot {i + 1}: Mod", options, selected);
            ms.OnOptionUpdated += idx =>
            {
                if (idx <= 0) ModSlots.slots[i] = new ModSlot();
                else if (cats[idx - 1].Identifier != s.category)
                    ModSlots.slots[i] = new ModSlot { category = cats[idx - 1].Identifier };
                Changed(i);
            };
            QuickMenuAPI.OpenMultiSelect(ms);
        }

        private static void PickSetting(int i)
        {
            var s = ModSlots.slots[i];
            var cat = ModSlots.FindCategory(s.category);
            if (cat == null) return;
            var entries = ModSlots.Entries(cat);
            if (entries.Count == 0) return;
            var options = entries.Select(e => ModSlots.Name(e)).ToArray();
            var selected = Math.Max(0, entries.FindIndex(e => e.Identifier == s.entry));
            var ms = new MultiSelection($"Slot {i + 1}: Setting", options, selected);
            ms.OnOptionUpdated += idx =>
            {
                if (idx < 0 || idx >= entries.Count) return;
                var e = entries[idx];
                if (e.Identifier != s.entry)
                {
                    s.entry = e.Identifier;
                    s.control = ModSlots.ControlsFor(e).FirstOrDefault();
                    s.min = s.max = null;
                }
                Changed(i);
            };
            QuickMenuAPI.OpenMultiSelect(ms);
        }

        private static void PickType(int i)
        {
            var s = ModSlots.slots[i];
            var e = ModSlots.FindEntry(s.category, s.entry);
            if (e == null) return;
            var controls = ModSlots.ControlsFor(e);
            var options = controls.Select(c => ModSlots.ControlNames[c]).ToArray();
            var selected = Math.Max(0, controls.IndexOf(s.control ?? ""));
            var ms = new MultiSelection($"Slot {i + 1}: Type", options, selected);
            ms.OnOptionUpdated += idx =>
            {
                if (idx < 0 || idx >= controls.Count) return;
                s.control = controls[idx];
                Changed(i);
            };
            QuickMenuAPI.OpenMultiSelect(ms);
        }

        private static void PickLimit(int i, bool isMin)
        {
            var s = ModSlots.slots[i];
            var e = ModSlots.FindEntry(s.category, s.entry);
            if (e == null) return;
            var (min, max) = ModSlots.SliderRange(s, e);
            QuickMenuAPI.OpenNumberInput(isMin ? "Minimum" : "Maximum", isMin ? min : max, v =>
            {
                if (isMin) s.min = v; else s.max = v;
                Changed(i);
            });
        }
    }
}
