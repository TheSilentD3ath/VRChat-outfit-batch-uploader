# Handoff: Review-Fixes nach Projektdurchsicht

Datum: 13. November 2026
Branch: `claude/youthful-gates-udtpmv`

## Anlass

Der Nutzer bat um eine frische Einschätzung des Projekts. Gelesen wurden rund
zwei Drittel des Editor-Codes vollständig (Hauptfenster, Datenspeicher,
Onboarding, Varianten, Items, Upload-All-Gate, SdkCompat, Dry Run,
Express-Ablauf). Aus sechs Befunden hat der Nutzer vier zur Umsetzung
freigegeben; einer ist eine reine Sichtprüfung, einer wartet auf seine
Entscheidung.

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

## Geprüft

Nur statisch: Klammerbilanz der drei geänderten C#-Dateien gegen `HEAD`,
Suche nach verbliebenen `typeof`-Referenzen auf die SDK-Klasse, Gegenlesen des
Diffs. **Kein Unity-Compile** — in der Sitzung gibt es keinen C#-Compiler.

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

- **Entscheidung des Nutzers:** Select auf einem Eintrag ohne Blueprint-ID
  lässt die vorherige ID auf dem PipelineManager (`ActivateOutfit`). Ein
  manueller Upload im SDK-Fenster überschreibt dann den zuvor aktiven Avatar.
  Vorschlag war, die ID in dem Fall zu leeren. Dem Nutzer wurde das noch
  einmal erklärt; die Antwort steht aus.
- **Sichtprüfung:** Höhe der Onboarding-Karte bei normaler Fensterhöhe.
- Compile, Batch-Nachtest und VRChat-Test wie in `PROJECT_STATE.md`.
