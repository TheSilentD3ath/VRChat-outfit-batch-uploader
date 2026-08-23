# VRChat Outfit Batch Uploader – Arbeitsregeln

## Projektgrenzen

Dieses Repository ist die maßgebliche Codex-Arbeitskopie des Unity-Editor-
Plugins. Es basiert auf
`https://github.com/TheSilentD3ath/VRChat-outfit-batch-uploader`.

Das installierte Plugin und freigegebene Unity-Testziel liegt unter:

`D:\Local\VRChatProjects\Main avi\Assets\VRChat-outfit-batch-uploader-3.0.0\VRChat-outfit-batch-uploader-2.0.0`

Der innere Ordnername `2.0.0` ist veraltet und falsch. Sein Inhalt entspricht
Version 3.1 beziehungsweise dem Stand nach v3.1.1 plus einer noch nicht
veröffentlichten Änderung. Den Ordnernamen niemals zur Versionsbestimmung
verwenden.

## Pflichtablauf

Vor Änderungen `PROJECT_STATE.md` und die relevanten Dateien unter
`project-memory/` lesen. Zuerst die vorhandenen Partial-Klassen und ihre
Zuständigkeiten untersuchen; keine parallelen Manager- oder Zustandsmodelle
einführen.

Nach materiellen Änderungen:

1. Unity-Kompilierung und einen passenden Editor-Workflow im Testprojekt
   prüfen, soweit verfügbar.
2. Eine Handoff-Datei unter
   `project-memory/handoffs/YYYY-MM-DD-kurzbeschreibung.md` schreiben.
3. `PROJECT_STATE.md` aktualisieren.
4. Geänderte Dateien, Tests und offene Risiken im Abschluss nennen.

## Entwicklungs- und Testworkflow

- Dieses Git-Repository bleibt die eigenständige, maßgebliche Code-
  Arbeitskopie. Änderungen werden zuerst hier nachvollziehbar entwickelt.
- Der oben genannte Pluginordner im laufenden Unity-Projekt auf `D:` ist als
  Testziel freigegeben. Für Kompilierungs- und Funktionstests dürfen geänderte
  Plugin-Dateien dorthin synchronisiert werden; Unity kompiliert sie während
  des Betriebs automatisch neu.
- Die Freigabe gilt ausschließlich für den genannten Pluginordner. Andere
  Assets, Szenen, Avatare, Packages oder ProjectSettings des Unity-Projekts
  dürfen nur verändert werden, wenn der Nutzer dies für einen konkreten Test
  verlangt.
- Vor jeder Synchronisierung die exakten Zieldateien nennen beziehungsweise
  dokumentieren. Keine pauschale Spiegelung, die projektspezifische Daten oder
  nicht zugehörige Dateien löschen könnte.
- Nach einem Test relevante Unity-Compilerfehler und das beobachtete Verhalten
  dokumentieren. Änderungen, die Unity selbst an `.meta`-Dateien vornimmt,
  kontrolliert gegen die Arbeitskopie prüfen.

## Sicherheits- und Release-Regeln

- `ProjectSettings/ShiroOutfit_data.json`,
  `ProjectSettings/ShiroOutfit_versions.json` und Upload-Logs enthalten
  nutzer- beziehungsweise avatarspezifischen Zustand und gehören nicht in das
  Plugin-Repository.
- Unity-`.meta`-Dateien erhalten; GUIDs nicht ohne zwingenden Grund ändern.
- Destruktive Texture-Import-Optimierungen nur nach expliziter Bestätigung und
  mit Hinweis, dass gemeinsam verwendete Texturen alle Outfits betreffen.
- VRChat-SDK-Interna werden per Reflection verwendet und müssen bei SDK-
  Updates defensiv ausfallen.
- Veröffentlichung, Commit, Push, Tag oder Release nur auf ausdrücklichen
  Auftrag.

## Architekturgrundsatz

Das Hauptfenster ist als `partial class OutfitBatchUploader` auf mehrere
Editor-Dateien verteilt. Neue Funktionen gehören in die fachlich passende
Partial-Datei. Gemeinsamer Zustand soll die vorhandene Persistenz in
`OutfitProjectData.cs` und die vorhandenen Domain-Reload-Mechanismen nutzen.
