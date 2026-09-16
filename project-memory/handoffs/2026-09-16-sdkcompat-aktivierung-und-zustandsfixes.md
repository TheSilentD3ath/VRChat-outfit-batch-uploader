# Handoff: SdkCompat-Aktivierung und avatarbezogene Zustandsfixes

Datum: 16. September 2026

## Ausgangslage

Ein Review des Standes nach Commit `54a0bb6` ergab, dass die dort eingeführte
Reflection-Schicht `Editor/SdkCompat.cs` zwar existiert, aber keinen einzigen
Aufrufer hatte. Die vier Reflection-Stellen liefen unverändert inline weiter.
Das Ergebnis war das Gegenteil des Commit-Ziels: statt eines zentralen
Anlaufpunkts existierten zwei Implementierungen derselben SDK-Zugriffe, und die
ungenutzte trug den `MethodInfo`-Cache sowie die explizite Fehlermeldung bei
fehlendem `Result`.

Zusätzlich hatte derselbe Commit die vier `RELEASE_NOTES_v3.*.md` und ihre
`.meta`-Dateien nicht gelöscht, sondern auf 0 Byte geleert. Leere `.meta`-Dateien
verletzen die Regel, Unity-GUIDs zu erhalten: Unity regeneriert sie mit neuer
GUID.

## Umgesetzte Änderungen

### Reflection-Schicht aktiviert

- `SdkCompat.FindMethod` nimmt jetzt optional eine Signatur entgegen und fängt
  `AmbiguousMatchException` ab. Ohne das wäre die Umstellung eine Regression
  gewesen: `PlayConfirmSound` band `PlayPreviewClip` bisher über die exakte
  Signatur `(AudioClip, int, bool)`, `SdkCompat` suchte nur über den Namen und
  wäre bei Unity-Versionen mit Overloads geworfen. Der Cache-Key enthält die
  Signatur, damit signierte und unsignierte Lookups sich nicht überschreiben.
- `OutfitBatchUploader.PlayConfirmSound` nutzt `SdkCompat.PlayPreviewClip`.
- `OutfitBatchUploader.PreConsentAllAsync` nutzt `SdkCompat.AgreeCopyrightAsync`.
  Die Warnung bei `false` bleibt am Aufrufer; der Hinweis auf eine fehlende
  Methode kommt jetzt einmalig aus `SdkCompat`.
- `OutfitApiTools.FetchAvatarListAsync` nutzt `GetAvatarsMethod`,
  `BuildGetAvatarsArgs` und `InvokeTaskWithResultAsync`. Damit ist der bisher
  stille Ausfall behoben: ein SDK, das ein nicht-generisches `Task`
  zurückgibt, lieferte vorher `null` und damit eine leere Avatarliste ohne
  Fehlermeldung. Jetzt entsteht eine klare Exception, die
  `EnsureAvatarsFetchedAsync` als Statusmeldung anzeigt.
- `OutfitApiTools.UploadThumbnailAsync` nutzt `SdkCompat.UpdateAvatarImageAsync`.
- `using System.Reflection` in `OutfitBatchUploader.cs` entfällt dadurch.

### Avatarbezogener UI- und Cachezustand

Sämtliche Per-Outfit-Caches sind über den Outfit-**Namen** verschlüsselt, und
Outfitnamen wiederholen sich zwischen Avataren. Ein Avatarwechsel hat bisher
nichts davon verworfen. Neu ist `ResetPerAvatarUiStateIfAvatarChanged()` in
`OutfitBatchUploader.cs`, aufgerufen am Anfang von `RebuildOutfitList()` und
abgesichert über das neue Feld `_uiStateAvatar`, sodass das erneute Tippen des
Outfits-Parent-Namens keine offenen Entwürfe verwirft. Die Module räumen jeweils
selbst auf:

- `OutfitNewSetup.ResetNewSetupUiState()` leert `_nsDrafts` und
  `_nsAdvancedOutfit`. Das war der gravierendste Fall: ein Entwurf trägt
  Avatarname, Beschreibung und Content-Warning-Tags des Uploads, und
  `EnsureDraft` steigt bei vorhandenem Schlüssel sofort aus. Ein auf Avatar A
  geöffnetes Advanced-Panel konnte damit bestimmen, unter welchen Angaben ein
  neuer Avatar B veröffentlicht wird.
- `OutfitItems.ResetItemUiState()` leert Ausklappzustand, Suche und Scrollposition.
- `OutfitFaceEmo.ResetFaceEmoUiState()` leert den Ausklappzustand.
- `ClearVramCache()` und `MarkBudgetsDirty()` werden mitgerufen; die
  Quick-Pick-Buttons taten das bisher nicht, sodass VRAM- und Budgetzahlen bis
  zum 10-Sekunden-Fallback vom vorherigen Avatar stammten.

