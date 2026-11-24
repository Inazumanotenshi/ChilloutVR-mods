using HarmonyLib;
using MelonLoader;
using UnityEngine;
using ABI_RC.Core.Savior;
using ABI_RC.Core.InteractionSystem;
using ABI.CCK.Scripts;
using System.Collections.Generic;
using System.Linq;
using cohtml;
using System;
using Newtonsoft.Json;
using System.IO;
using Valve.VR;
using VRBinding;
using ABI_RC.Systems.InputManagement;
using ABI_RC.Systems.GameEventSystem;
using ABI_RC.Core.UI;
using ABI_RC.Core.UI.UIRework.Managers;

using PlayerSetup = ABI_RC.Core.Player.PlayerSetup;
using SettingsType = ABI.CCK.Scripts.CVRAdvancedSettingsEntry.SettingsType;
using MovementSystem = ABI_RC.Systems.Movement.BetterBetterCharacterController;
using ABI_RC.Systems.Camera;
using System.Reflection;
using System.Runtime.InteropServices;

[assembly:MelonGame(null, "ChilloutVR")]
[assembly:MelonInfo(typeof(ActionMenu.ActionMenuMod), "AdvActionMenu", ActionMenu.ActionMenuMod.ModVersion, "Inazumanotenshi (based on Action Menu by daky)", "https://github.com/dakyneko/DakyModsCVR")]
[assembly:MelonAdditionalDependencies("VRBinding")]

namespace ActionMenu
{
    using static Daky.Dakytils;
    using MenuBuilder = Func<List<MenuItem>>;
    public class ActionMenuMod : MelonMod
    {
        // Compatibility with mods built for daky's "ActionMenu":
        // if the original is missing, every request for the "ActionMenu" assembly is answered with ours.
        // Namespace and classes are the same, so the API (Lib, API, ...) works unchanged.
        // Runs in the static constructor because MelonLoader checks the dependencies of other mods right after
        // creating all mods, i.e. before OnInitializeMelon.
        static ActionMenuMod()
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveOriginalActionMenu;
        }

        private static Assembly? ResolveOriginalActionMenu(object sender, ResolveEventArgs args)
        {
            try
            {
                if (new AssemblyName(args.Name).Name != "ActionMenu") return null;
                // daky's original itself is loaded, don't redirect anything
                if (AppDomain.CurrentDomain.GetAssemblies().Any(x => x.GetName().Name == "ActionMenu")) return null;
                return typeof(ActionMenuMod).Assembly;
            }
            catch
            {
                return null;
            }
        }

        public const string ModVersion = "0.3.0";

        // debug mode: verbose log and version/debug display below the menu
        internal static bool DebugMode => instance?.debugMode?.Value == true;

        /// <summary>
        /// Public library for all mods to use, you can extend this
        /// </summary>
        public class Lib
        {
            private ActionMenuMod instance;
            public readonly string prefixNs;

            public Lib()
            {
                instance = ActionMenuMod.instance;

                // auto detect namespace of caller so we can suffix identifier to avoid collision between mods
                // basically, reflection goes weeeeeeeeee
                var stackTrace = new System.Diagnostics.StackTrace();
                prefixNs = stackTrace.GetFrame(1).GetMethod().DeclaringType.Namespace;

                RegisterOnLoaded();
            }

            // auto-register your mod to create menus
            // if you don't need this, you can override it empty
            virtual protected void RegisterOnLoaded()
            {
                API.OnGlobalMenuLoaded += OnGlobalMenuLoaded;
                API.OnAvatarMenuLoaded += OnAvatarMenuLoaded;
            }

            virtual protected string modName => prefixNs; // by default your class name
            virtual protected string? modIcon => null; // accept: URLs, on-disk-path or inlined data:image/jpeg;base64…
            virtual protected string entry => "main"; // the name of your mod main menu, don't change unless you know what you're doing
            virtual protected string ModMenuPath(params string[] components)
                => prefixNs + hierarchySeparator + string.Join(hierarchySeparator.ToString(), components);

            // Mods can create a menu very easily by filling up some items here.
            virtual protected List<MenuItem> modMenuItems() => new();

            // Or if you need finer control, you will need to do more yourself
            // Create many menus yourself for your mod. Your menus will have automatically the prefixNs prefix.
            virtual public Menus BuildModMenu()
            {
                return new()
                {
                    // the main menu of the mod
                    [entry] = modMenuItems(),
                    // more submenus can be added separately here
                };
            }

            /// <summary>
            /// Or even finer control, total control but it comes with responsability: pick unique names for your menus!
            /// Override this to manipulate/add menus after all other menus are built. Usually not required for simple mod menus.
            /// </summary>
            /// <param name="menus"></param>
            virtual protected void OnGlobalMenuLoaded(Menus menus)
            {
                // the default behavior below makes modders' life easier and prevents collision between mods
                var m = BuildModMenu();
                if (m.Keys.SequenceEqual(new string[] { entry }) && m.GetWithDefault(entry).Count == 0)
                    return; // if empty don't create anything

                // add all mod menus and take care of the prefixNs prefix
                var menuPrefix = ModMenuPath();
                foreach (var x in m) {
                    var items = x.Value.Select(item =>
                    {
                        // when refer to other menu, add prefix but only if it's missing
                        if (item.action.type == "menu" && !item.action.menu.StartsWith(menuPrefix))
                            item.action.menu = ModMenuPath(item.action.menu);
                        return item;
                    });
                    // we check if prefix is already added by chance, only add if missing
                    var menuWithPrefix = !x.Key.StartsWith(menuPrefix) ? ModMenuPath(x.Key) : x.Key;
                    menus.GetWithDefault(menuWithPrefix).AddRange(items);
                }
                ModsMainMenu(menus).Add(new MenuItem()
                {
                    name = modName,
                    icon = modIcon,
                    action = new ItemAction() { type = "menu", menu = ModMenuPath(entry) },
                });
            }

            public static List<MenuItem> ModsMainMenu(Menus menus) => menus.GetWithDefault(modsMenuName);

            /// <summary>
            /// override this to manipulate avatar menus after they're built
            /// </summary>
            /// <param name="avatarGuid"></param>
            /// <param name="menus"></param>
            virtual protected void OnAvatarMenuLoaded(string avatarGuid, Menus menus)
            {
            }

            /// <summary>
            /// Nice way to edit menus by applying patches. This is to play nice with other mods as well.
            /// </summary>
            /// <param name="menus"></param>
            /// <param name="patch"></param>
            public static void ApplyMenuPatch(Menus menus, MenusPatch patch) => patch.ApplyToMenu(menus);

            private int uniqueItemId = 0;
            private string nextUniqueItemId(string suffix) => $"item_{uniqueItemId++}_{suffix}";

            // New easier API to create MenuItem with ItemAction in 1-call
            public MenuItem Button(string label, Action callback, bool? enabled = null, string? icon = null, bool exclusiveOption = false)
            {
                return new MenuItem(label, icon, BuildButtonItem(nextUniqueItemId("button"), callback, exclusiveOption: exclusiveOption), enabled: enabled);
            }
            public MenuItem Toggle(string label, Action<bool> callback, bool? enabled = null, string? icon = null)
            {
                return new MenuItem(label, icon, BuildToggleItem(nextUniqueItemId(label), callback), enabled: enabled);
            }
            public MenuItem Impulse(string label, Action<bool> callback, float duration = 1f, object? value = null, string? icon = null)
            {
                return new MenuItem(label, icon, BuildImpulseItem(nextUniqueItemId(label), callback, duration: duration, value: value));
            }
            public MenuItem Radial(string label, Action<float> callback, string? icon = null,
                float? minValue = null, float? maxValue = null, float? defaultValue = null)
            {
                return new MenuItem(label, icon, BuildRadialItem(nextUniqueItemId("radial"), callback,
                    minValue: minValue, maxValue: maxValue, defaultValue: defaultValue));
            }
            public MenuItem Menu(string label, MenuBuilder menuBuilder, string? icon = null)
            {
                return new MenuItem(label, icon, BuildCallbackMenu(nextUniqueItemId("menu"), menuBuilder));
            }
            public MenuItem Joystick2D(string label, Action<float, float> callback, string? icon = null,
                float? minValueX = null, float? maxValueX = null, float? defaultValueX = null,
                float? minValueY = null, float? maxValueY = null, float? defaultValueY = null)
            {
                return new MenuItem(label, icon, BuildJoystick2D(nextUniqueItemId("joystick2d"), callback,
                minValueX: minValueX, maxValueX: maxValueX, defaultValueX: defaultValueX,
                minValueY: minValueY, maxValueY: maxValueY, defaultValueY: defaultValueY));
            }
            public MenuItem InputVector2D(string label, Action<float, float> callback, string? icon = null,
                float? minValueX = null, float? maxValueX = null, float? defaultValueX = null,
                float? minValueY = null, float? maxValueY = null, float? defaultValueY = null)
            {
                return new MenuItem(label, icon, BuildInputVector2D(nextUniqueItemId("vector2d"), callback,
                minValueX: minValueX, maxValueX: maxValueX, defaultValueX: defaultValueX,
                minValueY: minValueY, maxValueY: maxValueY, defaultValueY: defaultValueY));
            }

            // below is older API where you need to build more stuff yourself

            /// <summary>
            /// Create an ItemAction when triggered, will call your function
            /// Basically for button/widget item for callback in your mod
            /// </summary>
            /// <param name="name"></param>
            /// <param name="callback"></param>
            /// <param name="exclusiveOption"></param>
            /// <returns></returns>
            public ItemAction BuildButtonItem(string name, Action callback, bool exclusiveOption = false)
            {
                var identifier = prefixNs + ".call." + name;
                instance.callbackItems[identifier] = callback;
                return new ItemAction()
                {
                    type = "callback",
                    parameter = identifier,
                    exclusive_option = exclusiveOption,
                };
            }

            private ItemAction BuildBoolItem(string name, Action<bool> callback, string control,
                object? value = null, float? duration = null)
            {
                var identifier = prefixNs + "." + control + "." + name;
                instance.callbackItems_bool[identifier] = callback;
                return new ItemAction()
                {
                    type = "callback",
                    parameter = identifier,
                    control = control,
                    toggle = true,
                    duration = duration,
                    value = value,
                };
            }

            /// <summary>
            /// Creates a button with two states: enabled and disabled.
            /// </summary>
            /// <param name="name"></param>
            /// <param name="callback"></param>
            /// <returns></returns>
            public ItemAction BuildToggleItem(string name, Action<bool> callback)
                => BuildBoolItem(name, callback, "toggle", value: true);
            /// <summary>
            /// Creates a button that temporarily switch its value, then back to default_value
            /// </summary>
            /// <param name="name"></param>
            /// <param name="callback"></param>
            /// <param name="duration"></param>
            /// <param name="value"></param>
            /// <returns></returns>
            public ItemAction BuildImpulseItem(string name, Action<bool> callback, float duration = 1f, object? value = null)
                => BuildBoolItem(name, callback, "impulse", value: value, duration: duration);

            /// <summary>
            /// Creates a radial widget for picking values between a min and max.
            /// </summary>
            /// <param name="name"></param>
            /// <param name="callback"></param>
            /// <param name="minValue"></param>
            /// <param name="maxValue"></param>
            /// <param name="defaultValue"></param>
            /// <returns></returns>
            public ItemAction BuildRadialItem(string name, Action<float> callback,
                float? minValue = null, float? maxValue = null, float? defaultValue = null)
            {
                var identifier = prefixNs + ".radial." + name;
                instance.callbackItems_float[identifier] = callback;
                return new ItemAction() {
                    type = "callback",
                    parameter = identifier,
                    control = "radial",
                    min_value = minValue,
                    max_value = maxValue,
                    default_value = defaultValue,
                };
            }

