# TODO

## Erledigt

### Uhrzeit und FPS im Menü (0.3.0-beta.2 bis beta.4)

- Oben links die Uhrzeit, oben rechts die FPS, in den freien Ecken über dem Hexagon
- Stil wie die Versionsanzeige unten: kleine Schrift mit Kontur
- Wird nur bei offenem Menü und nur bei Änderungen geschickt (Uhrzeit 1x pro Minute, FPS bei neuem Wert)
- Einstellungen: „Show clock“ (Standard an), „Show FPS“ (Standard aus), „12-hour clock“ (Standard aus, `14:05` bzw. `2:05 PM`)
- Umschalten lädt die Oberfläche nicht neu, die Anzeige aktualisiert sich sofort

## Ideen

### Ziffernblock direkt über die Schnittstelle öffnen

- Heute geht der Ziffernblock nur als Menüeintrag (`control = "number"`): Der Spieler muss den Eintrag erst anwählen
- Neu: Eine Mod kann den Ziffernblock selbst aufrufen, z. B. `Lib.OpenNumberPad(titel, aktuellerWert, min, max, onDone)`, ähnlich wie CVRs `QuickMenuAPI.OpenNumberInput`
- Öffnet das Action Menu, falls es zu ist, und zeigt den Ziffernblock mit Titel, aktuellem Wert und Grenzen wie gewohnt
- `onDone(float)` bekommt den eingegebenen Wert, bei Abbruch (✕ / Esc) optional `onCancel`
- Nur ein Ziffernblock gleichzeitig: Ein neuer Aufruf ersetzt einen offenen
- Danach in die README unter „For modders“ aufnehmen
