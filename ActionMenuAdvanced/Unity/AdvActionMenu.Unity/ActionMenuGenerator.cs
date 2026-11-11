// AdvActionMenu - Action Menu Generator (Daten-Komponente)
// Liegt auf dem Avatar und merkt sich Icons und Einstellungen pro Menuepunkt.
// Der eigentliche Generator steckt in AdvActionMenu.Unity.Editor.dll.
// Beim Upload ignoriert das CCK diese Komponente.

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
            ActionMenu,    // daky's original Action Menu: ganze Menues per "overwrites"
            AdvActionMenu, // AdvActionMenu: nur Aenderungen per "item_overrides", plus Zahleneingabe und Grenzen
        }

        public enum IconMode
        {
            EmbedBase64, // Icons stecken als data:-URI in der JSON (empfohlen)
            PngFiles,    // Icons als PNG-Dateien im UIResources-Ordner der Mod
        }

        public enum NumberControl
        {
            Auto,      // wie die Mod es selbst entscheidet (Slider bleibt Slider, Input Single wird Ziffernblock)
            Slider,
            NumberPad,
        }

        [Serializable]
        public class Entry
        {
            [Tooltip("Menue-Pfad des Eintrags, z. B. avatar/Clothes/Hat")]
            public string path;
            public Texture2D icon;
            public NumberControl control = NumberControl.Auto;
            public bool useLimits;
            public float min;
            public float max = 1f;

            public bool IsEmpty => icon == null && control == NumberControl.Auto && !useLimits;
        }

        public TargetMod target = TargetMod.AdvActionMenu;

        [Tooltip("Leer = das Objekt, auf dem diese Komponente liegt")]
        public GameObject avatar;

        [Tooltip("Leer = ID aus der CVRAssetInfo des Avatars")]
        public string avatarGuidOverride = "";

        public IconMode iconMode = IconMode.EmbedBase64;

        [Range(32, 512)]
        public int iconSize = 128;

        [Tooltip("Nur Action Menu: Emotes-Link behalten, wenn das Hauptmenue ueberschrieben wird")]
        public bool keepEmotesLink = true;

        [Tooltip("Nur Action Menu: Profiles-Link behalten, wenn das Hauptmenue ueberschrieben wird")]
        public bool keepProfilesLink = true;

        [Tooltip("Nur Action Menu: alle Menues schreiben, nicht nur die mit Icons")]
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

        // leere Eintraege entfernen, damit die Liste uebersichtlich bleibt
        public void Cleanup() => entries.RemoveAll(e => e == null || string.IsNullOrEmpty(e.path) || e.IsEmpty);
    }
}
