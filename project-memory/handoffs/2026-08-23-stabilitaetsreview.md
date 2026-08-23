# Handoff: Vollständiger Stabilitätsreview

Datum: 23. August 2026

## Umfang

Alle zehn C#-Dateien unter `Editor/` wurden vollständig auf Zustandskorrektheit,
Domain-Reload-Verhalten, Persistenz, Performance, destruktive Operationen und
SDK-Reflection geprüft. Die anschließend umgesetzten Änderungen verwenden
weiterhin die vorhandene `partial class OutfitBatchUploader` und
`OutfitProjectData`; es wurde kein paralleles Manager- oder Zustandsmodell
eingeführt.

## Behobene Fehler

- `OutfitBatchUploader.cs`: verwaltete Blendshapes werden zwischen Outfits
  zurückgesetzt; Skin-Autodetection validiert die Avatarzugehörigkeit;
  Plattformwechsel prüfen den Unity-Rückgabewert; fehlende Queue-Outfits werden
  als Fehler erfasst; Session-Daten und verwaiste Blendshape-Snapshots werden
  bereinigt; Versionspersistenz nutzt atomare Writes und Backup-Recovery.
- `OutfitContacts.cs`: Outfit-/Item-Buckets ignorieren nur den temporären
  EditorOnly-Tag des jeweiligen Owner-Roots, nicht dessen Inhalt pauschal;
  explizit ausgeschlossene Unterbäume bleiben ausgeschlossen. Baked Lights
  zählen nicht mehr als Laufzeit-Light.
- `OutfitItems.cs` und `OutfitProjectData.cs`: Item-Auswahl invalidiert VRAM und
  Budgets sofort; All/None wird in einem Schreibvorgang persistiert.
- `OutfitProjectData.cs`: zentrale JSON-Dateien werden über eine Temp-Datei
  ersetzt, die vorherige Version bleibt als `.bak`; Laden versucht bei einer
  beschädigten Hauptdatei das Backup.
- `OutfitNewSetup.cs`: Thumbnail und Resume-State entstehen vor dem Leeren des
  PipelineManagers beziehungsweise vor Auto-Fixes/Refresh; SPS/DPS-Erkennung
  bewertet das Ziel-Outfit und ausgewählte Items unabhängig vom aktuell
  gesetzten Owner-Root-Tag; Temp-Cleanup löscht nur eigene Thumbnail-Dateien.
  Die komplette Defaults-Seite ist in einem höhenadaptiven Scrollbereich
  (180–520 px) bedienbar und als verschachtelte Scrollfläche registriert.
- `OutfitTextureOptimizer.cs`: Progressbar-Cleanup liegt in `finally` und
  VRAM-Pump-Fehler werden sichtbar protokolliert.

Ein zunächst angenommener Nullfall bei `VRCAvatar.FirstOrDefault` wurde nicht
beibehalten: Der isolierte Unity-Compile zeigte, dass `VRCAvatar` in der
installierten SDK-Version ein Werttyp ist; der vorhandene ID-Leercheck ist
korrekt.

## Verifikation

- `git diff --check`: sauber.
- Geänderte Plugin-C#-Dateien wurden einzeln in den freigegebenen Pluginordner
  auf `D:` kopiert und per SHA-256 bytegleich verifiziert.
- Der laufende Unity-Editor führte während der Beobachtung keinen Auto-Refresh
  aus; Editor-Log und reguläre ScriptAssembly blieben unverändert.
- Daher wurde die vorhandene Unity-Bee-Response-Datei kopiert, nur ihre beiden
  Ausgabepfade in einen neuen Temp-Ordner umgeleitet und die vollständige
  `Assembly-CSharp-Editor` mit Unitys Mono/Roslyn kompiliert.
- Ergebnis: `COMPILER_EXIT=0`, Plugin-Code ohne Compilerfehler. Zwei CS8032-
  Warnungen entstanden ausschließlich durch Source-Generator/Roslyn-
  Versionsauflösung beim isolierten Aufruf außerhalb des normalen Bee-Runners.

## Bewusst nicht umgesetzt

- Keine sofortige Migration von namensbasierten Avatar-/Outfit-Schlüsseln auf
  neue stabile IDs: Das wäre eine riskante Datenmigration und gehört in einen
  eigenen, getesteten Versionsschritt.
- Keine rein kosmetische Zerlegung der großen Hauptdatei während desselben
  Stabilitäts-Passes: Funktionsfixes bleiben dadurch leichter reviewbar.
- Keine TextureImporter-Optimierung wurde ausgeführt.
- Kein Commit, Push, Tag oder Release wurde erstellt.

## Empfohlene manuelle Regressionen

- Outfit A mit Override und Outfit B ohne denselben Override abwechselnd
  aktivieren; B muss den verwalteten Wert auf 0 setzen.
- Zwischen zwei Avataren wechseln; Skin-Feld und Blendshape-Liste müssen zum
  jeweils ausgewählten Avatar gehören.
- Zwei Outfits und Items mit unterschiedlichen Contacts/Lights vergleichen,
  unabhängig davon, welches Outfit aktuell aktiv ist.
- Item All/None umschalten und unmittelbare Budget-/VRAM-Aktualisierung prüfen.
- Express mit SDK-Auto-Fix/Domain-Reload prüfen; Resume-Daten und Thumbnail
  müssen erhalten bleiben.
