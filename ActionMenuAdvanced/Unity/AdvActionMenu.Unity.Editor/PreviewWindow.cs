// AdvActionMenu - preview window
// AdvActionMenu: hexagon with gaps, first entry at the top, clockwise
// daky's Action Menu: round wheel, first entry at the bottom
// Clicking a submenu opens it, clicking a toggle switches it (preview only), center = back.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Gen = AdvActionMenu.Unity.ActionMenuGenerator;

namespace AdvActionMenu.Unity.EditorTools
{
    public class PreviewWindow : EditorWindow
    {
        [SerializeField] Gen gen;
        [SerializeField] string currentMenu = AMBuilder.RootMenu;
        [SerializeField] List<string> history = new List<string>();

        readonly Dictionary<string, bool> _states = new Dictionary<string, bool>();
        AMMenus _menus;
        int _hover = -1;
        bool _hoverCenter;
        static GUIStyle _label;

        // colors like in game (CVR default)
        static readonly Color HexBase = new Color(0.133f, 0.133f, 0.133f, 0.85f);
        static readonly Color HexHover = new Color(0.20f, 0.20f, 0.20f, 1f);
        static readonly Color Accent = new Color(0.663f, 0.157f, 0f, 1f);
        static readonly Color Background = new Color(0.36f, 0.40f, 0.33f, 1f);

        struct ViewItem
        {
            public string key;
            public AMItem item;
            public Texture2D icon;
        }

        [MenuItem("Tools/AdvActionMenu/Preview")]
        static void OpenFromMenu()
        {
            var go = Selection.activeGameObject;
            Open(go != null ? go.GetComponent<Gen>() : null);
        }

        public static void Open(Gen g)
        {
            var w = GetWindow<PreviewWindow>("Action Menu Preview");
            w.minSize = new Vector2(320, 400);
            if (g != null && w.gen != g) { w.gen = g; w.ResetNav(); }
            w.Show();
        }

        void ResetNav()
        {
            currentMenu = AMBuilder.RootMenu;
            history.Clear();
            _states.Clear();
        }

        void OnEnable() => wantsMouseMove = true;
        void OnInspectorUpdate() => Repaint();

        void OnSelectionChange()
        {
            var go = Selection.activeGameObject;
            var g = go != null ? go.GetComponent<Gen>() : null;
            if (g != null && g != gen) { gen = g; ResetNav(); Repaint(); }
        }

        bool Advanced => gen != null && gen.target == Gen.TargetMod.AdvActionMenu;

        List<ViewItem> BuildView()
        {
            _menus = AMBuilder.Build(gen.Avatar, Advanced);
            var list = new List<ViewItem>();
            if (_menus.TryGetValue(currentMenu, out var items))
                foreach (var it in items)
                {
                    var key = currentMenu + AMBuilder.Sep + it.name;
                    list.Add(new ViewItem { key = key, item = it, icon = gen.GetIcon(key) });
                }
            if (currentMenu == AMBuilder.RootMenu)
            {
                var emotes = Advanced || gen.keepEmotesLink;
                var profiles = Advanced || gen.keepProfilesLink;
                foreach (var (key, extra) in AMBuilder.RootExtras(emotes, profiles))
                    list.Add(new ViewItem { key = key, item = extra, icon = gen.GetIcon(key) });
            }
            return list;
        }

        bool IsEnabled(ViewItem vi) => _states.TryGetValue(vi.key, out var v) ? v : vi.item.enabled ?? false;

