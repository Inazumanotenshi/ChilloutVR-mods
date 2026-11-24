// AdvActionMenu - menu model and a copy of the mod's menu generation
// Reads the CCK's Advanced Avatar Settings via reflection (not tied to a CCK version)
// and builds the same menu tree from it that the mod creates in game.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;

namespace AdvActionMenu.Unity.EditorTools
{
    internal class AMAction
    {
        public string type;
        public string menu;
        public string control;
        public string parameter;
        public object value;
        public float? min_value;
        public float? max_value;
        public object default_value;
        public float? min_value_x;
        public float? max_value_x;
        public float? default_value_x;
        public float? min_value_y;
        public float? max_value_y;
        public float? default_value_y;
        public bool? exclusive_option;
        public float? duration;
    }

    internal struct AMItem
    {
        public string name;
        public string icon;
        public bool? enabled;
        public AMAction action;
        // type from the CCK (Toggle, Dropdown, Slider, InputSingle ...), only for the inspector
        public string kind;

        public bool IsNumeric => kind == "Slider" || kind == "InputSingle";
    }

    internal class AMMenus : Dictionary<string, List<AMItem>>
    {
        public List<AMItem> Get(string key)
        {
            if (!TryGetValue(key, out var list))
            {
                list = new List<AMItem>();
                this[key] = list;
            }
            return list;
        }
    }

    internal static class AMBuilder
    {
        public const string RootMenu = "avatar";
        public const char Sep = '/';
        public const string EmotesKey = RootMenu + "/Emotes";
        public const string ProfilesKey = RootMenu + "/Profiles";

        public static string PathJoin(params string[] parts) => string.Join(Sep.ToString(), parts);

        // ----- reflection helpers for the CCK -----
        const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        static object Get(object o, string name)
        {
            if (o == null) return null;
            var t = o.GetType();
            var f = t.GetField(name, BF);
            if (f != null) return f.GetValue(o);
            var p = t.GetProperty(name, BF);
            return p != null && p.GetIndexParameters().Length == 0 ? p.GetValue(o) : null;
        }

        static Component FindComponent(GameObject go, string fullName)
            => go == null ? null : go.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().FullName == fullName);

        public static Component FindCvrAvatar(GameObject go) => FindComponent(go, "ABI.CCK.Components.CVRAvatar");

        public static string FindAvatarGuid(GameObject go)
            => Get(FindComponent(go, "ABI.CCK.Components.CVRAssetInfo"), "objectId") as string;

        static float? F(object o) => o == null ? (float?)null : Convert.ToSingle(o, CultureInfo.InvariantCulture);
        static Vector2 V2(object o) => o is Vector2 v ? v : Vector2.zero;

        // ----- build the menu (like the mod's AvatarAdvancedSettingsToMenus + ReifyHierarchyInNames) -----
        public static AMMenus Build(GameObject avatarGo, bool advanced)
        {
            var m = new AMMenus();
            var settings = Get(Get(FindCvrAvatar(avatarGo), "avatarSettings"), "settings") as IEnumerable;
            if (settings == null) return m;

            foreach (var s in settings)
            {
                var name = Get(s, "name") as string;
                var item = ParamToItem(s, name, RootMenu, m, advanced);
                if (item == null) continue;
                var it = item.Value;
                it.name = name;
                m.Get(RootMenu).Add(it);
            }

            return Reify(m);
        }

        // the mod adds Emotes/Profiles to the main menu itself
        public static List<(string key, AMItem item)> RootExtras(bool emotes, bool profiles)
        {
            var list = new List<(string, AMItem)>();
            if (emotes)
                list.Add((EmotesKey, new AMItem
                {
                    name = "Emotes",
                    icon = "icon_avatar_emotes.svg",
                    kind = "Menu",
                    action = new AMAction { type = "menu", menu = PathJoin(RootMenu, "emotes") },
                }));
            if (profiles)
                list.Add((ProfilesKey, new AMItem
                {
                    name = "Profiles",
                    icon = "icon_avatar_settings_profile.svg",
                    kind = "Menu",
                    action = new AMAction { type = "menu", menu = PathJoin(RootMenu, "profiles") },
                }));
            return list;
        }