            private ItemAction Build2DItem(string name, Action<float, float> callback, string control,
                float? minValueX = null, float? maxValueX = null, float? defaultValueX = null,
                float? minValueY = null, float? maxValueY = null, float? defaultValueY = null)
            {
                var identifier = prefixNs + "." + control + "." + name;
                instance.callbackItems_float_float[identifier] = callback;
                return new ItemAction() {
                    type = "callback",
                    parameter = identifier,
                    control = control,
                    min_value_x = minValueX,
                    max_value_x = maxValueX,
                    default_value_x = defaultValueX,
                    min_value_y = minValueY,
                    max_value_y = maxValueY,
                    default_value_y = defaultValueY,
                };
            }

            /// <summary>
            /// Creates a 2D widget for picking two values simultaneously setting absolute coordinates, between min and max values.
            /// </summary>
            /// <param name="name"></param>
            /// <param name="callback"></param>
            /// <param name="minValueX"></param>
            /// <param name="maxValueX"></param>
            /// <param name="defaultValueX"></param>
            /// <param name="minValueY"></param>
            /// <param name="maxValueY"></param>
            /// <param name="defaultValueY"></param>
            /// <returns></returns>
            public ItemAction BuildJoystick2D(string name, Action<float, float> callback,
                float? minValueX = null, float? maxValueX = null, float? defaultValueX = null,
                float? minValueY = null, float? maxValueY = null, float? defaultValueY = null)
                => Build2DItem(name, callback, "joystick_2d",
                        minValueX: minValueX, maxValueX: maxValueX, defaultValueX: defaultValueX,
                        minValueY: minValueY, maxValueY: maxValueY, defaultValueY: defaultValueY
                    );
            /// <summary>
            /// Creates a 2D widget for picking two values simultaneously moving in relative coordinates, between min and max values.
            /// </summary>
            /// <param name="name"></param>
            /// <param name="callback"></param>
            /// <param name="minValueX"></param>
            /// <param name="maxValueX"></param>
            /// <param name="defaultValueX"></param>
            /// <param name="minValueY"></param>
            /// <param name="maxValueY"></param>
            /// <param name="defaultValueY"></param>
            /// <returns></returns>
            public ItemAction BuildInputVector2D(string name, Action<float, float> callback,
                float? minValueX = null, float? maxValueX = null, float? defaultValueX = null,
                float? minValueY = null, float? maxValueY = null, float? defaultValueY = null)
                => Build2DItem(name, callback, "input_vector_2d",
                        minValueX: minValueX, maxValueX: maxValueX, defaultValueX: defaultValueX,
                        minValueY: minValueY, maxValueY: maxValueY, defaultValueY: defaultValueY
                    );

            /// <summary>
            /// Create an ItemAction when triggered, will call you back so you can build your own menu dynamically
            /// Basically building dynamic menus from a mod
            /// </summary>
            /// <param name="name"></param>
            /// <param name="menuBuilder"></param>
            /// <returns></returns>
            public ItemAction BuildCallbackMenu(string name, MenuBuilder menuBuilder)
            {
                var identifier = prefixNs + "." + name;
                instance.dynamic_menus[identifier] = menuBuilder;
                return new ItemAction()
                {
                    type = "dynamic menu",
                    menu = identifier,
                };
            }

            /// <summary>
            /// if you want to expose your MelonPreference into a menu, build them with this.
            /// beware that it's still rudimentary: toggles are fine but floats are between 0 and 1, that's all
            /// to refer it, look below at the path, it has prefix, example: YourNameMod/settings
            /// </summary>
            /// <param name="melonPrefs"></param>
            /// <returns></returns>
            public Menus BuildMelonPrefsMenus(List<MelonPreferences_Entry> melonPrefs)
            {
                var m = new Menus();
                var items = m.GetWithDefault(Path(prefixNs, "settings"));

                foreach (var e_ in melonPrefs)
                {
                    switch (e_)
                    {
                        case MelonPreferences_Entry<bool> e:
                            items.Add(new MenuItem()
                            {
                                name = e.DisplayName,
                                enabled = e.Value,
                                action = new()
                                {
                                    type = "set melon preference",
                                    control = "toggle",
                                    parameter = e.Identifier,
                                    toggle = true,
                                }
                            });
                            break;

                        case MelonPreferences_Entry<float> e:
                            items.Add(new MenuItem()
                            {
                                name = e.DisplayName,
                                action = new()
                                {
                                    type = "set melon preference",
                                    control = "radial",
                                    parameter = e.Identifier,
                                    default_value = e.Value,
                                }
                            });
                            break;

                        case MelonPreferences_Entry<Vector2> e:
                            var v = e.Value;
                            items.Add(new MenuItem()
                            {
                                name = e.DisplayName,
                                action = new()
                                {
                                    type = "set melon preference",
                                    control = "input_vector_2d",
                                    parameter = e.Identifier,
                                    default_value_x = v.x,
                                    default_value_y = v.y,
                                }
                            });
                            break;

                        // TODO: add support for enum and other types
                        default:
                            // e.g. key bindings (KeyCode), they don't fit into the wheel; only report in debug mode
                            if (DebugMode) logger.Msg($"BuildMelonPrefsMenus {e_.Identifier} skipped, unsupported type {e_.GetReflectedType()}");
                            break;
                    }
                }

                return m;
            }
        }

        // various parameters for consistency everywhere
        public static readonly string mainMenuName = "main";
        public static readonly string modsMenuName = "mods";
        public static readonly string avatarMenuName = "avatar";
        public static readonly char hierarchySeparator = '/'; // mostly for ReifyHierarchyInNames (avatar menus)
        public static readonly string couiPath = @"ChilloutVR_Data\StreamingAssets\Cohtml\UIResources\AdvActionMenu";
        public static readonly string couiUrl = "coui://UIResources/AdvActionMenu";
        // 500 wide, 630 high: 130px at the top for the number pad text box, the 500x500 menu below
        public static readonly Vector2Int canvasSize = new Vector2Int(500, 630);
        // offset of the menu center from the canvas center (in px, downwards)
        private const float menuCenterOffsetPx = 130f + 250f - 315f;
        public static readonly float menuBaseSizeDesktop = 1f;
        public static readonly float menuBaseSizeVr = 0.6f;
        public static readonly string[] couiFiles = new string[]
        {
            "index.html", "index.js", "index.css", "actionmenu.json", "Montserrat-Regular.ttf",
            "icon_actionmenu.svg", "icon_menu.svg", "icon_back.svg", "icon_avatar_emotes.svg", "icon_melon.svg",
            "icon_avatar_settings_profile.svg",
            "icon_np_backspace.svg", "icon_np_cancel.svg",
            // own icons in the style of the CVR icons
            "icon_hud.svg", "icon_audio.svg", "icon_animation.svg", "icon_anim_spawn.svg", "icon_anim_fade.svg", "icon_anim_off.svg", "icon_scale_avatar.svg", "icon_resize.svg", "icon_move.svg", "icon_rotate.svg", "icon_flick.svg", "icon_back_item.svg", "icon_pages.svg", "icon_debug.svg", "icon_mod.svg", "icon_mods.png", "icon_clock.svg", "icon_fps.svg", "icon_avatar_height.svg",
        };

        // Show or hide the Action menu, use it cautiously
        public static void Toggle(bool show) => instance.ToggleMenu(show);
        // Build the path of hierarchy of submenu
        public static string Path(params string[] components) => string.Join(hierarchySeparator.ToString(), components);

        // Build all menu hierarchy found in names (hierarchy separator character)
        // Example if there are items: Clothing/Dress/Long and Clothing/Dress/Short then the items themselves will be moved into new nested menus Clothing > Dress
        public static Menus ReifyHierarchyInNames(Menus menus)
        {
            var m = new Menus(); // we'll rebuild the menu from scratch, ensuring it's clean
            var hierarchyPairs = new HashSet<(string parent, string child)>();

            foreach (var x in menus)
            {
                foreach (var item in x.Value)
                {
                    // split basename from parents, like uri or paths
                    var name = item.name;
                    var parents = x.Key;
                    var i = name.LastIndexOf(hierarchySeparator);
                    if (i >= 0) { // is in a folder
                        parents += hierarchySeparator + item.name.Substring(0, i);
                        name = name.Substring(i + 1);
                    }

                    var item2 = item;
                    item2.name = name;
                    var aitems = m.GetWithDefault(parents);
                    aitems.Add(item2);

                    // register the hierarchy upward
                    var hierarchy = parents.Split(hierarchySeparator);
                    var child = hierarchy[0];
                    for (var j = 1; j < hierarchy.Length; ++j) {
                        var parent = child;
                        child = parent + hierarchySeparator + hierarchy[j];
                        hierarchyPairs.Add((parent, child));
                    }
                }
            }

            // now build the hierarchy, non-leaves up to the root menu (hierarchy of menus)
            foreach (var x in hierarchyPairs)
            {
                var parent = x.parent;
                var child = x.child;
                var i = child.LastIndexOf(hierarchySeparator);
                var childName = (i >= 0) ? x.child.Substring(i + 1) : child;

                var aitems = m.GetWithDefault(parent);
                // first check if the link to this menu was already created
                // for ex: avatar drop down created by AvatarParamToItem already add a reference to its menu
                if (aitems.Any(item => item.action.menu == child)) continue;

                var item = new MenuItem
                {
                    name = childName,
                    action = new ItemAction
                    {
                        type = "menu",
                        menu = child,
                    },
                };
                aitems.Add(item);
            }

            return m;
        }

        // Allows to update the state of any items having those item action
        // especially useful for toggle where the state is changed externally, use this.
        public static void UpdateItemState(ItemAction action) => instance.SendItemUpdate(action);

        public static Menus AvatarAdvancedSettingsToMenus(List<CVRAdvancedSettingsEntry> advSettings, Animator animator, string menuPrefix)
        {
            var m = new Menus();

            if (advSettings == null || advSettings.Count == 0)
                return m;

            // Build menus from the avatar parameters, focus on items directly (leaves in the hierarchy)
            foreach (var s in advSettings)
            {
                switch (AvatarParamToItem(s, animator, menuPrefix, m))
                {
                    case null:
                        continue;

                    case MenuItem item:
                        item.name = s.name;
                        if (DebugMode) logger.Msg($"Avatar setting \"{s.name}\" ({s.machineName}): {s.type} -> {item.action.control ?? item.action.type}");
                        var aitems = m.GetWithDefault(menuPrefix);
                        aitems.Add(item);
                        break;
                }
            }

            return ReifyHierarchyInNames(m);
        }

        public static Menus SplitOverCrowdedMenus(Menus m, uint overcrowdedMenuValue = 8)
        {
            var pagePrefix = "page-";
            var keys = m.Keys.OrderBy(n => -n.Length).ToArray(); // process bottom-up (deepest first)
            foreach (var parents in keys)
            {
                var items = m[parents];
                if (items.Count <= overcrowdedMenuValue) continue;

                // first create new submenu for each page with the crowded items
                var page = 1;
                var pageItems = 0;
                foreach (var item in items)
                {
                    if (pageItems >= overcrowdedMenuValue)
                    {
                        ++page;
                        pageItems = 0;
                    }

                    m.GetWithDefault(Path(parents, pagePrefix + page)).Add(item);
                    ++pageItems;
                };

                // now create links to the submenu in the parent
                items.Clear();
                for (var i = 1; i <= page; ++i)
                {
                    items.Add(new MenuItem()
                    {
                        name = $"Page {i}",
                        action = new ItemAction { type = "menu", menu = Path(parents, pagePrefix + i) },
                    });
                }
            }

            return m;
        }

