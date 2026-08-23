# Handoff: Item-VRAM für Git vorbereitet

Datum: 23. August 2026

## Ziel und Grenzen

Der im Unity-Liveprojekt bereits erfolgreich verwendete Item-VRAM-Stand wurde
in der Codex-Arbeitskopie statisch geprüft und für eine spätere Veröffentlichung
dokumentiert. Das Liveprojekt auf `D:` wurde nicht verändert. Es wurden weder
Commit noch Push, Tag oder Release erstellt.

## Statische Prüfung

- `BuildOptimizationPlan(OutfitEntry, out int)` liest die effektive
  outfitspezifische Item-Auswahl über die bestehende Items-/Projektpersistenz.
- Outfit und ausgewählte Items fließen in denselben Optimierungsplan ein.
- Das vorhandene `HashSet<Texture2D>` verhindert die mehrfache Verarbeitung
  einer Textur, die von Outfit und Items gemeinsam verwendet wird.
- Manueller VRAM-Button und Express-Ablauf verwenden denselben Plan.
- Fehlende Items beziehungsweise fehlender Items-Parent fallen ohne harte
  Abhängigkeit auf reine Outfit-Optimierung zurück.
- Standard-`TextureImporter`-Prüfung, Vorschau und bestehende Warnung vor
  irreversiblen Importer-Änderungen bleiben erhalten.

## Sicherheitsanpassung

Der neue EditorPrefs-Schalter `ShiroNewOutfit_OptItems` verwendet für bisher
unbekannte Installationen nun `false` als Standard. Grund: Bei Nutzern, die
Express bereits auf automatische Optimierung ohne Nachfrage gestellt haben,
darf ein Update nicht ohne explizites Opt-in erstmals Item-Texturen verändern.
Explizit gespeicherte Werte bleiben unverändert wirksam.

## Dokumentation

Aktualisiert wurden:

- `README.md`
- `Wiki/VRAM-Optimization.md`
- `Wiki/Items.md`
- neue vorbereitete `RELEASE_NOTES_v3.2.md`

Dokumentiert sind Auswahlumfang, Anwendung auf manuellen und Express-Ablauf,
Deduplizierung, Opt-in-Standard sowie die globale Wirkung gemeinsam verwendeter
TextureImporter-Einstellungen. Zusätzlich wurde die veraltete Aussage in der
Items-Wiki korrigiert, Item-Auswahl werde noch in EditorPrefs gespeichert.

## Verifikation und offene Risiken

Statisch geprüft wurden Aufrufwege, Persistenzzugriff und Diff; `git diff
--check` war sauber.

Nach nachträglicher Freigabe des Unity-Testziels wurde ausschließlich
`Editor/OutfitTextureOptimizer.cs` in den installierten Pluginordner kopiert.
SHA-256 von Quelle und Ziel stimmten danach überein
(`DE3B739A43E234B70AF0A70FB9A7F270A724D1160B7963988BFC4A16D412621D`).
Keine `.meta`-Datei und keine andere Datei des Unity-Projekts wurde verändert.

Die erwartete automatische Kompilierung fand während der Prüfung nicht statt:
Es war kein `Unity.exe`- oder Unity-Hub-Prozess sichtbar. Das lokale
`Editor.log`, `Temp/UnityLockfile` und `Library/ScriptAssemblies` waren zuletzt
am 8. August 2026 aktualisiert worden; der letzte Logzustand endete mit einem
damaligen D3D11-Gerätefehler. Daher kann die aktuelle Kompilierung weder als
erfolgreich noch als fehlgeschlagen gewertet werden. Unity-Kompilierung und
Editor-Workflow bleiben offen.

Vor Veröffentlichung weiterhin empfohlen:

- Option aus: Verhalten entspricht der bisherigen reinen Outfit-Optimierung.
- Zwei Outfits mit unterschiedlichen Item-Auswahlen.
- Gemeinsame Textur zwischen Outfit und Item wird nur einmal geplant.
- Manueller VRAM-Button und Express mit/ohne Nachfrage.
- Projekt ohne Items beziehungsweise ohne Items-Parent.
- Hinweis auf irreversible, global wirksame Importer-Änderungen kontrollieren.

## Empfohlene Git-Trennung

1. Feature-Commit: `Editor/OutfitTextureOptimizer.cs`, README, beide Wiki-Seiten
   und `RELEASE_NOTES_v3.2.md`.
2. Projektgedächtnis-Commit: `AGENTS.md`, `PROJECT_STATE.md`,
   `project-memory/ARCHITECTURE_MAP.md` und beide Handoff-Dateien.

Die Erweiterung ist eine neue sichtbare Funktion und Einstellung; gegenüber
dem öffentlichen v3.1.x-Stand ist daher v3.2.0 sinnvoller als ein Patch-Release.
