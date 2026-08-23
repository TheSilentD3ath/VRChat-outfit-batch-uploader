# Handoff: Migration aus Claude Cowork

Datum: 23. August 2026

## Quellen

Die öffentliche GitHub-Historie wurde als Git-Baseline geklont. Zusätzlich
wurden der installierte Pluginordner im Unity-Projekt und die einschlägigen
Claude-Cowork-Sitzungen geprüft. Das bisherige Claude-Projektgedächtnis bestand
nur aus einer leeren Überschrift und enthielt keine verwertbare Übergabe.

## Entwicklungsgeschichte

Das Tool entstand zur Verwaltung mehrerer VRChat-Outfit-Uploads aus einem
Unity-Projekt. Spätere Claude-Aufgaben erweiterten es unter anderem um
plattformgruppierte Batches, Domain-Reload-Fortsetzung, Erstanlage per
Express/Advanced, Items, FaceEmo, Budgetanzeigen, VRAM-Optimierung,
projektbezogene Persistenz, Dry Run, Upload-Log, Retry, Blueprint-ID-Hilfen und
Thumbnail-Workflows. Der öffentliche Stand ist in README, Wiki,
Release Notes und Git-Historie dokumentiert.

## Wichtiger Versionshinweis

Der Pfad im echten Unity-Projekt enthält außen `3.0.0` und innen `2.0.0`.
Beide Namen sind als Versionsquelle unzuverlässig. Der Inhalt entspricht
GitHub v3.1.1 mit einer zusätzlichen, noch unveröffentlichten Änderung.

## Unveröffentlichte Änderung

Nur `Editor/OutfitTextureOptimizer.cs` wich semantisch vom GitHub-Stand ab.
Die Änderung fügt die Einstellung `ShiroNewOutfit_OptItems` hinzu und erweitert
den Optimierungsplan um Texturen der für das jeweilige Outfit ausgewählten
Items. Mehrfach verwendete Texturen bleiben durch das vorhandene `HashSet`
dedupliziert. Die Änderung wurde in die Codex-Arbeitskopie migriert. Der Nutzer
setzt genau diesen Live-Stand bereits erfolgreich im eigenen Unity-Projekt ein.

Eine Abfrage aller öffentlichen Branches, Tags und GitHub-Releases am
23. August 2026 ergab `main` auf Commit `75943ee`, Tags bis `3.1.0` und ein
öffentliches Asset
`VRChat-outfit-batch-uploader-3.1.0.unitypackage` (SHA-256 laut GitHub:
`56d670a8476dd99748355e12b65754deea8bd6f6fe51c1f85d082bba049e8f0f`).
Das Paket wurde entpackt; sein `OutfitTextureOptimizer.cs` enthält die
Item-VRAM-Erweiterung nicht. Der im Unity-Projekt erfolgreich verwendete Stand
ist damit neuer als das öffentliche 3.1.0-Paket, auch wenn Release-Assets in
diesem Projekt historisch nicht immer aus einem anschließend gepushten Commit
entstanden.

## Künftige Regressionstests

- Projekt ohne Items und mit deaktivierter Option testen.
- Zwei Outfits mit unterschiedlichen Item-Auswahlen testen.
- Gemeinsame Textur zwischen Outfit und Item auf einmalige Verarbeitung
  prüfen.
- Automatische Express-Optimierung sowie manuellen VRAM-Button testen.
- Sicherstellen, dass die Warnung weiterhin klar auf irreversible
  Importer-Änderungen hinweist.

## Freigabe des Unity-Testziels

Der Nutzer hat am 23. August 2026 ausdrücklich freigegeben, geänderte
Plugin-Dateien für Tests in den genannten Pluginordner seines laufenden
Unity-Projekts zu synchronisieren. Unity kompiliert Änderungen dort automatisch
neu. Die Git-Arbeitskopie bleibt die eigenständige Codebasis; die Freigabe
erstreckt sich nicht pauschal auf andere Teile des Unity-Projekts.