        // Older override JSONs (e.g. from the icon generator) still contain daky's 0-1 slider for
        // Input Single entries. If an entry is exactly such a slider and the avatar parameter is an Input Single,
        // it gets the number pad instead. Other limits that were set on purpose stay unchanged.
        private static void UpgradeInputSingleToNumber(Menus m, List<CVRAdvancedSettingsEntry> advSettings)
        {
            if (advSettings == null) return;
            var inputSingles = new HashSet<string>(advSettings
                .Where(s => s.type == SettingsType.InputSingle && !string.IsNullOrEmpty(s.machineName))
                .Select(s => s.machineName));
            if (inputSingles.Count == 0) return;

            var upgraded = 0;
            foreach (var items in m.Values)
            {
                if (items == null) continue;
                for (var i = 0; i < items.Count; i++)
                {
                    var item = items[i]; // MenuItem is a struct: change a copy and write it back
                    var a = item.action;
                    if (a.type != "avatar parameter" || a.control != "radial" || a.parameter == null || a.control_locked) continue;
                    if (!inputSingles.Contains(a.parameter)) continue;
                    if ((a.min_value ?? 0f) != 0f || (a.max_value ?? 1f) != 1f) continue;

                    a.control = "number";
                    a.min_value = null;
                    a.max_value = null;
                    item.action = a;
                    items[i] = item;
                    ++upgraded;
                }
            }
            if (upgraded > 0)
                logger.Msg($"{upgraded} Input Single item(s) from overrides now use the number pad");
            else if (DebugMode)
                logger.Msg($"Number pad upgrade: avatar has {inputSingles.Count} Input Single(s), none of them was a 0-1 slider in the overrides");
        }

        public static MenuItem? AvatarParamToItem(CVRAdvancedSettingsEntry s, Animator animator,
            // Menus is used in case of nested menus like drop down
            string menuPrefix, Menus m)
        {
            if (s.name.EndsWith("<hidden>")) return null;

            var item = new MenuItem();

            // build the items in the menu
            switch (s.type)
            {
                case SettingsType.GameObjectToggle: {
                    var (isImpulse, duration) = ParseImpulseTag(s.machineName);
                    item.enabled = animator?.GetBool(s.machineName) ?? s.toggleSettings?.defaultValue ?? false;
                    item.action = new ItemAction
                    {
                        type = "avatar parameter",
                        parameter = s.machineName,
                        control = isImpulse ? "impulse" : "toggle",
                        duration = duration,
                        value = 1f,
                    };
                    break;
                }

                case SettingsType.GameObjectDropdown: {
                    var submenuName = Path(menuPrefix, s.name);
                    // if parameter name has suffix Impulse, adapt control type
                    var (isImpulse, duration) = ParseImpulseTag(s.machineName);
                    var dd = s.dropDownSettings;
                    var selectedValue = animator?.GetInteger(s.machineName) ?? dd?.defaultValue;

                    item.action = new ItemAction
                    {
                        type = "menu",
                        menu = submenuName,
                    };

                    List<MenuItem> sitems = dd.options.Select((o, index) => new MenuItem
                    {
                        name = o.name,
                        enabled = index == selectedValue,
                        action = new ItemAction
                        {
                            type = "avatar parameter",
                            parameter = s.machineName,
                            control = isImpulse ? "impulse" : "toggle",
                            duration = duration,
                            value = index,
                            exclusive_option = !isImpulse,
                        },
                    }).ToList();

                        m.Add(submenuName, sitems);
                    break;
                }

                case SettingsType.Slider: {
                    var defaultValue = animator?.GetFloat(s.machineName) ?? s.sliderSettings?.defaultValue;
                    item.action = new ItemAction
                    {
                        type = "avatar parameter",
                        parameter = s.machineName,
                        control = "radial",
                        default_value = defaultValue,
                        min_value = 0.0f,
                        max_value = 1.0f,
                    };
                    break;
                }

                case SettingsType.InputSingle: {
                    var defaultValue = animator?.GetFloat(s.machineName) ?? s.inputSingleSettings?.defaultValue;
                    item.action = new ItemAction
                    {
                        type = "avatar parameter",
                        parameter = s.machineName,
                        // free number input with the number pad; min_value/max_value can be set in the override JSON
                        control = "number",
                        default_value = defaultValue,
                    };
                    break;
                }

                case SettingsType.Joystick2D: {
                    var sjoy = s.joystick2DSetting;
                    var defaultValueX = animator?.GetFloat(s.machineName + "-x") ?? sjoy.defaultValue.x;
                    var defaultValueY = animator?.GetFloat(s.machineName + "-y") ?? sjoy.defaultValue.y;
                    item.action = new ItemAction
                    {
                        type = "avatar parameter",
                        parameter = s.machineName,
                        control = "joystick_2d",
                        min_value_x = 0.0f, // TODO: cvr seems to ignore min/max defined in unity, sad :(
                        max_value_x = 1.0f,
                        default_value_x = defaultValueX,
                        min_value_y = 0.0f,
                        max_value_y = 1.0f,
                        default_value_y = defaultValueY,
                    };
                    break;
                }

                case SettingsType.InputVector2: {
                    var svec = s.inputVector2Settings;
                    item.action = new ItemAction
                    {
                        type = "avatar parameter",
                        parameter = s.machineName,
                        control = "input_vector_2d",
                        min_value_x = 0.0f, // TODO: cvr has no min max values so we're left guessing here
                        max_value_x = 1.0f,
                        default_value_x = svec.defaultValue.x,
                        min_value_y = 0.0f,
                        max_value_y = 1.0f,
                        default_value_y = svec.defaultValue.y,
                    };
                    break;
                }

                case SettingsType.MaterialColor:
                case SettingsType.Joystick3D:
                case SettingsType.InputVector3:
                    logger.Warning($"Avatar parameter {s.name} ignored, its type {s.type} is not supported yet");
                    return null; // TODO: unsupported
            };

            return item;
        }

        private static readonly System.Text.RegularExpressions.Regex impulseTagRegex = new System.Text.RegularExpressions.Regex(
            @"\s*<impulse(?:=(?<duration>\d+(?:\.?\d*)))>$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled
            );

        private static (bool isImpulse, float? duration) ParseImpulseTag(string machineName)
        {
            if (machineName.EndsWith("Impulse")) return (isImpulse: true, duration: null);
            var match = impulseTagRegex.Match(machineName);
            if (!match.Success) return (isImpulse: false, duration: null);
            var durationStr = match.Groups["duration"].Value;
            float? duration = null;
            if (durationStr != string.Empty)
                if (float.TryParse(durationStr, out var value))
                    duration = value;
                else
                    logger.Warning($"Avatar parameter {machineName} is impulse but invalid duration format: {durationStr}");
            return (isImpulse: true, duration: duration);
        }


        // ---------------------------
        // WARNING: implementation follows, all stuff below are private and shouldn't be used if possible, please
        // ---------------------------
        private static ActionMenuMod instance;
        private static MelonLogger.Instance logger;
        internal static MelonLogger.Instance Log => logger;

        // rebuild the menus and send them to the wheel (e.g. after the mod slots were changed in the Quick Menu)
        internal static void RefreshMenus()
        {
            if (instance == null || cohtmlView?.View == null || cohtmlReadyState < 1) return;
            instance.OnActionMenuReady();
        }
        private static Transform menuTransform;
        private static CohtmlView cohtmlView;
        private static Collider menuCollider;
        private static MeshRenderer? menuRenderer;
        // whether the menu is open; the view itself stays visible a moment longer when closing, for the animation
        private static bool menuOpen = false;
        // true = menu is completely hidden (no close animation running anymore either)
        private static bool menuHidden = true;
        private static int closeToken = 0;
        private const float closeAnimationSeconds = 0.22f;
        private static CVR_MenuManager menuManager;
        private static Animator menuAnimator;
        private static int cohtmlReadyState = 0; // 0=startup, 1=binding ready, 2=ActionMenuReady
        private static bool anchorToLeftHand = true;
        private static Lib ourLib;

        // our melon prefs
        private MelonPreferences_Category melonPrefs;
        private Dictionary<string, MelonPreferences_Entry> melonPrefsMap;
        private MelonPreferences_Entry<bool> flickSelection, boringBackButton, dontInstallResources,
            splitAvatarOvercrowdedMenu, scaleWithAvatar, debugMode, showClock, showFps, clock12h;
        private MelonPreferences_Entry<string> animationStyle;
        private static readonly string[] animationStyles = { "spawn", "fade", "disabled" };
        private bool AnimationsOn => animationStyle?.Value != "disabled";
        private MelonPreferences_Entry<float> menuSize;
        private MelonPreferences_Entry<Vector2> menuPositionOffset, menuRotationXY;
        private MelonPreferences_Entry<KeyCode> openKeyBinding, reloadKeyBinding;

        // for mod dynamic items and menus: unique identifier -> function or menu
        private Dictionary<string, Action> callbackItems = new();
        private Dictionary<string, Action<bool>> callbackItems_bool = new();
        private Dictionary<string, Action<float>> callbackItems_float = new();
        private Dictionary<string, Action<float, float>> callbackItems_float_float = new();
        private Dictionary<string, MenuBuilder> dynamic_menus = new();

