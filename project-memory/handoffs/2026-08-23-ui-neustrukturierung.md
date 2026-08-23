# Handoff: UI-Neustrukturierung, erster Schritt

Datum: 23. August 2026

## Ziel

Das zunehmend lange Hauptfenster wird nach Arbeitsphasen getrennt, ohne
Uploadlogik oder Projektpersistenz zu verändern.

## Umgesetzt

- Eine Hauptnavigation mit `Outfits`, `New Outfit` und `Defaults` wurde in
  `OutfitBatchUploader.OnGUI` ergänzt.
- `New Outfit` zeigt ausschließlich erkannte Outfits ohne Blueprint-ID und
  behält deren Express-/Advanced-Aktionen in der vorhandenen Outfitkarte.
- `Defaults` verwendet die gesamte verbleibende Fensterhöhe statt als langer
  Bereich unter der Outfitliste zu erscheinen.
- Die Batch-Sektion bleibt in `Outfits` und `New Outfit` unter der flexibel
  scrollenden Liste sichtbar.
- Der ausgewählte Hauptbereich ist als reiner EditorWindow-UI-Zustand
  serialisiert; das Format von `OutfitProjectData` bleibt unverändert.
- Nach Sichtprüfung per Unity-Video wurden die Outfitkarten auf eine kompakte
  zweistufige Darstellung umgestellt. Die geschlossene Karte behält Name,
  Aktivstatus, Batch-Auswahl, Plattformzusammenfassung, Select/Ping/Upload und
  Performancewerte sichtbar. Blueprint-ID, Sekundäraktionen und fachliche
  Detailbereiche erscheinen erst nach Aufklappen.
- Neue Outfits zeigen Express/Advanced weiterhin auch bei geschlossener Karte,
  damit die Erstkonfiguration nicht versteckt wird.
- Reine `Very Poor`-Ränge für Contacts, Lights und VRAM verwenden nach einer
  zweiten Video-Sichtprüfung ein gedämpftes Orange. Harte Contact-/Parameter-
  Limits bleiben rot, damit echte Blocker in der kompakten Liste auffallen.

## Verifikation

- `git diff --check`: ohne Whitespace-Fehler.
- Nur `Editor/OutfitBatchUploader.cs` und `Editor/OutfitNewSetup.cs` wurden in
  das freigegebene Unity-Testziel synchronisiert.
- Vollständiger isolierter Unity-Editor-Compile: Exitcode 0, keine
  Plugin-Compilerfehler. Zwei CS8032-Warnungen sind die bereits bekannte
  Source-Generator-Abweichung des isolierten Mono/Roslyn-Aufrufs.

## Nächster visueller Schritt

Die verdichteten Karten und das Verhalten bei schmaler Fensterbreite in Unity
prüfen. Besonders kontrollieren, ob Name, Plattformzusammenfassung und die drei
Primäraktionen ohne störende Umbrüche bleiben.

Es wurde kein Commit, Push, Tag oder Release erstellt.