        // ------------------------------------------------------------------
        void OnGUI()
        {
            if (gen == null || AMBuilder.FindCvrAvatar(gen.Avatar) == null)
            {
                EditorGUILayout.HelpBox("Select your avatar with the AdvActionMenu Action Menu Generator component.", MessageType.Info);
                return;
            }

            var view = BuildView();
            if (view.Count == 0 && currentMenu != AMBuilder.RootMenu) { ResetNav(); view = BuildView(); }

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                using (new EditorGUI.DisabledScope(history.Count == 0))
                    if (GUILayout.Button("Back", EditorStyles.toolbarButton, GUILayout.Width(50)))
                        GoBack();
                if (GUILayout.Button("Home", EditorStyles.toolbarButton, GUILayout.Width(50)))
                    ResetNav();
                GUILayout.Label(currentMenu.Replace("/", "  ›  "), EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                GUILayout.Label(Advanced ? "AdvActionMenu" : "Action Menu", EditorStyles.miniLabel);
            }

            var area = GUILayoutUtility.GetRect(10, 10, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(area, Background);
            var size = Mathf.Min(area.width, area.height) - 20;
            if (size > 60)
            {
                var r = new Rect(area.center.x - size / 2, area.center.y - size / 2, size, size);
                HandleInput(r, view);
                if (Event.current.type == EventType.Repaint)
                {
                    if (Advanced) DrawHexMenu(r, view);
                    else DrawRoundMenu(r, view);
                }
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (_hover >= 0 && _hover < view.Count)
                {
                    var vi = view[_hover];
                    EditorGUILayout.LabelField(vi.item.name, vi.item.action?.type == "menu" ? "submenu" : vi.item.action?.control ?? "", EditorStyles.boldLabel);
                    if (!string.IsNullOrEmpty(vi.item.action?.parameter))
                        EditorGUILayout.LabelField("Parameter", vi.item.action.parameter, EditorStyles.miniLabel);
                }
                else
                    EditorGUILayout.LabelField("Click: open submenu or switch toggle (preview only) · Center: back", EditorStyles.miniLabel);
            }
        }

        void GoBack()
        {
            if (history.Count == 0) return;
            currentMenu = history[history.Count - 1];
            history.RemoveAt(history.Count - 1);
        }

        // angle in degrees, clockwise: AdvActionMenu 0 = top, Action Menu 0 = bottom
        Vector2 Dir(float deg)
        {
            var a = deg * Mathf.Deg2Rad;
            return Advanced ? new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) : new Vector2(-Mathf.Sin(a), Mathf.Cos(a));
        }

        float InnerRatio => Advanced ? 0.24f : 0.36f;

        void HandleInput(Rect r, List<ViewItem> view)
        {
            var e = Event.current;
            var c = r.center;
            var R = r.width / 2f;
            var d = e.mousePosition - c;
            var dist = d.magnitude;
            var n = view.Count;
            int prevHover = _hover; var prevCenter = _hoverCenter;
            _hover = -1;
            _hoverCenter = dist <= R * InnerRatio;
            if (n > 0 && !_hoverCenter && dist <= R)
            {
                var phi = Advanced ? Mathf.Atan2(d.x, -d.y) : Mathf.Atan2(-d.x, d.y);
                if (phi < 0) phi += 2 * Mathf.PI;
                _hover = Mathf.RoundToInt(phi * n / (2 * Mathf.PI)) % n;
            }
            if (e.type == EventType.MouseMove && (prevHover != _hover || prevCenter != _hoverCenter)) Repaint();
            if (e.type != EventType.MouseDown || e.button != 0) return;

            if (_hoverCenter) { GoBack(); e.Use(); return; }
            if (_hover < 0) return;
            var vi = view[_hover];
            var a = vi.item.action;
            if (a == null) return;
            if (a.type == "menu")
            {
                if (a.menu != null && _menus.ContainsKey(a.menu)) { history.Add(currentMenu); currentMenu = a.menu; _hover = -1; }
                else ShowNotification(new GUIContent("The mod builds this menu in game"));
            }
            else if (a.control == "toggle")
            {
                if (a.exclusive_option == true)
                {
                    foreach (var o in view) if (o.item.action?.parameter == a.parameter) _states[o.key] = false;
                    _states[vi.key] = true;
                }
                else _states[vi.key] = !IsEnabled(vi);
            }
            else ShowNotification(new GUIContent("In game this opens the " + (a.control ?? "?") + " widget"));
            e.Use();
            Repaint();
        }

        // ----- Hexagon (AdvActionMenu) -----
        static float HexEdge(float R, float deg)
        {
            var local = ((deg % 60f) + 60f) % 60f - 30f;
            return R * Mathf.Cos(30f * Mathf.Deg2Rad) / Mathf.Cos(local * Mathf.Deg2Rad);
        }

