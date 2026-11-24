// AdvActionMenu - Action Menu Generator (data component)
// Sits on the avatar and stores icons and settings per menu entry.
// The actual generator lives in AdvActionMenu.Unity.Editor.dll.
// The CCK ignores this component on upload.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace AdvActionMenu.Unity
{
    [AddComponentMenu("AdvActionMenu/Action Menu Generator")]
    [DisallowMultipleComponent]
    public class ActionMenuGenerator : MonoBehaviour
    {
        public enum TargetMod
        {
            ActionMenu,    // daky's original Action Menu: whole menus via "overwrites"
            AdvActionMenu, // AdvActionMenu: only changes via "item_overrides", plus number input and limits
        }

        public enum IconMode
        {
            EmbedBase64, // icons are embedded as data: URI in the JSON (recommended)
            PngFiles,    // icons as PNG files in the mod's UIResources folder
        }

        public enum NumberControl
        {
            Auto,      // like the mod decides itself (slider stays slider, Input Single becomes number pad)
            Slider,
            NumberPad,
        }

        [Serializable]
        public class Entry
        {
            [Tooltip("Menu path of the entry, e.g. avatar/Clothes/Hat")]
            public string path;
            public Texture2D icon;
            public NumberControl control = NumberControl.Auto;
            public bool useLimits;
            public float min;
            public float max = 1f;

            public bool IsEmpty => icon == null && control == NumberControl.Auto && !useLimits;
        }

        public TargetMod target = TargetMod.AdvActionMenu;

        [Tooltip("Empty = the object this component is on")]
        public GameObject avatar;

        [Tooltip("Empty = ID from the avatar's CVRAssetInfo")]
        public string avatarGuidOverride = "";

        public IconMode iconMode = IconMode.EmbedBase64;

        [Range(32, 512)]
        public int iconSize = 128;

        [Tooltip("Action Menu only: keep the Emotes link when the main menu is overwritten")]
        public bool keepEmotesLink = true;

        [Tooltip("Action Menu only: keep the Profiles link when the main menu is overwritten")]
        public bool keepProfilesLink = true;

        [Tooltip("Action Menu only: write all menus, not only the ones with icons")]
        public bool writeAllMenus;

        public List<Entry> entries = new List<Entry>();

        public GameObject Avatar => avatar != null ? avatar : gameObject;

        public Entry Find(string path)
        {
            foreach (var e in entries)
                if (e != null && e.path == path)
                    return e;
            return null;
        }

        public Entry GetOrCreate(string path)
        {
            var e = Find(path);
            if (e == null)
            {
                e = new Entry { path = path };
                entries.Add(e);
            }
            return e;
        }

        public Texture2D GetIcon(string path) => Find(path)?.icon;

        public void SetIcon(string path, Texture2D tex)
        {
            GetOrCreate(path).icon = tex;
            Cleanup();
        }

        // remove empty entries so the list stays tidy
        public void Cleanup() => entries.RemoveAll(e => e == null || string.IsNullOrEmpty(e.path) || e.IsEmpty);
    }
}
