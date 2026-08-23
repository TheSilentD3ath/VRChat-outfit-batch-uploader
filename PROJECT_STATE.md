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

README, Wiki und `RELEASE_NOTES_v3.2.md` beschreiben die Funktion, die
Deduplizierung und die globalen Auswirkungen gemeinsam verwendeter
TextureImporter-Einstellungen.

Am 23. August 2026 wurde ausschließlich
`Editor/OutfitTextureOptimizer.cs` aus der Git-Arbeitskopie in den freigegebenen
Pluginordner auf `D:` synchronisiert; Quell- und Ziel-SHA-256 waren identisch.
Andere Dateien des Unity-Projekts wurden nicht verändert. Eine automatische
Unity-Kompilierung konnte noch nicht bestätigt werden: Es war kein laufender
Unity-Prozess sichtbar, `Editor.log`, `Temp/UnityLockfile` und die zuletzt
gebauten ScriptAssemblies stammen vom 8. August 2026. Der Kompilierungs- und
manuelle Regressionstest bleibt daher offen.

## Veröffentlichung v3.2.0

Die vorbereitete Änderung wurde am 23. August 2026 veröffentlicht:

- Feature-Commit `c1adec8` trennt Quellcode und öffentliche Dokumentation.
- Projektgedächtnis-Commit `79ae6b0` enthält Arbeitsregeln, Status und
  Migrationsübergaben.
- Annotierter Git-Tag `3.2.0` zeigt auf `79ae6b0`.
- GitHub-Release: `v3.2.0 — Selected-item VRAM optimization`.
- Release-Asset: `VRChat-outfit-batch-uploader-3.2.0.unitypackage`, 113351
  Bytes, SHA-256
  `2AA0E1969CD8546341CC9CE597212C42F06F552FAB58B9854C7C25D51E59281E`.

Das Unitypackage folgt dem historischen v3.1.0-Importlayout unter dem weiterhin
veralteten doppelten Ordnerpfad und enthält Plugin-Code, Wiki, Lizenz sowie die
v3.2-Release Notes. Interne Codex-Dateien und projektspezifischer Zustand sind
nicht enthalten.

## Unveröffentlichter Stabilitätsstand nach v3.2.0

Nach einem vollständigen Review aller Editor-C#-Dateien wurde am 23. August
2026 ein noch nicht committeter Stabilitäts-Pass umgesetzt:

- Beim Outfitwechsel werden alle vom Tool verwalteten Blendshapes zunächst auf
  null gesetzt, bevor die Ziel-Overrides angewendet werden.
- Ein Avatarwechsel kann keinen Skin-Renderer des vorherigen Avatars mehr
  behalten.
- Contact-/Light-Budgets werden unabhängig vom momentan aktiven Outfit in
  Outfit- und Item-Buckets erfasst; nur wirklich untergeordnete
  `EditorOnly`-Teilbäume werden ausgeschlossen. Rein gebackene Lights zählen
  nicht mehr als Laufzeit-Light.
- Abgelehnte Unity-Plattformwechsel brechen kontrolliert ab.
- Verschwundene Outfits werden im Batch als fehlgeschlagen statt erfolgreich
  gezählt; Session-Queue-Daten werden nach Abschluss/Abbruch bereinigt.
- Item-Änderungen invalidieren Budget/VRAM sofort; All/None speichert gebündelt.
- Projekt- und Versions-JSON werden atomar mit `.tmp`/`.bak` geschrieben und
  können bei beschädigter Hauptdatei aus dem Backup wiederhergestellt werden.
- Express speichert seinen Resume-Datensatz vor möglichen Domain Reloads und
  erzeugt das Thumbnail vor dem Leeren der Blueprint-ID.
- Nur tatsächlich vom Tool erzeugte `shiro_thumb_*.png`-Dateien werden als
  temporäre Thumbnails gelöscht.
- Texture-Optimierung räumt den Progressbar auch bei Fehlern auf und meldet
  fehlgeschlagene VRAM-Schätzungen.
- Die gesamte aufgeklappte Seite `New Outfit Defaults` besitzt einen eigenen,
  an die Fensterhöhe angepassten Scrollbereich; ihre verschachtelten Listen
  behalten die bestehende Scroll-Routenlogik.

Die sechs geänderten Plugin-Dateien wurden gezielt in das freigegebene
Unity-Testziel synchronisiert. Ein isolierter Compile der vollständigen
`Assembly-CSharp-Editor` mit Unity 2022.3.22f1 und der vorhandenen
Bee-Response-Datei endete mit Exitcode 0. Zwei CS8032-Warnungen betrafen nur die
außerhalb von Bee gestarteten Unity-Source-Generatoren; der Plugin-Code hatte
keine Compilerfehler. Unitys laufender Editor hatte Auto-Refresh während der
Prüfung nicht ausgeführt.

## Nächste Schritte

1. Bei der nächsten funktionalen Änderung den synchronisierten Pluginstand im
   Unity-Testziel kompilieren und einen passenden Editor-Workflow prüfen.
2. Die wichtigsten Zustandswechsel (Outfit/Blendshape, Avatarwechsel,
   per-Outfit-Budgets und Express-Resume) bei Gelegenheit manuell im Editor
   regressionsprüfen.
