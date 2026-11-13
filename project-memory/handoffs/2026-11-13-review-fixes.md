# Handoff: Review-Fixes nach Projektdurchsicht

Datum: 13. November 2026
Branch: `claude/youthful-gates-udtpmv`

## Anlass

Der Nutzer bat um eine frische Einschätzung des Projekts. Gelesen wurden rund
zwei Drittel des Editor-Codes vollständig (Hauptfenster, Datenspeicher,
Onboarding, Varianten, Items, Upload-All-Gate, SdkCompat, Dry Run,
Express-Ablauf). Aus sechs Befunden hat der Nutzer vier zur Umsetzung
freigegeben; einer ist eine reine Sichtprüfung. Den sechsten (Punkt 5 unten)
hat er nach einer zweiten Erklärung als Bug bestätigt und ebenfalls
freigegeben, zusammen mit dem Build-Skript fürs Repository.

## Umgesetzt

### 1. SDK-Interna-Typ per Name statt `typeof` (`Editor/SdkCompat.cs`)

`AgreeCopyrightAsync` hing über `typeof(VRCCopyrightAgreement)` zur
Compile-Zeit an einer undokumentierten SDK-Klasse. Genau diese Referenz war der
CS0246 des ersten Unity-Tests. Verschiebt oder entfernt VRChat die Klasse,
kompiliert das ganze Plugin nicht mehr — obwohl nur eine Komfortfunktion
(einmalige statt wiederholte Eigentumsbestätigung) betroffen wäre.

Neu: `FindType(simpleName, feature)` sucht den Typ einmalig über die geladenen
Assemblies (SDK-Assemblies zuerst, Treffer und Fehlschlag gecacht, eine
Warnung). `Agree` wird jetzt mit `Public | NonPublic` gesucht und übersteht
damit auch eine geänderte Sichtbarkeit. Die `using`-Zeilen sind unverändert
geblieben; sie haben in Unity kompiliert und sind unbenutzt harmlos.

### 2. Aufklapp-Zustand überlebt den Listen-Neuaufbau (`Editor/OutfitBatchUploader.cs`)

`RebuildOutfitList` erzeugt alle `OutfitEntry`-Objekte neu. Dadurch klappte
„Add Variant" das Basis-Outfit zu, in dem man gerade arbeitete, und solange
die Onboarding-Karte sichtbar ist, baut jede Hierarchie-Änderung die Liste neu.

Neu: Vor dem Leeren merkt sich die Methode die bisherigen Zeilen nach Name
(nur wenn der Avatar derselbe ist), `KeepRowUiState` überträgt
`DetailsExpanded`, `BlendShapeExpanded`, `BlendShapeSearch` und
`BlendShapeScroll` auf die neuen Einträge. ↺ (`ScanScene`) leert die Liste
vorher selbst und klappt deshalb weiterhin alles zu — bewusst nicht angefasst.

### 3. „rig" nur noch als ganzes Wort (`Editor/OutfitBatchUploader.cs`)

`HandleBatchError` stufte jede Fehlermeldung mit der Zeichenfolge „rig" als
Validierungsfehler ein. Das traf auch „copyright", „right" und „original"; ein
solcher Fehler wurde für jedes weitere Outfit still übersprungen, statt den
Dialog „Continue / Stop" zu zeigen. Neu: `RIG_WORD_REGEX`
(`\brig(s|ged|ging)?\b`, ohne Groß-/Kleinschreibung). Die übrigen Stichwörter
sind unverändert.

### 4. Doku

- `README.md`: Varianten und Erststart-Hilfe ergänzt (Funktionsliste, eigener
  Abschnitt unter Items, Hinweis im Setup). Die Kopfzeile nennt weiter v3.3 —
  die Versionsnummer wird erst beim Release angehoben.
- `Editor/OutfitBatchUploader.cs`: Dateikopf behauptete noch, die
  Einstellungen lägen in EditorPrefs.
- `Editor/OutfitDryRun.cs`: Der Kommentar zu doppelten Avatarnamen stand über
  dem Varianten-Check; beide Kommentare stehen wieder an ihrem Code.
- `CHANGELOG.md`: zwei Einträge unter `[Unreleased]` → Fixed.
- `project-memory/ARCHITECTURE_MAP.md`: Regel zu `typeof` und `FindType`.
- `PROJECT_STATE.md`: von 260 Zeilen Chronik auf den aktuellen Stand gekürzt,
  mit Verweis auf die Handoffs. Die alte Fassung: `git show e9958f9:PROJECT_STATE.md`.

### 5. Select ohne Blueprint-ID leert die ID am Avatar (`Editor/OutfitBatchUploader.cs`)