        public override void OnInitializeMelon()
        {
            logger = LoggerInstance;
            instance = this;
            ourLib = new OurLib();

            // own category so we don't share the settings with daky's original
            melonPrefs = MelonPreferences.CreateCategory("AdvActionMenu", "AdvActionMenu");
            flickSelection = melonPrefs.CreateEntry("flick_selection", false, "Flick select",
                description: "Trigger items by just selecting one and recentering selection (no need for trigger)");
            boringBackButton = melonPrefs.CreateEntry("boring_back_button", false, "Eccentric 'Back'",
                description: "Show the Back button like other items (not in the middle), as the last element in all menus");
            dontInstallResources = melonPrefs.CreateEntry("dont_install_resources", false, "Dev mode",
                description: "Don't install nor overwrite the resource files (useful for developping the Action Menu)");
            splitAvatarOvercrowdedMenu = melonPrefs.CreateEntry("split_overcrowded_avatar_menu", false, "No crowded menus",
                description: "Split avatar menu in multiple pages when it's too crowded");
            animationStyle = melonPrefs.CreateEntry("animation_style", "spawn", "Animation",
                description: "How the tiles appear when opening the menu or switching to a submenu: spawn, fade or disabled");
            if (!animationStyles.Contains(animationStyle.Value)) animationStyle.Value = "spawn";
            debugMode = melonPrefs.CreateEntry("debug_mode", false, "Debug mode",
                description: "Verbose logging and show version and debug info below the menu");
            showClock = melonPrefs.CreateEntry("show_clock", true, "Show clock",
                description: "Show the time in the top left corner of the menu");
            showFps = melonPrefs.CreateEntry("show_fps", false, "Show FPS",
                description: "Show the frame rate in the top right corner of the menu");
            clock12h = melonPrefs.CreateEntry("clock_12h", false, "12-hour clock",
                description: "Show the time as 12-hour clock with AM/PM instead of 24-hour");
            scaleWithAvatar = melonPrefs.CreateEntry("scale_with_avatar", true, "Scale with avatar",
                description: "Scale the menu size and distance with your avatar height, also live while using avatar scaling (VR and desktop)");
            menuSize = melonPrefs.CreateEntry("menu_size", 0.5f, "Resize",
                description: "Resize the menu bigger or small as you see fit");
            menuPositionOffset = melonPrefs.CreateEntry("menu_position_offset", 0.5f * Vector2.one, "Reposition",
                description: "Move the menu off-center by this offset (in ratio of the screen height)");
            menuRotationXY = melonPrefs.CreateEntry("menu_rotation_xy", Vector2.zero, "Rotation XY",
                description: "Rotate the menu in the X and Y axis (tilt)");
            openKeyBinding = melonPrefs.CreateEntry("open_key_binding", KeyCode.R, "Open binding",
                description: "Key binding to open the menu");
            reloadKeyBinding = melonPrefs.CreateEntry("reload_key_binding", KeyCode.F3, "Reload binding",
                description: "Key binding to reload the menu (combine with shift for config reload)");
            originalChoice = melonPrefs.CreateEntry("original_action_menu", "ask", "Original Action Menu",
                description: "What to do when daky's original Action Menu is installed too: ask, advanced (pause the original) or original (pause AdvActionMenu)");
            ModSlots.Init(melonPrefs);

            // Is daky's original installed too? Then ask which menu should be used
            if (!HandleOriginalActionMenu())
            {
                dormant = true;
                logger.Msg("Daky's original Action Menu is used, AdvActionMenu stays inactive (change original_action_menu in the settings to switch)");
                return;
            }

            // settings of other mods in the wheel (picked in the Quick Menu, Misc tab)
            ModSlots.EnsureIconFolder();
            ModSlots.SetupQuickMenu();

            melonPrefsMap = new();
            foreach (var e in melonPrefs.Entries)
                melonPrefsMap.Add(e.Identifier, e);
            BuildOurMelonPrefsMenus();

            // listen to change to our melon prefs so we can reload the action menu
            // some like positionOffset already change on the fly, so we don't want to reload!
            foreach (var e_ in melonPrefs.Entries)
                switch (e_) {
                    // clock/FPS: update live instead of reloading the whole UI
                    // (reloading closed and reopened the menu, in VR you could move in between)
                    case MelonPreferences_Entry<bool> e when e == showClock || e == showFps || e == clock12h:
                        e.OnEntryValueChanged.Subscribe((_, _) => {
                            hudForce = true;
                            SendHudInfo();
                        });
                        break;

                    // Before: reload the whole UI (FullReload). That closed and reopened the menu,
                    // in VR you could move in between and you ended up in the main menu again.
                    // Now: only send the menus again, the menu stays open and in the same submenu.
                    case MelonPreferences_Entry<bool> e:
                        e.OnEntryValueChanged.Subscribe((_, _) => SoftReload());
                        break;

                    case MelonPreferences_Entry<string> e when e.Identifier == animationStyle.Identifier:
                        e.OnEntryValueChanged.Subscribe((_, _) => SoftReload());
                        break;

                    default:
                        break; // nothing
                }

            // some need custom handling
            menuSize.OnEntryValueChanged.Subscribe((_, v) => {
                UpdateMenuScale();
            });

            CVRGameEventSystem.MainMenu.OnOpen.AddListener(() => ToggleMenu(false, handleDesktopInputs: false));
            CVRGameEventSystem.QuickMenu.OnOpen.AddListener(() => ToggleMenu(false, handleDesktopInputs: false));
            CVRGameEventSystem.Microphone.OnMute.AddListener(() => OnCVRMicrophoneToggle(false));
            CVRGameEventSystem.Microphone.OnUnmute.AddListener(() => OnCVRMicrophoneToggle(true));

            // immobilize when the action menu is open
            // FIXME: this stops the avatar animator from moving too, but not ideal, cannot fly anymore or rotate head
            HarmonyInstance.Patch(
                SymbolExtensions.GetMethodInfo(() => default(MovementSystem).Update()),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(ActionMenuMod), nameof(OnUpdateMovementSystem))));

            // build avatar menu from parameters after avatar is loaded
            HarmonyInstance.Patch(
                SymbolExtensions.GetMethodInfo(() => default(PlayerSetup).initializeAdvancedAvatarSettings()),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(ActionMenuMod), nameof(OnAvatarAdvancedSettings))));

            // monitor game changes to update menu items state
            HarmonyInstance.Patch(
                SymbolExtensions.GetMethodInfo(() => default(PortableCameraController).Toggle()),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(ActionMenuMod), nameof(OnCVRCameraToggle))));
            HarmonyInstance.Patch(
                SymbolExtensions.GetMethodInfo(() => default(PlayerSetup).SwitchSeatedPlay(default)),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(ActionMenuMod), nameof(OnCVRSeatedToggle))));
            HarmonyInstance.Patch(
                SymbolExtensions.GetMethodInfo(() => default(MovementSystem).ToggleFlight()),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(ActionMenuMod), nameof(OnCVRFlyToggle))));

            // cohtml reads files so let's install all that stuff, it's easier for everybody
            if (dontInstallResources.Value)
            {
                logger.Msg($"We won't install resource files as requested. Beware of updates though.");
            }
            else
            {
                Directory.CreateDirectory(couiPath);
                var couiNs = this.GetType().Namespace + ".UIResources";
                foreach (var fname in couiFiles)
                {
                    var raw = BytesFromAssembly(couiNs, fname);
                    if (raw == null)
                    {
                        logger.Error($"File missing from assembly {fname}");
                        continue;
                    }
                    File.WriteAllBytes(couiPath + @"\" + fname, raw);
                }
                if (DebugMode) logger.Msg($"Installed {couiFiles.Length} resource files from assembly into {couiPath}");
            }
            MelonCoroutines.Start(WaitCohtmlSpawned());

            VRBindingMod.RegisterBinding("ActionMenuOpen", "Open Action Menu (left hand)", VRBindingMod.Requirement.optional, a =>
            {
                if (a.GetStateDown(SteamVR_Input_Sources.Any))
                    ToggleMenu(!menuOpen, leftSide: true);
            });
            VRBindingMod.RegisterBinding("ActionMenuOpenRight", "Open Action Menu (right hand)", VRBindingMod.Requirement.optional, a =>
            {
                if (a.GetStateDown(SteamVR_Input_Sources.Any))
                    ToggleMenu(!menuOpen, leftSide: false);
            });
        }

        private static bool OnUpdateMovementSystem(MovementSystem __instance)
        {
            if (!MetaPort.Instance.isUsingVr) return true;
            return !menuOpen; // TODO: animation still run, prevent emotes
        }

        private void MenuManagerRegisterEvents()
        {
            var view = cohtmlView.View;
            if (DebugMode) logger.Msg($"MenuManagerRegisterEvents called {view}");
            view.RegisterForEvent("ActionMenuReady", new Action(OnActionMenuReady));
            view.BindCall("CVRAppCallSystemCall", new Action<string, string, string, string, string>(menuManager.HandleSystemCall));
            view.BindCall("CVRAppCallSaveSetting", new Action<string, string>(MetaPort.Instance.settings.SetSetting));
            view.BindCall("SetHudVisible", new Action<bool>(OnSetHudVisible));
            view.BindCall("SetMelonPreference", new Action<string, string>(OnSetMelonPreference));
            view.BindCall("ItemCallback", new Action<string>(OnItemCallback));
            view.BindCall("ItemCallback_bool", new Action<string, bool>(OnItemCallback_bool));
            view.BindCall("ItemCallback_float", new Action<string, float>(OnItemCallback_float));
            view.BindCall("ItemCallback_float_float", new Action<string, float, float>(OnItemCallback_float_float));
            view.BindCall("RequestDynamicMenu", new Action<string>(OnRequestDynamicMenu));

            // TODO: adjust effect
            var material = menuTransform.GetComponent<MeshRenderer>().materials[0];
            material.SetTexture("_DesolvePattern", menuManager.pattern);
            material.SetTexture("_DesolveTiming", menuManager.timing);
            material.SetTextureScale("_DesolvePattern", new Vector2(1f, 1f));

            cohtmlReadyState = 1;
        }

        private System.Collections.IEnumerator WaitCohtmlSpawned()
        {
            GameObject cwv;
            CohtmlUISystem cohtmlUISystem;
            while ((cwv = GameObject.Find("/Cohtml/MainMenuParent/Offset/CohtmlWorldView")) == null)
                yield return null;
            while ((cohtmlUISystem = GameObject.Find("/Cohtml/CohtmlDefaultUISystem")?.GetComponent<CohtmlControlledUISystem>()) == null)
                yield return null;
            if (DebugMode) logger.Msg($"WaitCohtmlSpawned start {cwv}");
            menuManager = CVR_MenuManager.Instance;

            var parent = cohtmlUISystem.transform.parent;
            var animator = cwv.GetComponent<Animator>().runtimeAnimatorController;

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.SetActive(false);
            go.name = "ActionMenu";
            go.layer = LayerMask.NameToLayer("UI Internal");

            var t = menuTransform = go.transform;
            t.SetParent(parent, false);
            UpdateMenuScale();

            // The collider of the quad isn't needed (the mod reads joystick/mouse itself).
            // It sits on the UI layer and caught all clicks and lasers even with the menu closed -> remove it.
            var col = t.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.DestroyImmediate(col);
            menuCollider = null;

            var r = menuRenderer = t.GetComponent<MeshRenderer>();
            r.sortingLayerID = 0;
            r.sortingOrder = 10;

            var a = menuAnimator = go.AddComponent<Animator>();
            a.runtimeAnimatorController = animator;
            a.SetBool("Open", true);
            // The animator also drives shader values of the quad, so it has to run.
            // But we skip its open/close snapping (see SettleMenuAnimator).

            var v = cohtmlView = go.AddComponent<CohtmlView>();
            v.Listener.ReadyForBindings += MenuManagerRegisterEvents;
            v.enabled = false;
            v.CohtmlUISystem = cohtmlUISystem;
            v.IsTransparent = true;
            v.Width = canvasSize.x;
            v.Height = canvasSize.y;
            v.Page = couiUrl + "/index.html";

            // ready set go
            v.enabled = true;
            go.SetActive(true);
            SettleMenuAnimator();
            UpdatePositionToAnchor();
        }

        // ===== Avatar Height (CVR universal scaling) =====
        private const string avatarHeightId = "advactionmenu.avatar_height";
        private static float lastSentAvatarHeight = -1f;

        private static float CurrentAvatarHeight() => ABI_RC.Systems.AvatarScaling.UniversalScaling.Height;

        private static MenuItem BuildAvatarHeightItem()
        {
            instance.callbackItems_float[avatarHeightId] = SetAvatarHeight;
            float? min = null, max = null;
            var lo = ABI_RC.Systems.AvatarScaling.UniversalScaling.HeightMinimum;
            var hi = ABI_RC.Systems.AvatarScaling.UniversalScaling.HeightMaximum;
            if (lo > 0f && hi > lo) { min = (float)Math.Round(lo, 2); max = (float)Math.Round(hi, 2); }
            lastSentAvatarHeight = (float)Math.Round(CurrentAvatarHeight(), 2);
            return new MenuItem
            {
                name = "Avatar Height",
                icon = "icon_avatar_height.svg",
                action = new ItemAction
                {
                    type = "callback",
                    parameter = avatarHeightId,
                    control = "number",
                    min_value = min,
                    max_value = max,
                    default_value = lastSentAvatarHeight,
                    control_locked = true,
                },
            };
        }

        private static void SetAvatarHeight(float meters)
        {
            if (meters <= 0f) return;
            var ok = ABI_RC.Systems.AvatarScaling.UniversalScaling.TrySetHeight(meters, ABI_RC.Systems.AvatarScaling.HeightSource.User);
            if (!ok) logger.Msg($"Avatar Height {meters:0.00} m was not applied (not allowed in this world or out of range)");
            else if (DebugMode) logger.Msg($"Avatar Height set to {meters:0.00} m");
            lastSentAvatarHeight = -1f; // send the real value back on the next poll (clamped or refused)
        }

        // show height changes from outside (CVR menu, gestures, world rules) in the wheel
        private void PollAvatarHeight()
        {
            var h = (float)Math.Round(CurrentAvatarHeight(), 2);
            if (Math.Abs(h - lastSentAvatarHeight) < 0.005f) return;
            lastSentAvatarHeight = h;
            SendItemUpdate(new ItemAction { type = "callback", parameter = avatarHeightId, default_value = h });
        }

        private void SendItemUpdate(ItemAction action)
        {
            var u = new MenuItemValueUpdate() { action = action };
            cohtmlView?.View?.TriggerEvent<string>("GameValueUpdate", JsonSerialize(u));
        }

        private static void OnCVRCameraToggle(PortableCameraController __instance)
        {
            if (cohtmlReadyState < 2) return; // not ready for events
            if (DebugMode) MelonLogger.Msg($"OnCVRCameraToggle {__instance}");
            var action = new ItemAction()
            {
                type = "system call",
                event_ = "AppToggleCamera",
                value = __instance?._portableCamera?._camera?.isActiveAndEnabled ?? false
            };
            instance.SendItemUpdate(action);
        }

        private static void OnCVRMicrophoneToggle(bool active)
        {
            if (cohtmlReadyState < 2) return; // not ready for events
            if (DebugMode) MelonLogger.Msg($"OnCVRMicrophoneToggle {active}");
            var action = new ItemAction()
            {
                type = "system call",
                event_ = "AppToggleMute",
                value = !active,
            };
            instance.SendItemUpdate(action);
        }

        private static void OnCVRSeatedToggle(PlayerSetup __instance, bool seated)
        {
            if (cohtmlReadyState < 2) return; // not ready for events
            if (DebugMode) MelonLogger.Msg($"OnCVRSeatedToggle {seated}");
            var action = new ItemAction()
            {
                type = "system call",
                event_ = "AppToggleSeatedPlay",
                value = seated,
            };
            instance.SendItemUpdate(action);
        }

        private static void OnCVRFlyToggle(MovementSystem __instance)
        {
            if (cohtmlReadyState < 2) return; // not ready for events
            if (DebugMode) MelonLogger.Msg($"OnCVRFlyToggle {__instance.IsFlying()}");
            var action = new ItemAction()
            {
                type = "system call",
                event_ = "AppToggleFLightMode",
                value = __instance.IsFlying(),
            };
            instance.SendItemUpdate(action);
        }

        [Serializable]
        internal struct InputData
        {
            public Vector2 joystick;
            public float trigger;
        }

        private void ToggleMenu(bool show, bool handleDesktopInputs = true, bool leftSide = true)
        {
            if (DebugMode) logger.Msg($"ToggleMenu show={show} , cohtmlView.enabled={cohtmlView?.enabled} collider={menuCollider?.enabled} vr={MetaPort.Instance.isUsingVr}");
            if (cohtmlView == null || cohtmlView.View == null) return;

            if (show)
            {
                // need to close down quick + main menu
                var mm = CVR_MenuManager.Instance;
                var vm = ViewManager.Instance;
                if (mm?.IsViewShown == true) mm.ToggleQuickMenu(false);
                else if (vm?.IsViewShown == true) vm.UiStateToggle(false);

                // remember which hand served to open the menu
                anchorToLeftHand = leftSide;
            }

            menuOpen = show;
            if (show) { SendAccentColor(); PollWatchedParams(force: false); hudForce = true; SendHudInfo(); } // only changed values, unknown ones count as changed
            cohtmlView.View.TriggerEvent<bool>("ToggleActionMenu", show);

            SettleMenuAnimator(); // always in the fully opened state, no open/close snapping
            if (AnimationsOn)
            {
                // own animation in the menu, keep it visible a moment when closing
                if (show)
                {
                    ++closeToken; // cancel a running close animation
                    menuHidden = false;
                    cohtmlView.enabled = true;
                    if (menuRenderer != null) menuRenderer.enabled = true;
                }
                else
                {
                    MelonCoroutines.Start(HideAfterCloseAnimation(++closeToken));
                }
            }
            else
            {
                // no animation: show or hide right away
                ++closeToken;
                menuHidden = !show;
                cohtmlView.enabled = show;
                if (menuRenderer != null) menuRenderer.enabled = show;
                if (!show && menuAnimator != null) menuAnimator.enabled = false;
            }

            if (show)
                UpdatePositionToAnchor();

            var vr = MetaPort.Instance.isUsingVr;
            if (!vr)
            {
                // Only unlock the cursor on a normal open, but ALWAYS give the unlock back when closing.
                // In daky's original it got stuck when the menu was closed by Esc/Tab (CVR menu opens),
                // and the cursor wasn't captured by the game anymore afterwards.
                if (show && handleDesktopInputs)
                    CursorLockManager.Instance.SetUnlockWithId(true, "actionmenu");
                else if (!show)
                    CursorLockManager.Instance.SetUnlockWithId(false, "actionmenu");
            }
        }

        public override void OnLateUpdate()
        {
            if (dormant) return;
            if (menuManager == null || menuTransform == null) return;
            if (menuManager._inputManager == null)
                menuManager._inputManager = CVRInputManager.Instance;

            if (!menuOpen || cohtmlView?.View == null) return;

            var joystick = Vector2.zero;
            var trigger = 0f;
            if (!MetaPort.Instance.isUsingVr) // Desktop mode
            {
                if (menuManager._camera == null)
                    menuManager._camera = PlayerSetup.Instance.desktopCamera.GetComponent<Camera>();

                var halfScreen = 0.5f * new Vector2(Screen.width, Screen.height);
                var mousePos = ((Vector2)Input.mousePosition - halfScreen) / canvasSize.x;
                joystick = Vector2.ClampMagnitude(3f * mousePos, 1f);

                trigger = Input.GetMouseButtonDown(0) ? 1 : 0; // do we need button up anyway?
                UpdatePositionToAnchor();
            }
            else
            {
                UpdatePositionToAnchor();

                var im = menuManager._inputManager;
                joystick = anchorToLeftHand ?
                    new Vector2(im.movementVector.x, im.movementVector.z) : // y is 0 and irrelevant
                    new Vector2(im.rawLookVector.x, -im.rawLookVector.y); // vertical is inverted
                trigger = anchorToLeftHand ? menuManager._inputManager.interactLeftValue : menuManager._inputManager.interactRightValue;
            }

            if (cohtmlReadyState < 2) return;
            var view = cohtmlView.View;
            var data = new InputData {
                joystick = joystick,
                trigger = trigger,
            };
            view.TriggerEvent<string>("InputData", JsonUtility.ToJson(data));
        }

        private void UpdatePositionToAnchor()
        {
            if (cohtmlReadyState < 1) return;

            var vr = MetaPort.Instance.isUsingVr;
            var t = vr
                ? (anchorToLeftHand ? menuManager._leftVrAnchor : menuManager._rightVrAnchor).transform
                : MovementSystem.Instance.RotationPivot;

            // avatar scaling can change at any time, so check it every frame here (costs practically nothing)
            var factor = AvatarScaleFactor();
            if (Mathf.Abs(factor - lastScaleFactor) > 0.001f * lastScaleFactor)
                UpdateMenuScale();

            var offset = factor * 0.75f * (menuPositionOffset.Value - 0.5f * Vector2.one); // first value can be tweaked
            // TODO: which direction to pick?
            menuTransform.rotation = Quaternion.Euler(180*menuRotationXY.Value) * t.rotation;
            menuTransform.position = t.position + t.rotation * new Vector3(offset.x, offset.y, vr ? 0 : factor);
            // canvas is taller than wide: move it up so the hexagon center sits at the anchor
            menuTransform.position += menuTransform.up * (menuTransform.localScale.x * menuCenterOffsetPx / canvasSize.x);
        }

        private Menus? melonPrefsMenus;
        private void BuildOurMelonPrefsMenus()
        {
            melonPrefsMenus = ourLib.BuildMelonPrefsMenus(
                // that one is for devs, change it in the melonprefs conf
                melonPrefs.Entries.FindAll(e => e.Identifier != dontInstallResources.Identifier && e.Identifier != originalChoice.Identifier
                    && e.Identifier != animationStyle.Identifier && e.Identifier != ModSlots.pref?.Identifier));

            // submenu "Animation" with Spawn / Fade / Disabled as options
            var settingsKey = melonPrefsMenus.Keys.First();
            var subKey = settingsKey + hierarchySeparator + "animation";
            MenuItem Option(string name, string value) => new MenuItem
            {
                name = name,
                enabled = animationStyle.Value == value,
                action = new()
                {
                    type = "set melon preference",
                    control = "toggle",
                    parameter = animationStyle.Identifier,
                    value = value,
                    toggle = true,
                    exclusive_option = true,
                },
            };
            melonPrefsMenus[settingsKey].Insert(0, new MenuItem { name = "Animation", icon = "icon_animation.svg", action = new() { type = "menu", menu = subKey } });
            var spawn = Option("Spawn", "spawn"); spawn.icon = "icon_anim_spawn.svg";
            var fade = Option("Fade", "fade"); fade.icon = "icon_anim_fade.svg";
            var off = Option("Disabled", "disabled"); off.icon = "icon_anim_off.svg";
            melonPrefsMenus[subKey] = new List<MenuItem> { spawn, fade, off };

            // icons for the other settings (by setting identifier)
            var prefIcons = new Dictionary<string, string>
            {
                ["flick_selection"] = "icon_flick.svg",
                ["boring_back_button"] = "icon_back_item.svg",
                ["split_overcrowded_avatar_menu"] = "icon_pages.svg",
                ["debug_mode"] = "icon_debug.svg",
                ["show_clock"] = "icon_clock.svg",
                ["show_fps"] = "icon_fps.svg",
                ["clock_12h"] = "icon_clock.svg",
                ["scale_with_avatar"] = "icon_scale_avatar.svg",
                ["menu_size"] = "icon_resize.svg",
                ["menu_position_offset"] = "icon_move.svg",
                ["menu_rotation_xy"] = "icon_rotate.svg",
            };
            var settingsItems = melonPrefsMenus[settingsKey];
            for (var i = 0; i < settingsItems.Count; ++i)
            {
                var it = settingsItems[i];
                if (it.icon == null && it.action.parameter != null && prefIcons.TryGetValue(it.action.parameter, out var ic))
                {
                    it.icon = ic;
                    settingsItems[i] = it;
                }
            }
        }


        private static Menus? avatarMenus;
        private static void OnAvatarAdvancedSettings(PlayerSetup __instance)
        {
            var animator = __instance.Animator;
            var advSettings = __instance.AvatarDescriptor.avatarSettings.settings;
            var menuPrefix = avatarMenuName;
            var m = avatarMenus = AvatarAdvancedSettingsToMenus(advSettings, animator, menuPrefix);

            if (DebugMode) logger.Msg($"OnAvatarAdvancedSettings {advSettings.Count} items");

            if (instance.splitAvatarOvercrowdedMenu.Value)
                m = SplitOverCrowdedMenus(m);

            // add avatar emotes
            var emoteNames = __instance?.AnimatorManager.GetLegacyEmoteNames();
            if (emoteNames != null && emoteNames.Length > 0)
            {
                var parents = Path(menuPrefix, "emotes");
                var aitems = m.GetWithDefault(parents);
                var i = 1;
                emoteNames.Do(name =>
                {
                    if (DebugMode) logger.Msg($"OnAvatarAdvancedSettings emote {name} <- {parents}");
                    var item = new MenuItem
                    {
                        name = name,
                        action = new ItemAction
                        {
                            type = "system call",
                            event_ = "AppPlayEmote",
                            event_arguments = new string[] { i.ToString() },
                            exclusive_option = true,
                            toggle = true, // TODO: would be nice to have control=impulse here
                        },
                    };
                    aitems.Add(item);
                    ++i;
                });

                m.GetWithDefault(menuPrefix).Add(new MenuItem()
                {
                    name = "Emotes",
                    icon = "icon_avatar_emotes.svg",
                    action = new() { type = "menu", menu = parents },
                });
            }
            // TODO: add cvr avatar ToggleState?

            // add avatar advanced settings profiles
            var profilesNames = __instance.getCurrentAvatarSettingsProfiles()?.ToList();
            if (profilesNames != null && profilesNames.Count > 0)
            {
                profilesNames.Add("default"); // there is an implicit default in cvr
                var parents = Path(menuPrefix, "profiles");
                var aitems = m.GetWithDefault(parents);
                profilesNames.Do(name =>
                {
                    if (DebugMode) logger.Msg($"OnAvatarAdvancedSettings profiles {name} <- {parents}");
                    var item = new MenuItem
                    {
                        name = name,
                        action = new ItemAction
                        {
                            type = "system call",
                            event_ = "AppChangeAvatarProfile",
                            // TODO: the avatar items should reload all the parameters values
                            event_arguments = new string[] { name },
                            exclusive_option = true,
                            toggle = true,
                        },
                    };
                    aitems.Add(item);
                });

                m.GetWithDefault(menuPrefix).Add(new MenuItem()
                {
                    name = "Profiles",
                    icon = "icon_avatar_settings_profile.svg",
                    action = new() { type = "menu", menu = parents },
                });
            }

            // CVR's universal avatar height (Unity 6): number pad in meters, works for every avatar
            try
            {
                m.GetWithDefault(menuPrefix).Add(BuildAvatarHeightItem());
            }
            catch (Exception e)
            {
                logger.Warning($"Avatar Height entry not available: {e.Message}");
            }

            // let mods modify the avatar menu
            var avatarGuid = __instance?.AvatarDescriptor?.avatarSettings?._avatarGuid ?? "default";
            API.InvokeOnAvatarMenuLoaded(avatarGuid, m);

            // avatar menu override from json file
            var avatarOverridesFile = @"UserData\ActionMenu\AvatarOverrides\for_" + avatarGuid + ".json";
            if (File.Exists(avatarOverridesFile))
            {
                try
                {
                    logger.Msg($"loading avatar overrides for {avatarGuid}: {avatarOverridesFile}");
                    var txt = File.ReadAllText(avatarOverridesFile);
                    var patch = JsonConvert.DeserializeObject<MenusPatch>(txt);
                    patch.ApplyToMenu(m);
                }
                catch (Exception ex)
                {
                    logger.Error($"Error while reading avatar overrides json for avatar {avatarGuid}: {ex}");
                }
            }

            UpgradeInputSingleToNumber(m, advSettings);

            instance.OnActionMenuReady(); // reload
        }

        private void OnActionMenuReady()
        {
            var view = cohtmlView?.View;
            if (cohtmlView == null) return; // cannot proceed
            if (DebugMode) logger.Msg($"OnActionMenuReady for view {view}");
            string fromFile;
            try
            {
                fromFile = File.ReadAllText(couiPath + @"\actionmenu.json");
            }
            catch (Exception)
            {
                logger.Error($"Cannot read main json file. Erase your melon preference for ActionMenu and try reinstalling the mod.");
                return;
            }
            var config = JsonConvert.DeserializeObject<Menu>(fromFile);
            if (DebugMode) logger.Msg($"Loaded config with {config.menus.Count} menus: {string.Join(", ", config.menus.Keys)}");

            // add our melon prefs
            if (melonPrefsMenus != null)
            {
                foreach (var x in melonPrefsMenus)
                    config.menus[x.Key] = x.Value;
            }

            // avatar menu from avatar itself (cvr advanced settings)
            if (avatarMenus != null)
            {
                foreach (var x in avatarMenus)
                    config.menus[x.Key] = x.Value;
                logger.Msg($"Loaded config from avatar {avatarMenus.Count} menus: {string.Join(", ", avatarMenus.Keys)}");
            }

            // global overrides
            var globalOverridesFiles = TryOrDefault(() => Directory.GetFiles(@"UserData\ActionMenu\GlobalOverrides"));
            globalOverridesFiles?
                .Where(fpath => fpath.EndsWith(".json"))
                .Do(fpath =>
            {
                try
                {
                    var txt = File.ReadAllText(fpath);
                    var patch = JsonConvert.DeserializeObject<MenusPatch>(txt);
                    patch.ApplyToMenu(config.menus);
                }
                catch (Exception ex)
                {
                    logger.Error($"Error while reading global overrides json {fpath}: {ex}");
                }
            });

            API.InvokeOnGlobalMenuLoaded(config.menus);

            // selected settings of other mods (mod slots) under "Mods"
            try
            {
                ModSlots.AddToMenus(config.menus);
            }
            catch (Exception ex)
            {
                logger.Error($"Error while adding mod slots: {ex}");
            }

            // mods without an icon get the puzzle piece under "Mods" (only the mods themselves, not their entries)
            var modsList = config.menus.GetWithDefault(modsMenuName);
            for (var i = 0; i < modsList.Count; ++i)
            {
                if (!string.IsNullOrEmpty(modsList[i].icon)) continue;
                var it = modsList[i];
                it.icon = "icon_mod.svg";
                modsList[i] = it;
            }

            // add item to link to all mods menu if any is present
            if (config.menus.GetWithDefault(modsMenuName).Count > 0)
            {
                config.menus.GetWithDefault(mainMenuName).Add(new MenuItem()
                {
                    name = "Mods",
                    icon = "icon_mods.png",
                    action = new ItemAction() { type = "menu", menu = modsMenuName },
                });
            }

            var settings = new MenuSettings
            {
                in_vr = MetaPort.Instance.isUsingVr,
                flick_selection = flickSelection.Value,
                boring_back_button = boringBackButton.Value,
                debug = debugMode.Value,
                animation_style = animationStyle.Value,
                version = ModVersion,
                keep_menu = keepMenuOnce,
            };

            CollectWatchedParams(config.menus);
            NormalizeDataIcons(config.menus);

            var configTxt = JsonSerialize(config);
            var settingsTxt = JsonSerialize(settings);
            if (cohtmlReadyState > 0) {
                view.TriggerEvent<string, string>("LoadActionMenu", configTxt, settingsTxt);
                cohtmlReadyState = 2;
                hudForce = true; // UI reloaded -> send clock/FPS again
                cohtmlView.enabled = false; // hide it by default
                menuOpen = false;
                menuHidden = true;
                if (menuRenderer != null) menuRenderer.enabled = false;
                UpdateMenuScale(); // in case change avatar = new scale
            }
        }

        internal struct MenuSettings
        {
            public bool in_vr;
            public bool flick_selection;
            public bool boring_back_button;
            public bool debug;
            public string animation_style;
            public string version;
            public bool keep_menu; // stay in the current submenu (a setting was changed in the wheel)
        }

        private static string JsonSerialize(object value) => JsonConvert.SerializeObject(value, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

        private void ConfigReload()
        {
            OnAvatarAdvancedSettings(PlayerSetup.Instance);
        }

        // Save inlined icons (data:image/...;base64,...) as files and load them by path.
        // Cohtml turns the "+" in "data:image/svg+xml" into a space ("Malformed URL") and even crashed
        // on it while loading resources (ExecutionEngineException in UnityPlugin.ProcessResources).
        private const string dataIconDir = "dataicons";
        private static readonly Dictionary<string, string> dataIconCache = new();

        private static void NormalizeDataIcons(Menus menus)
        {
            foreach (var list in menus.Values)
            {
                for (var i = 0; i < list.Count; ++i)
                {
                    var icon = list[i].icon;
                    if (icon == null || !icon.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
                    var it = list[i];
                    it.icon = DataIconToFile(icon); // null = show without icon instead of crashing
                    list[i] = it;
                }
            }
        }

        private static string? DataIconToFile(string uri)
        {
            if (dataIconCache.TryGetValue(uri, out var cached)) return cached;
            string? result = null;
            try
            {
                var comma = uri.IndexOf(',');
                if (comma > 5)
                {
                    var header = uri.Substring(5, comma - 5).ToLowerInvariant(); // e.g. "image/svg+xml;base64"
                    var payload = uri.Substring(comma + 1);
                    var isBase64 = header.EndsWith(";base64");
                    var mime = isBase64 ? header.Substring(0, header.Length - 7) : header.Split(';')[0];
                    mime = mime.Replace(' ', '+'); // in case the "+" was already lost
                    string? ext = mime switch
                    {
                        "image/png" => "png",
                        "image/jpeg" or "image/jpg" => "jpg",
                        "image/gif" => "gif",
                        "image/webp" => "webp",
                        "image/svg+xml" => "svg",
                        _ => null,
                    };
                    if (ext != null)
                    {
                        var bytes = isBase64
                            ? Convert.FromBase64String(payload.Replace(' ', '+'))
                            : System.Text.Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload));
                        string hash;
                        using (var sha = System.Security.Cryptography.SHA1.Create())
                            hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                        var dir = System.IO.Path.Combine(couiPath, dataIconDir);
                        Directory.CreateDirectory(dir);
                        var file = System.IO.Path.Combine(dir, hash + "." + ext);
                        if (!File.Exists(file)) File.WriteAllBytes(file, bytes);
                        result = dataIconDir + "/" + hash + "." + ext;
                    }
                }
                if (result == null && DebugMode) logger.Warning($"Unsupported inlined icon skipped: {uri.Substring(0, Math.Min(40, uri.Length))}...");
            }
            catch (Exception e)
            {
                logger.Warning($"Inlined icon could not be converted, shown without icon: {e.Message}");
            }
            dataIconCache[uri] = result!;
            return result;
        }

        private static bool keepMenuOnce;

        // send menus and settings to the wheel again without reloading the UI
        private void SoftReload()
        {
            try
            {
                BuildOurMelonPrefsMenus();
                if (cohtmlReadyState < 1 || cohtmlView?.View == null) return; // it's loading anyway
                keepMenuOnce = true;
                OnActionMenuReady();
            }
            catch (Exception e)
            {
                logger.Error($"Reloading the menu failed: {e}");
            }
            finally
            {
                keepMenuOnce = false;
            }
        }

        private void FullReload()
        {
            if (cohtmlView == null)
            {
                logger.Error($"Reload view is null!");
                return;
            }
            cohtmlReadyState = 0;
            OnAvatarAdvancedSettings(PlayerSetup.Instance);
            cohtmlView.View.Reload();
            MelonCoroutines.Start(DelayedRestart(menuOpen)); // yes it's a ugly hack but it works, right?
            ToggleMenu(true);
        }

        private System.Collections.IEnumerator DelayedRestart(bool wasEnabled)
        {
            uint i = 42; // rolled a big dice, that's what came out, I swear
            while (--i > 0)
                yield return null;
            ToggleMenu(wasEnabled);
            if (DebugMode) logger.Msg($"view reloaded {cohtmlView} {cohtmlView.View}");
        }

        private void OnSetMelonPreference(string identifier, string value)
        {
            if (identifier.StartsWith(ModSlots.ParamPrefix))
            {
                ModSlots.SetValue(identifier, value);
                return;
            }
            MelonPreferences_Entry e_;
            if (!melonPrefsMap.TryGetValue(identifier, out e_) || e_ == null)
            {
                logger.Error($"didn't find preference {identifier}");
                return;
            }

            switch (e_)
            {
                case MelonPreferences_Entry<bool> e: {
                        if (float.TryParse(value, out float valueFloat))
                            e.Value = valueFloat != 0;
                        break;
                    }

                case MelonPreferences_Entry<float> e: {
                        if (float.TryParse(value, out float valueFloat))
                            e.Value = valueFloat;
                        break;
                    }

                case MelonPreferences_Entry<string> e: {
                        // selection options: "0" comes when the already active option is pressed again
                        if (!string.IsNullOrEmpty(value) && value != "0")
                            e.Value = value;
                        break;
                    }

                case MelonPreferences_Entry<Vector2> e: {
                        var strs = value.Split(',');
                        if (float.TryParse(strs[0], out float valueFloatX) && float.TryParse(strs[1], out float valueFloatY))
                            e.Value = new Vector2(valueFloatX, valueFloatY);
                        break;
                    }
                // TODO: implement other types

                default:
                    logger.Error($"OnSetMelonPreference {identifier} unsupported type {e_.GetReflectedType()}");
                    return;
            }
        }

        private void OnItemCallback(string identifier)
        {
            Action f;
            if (!callbackItems.TryGetValue(identifier, out f) || f == null)
            {
                logger.Error($"didn't find callback {identifier}");
                return;
            }

#if DEBUG
            logger.Msg($"OnItemCallback calling {identifier}: {f}"); // TODO debug
#endif
            try { f(); }
            catch (Exception e) { logger.Error($"failure in callback {identifier}: {e}"); }
        }

        private void OnItemCallback_bool(string identifier, bool value)
        {
            Action<bool> f;
            if (!callbackItems_bool.TryGetValue(identifier, out f) || f == null)
            {
                logger.Error($"didn't find callback {identifier}");
                return;
            }

            try { f(value); }
            catch (Exception e) { logger.Error($"failure in callback {identifier}: {e}"); }
        }

        private void OnItemCallback_float(string identifier, float value)
        {
            Action<float> f;
            if (!callbackItems_float.TryGetValue(identifier, out f) || f == null)
            {
                logger.Error($"didn't find callback {identifier}");
                return;
            }

            try { f(value); }
            catch (Exception e) { logger.Error($"failure in callback {identifier}: {e}"); }
        }

        private void OnItemCallback_float_float(string identifier, float x, float y)
        {
            Action<float, float> f;
            if (!callbackItems_float_float.TryGetValue(identifier, out f) || f == null)
            {
                logger.Error($"didn't find callback {identifier}");
                return;
            }

            try { f(x, y); }
            catch (Exception e) { logger.Error($"failure in callback {identifier}: {e}"); }
        }

        private void OnRequestDynamicMenu(string identifier)
        {
            MenuBuilder f;
            if (!dynamic_menus.TryGetValue(identifier, out f) || f == null)
            {
                logger.Error($"didn't find builder {identifier}");
                return;
            }

#if DEBUG
            logger.Msg($"OnRequestDynamicMenu calling {identifier}: {f}"); // TODO debug
#endif
            try
            {
                var items = f();
                var menus = new Menus { [identifier] = items };
                cohtmlView.View.TriggerEvent<string>("DynamicMenuData", JsonSerialize(menus));
            }
            catch (Exception e)
            {
                logger.Error($"failure in callback: {e}");
            }
        }

        public override void OnUpdate()
        {
            if (dormant) return;

            // If our menu is hidden, keep animator, quad and view off every frame
            // (CVR's animator and the first load of the CVR menus turn the quad back on otherwise)
            if (menuHidden)
            {
                if (menuAnimator != null && menuAnimator.enabled) menuAnimator.enabled = false;
                if (menuRenderer != null && menuRenderer.enabled) menuRenderer.enabled = false;
                if (cohtmlView != null && cohtmlView.enabled && cohtmlReadyState == 2) cohtmlView.enabled = false;
            }
            if (menuTransform != null)
                UpdatePositionToAnchor();

            ModSlots.FlushSave(); // save changed mod settings so the mods apply them

            // count FPS (always, so there's a value right when opening) and send clock/FPS to the menu
            ++fpsFrames;
            fpsElapsed += Time.unscaledDeltaTime;
            if (fpsElapsed >= hudInfoInterval)
            {
                fpsValue = fpsFrames / fpsElapsed;
                fpsFrames = 0;
                fpsElapsed = 0f;
                if (cohtmlReadyState == 2 && menuOpen && (showClock?.Value == true || showFps?.Value == true || lastHudClock != "" || lastHudFps != ""))
                    SendHudInfo();
            }

            // parameter sync only while the menu is open
            if (cohtmlReadyState == 2 && menuOpen && Time.unscaledTime >= nextParamPoll)
            {
                nextParamPoll = Time.unscaledTime + paramPollInterval;
                PollWatchedParams(force: false);
                try { ModSlots.PollValues(SendItemUpdate); } catch (Exception ex) { if (DebugMode) logger.Warning($"mod slot sync: {ex.Message}"); }
                try { PollAvatarHeight(); } catch (Exception ex) { if (DebugMode) logger.Warning($"avatar height sync: {ex.Message}"); }
            }

            if (KeyboardManager.Instance?.IsViewShown ?? true) return;

            // pass desktop keyboard input to the number pad (it ignores it when closed)
            if (!MetaPort.Instance.isUsingVr && menuOpen && cohtmlView?.View != null)
            {
                var keys = Input.inputString;
                if (Input.GetKeyDown(KeyCode.Escape)) keys += "\u001b";
                if (!string.IsNullOrEmpty(keys))
                    cohtmlView.View.TriggerEvent<string>("KeyInput", keys);
            }

            if (Input.GetKeyDown(reloadKeyBinding.Value)) {
                var shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                if (shift) ConfigReload();
                else FullReload();
            }

            if (Input.GetKeyDown(openKeyBinding.Value) && cohtmlView != null) {
                ToggleMenu(!menuOpen);
            }
        }

        // Let CVR's menu animator jump to the end of its open animation right away
        // instead of playing it (it also sets shader values, without them the quad looks broken)
        private static void SettleMenuAnimator()
        {
            if (menuAnimator == null) return;
            menuAnimator.enabled = true;
            menuAnimator.SetBool("Open", true);
            if (menuAnimator.isActiveAndEnabled)
                for (var i = 0; i < 4; ++i) menuAnimator.Update(5f); // several steps in case there are transitions in a row
        }

        // only hide the view once the close animation in the menu is done (and it wasn't reopened in the meantime)
        private System.Collections.IEnumerator HideAfterCloseAnimation(int token)
        {
            var until = Time.unscaledTime + closeAnimationSeconds;
            while (Time.unscaledTime < until) yield return null;
            if (token != closeToken || menuOpen) yield break;
            menuHidden = true;
            // stop the animator: its "open" state turns the quad back on every frame otherwise
            if (menuAnimator != null) menuAnimator.enabled = false;
            if (cohtmlView != null) cohtmlView.enabled = false;
            if (menuRenderer != null) menuRenderer.enabled = false;
        }

        // ===== working next to daky's original =====
        // Both mods register the same VR bindings ("ActionMenuOpen"), with VRBinding the last one registered wins.
        // If we "step in front", we pause the original and forward its calls to us.
        // That way the VR bindings keep working, no matter which mod was loaded first.

        private MelonPreferences_Entry<string> originalChoice;
        private bool dormant = false; // true = the original is used, we do nothing

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

        private static Type? FindOriginalModType()
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "ActionMenu" && a != typeof(ActionMenuMod).Assembly);
            return asm?.GetType("ActionMenu.ActionMenuMod");
        }

        // true = AdvActionMenu runs, false = the original is used
        private bool HandleOriginalActionMenu()
        {
            var original = FindOriginalModType();
            if (original == null) return true; // original not installed

            var choice = (originalChoice.Value ?? "ask").Trim().ToLowerInvariant();
            if (choice != "advanced" && choice != "original")
            {
                choice = AskWhichActionMenu();
                originalChoice.Value = choice;
                MelonPreferences.Save();
            }
            logger.Msg($"Daky's original Action Menu found, using: {choice}");
            if (choice == "original") return false;

            PauseOriginal(original);
            return true;
        }

        private string AskWhichActionMenu()
        {
            const uint MB_YESNO = 0x4, MB_ICONQUESTION = 0x20, MB_SETFOREGROUND = 0x10000, MB_TOPMOST = 0x40000;
            const int IDNO = 7;
            try
            {
                var text = "Daky's original Action Menu is installed as well.\n\n" +
                    "Which one do you want to use?\n\n" +
                    "Yes = AdvActionMenu (the original gets paused)\n" +
                    "No = original Action Menu (AdvActionMenu gets paused)\n\n" +
                    "Your VR bindings keep working either way. The choice is saved, you can change it later " +
                    "with \"original_action_menu\" in UserData\\MelonPreferences.cfg.";
                var r = MessageBoxW(IntPtr.Zero, text, "AdvActionMenu", MB_YESNO | MB_ICONQUESTION | MB_SETFOREGROUND | MB_TOPMOST);
                return r == IDNO ? "original" : "advanced";
            }
            catch (Exception e)
            {
                logger.Warning($"Could not show the question dialog ({e.Message}), using AdvActionMenu");
                return "advanced";
            }
        }

        private void PauseOriginal(Type original)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            void Patch(string name, string prefix)
            {
                var m = original.GetMethod(name, flags);
                if (m == null) { logger.Warning($"Original Action Menu: method {name} not found, cannot pause it"); return; }
                HarmonyInstance.Patch(m, prefix: new HarmonyMethod(AccessTools.Method(typeof(ActionMenuMod), prefix)));
            }
            Patch("OnUpdate", nameof(SkipOriginal));       // keys (R, F3) only handled by us
            Patch("OnLateUpdate", nameof(SkipOriginal));
            Patch("ToggleMenu", nameof(ForwardOriginalToggle)); // forward VR bindings and menu events to us
            logger.Msg("Daky's original Action Menu is paused, its VR bindings now open AdvActionMenu");
        }

        private static bool SkipOriginal() => false;

        private static bool ForwardOriginalToggle(bool show, bool leftSide)
        {
            var me = instance;
            if (me == null || me.dormant || cohtmlView == null) return false;
            // The original is never open, so it always calls with show=true when its VR button is pressed:
            // treat it as a toggle. show=false (e.g. CVR menu opens) means close.
            if (show) me.ToggleMenu(!menuOpen, leftSide: leftSide);
            else if (menuOpen) me.ToggleMenu(false);
            return false; // never run the original itself
        }

        // ===== parameter sync from outside =====
        // Avatar parameters can change without the menu too (CVR menu, avatar logic, OSC, other mods).
        // We remember all parameters used in the menu and report changes to the menu.

        private struct WatchedParam
        {
            public string parameter; // name as in the menu (for 2D without -x/-y)
            public bool is2D;
        }

        private const float paramPollInterval = 0.1f; // 10x per second is plenty

        // clock top left, FPS top right; checked 2x per second so the number doesn't flicker
        private const float hudInfoInterval = 0.5f;
        private int fpsFrames;
        private float fpsElapsed, fpsValue;

        private string lastHudClock = "", lastHudFps = "";
        private bool hudForce = true;      // send once for sure after opening/reloading
        private int hudErrors;             // turn off after several errors instead of disturbing the game

        private void SendHudInfo()
        {
            if (hudErrors >= 3) return;
            // only to a fully loaded, visible UI (not while it reloads)
            if (cohtmlReadyState != 2 || !menuOpen || menuHidden || cohtmlView == null || cohtmlView.View == null) return;
            try
            {
                var clock = showClock?.Value == true
                    ? DateTime.Now.ToString(clock12h?.Value == true ? "h:mm tt" : "HH:mm", System.Globalization.CultureInfo.InvariantCulture)
                    : "";
                var fps = showFps?.Value == true && fpsValue > 0f && fpsValue < 10000f ? Mathf.RoundToInt(fpsValue) + " FPS" : "";
                // only send when something changed (clock once a minute, FPS only on a new value)
                if (!hudForce && clock == lastHudClock && fps == lastHudFps) return;
                hudForce = false;
                lastHudClock = clock;
                lastHudFps = fps;
                cohtmlView.View.TriggerEvent<string, string>("HudInfo", clock, fps);
            }
            catch (Exception e)
            {
                ++hudErrors;
                logger.Warning($"Clock/FPS display failed ({hudErrors}/3){(hudErrors >= 3 ? ", turned off for this session" : "")}: {e.Message}");
            }
        }
        private float nextParamPoll = 0f;
        private readonly List<WatchedParam> watchedParams = new();
        private readonly Dictionary<string, float> lastParamValues = new();
        private Dictionary<string, AnimatorControllerParameterType> paramTypes = new();
        private Animator? watchedAnimator;

        private void CollectWatchedParams(Menus menus)
        {
            watchedParams.Clear();
            lastParamValues.Clear();
            watchedAnimator = null; // read the types again on the next poll

            var seen = new HashSet<string>();
            foreach (var items in menus.Values)
            {
                if (items == null) continue;
                foreach (var item in items)
                {
                    var a = item.action;
                    if (a.type != "avatar parameter" || string.IsNullOrEmpty(a.parameter)) continue;
                    var is2D = a.control == "joystick_2d" || a.control == "input_vector_2d";
                    if (seen.Add((is2D ? "2d|" : "1d|") + a.parameter))
                        watchedParams.Add(new WatchedParam { parameter = a.parameter!, is2D = is2D });
                }
            }
            if (DebugMode) logger.Msg($"Parameter-Sync: {watchedParams.Count} parameters watched");
        }

        private float? ReadAnimatorParam(Animator animator, string name)
        {
            if (!paramTypes.TryGetValue(name, out var type)) return null;
            switch (type)
            {
                case AnimatorControllerParameterType.Float: return animator.GetFloat(name);
                case AnimatorControllerParameterType.Int: return animator.GetInteger(name);
                case AnimatorControllerParameterType.Bool:
                case AnimatorControllerParameterType.Trigger: return animator.GetBool(name) ? 1f : 0f;
                default: return null;
            }
        }

        // true = value changed (or force), new value in value
        private bool ParamChanged(Animator animator, string name, bool force, out float value)
        {
            value = 0f;
            var v = ReadAnimatorParam(animator, name);
            if (v == null) return false;
            value = v.Value;
            var changed = force || !lastParamValues.TryGetValue(name, out var last) || Mathf.Abs(last - value) > 0.0001f;
            lastParamValues[name] = value;
            return changed;
        }

        private void PollWatchedParams(bool force)
        {
            if (watchedParams.Count == 0 || cohtmlView?.View == null) return;
            try
            {
                var animator = PlayerSetup.Instance?.Animator;
                if (animator == null || animator.runtimeAnimatorController == null) return;

                if (animator != watchedAnimator)
                {
                    watchedAnimator = animator;
                    lastParamValues.Clear();
                    paramTypes = new();
                    foreach (var p in animator.parameters)
                        paramTypes[p.name] = p.type;
                    force = true;
                }

                foreach (var w in watchedParams)
                {
                    if (w.is2D)
                    {
                        var cx = ParamChanged(animator, w.parameter + "-x", force, out var x);
                        var cy = ParamChanged(animator, w.parameter + "-y", force, out var y);
                        if (cx || cy)
                            SendItemUpdate(new ItemAction { type = "avatar parameter", parameter = w.parameter, default_value_x = x, default_value_y = y });
                    }
                    else if (ParamChanged(animator, w.parameter, force, out var v))
                    {
                        SendItemUpdate(new ItemAction { type = "avatar parameter", parameter = w.parameter, value = v, default_value = v });
                    }
                }
            }
            catch (Exception e)
            {
                logger.Warning($"Parameter-Sync failed: {e.Message}");
            }
        }

        // CVR's default menu colors (from Common/vars.css)
        private const string cvrDefaultPrimary = "222222";
        private const string cvrDefaultAccent = "a92800";

        private static string? ColorToHex(object? v)
        {
            switch (v)
            {
                case Color c: return ColorUtility.ToHtmlStringRGB(c);
                case Color32 c32: return ColorUtility.ToHtmlStringRGB(c32);
                case string str:
                    var t = str.Trim().TrimStart('#');
                    return t.Length >= 6 && t.Substring(0, 6).All(Uri.IsHexDigit) ? t.Substring(0, 6) : null;
                default: return null;
            }
        }

        // first field/property whose name contains the text and that is a color
        private static string? FindColorMember(object o, string nameContains)
        {
            const BindingFlags f = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var fi in o.GetType().GetFields(f))
                if (fi.Name.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0)
                { var h = ColorToHex(fi.GetValue(o)); if (h != null) return h; }
            foreach (var pi in o.GetType().GetProperties(f))
                if (pi.CanRead && pi.GetIndexParameters().Length == 0 && pi.Name.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0)
                { try { var h = ColorToHex(pi.GetValue(o)); if (h != null) return h; } catch { } }
            return null;
        }

        // cache reflection results: TypeByName and FindFirstObjectByType are expensive and made opening stutter
        private static bool uiTypesLooked = false;
        private static Type? cachedUiType, cachedPcmType;
        private static UnityEngine.Object? cachedUiManager;
        private static readonly Dictionary<Type, object> cachedInstances = new();

        private static void LookUpUiTypes()
        {
            if (uiTypesLooked) return;
            uiTypesLooked = true;
            cachedUiType = AccessTools.TypeByName("ABI_RC.Core.UI.UIRework.CVRUIManagerBase");
            cachedPcmType = AccessTools.TypeByName("ABI_RC.Systems.PlayerColors.PlayerColorsManager");
        }

        private static object? GetStaticOrInstance(Type t, string member)
        {
            const BindingFlags all = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            object? target = null;
            var prop = t.GetProperty(member, all);
            var field = prop == null ? t.GetField(member, all) : null;
            var isStatic = prop?.GetGetMethod(true)?.IsStatic ?? field?.IsStatic ?? false;
            if (!isStatic)
            {
                cachedInstances.TryGetValue(t, out var inst);
                if (inst is UnityEngine.Object uo && uo == null) inst = null; // destroyed Unity object
                inst ??= (object?)t.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null)
                    ?? t.GetField("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null);
                if (inst == null && typeof(UnityEngine.Object).IsAssignableFrom(t)) inst = UnityEngine.Object.FindFirstObjectByType(t);
                if (inst == null) return null;
                cachedInstances[t] = inst;
                target = inst;
            }
            return prop != null ? prop.GetValue(target) : field?.GetValue(target);
        }

        // Reads the colors the same way CVR colors its own menus:
        // with "Colourize Menu" first player color = background, second = accent, otherwise CVR default
        private (string primary, string accent, float maxLuma, bool colourize) ReadUiColors()
        {
            var primary = cvrDefaultPrimary; var accent = cvrDefaultAccent;
            var colourize = false; var maxLuma = 20f;
            var dbg = new List<string>();

            // CVR UI settings (Colourize Menu, maximum brightness of the background)
            try
            {
                LookUpUiTypes();
                var uiType = cachedUiType;
                if (cachedUiManager == null && uiType != null && typeof(UnityEngine.Object).IsAssignableFrom(uiType))
                    cachedUiManager = UnityEngine.Object.FindFirstObjectByType(uiType);
                var ui = cachedUiManager;
                if (ui != null)
                {
                    if (AccessTools.Field(uiType, "_colourizeMenu")?.GetValue(ui) is bool b) colourize = b;
                    var luma = AccessTools.Field(uiType, "_primaryColourMaxLuma")?.GetValue(ui);
                    if (luma != null) maxLuma = Convert.ToSingle(luma);
                    dbg.Add($"ui manager {ui.GetType().Name}: colourize={colourize}, maxLuma={maxLuma}");
                }
                else
                {
                    colourize = MetaPort.Instance.settings.GetSettingsBool("GeneralUIColourizeMenu");
                    dbg.Add($"no ui manager found, setting GeneralUIColourizeMenu={colourize}");
                }
            }
            catch (Exception e) { dbg.Add($"ui settings failed: {e.Message}"); }

            if (colourize)
            {
                string? p = null, a = null;
                try
                {
                    LookUpUiTypes();
                    var pcmType = cachedPcmType;
                    var colors = pcmType != null ? GetStaticOrInstance(pcmType, "CurrentColors") : null;
                    if (colors != null)
                    {
                        p = FindColorMember(colors, "primary");
                        a = FindColorMember(colors, "secondary");
                        dbg.Add($"player colors ({colors.GetType().Name}): primary={p ?? "?"}, secondary={a ?? "?"}");
                        if (DebugMode && (p == null || a == null))
                            dbg.Add("player color members: " + string.Join(", ", colors.GetType()
                                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                                .Select(x => $"{x.Name}:{x.FieldType.Name}")));
                    }
                    else dbg.Add("PlayerColorsManager.CurrentColors not found");
                }
                catch (Exception e) { dbg.Add($"player colors failed: {e.Message}"); }

                // fall back to the saved settings
                try
                {
                    p ??= ColorToHex(MetaPort.Instance.settings.GetSettingsString("PlayerColorPrimary"));
                    a ??= ColorToHex(MetaPort.Instance.settings.GetSettingsString("PlayerColorSecondary"));
                }
                catch { }
                if (p != null) primary = p;
                if (a != null) accent = a;
            }

            if (DebugMode) logger.Msg($"UI colors: primary #{primary}, accent #{accent} | " + string.Join(" | ", dbg));
            return (primary, accent, maxLuma, colourize);
        }

        private void SendAccentColor()
        {
            try
            {
                var (primary, accent, maxLuma, _) = ReadUiColors();
                cohtmlView?.View?.TriggerEvent<string, string, float>("UiColors", primary, accent, maxLuma);
            }
            catch (Exception e)
            {
                logger.Warning($"Could not read UI colors: {e.Message}");
            }
        }

        // show/hide the HUD: CVR doesn't react live to the GeneralShowHud setting anymore,
        // so go through the HUD itself (and also save the setting for the next start)
        private void OnSetHudVisible(bool show)
        {
            try
            {
                var hud = ABI_RC.Core.UI.CohtmlHud.Instance;
                if (hud != null)
                {
                    if (show) hud.ShowHud(); else hud.HideHud();
                }
                MetaPort.Instance.settings.SetSetting("GeneralShowHud", show ? "True" : "False");
                if (DebugMode) logger.Msg($"HUD {(show ? "shown" : "hidden")} (hud found: {hud != null})");
            }
            catch (Exception e)
            {
                logger.Warning($"Could not toggle the HUD: {e.Message}");
            }
        }

        // reference height: a 1.8 m avatar gets the menu at its original size
        private const float referenceAvatarHeight = 1.8f;
        private float lastScaleFactor = 1f;

        // ratio of the current avatar height (including avatar scaling) to the reference height
        private float AvatarScaleFactor()
        {
            if (scaleWithAvatar?.Value != true) return 1f;
            var height = PlayerSetup.Instance != null ? PlayerSetup.Instance.UniversalHeight : referenceAvatarHeight; // GetAvatarHeight() was removed with Unity 6
            if (float.IsNaN(height) || height <= 0.001f) return 1f;
            return Mathf.Clamp(height / referenceAvatarHeight, 0.005f, 200f);
        }

        private void UpdateMenuScale()
        {
            if (menuTransform == null) return;
            var vr = MetaPort.Instance.isUsingVr;
            var scale = vr ? menuBaseSizeVr : menuBaseSizeDesktop;
            // on desktop size and distance grow together: on screen the menu keeps its size,
            // but isn't stuck in the head of giants and doesn't float meters away for tiny avatars
            lastScaleFactor = AvatarScaleFactor();
            var s = scale * lastScaleFactor * menuSize.Value;
            menuTransform.localScale = new Vector3(s, s * canvasSize.y / (float)canvasSize.x, s);
        }

        internal class OurLib : Lib
        {
            override protected void RegisterOnLoaded() { } // we don't need it ourself
        }
    }
}