### Abbruchsteuerung des Batches

`OnDisable` hat `_cts` disposed und genullt. Der Batch-Task überlebt das
Fenster aber, sodass der nächste Zugriff im Loop (`Task.Delay`, `VRCApi.GetAvatar`,
`BuildAndUpload`) auf `null` beziehungsweise eine disposete Quelle lief.

- `ProcessBatchQueueAsync` zieht die Quelle einmalig in eine lokale Variable und
  verwendet durchgängig dieses `CancellationToken`. Ein zwischenzeitlich
  ersetztes oder entferntes Feld kann den laufenden Durchlauf nicht mehr treffen.
- `OnDisable` fasst `_cts` nicht mehr an. Es feuert auch beim An- und Abdocken
  des Fensters und beim Domain Reload; ein Abbruch dort hätte einen laufenden
  beziehungsweise gerade fortsetzbaren Batch zerstört.
- Neu ist `OnDestroy`, das ausschließlich beim echten Schließen feuert und dort
  `_cts.Cancel()` aufruft. Der Loop läuft dadurch über
  `OperationCanceledException` in `CancelBatch()` und stellt Blendshapes und
  Ausgangsplattform wieder her, statt beides halb angewendet zu hinterlassen.
  Die Quelle bleibt am Leben, damit hängende Awaits sauber abbrechen; der
  nächste Batchstart disposed und ersetzt sie.

### Repository

Die acht 0-Byte-Dateien `RELEASE_NOTES_v3.*.md` und `.meta` wurden per `git rm`
tatsächlich entfernt. `CHANGELOG.md` bleibt die konsolidierte Quelle.

## Verhaltensänderungen

- Fenster schließen während eines Batches bricht den Batch jetzt definiert ab
  (vorher: `NullReferenceException` und ein „Upload Failed"-Dialog auf einem
  bereits geschlossenen Fenster). An- und Abdocken bricht ihn nicht mehr ab.
- Avatarwechsel verwirft offene Express-/Advanced-Entwürfe. Das ist
  beabsichtigt: der Entwurf gehört fachlich zum vorherigen Avatar.
- Ein SDK ohne brauchbares `GetAvatars`-Ergebnis meldet jetzt einen Fehler,
  statt eine leere Avatarliste vorzutäuschen.

## Prüfstand und offene Risiken

- Statisch geprüft: Delimiterbilanz aller elf Editor-Dateien identisch zu
  `HEAD`; alle neu aufgerufenen Symbole existieren genau einmal; keine Reste der
  ersetzten Inline-Reflection; `OnDestroy` kollidiert nicht mit dem
  gleichnamigen Member der verschachtelten `ThumbPreviewWindow`.
- **Offen: keine Unity-Kompilierung.** In der Arbeitsumgebung stand weder Unity
  noch ein C#-Compiler noch die VRChat-SDK-Assembly zur Verfügung. Ein
  Kompilier- und Funktionstest im freigegebenen Pluginordner auf `D:` steht aus,
  insbesondere für die vier umgestellten Reflection-Pfade (Sound, Pre-Consent,
  Avatarliste, Thumbnail-Update) und für das Schließen des Fensters während
  eines laufenden Batches.
- Bekannt und bewusst nicht behoben: Wird das Fenster mitten in einem sehr
  langen `BuildAndUpload` geschlossen und vor dessen Rückkehr wieder geöffnet,
  kann der neue Batchstart die alte Quelle disposen, während der alte Task noch
  läuft. Die daraus folgende `ObjectDisposedException` wird von
  `HandleBatchError` gefangen. Eine saubere Lösung bräuchte eine Lauf-ID; das
  gehört in einen eigenen Schritt.
- Nicht angefasst und weiterhin offen: Dateigröße von `OutfitBatchUploader.cs`,
  Doppelmodell `OutfitEntry`/`OutfitData`, namensbasierte Identität,
  `FlushScene()` speichert ungefragt, fehlende Tests/CI, fehlendes `.asmdef`,
  JSON-Schreibvorgang pro Tastendruck in den Textfeldern für Blueprint-ID,
  Version und Outfits-Parent, unterschiedlicher Umfang von VRAM-Anzeige und
  VRAM-Optimierung, langsamer `ShaderUtil`-Pfad in `CollectOutfitTextures`,
  `_expressQuietMode` bei frühen Returns, Texture-Leak in `MakeTex`, fehlender
  Dry-Run-Check auf doppelte Avatarnamen.