Vom Nutzer als Bug bestätigt und freigegeben. `ActivateOutfit` setzte die ID
am `PipelineManager` nur, wenn der Zieleintrag eine hatte. Bei einem Eintrag
ohne ID blieb die des zuvor aktiven Outfits stehen; ein Upload aus dem
VRChat-SDK-Fenster überschrieb dann diesen anderen Avatar. Mit Varianten ist
der Weg dorthin kurz: Variante anlegen, „Select" zum Ansehen, gewohnter
SDK-Button.

Neu: Die ID am Avatar folgt immer dem aktiven Eintrag — seine ID, sonst leer.
Die Änderung liegt in derselben Undo-Gruppe wie der Outfitwechsel.

Drei Folgen, die mitbehandelt sind:

- **Base Version.** Die Zeile und `LoadAvatarVersion` hängen an der ID am
  Avatar. Ohne Ausgleich wäre die Zeile verschwunden, der nächste Rescan (↺,
  Szenenwechsel, Domain Reload) hätte die Version geleert und der folgende
  Batch keine mehr gestempelt. `GetMainBlueprintId()` weicht deshalb bei
  leerer ID auf das erste Outfit mit gültiger ID aus. Das verbessert nebenbei
  den schon vorher möglichen Fall „Rescan während Express".
- **Unbekannte ID.** Steht am Avatar eine ID, die kein Outfit im Tool
  verwendet (zum Beispiel von einem Upload vor der Tool-Nutzung), wird auch sie
  entfernt. Abgewogen gegen „stehen lassen": Stehen lassen hieße, dass ein
  SDK-Upload genau diesen Avatar mit dem falschen Outfit überschreibt — die
  Fehlerklasse, um die es geht. Express hat die ID an dieser Stelle schon
  immer kommentarlos geleert. Damit sie nicht spurlos verschwindet, schreibt
  `ReportRemovedBlueprintId` sie in die Console und in die Statuszeile.
- **Onboarding-Text Schritt 4** (`Editor/OutfitOnboarding.cs`) nennt jetzt
  auch den ▾-Auswahlknopf neben dem Blueprint-ID-Feld, über den man die ID
  eines vorhandenen Avatars ohne Abtippen zuordnet.

Unverändert: Express ruft weiterhin `ClearBlueprintId()` auf (setzt zusätzlich
`completedSDK3Setup` zurück); Batch-Ziele haben immer eine ID.

### 6. Build-Skript im Repository (`tools/build-unitypackage.py`)

Das Skript lag bisher nur im Sitzungs-Scratchpad und war nach einem
Container-Neustart weg. Es ist neu geschrieben und am veröffentlichten
v3.3.0-Asset abgeglichen: Einträge `./<GUID>/…` mit Verzeichniseinträgen,
Modus 0777, uid/gid 0, Text mit CRLF, `pathname` ohne Zeilenumbruch. Für die
29 Pfade, die es in beiden Paketen gibt, stimmen alle GUIDs überein. `tools/`
ist nicht Teil des Pakets.

## Geprüft

Nur statisch: Klammerbilanz der geänderten C#-Dateien gegen `HEAD`, Suche nach
verbliebenen `typeof`-Referenzen auf die SDK-Klasse und nach allen Lesern und
Schreibern der ID am Avatar (es sind vier), Gegenlesen des Diffs. Das Paket
ist gegen `HEAD` inhaltsgleich geprüft. **Kein Unity-Compile** — in der Sitzung
gibt es keinen C#-Compiler.

## Risiken

- `FindType` läuft bei einem Fehlschlag einmal über alle geladenen Assemblies
  (`GetTypes`). Das kostet einmalig spürbar Zeit, wird aber gecacht und tritt
  nur ein, wenn die Klasse im SDK wirklich fehlt.
- `KeepRowUiState` ordnet nach Name zu. Zwei gleichnamige Outfits teilen sich
  den Zustand; der Dry Run meldet gleichnamige Outfits ohnehin als Fehler.
- Die „rig"-Änderung kann eine echte Validierungsmeldung, die das Wort nur als
  Wortteil enthält, jetzt als normalen Fehler behandeln. Folge wäre der
  Rückfrage-Dialog statt des stillen Überspringens, also die vorsichtigere
  Seite.

## Offen

- **Sichtprüfung:** Höhe der Onboarding-Karte bei normaler Fensterhöhe.
- **Test zu Punkt 5:** Select auf Variante ohne ID → Blueprint-ID im Pipeline
  Manager leer; Select zurück → ID wieder da; „Base Version" bleibt sichtbar.
- **Idee, nicht umgesetzt:** Trägt der Avatar eine ID, die kein Outfit
  verwendet, könnte Onboarding-Schritt 4 anbieten, sie dem ersten Outfit
  zuzuordnen, statt sie nur zu protokollieren.
- Compile, Batch-Nachtest und VRChat-Test wie in `PROJECT_STATE.md`.
