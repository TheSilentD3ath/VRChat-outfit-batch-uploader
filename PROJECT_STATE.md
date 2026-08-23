# VRChat Outfit Batch Uploader – Projektstatus

Letzte Aktualisierung: 23. August 2026

## Baseline

- Codex-Arbeitskopie: GitHub `main`, Commit
  `75943eeb4c90317044bf1a25f592348e5fa20963` (`v3.1.1`).
- Öffentliche Tags: `3.0.0` und `3.1.0`; `main` enthält danach den
  v3.1.1-Fix-Commit.
- Zielumgebung: Unity 2022.x mit VRChat Avatars SDK.
- Installierte und vom Nutzer zum Synchronisieren/Testen freigegebene Kopie:
  im laufenden Unity-Projekt `Main avi` auf Laufwerk `D:`. Unity darf
  Änderungen am Pluginordner automatisch neu kompilieren.
- Der dortige innere Ordnername `VRChat-outfit-batch-uploader-2.0.0` ist nur
  ein falscher Altname; der Inhalt ist nicht Version 2.0.

## Migrierter Claude-Arbeitsstand

Der Unity-Liveordner stimmt nach normalisiertem Textvergleich mit dem
GitHub-Stand überein, abgesehen von einer zusätzlichen Änderung in
`Editor/OutfitTextureOptimizer.cs`: Optional werden auch die Texturen der für
ein Outfit ausgewählten Items in den Optimierungsplan aufgenommen. Diese
Änderung wurde in die Codex-Arbeitskopie übernommen und wird vom Nutzer im
Unity-Projekt bereits erfolgreich verwendet. Sie ist jedoch weder in GitHub
`main` auf Commit `75943ee` noch im veröffentlichten Release-Asset
`VRChat-outfit-batch-uploader-3.1.0.unitypackage` enthalten. Das Asset wurde am
11. Juli 2026 veröffentlicht; sein tatsächlicher Inhalt wurde am 23. August
2026 entpackt und geprüft.

## Für Git vorbereiteter Arbeitsstand

Die Item-VRAM-Erweiterung wurde am 23. August 2026 erneut statisch geprüft und
für eine spätere Veröffentlichung dokumentiert. Outfit und ausgewählte Items
werden über einen gemeinsamen Plan verarbeitet; das bestehende `HashSet`
dedupliziert gemeinsam verwendete Texturen. Manuelle und Express-Optimierung
verwenden denselben Plan, und die Vorschau nennt die Anzahl einbezogener Items.

Als Sicherheitskorrektur ist die neue Item-Option in der Arbeitskopie nun
standardmäßig deaktiviert. Dadurch kann ein bereits auf "nicht mehr fragen"
gestellter Express-Workflow nach einem Plugin-Update nicht ohne explizites
Opt-in erstmals Item-Texturen verändern. Ein bereits gespeicherter
`ShiroNewOutfit_OptItems`-Wert bleibt wirksam.

README, Wiki und vorbereitete `RELEASE_NOTES_v3.2.md` beschreiben die Funktion,
die Deduplizierung und die globalen Auswirkungen gemeinsam verwendeter
TextureImporter-Einstellungen. Noch kein Commit, Push, Tag oder Release wurde
erstellt.

Am 23. August 2026 wurde ausschließlich
`Editor/OutfitTextureOptimizer.cs` aus der Git-Arbeitskopie in den freigegebenen
Pluginordner auf `D:` synchronisiert; Quell- und Ziel-SHA-256 waren identisch.
Andere Dateien des Unity-Projekts wurden nicht verändert. Eine automatische
Unity-Kompilierung konnte noch nicht bestätigt werden: Es war kein laufender
Unity-Prozess sichtbar, `Editor.log`, `Temp/UnityLockfile` und die zuletzt
gebauten ScriptAssemblies stammen vom 8. August 2026. Der Kompilierungs- und
manuelle Regressionstest bleibt daher offen.

## Nächste Schritte

1. Die vorbereitete Item-VRAM-Änderung in einem Unity-Testprojekt gegen die in
   der Handoff-Datei genannten Fälle regressionsprüfen.
2. Nach Freigabe Quellcode/Nutzerdokumentation und Projektgedächtnis in logisch
   getrennten Commits ablegen; für die neue Nutzerfunktion ist v3.2.0 sinnvoll.
3. Unity mit `Main avi` öffnen beziehungsweise den laufenden Prozess sichtbar
   machen, die bereits synchronisierte Datei kompilieren lassen und die
   Console auf Compilerfehler prüfen.
4. Vor dem nächsten Release Dokumentation und Versionsangaben abgleichen.