        static AMItem? ParamToItem(object s, string name, string menuPrefix, AMMenus m, bool advanced)
        {
            if (s == null || name == null) return null;
            if (name.EndsWith("<hidden>")) return null;

            var machineName = Get(s, "machineName") as string ?? "";
            var kind = Get(s, "type")?.ToString();
            var item = new AMItem { kind = kind };

            switch (kind)
            {
                case "Toggle":
                {
                    var (isImpulse, duration) = ParseImpulseTag(machineName);
                    item.enabled = Get(Get(s, "toggleSettings"), "defaultValue") as bool? ?? false;
                    item.action = new AMAction
                    {
                        type = "avatar parameter",
                        parameter = machineName,
                        control = isImpulse ? "impulse" : "toggle",
                        duration = duration,
                        value = 1f,
                    };
                    break;
                }

                case "Dropdown":
                {
                    var submenuName = PathJoin(menuPrefix, name);
                    var (isImpulse, duration) = ParseImpulseTag(machineName);
                    var dd = Get(s, "dropDownSettings");
                    var selected = Get(dd, "defaultValue") as int?;
                    item.action = new AMAction { type = "menu", menu = submenuName };

                    var sitems = new List<AMItem>();
                    if (Get(dd, "options") is IEnumerable options)
                    {
                        var index = 0;
                        foreach (var opt in options)
                        {
                            sitems.Add(new AMItem
                            {
                                name = Get(opt, "name") as string,
                                kind = "DropdownOption",
                                enabled = index == selected,
                                action = new AMAction
                                {
                                    type = "avatar parameter",
                                    parameter = machineName,
                                    control = isImpulse ? "impulse" : "toggle",
                                    duration = duration,
                                    value = index,
                                    exclusive_option = !isImpulse,
                                },
                            });
                            ++index;
                        }
                    }
                    m[submenuName] = sitems;
                    break;
                }

                case "Slider":
                    item.action = new AMAction
                    {
                        type = "avatar parameter",
                        parameter = machineName,
                        control = "radial",
                        default_value = F(Get(Get(s, "sliderSettings"), "defaultValue")),
                        min_value = 0f,
                        max_value = 1f,
                    };
                    break;

                case "InputSingle":
                    item.action = advanced
                        ? new AMAction // AdvActionMenu: number pad without limits
                        {
                            type = "avatar parameter",
                            parameter = machineName,
                            control = "number",
                            default_value = F(Get(Get(s, "inputSingleSettings"), "defaultValue")),
                        }
                        : new AMAction // original: 0-1 slider
                        {
                            type = "avatar parameter",
                            parameter = machineName,
                            control = "radial",
                            default_value = F(Get(Get(s, "inputSingleSettings"), "defaultValue")),
                            min_value = 0f,
                            max_value = 1f,
                        };
                    break;

                case "Joystick2D":
                {
                    var def = V2(Get(Get(s, "joystick2DSetting"), "defaultValue"));
                    item.action = new AMAction
                    {
                        type = "avatar parameter",
                        parameter = machineName,
                        control = "joystick_2d",
                        min_value_x = 0f, max_value_x = 1f, default_value_x = def.x,
                        min_value_y = 0f, max_value_y = 1f, default_value_y = def.y,
                    };
                    break;
                }

                case "InputVector2":
                {
                    var def = V2(Get(Get(s, "inputVector2Settings"), "defaultValue"));
                    item.action = new AMAction
                    {
                        type = "avatar parameter",
                        parameter = machineName,
                        control = "input_vector_2d",
                        min_value_x = 0f, max_value_x = 1f, default_value_x = def.x,
                        min_value_y = 0f, max_value_y = 1f, default_value_y = def.y,
                    };
                    break;
                }

                default:
                    // Color, Joystick3D, InputVector3 aren't supported by the mod (yet)
                    return null;
            }

            return item;
        }

        static readonly Regex ImpulseTagRegex = new Regex(
            @"\s*<impulse(?:=(?<duration>\d+(?:\.?\d*)))>$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static (bool isImpulse, float? duration) ParseImpulseTag(string machineName)
        {
            if (machineName.EndsWith("Impulse")) return (true, null);
            var match = ImpulseTagRegex.Match(machineName);
            if (!match.Success) return (false, null);
            var durationStr = match.Groups["duration"].Value;
            float? duration = null;
            if (durationStr != string.Empty &&
                float.TryParse(durationStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                duration = v;
            return (true, duration);
        }

        // 1:1 copy of the mod's ReifyHierarchyInNames
        static AMMenus Reify(AMMenus menus)
        {
            var m = new AMMenus();
            var hierarchyPairs = new HashSet<(string parent, string child)>();

            foreach (var x in menus)
            {
                foreach (var item in x.Value)
                {
                    var name = item.name;
                    var parents = x.Key;
                    var i = name.LastIndexOf(Sep);
                    if (i >= 0)
                    {
                        parents += Sep + item.name.Substring(0, i);
                        name = name.Substring(i + 1);
                    }

                    var item2 = item;
                    item2.name = name;
                    m.Get(parents).Add(item2);

                    var hierarchy = parents.Split(Sep);
                    var child = hierarchy[0];
                    for (var j = 1; j < hierarchy.Length; ++j)
                    {
                        var parent = child;
                        child = parent + Sep + hierarchy[j];
                        hierarchyPairs.Add((parent, child));
                    }
                }
            }

            foreach (var x in hierarchyPairs)
            {
                var i = x.child.LastIndexOf(Sep);
                var childName = i >= 0 ? x.child.Substring(i + 1) : x.child;
                var aitems = m.Get(x.parent);
                if (aitems.Any(it => it.action?.menu == x.child)) continue;
                aitems.Add(new AMItem
                {
                    name = childName,
                    kind = "Menu",
                    action = new AMAction { type = "menu", menu = x.child },
                });
            }

            return m;
        }
    }
}