3. Prüfen, wie Git-Arbeitskopie und Unity-Testziel künftig teilautomatisiert,
   aber ohne pauschales Spiegeln synchronisiert werden sollen.
4. Vor dem nächsten Release Dokumentation und Versionsangaben abgleichen.

## Unveröffentlichte UI-Neustrukturierung

Am 23. August 2026 wurde als erster Schritt gegen die zunehmende Dichte des
Hauptfensters die bisherige lange Einzelseite in drei Arbeitsbereiche geteilt:

- `Outfits` zeigt die vollständige Outfitliste.
- `New Outfit` filtert auf Outfits ohne Blueprint-ID und erklärt den
  Express-/Advanced-Erstupload.
- `Defaults` zeigt die komplette Konfiguration für neue Outfits in einer
  eigenen, die verfügbare Fensterhöhe nutzenden Scrollansicht.

Der Batch-Upload bleibt in den beiden outfitbezogenen Ansichten als feste
Fußsektion unter der flexibel scrollenden Liste sichtbar. Uploadlogik und
persistiertes Projektdatenformat wurden dabei nicht verändert.

Nach Sichtprüfung der neuen Navigation per Unity-Video wurden außerdem die
Outfitkarten verdichtet. Im geschlossenen Zustand zeigen sie nur noch Name,
Aktivstatus, Batch-Auswahl, Zielplattformen, Primäraktionen und die zentrale
Performance-Zusammenfassung. Blueprint-ID, VRAM-/Thumbnail-Aktionen,
Plattformbearbeitung, Uploadhistorie, Blendshapes, Items und FaceEmo liegen in
einer aufklappbaren Detailansicht. Outfits ohne Blueprint-ID behalten ihre
Express-/Advanced-Erstupload-Aktionen auch im kompakten Zustand.

Eine weitere Video-Sichtprüfung bestätigte stabile Kopfzeilen und die deutlich
höhere Informationsdichte. Um die dadurch entstandene durchgehend rote
Warnfläche zu entschärfen, werden reine `Very Poor`-Performancebewertungen für
Contacts, Lights und VRAM nun gedämpft orange dargestellt. Rot bleibt harten
Limits beziehungsweise echten Blockern vorbehalten.

`Editor/OutfitBatchUploader.cs` und `Editor/OutfitNewSetup.cs` wurden gezielt
in den freigegebenen Pluginordner auf `D:` synchronisiert. Der isolierte
Compile der vollständigen `Assembly-CSharp-Editor` mit Unity 2022.3.22f1 und
der vorhandenen Bee-Response-Datei endete mit Exitcode 0. Die zwei bekannten
CS8032-Warnungen betreffen nur die außerhalb von Bee gestarteten Unity-
Source-Generatoren.

## Release v3.3.0 vorbereitet

Der UI- und Backend-Stabilitätsstand wurde als öffentlicher Source-/Docs-Commit
`3c28eb7` (`feat: optimize plugin UI and backend stability`) zusammengeführt.
README und Wiki beschreiben die drei Arbeitsbereiche, kompakten Outfitkarten,
die feste Batch-Fußsektion und die Backend-Sicherheitsverbesserungen. Eine neue
Wiki-Seite `User-Interface.md` und englische `RELEASE_NOTES_v3.3.md` wurden
ergänzt.

Das Release-Asset
`VRChat-outfit-batch-uploader-3.3.0.unitypackage` wurde im historischen
kompatiblen Doppelordner-Layout gebaut und vollständig entpackt geprüft:

- 33 Unity-Assets mit vorhandenen `.meta`-GUIDs,
- 127687 Bytes,
- SHA-256 `3CF8A1D07E4A9354965E671FAE3FA8D34F3DB4238733C5842E4B571B720A323E`,
- enthält Editor-Code, Sounds, README, Wiki, Lizenz und Release Notes,
- enthält kein Projektgedächtnis, keine Arbeitsregeln und keine
  avatarspezifischen `ProjectSettings` oder Upload-Logs.

Der vollständige Editor-Code wurde vor der Veröffentlichung mit der realen
Unity-2022.3.22f1-/VRChat-SDK-Assemblykonfiguration kompiliert (Exitcode 0).
Die Oberfläche wurde zusätzlich in drei aufeinanderfolgenden Unity-Videos
visuell geprüft und iterativ verdichtet.

## Veröffentlichung v3.3.0

v3.3.0 wurde am 23. August 2026 veröffentlicht:

- Source-/Docs-Commit `3c28eb7`,
- Projektgedächtnis-/Release-Commit `5aca50e`,
- annotierter Tag `3.3.0` auf `5aca50e`,
- GitHub-Release: `v3.3.0 — UI & Backend Optimization Update`,
- URL: `https://github.com/TheSilentD3ath/VRChat-outfit-batch-uploader/releases/tag/3.3.0`,
- Release ist öffentlich, kein Draft und kein Prerelease,
- Assetgröße und GitHub-Digest stimmen mit dem lokal geprüften Paket überein.
- Das separate GitHub-Wiki wurde mit Commit `ca3659c` aktualisiert; sieben
  bestehende Seiten wurden gezielt angepasst und `User-Interface.md` ergänzt.
