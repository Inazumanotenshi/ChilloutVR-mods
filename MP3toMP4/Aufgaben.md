# Aufgaben: MP3toMP4 und der BlankUpdater

Diese Mod wird über den **BlankUpdater** verteilt, Inas eigenen Updater (Plugin `CVRModUpdater.BlankLoader.dll`, Projekt `D:\Unity\PluginDev\BlankUpdater`). Beim Spielstart vergleicht er die installierte DLL mit Inas Download-Server und ersetzt sie, wenn es eine neuere Version gibt. Damit das funktioniert, muss jede Änderung an dieser Mod die Regeln unten einhalten.

## Was der Updater liest

Der Updater lädt die DLL nicht, er liest nur das `MelonInfo`-Attribut aus der Datei, auf dem Server genauso wie im Spiel. Die Windows-Dateiversion (`AssemblyVersion`, `FileVersion`) ist ihm egal.

| Wert | Muss sein | Steht in |
|---|---|---|
| Name | `MP3toMP4` | `MelonInfo` |
| Autor | `Inazumanotenshi` | `MelonInfo` |
| Version | `MAJOR.MINOR.PATCH`, z. B. `1.2.0` oder `1.2.0-beta.3` | `src/Main.cs`, direkt im `MelonInfo` als Text (`"0.3.0"`) |
| DLL-Datei | `MP3toMP4.dll` | `AssemblyName` in der `.csproj` |

## Regeln

- **Name nicht ändern.** Der Updater ordnet installierte DLL und Server-Datei über den Namen im `MelonInfo` zu. Ein neuer Name ist für ihn eine fremde Mod
- **Autor nicht ändern, auch nicht in der Schreibweise.** Weicht der Autor ab, lässt der Updater die Datei absichtlich in Ruhe, damit er nie die Mod von jemand anderem überschreibt
- **Die Version im `MelonInfo` muss fester Text oder eine `const` sein.** Keine berechneten Werte, keine Werte aus Dateien. Der Updater liest sie aus den Metadaten der DLL, ohne Code auszuführen
- **Versionsnummer niemals selbstständig ändern.** Ina zählt Versionen passend zu ihren Git-Releases hoch. Wenn eine neue Nummer nötig ist, Ina fragen und ihre Antwort abwarten
- **Jeder Upload braucht eine neue Versionsnummer.** Gleiche Nummer mit anderer Datei erkennt der Updater nicht als Update, er schreibt nur eine Warnung ins Log
- **Beta-Versionen mit Punkt schreiben:** `1.2.0-beta.3`, nicht `1.2.0-beta3`. Mit Punkt wird die Zahl verglichen, ohne Punkt nur Text, und dann käme `beta10` vor `beta4`
- Eine Version mit Zusatz zählt als älter als ohne: `1.2.0-beta.3 < 1.2.0 < 1.2.1`
- **Den DLL-Namen nicht ändern.** Der Server legt die Datei unter genau diesem Namen ab
- Kein eigener Update- oder Download-Code in der Mod. Das macht ausschließlich der BlankUpdater
- Der BlankUpdater ist keine Abhängigkeit. Die Mod darf ihn nicht referenzieren und muss ohne ihn genauso laufen

## Veröffentlichen

Nur auf Inas ausdrückliche Anweisung:

1. Ina nach der neuen Versionsnummer fragen und sie bei `src/Main.cs`, direkt im `MelonInfo` als Text (`"0.3.0"`) eintragen
2. Bauen: `build.bat` im Ordner `MP3toMP4`
3. Die fertige `MP3toMP4.dll` nach `U:\ChilloutVR\Mods\Beta\` kopieren und die alte Datei überschreiben, ohne Umbenennen. `U:` ist Inas Netzlaufwerk auf den Download-Ordner des Servers
4. Prüfen, ob der Server die neue Version anzeigt: `https://download.blankinternational.com/downloads/.updater/manifest.php`
5. Ist die Beta getestet und Ina gibt sie frei, dieselbe DLL zusätzlich nach `U:\ChilloutVR\Mods\Stable\` kopieren

Spieler auf **Stable** bekommen nur Dateien aus `Stable`. Spieler auf **Beta** bekommen die neuere von Beta und Stable.

## Stand auf dem Server (22.11.2026)

Stable `0.3.0`, Beta `0.3.0` (zwei verschiedene Builds mit derselben Nummer)

## Besonderheiten dieser Mod

- Stable und Beta haben gerade dieselbe Versionsnummer, obwohl die Dateien verschieden sind. Der Updater erkennt dann keinen Unterschied und schreibt nur eine Warnung ins Log. Beim nächsten Release, das Ina freigibt, muss die Nummer steigen
- Ina will die Version nicht ohne Git-Release hochzählen. Also niemals selbstständig ändern, immer vorher fragen