        void DrawHexMenu(Rect r, List<ViewItem> view)
        {
            var c = r.center;
            var R = r.width / 2f * 0.98f;
            var n = view.Count;
            var w = n > 0 ? 360f / n : 360f;

            // segments as pie slices up to the hexagon edge (convex), gaps and center are drawn on top
            for (var i = 0; i < Mathf.Max(1, n); ++i)
            {
                Color col;
                if (n == 0) col = HexBase;
                else if (IsEnabled(view[i])) col = i == _hover ? Accent : new Color(Accent.r, Accent.g, Accent.b, 0.88f);
                else col = i == _hover ? HexHover : HexBase;

                var a0 = i * w - w / 2f; var a1 = a0 + w;
                var pts = new List<Vector3>();
                if (n > 1) pts.Add(c);
                var angles = new List<float> { a0 };
                for (var v = Mathf.Ceil(a0 / 60f) * 60f; v < a1; v += 60f) if (v > a0) angles.Add(v);
                angles.Add(a1);
                foreach (var a in angles) pts.Add(c + Dir(a) * HexEdge(R, a));
                Handles.color = col;
                Handles.DrawAAConvexPolygon(pts.ToArray());
            }

            // gaps
            if (n > 1)
            {
                Handles.color = Background;
                for (var i = 0; i < n; ++i)
                {
                    var a = i * w - w / 2f;
                    Handles.DrawAAPolyLine(r.width / 500f * 6f, (Vector3)c, (Vector3)(c + Dir(a) * R * 1.05f));
                }
            }

            // Mitte
            var inner = R * InnerRatio;
            var hex = new Vector3[6];
            for (var k = 0; k < 6; ++k) hex[k] = c + Dir(k * 60f) * inner;
            Handles.color = Background; Handles.DrawAAConvexPolygon(hex);
            for (var k = 0; k < 6; ++k) hex[k] = c + Dir(k * 60f) * (inner - r.width / 500f * 6f);
            Handles.color = _hoverCenter ? HexHover : new Color(0.09f, 0.09f, 0.09f, 0.95f);
            Handles.DrawAAConvexPolygon(hex);

            var s = r.width / 500f;
            for (var i = 0; i < n; ++i)
            {
                var ro = HexEdge(R, i * w);
                var narrow = w < 24f;
                var pos = c + Dir(i * w) * (narrow ? ro * 0.72f : ro * 0.64f);
                var avail = narrow ? (ro - R * InnerRatio) * 0.9f : 2f * ro * 0.64f * Mathf.Tan(Mathf.Min(w, 120f) / 2f * Mathf.Deg2Rad);
                var showIcon = view[i].icon != null && !narrow && avail >= 80f * s;
                DrawItem(pos, view[i].item.name, showIcon ? view[i].icon : null, s, Mathf.Clamp(avail / Mathf.Max(1, view[i].item.name.Length * 0.55f), 11f * s, 22f * s));
            }
        }

        // ----- round wheel (daky's Action Menu) -----
        void DrawRoundMenu(Rect r, List<ViewItem> view)
        {
            var c = r.center;
            var R = r.width / 2f;
            var n = view.Count;
            Handles.color = new Color(0.33f, 0.2f, 0.02f, 1f);
            Handles.DrawSolidDisc(c, Vector3.forward, R);
            for (var i = 0; i < n; ++i)
            {
                var on = IsEnabled(view[i]);
                if (!on && i != _hover) continue;
                var span = 360f / n; var start = i * span - span / 2f;
                var pts = new List<Vector3>();
                if (n > 1) pts.Add(c);
                var steps = Mathf.Max(2, Mathf.CeilToInt(span / 3f));
                for (var k = 0; k <= steps; ++k) pts.Add(c + Dir(start + span * k / steps) * R);
                Handles.color = new Color(0.871f, 0.835f, 0.188f, i == _hover ? 0.75f : 0.5f);
                Handles.DrawAAConvexPolygon(pts.ToArray());
            }
            Handles.color = new Color(0.882f, 0.698f, 0.051f, 1f);
            for (var i = 0; i < n && n > 1; ++i)
                Handles.DrawAAPolyLine(4f * r.width / 500f, (Vector3)c, (Vector3)(c + Dir((i + 0.5f) * 360f / n) * R));
            Handles.color = new Color(0.36f, 0.235f, 0f, 1f);
            Handles.DrawSolidDisc(c, Vector3.forward, R * InnerRatio);
            var s = r.width / 500f;
            for (var i = 0; i < n; ++i)
                DrawItem(c + Dir(i * 360f / n) * R * 0.71f, view[i].item.name, view[i].icon, s, 20f * s);
        }

        static void DrawItem(Vector2 pos, string label, Texture icon, float s, float fontSize)
        {
            if (_label == null)
                _label = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Overflow, wordWrap = false };
            _label.fontSize = Mathf.Max(7, Mathf.RoundToInt(fontSize));
            var content = new GUIContent(label ?? "");
            var ls = _label.CalcSize(content);
            var iconSize = icon != null ? 56f * s : 0f;
            var top = pos.y - (iconSize + ls.y) / 2f;
            if (icon != null)
                GUI.DrawTexture(new Rect(pos.x - iconSize / 2f, top, iconSize, iconSize), icon, ScaleMode.ScaleToFit, true);
            var lr = new Rect(pos.x - ls.x / 2f, top + iconSize, ls.x, ls.y);
            _label.normal.textColor = new Color(0, 0, 0, 0.9f);
            GUI.Label(new Rect(lr.x + 1, lr.y + 1, lr.width, lr.height), content, _label);
            _label.normal.textColor = Color.white;
            GUI.Label(lr, content, _label);
        }
    }
}
