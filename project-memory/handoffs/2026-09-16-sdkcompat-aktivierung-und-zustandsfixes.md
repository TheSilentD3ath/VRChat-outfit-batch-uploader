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
- Nicht angefasst und weiterhin offen: Doppelmodell `OutfitEntry`/`OutfitData`,
  namensbasierte Identität, `FlushScene()` speichert ungefragt, fehlende
  Tests/CI, fehlendes `.asmdef`, unterschiedlicher Umfang von VRAM-Anzeige und
  VRAM-Optimierung.

---

## Nachtrag desselben Tages: Eingabe-, Performance- und Lebenszyklusfixes

### Textfelder schreiben nicht mehr pro Tastendruck

Vier Felder committeten bei jedem Zeichen. `EditorGUILayout.DelayedTextField`
löst jetzt erst bei Enter oder Fokusverlust aus:

- Blueprint-ID (`OutfitBatchUploader.cs`): `OutfitProjectData.Save()`
  serialisiert und ersetzt den **gesamten** Store atomar — das lief bisher
  einmal pro getipptem Zeichen. Nebeneffekt: die Formatwarnung erscheint nicht
  mehr während des Tippens beziehungsweise bei halb eingefügten IDs.
- Base Version (`OutfitBatchUploader.cs`): `AvatarVersionManager.SetVersion`
  schreibt `ShiroOutfit_versions.json` über Temp-Datei, `File.Replace` und
  `.bak` neu.
- Outfits-Parent (`OutfitBatchUploader.cs`) und Items-Parent
  (`OutfitItems.cs`): zusätzlich zum EditorPrefs-Write lief ein vollständiger
  Listen-Rebuild pro Zeichen, und Zwischenstände wie `Outfi` finden nichts —
  die Outfit- beziehungsweise Item-Liste leerte sich sichtbar beim Tippen. Das
  Items-Feld war im ursprünglichen Review übersehen worden und fiel erst bei der
  abschließenden Prüfung auf.

Die reinen Filterfelder (Blendshape-Suche, Item-Suche, Item-Defaults-Suche)
bleiben bewusst live; sie lösen keinen IO aus. Die Entwurfsfelder in
`DrawAdvancedPanel` und `OutfitConfigWindow` schreiben nur in speicherinterne
`AdvancedDraft`-Objekte und bleiben ebenfalls live.

### Langsamer Shader-Pfad entfernt

`CollectOutfitTextures` in `OutfitTextureOptimizer.cs` iterierte weiterhin über
`ShaderUtil.GetPropertyCount`/`GetPropertyType`/`GetPropertyName`. Der
Dateikopf erklärt selbst, warum das auf Poiyomi und lilToon zu langsam ist, und
`ComputeVramFor` nutzt deshalb längst `Material.GetTexturePropertyNames()`.
Genau der Optimizer-Pfad, der beim VRAM-Button und in Express läuft, zahlte die
Kosten noch. Gleiche Texturmenge, jetzt derselbe Weg wie die Anzeige.

### Texture-Leak im Zeilenhintergrund

`MakeTex` erzeugte eine `Texture2D`, die ausschließlich vom `GUIStyle`
referenziert wurde. `_stylesInited` ist nicht serialisiert, also entstand nach
jedem Domain Reload und jedem An-/Abdocken eine neue Textur, während die alte
verwaiste — die Quelle von Unitys „Cleaning up leaked objects". Neu:

- `MakeTex` setzt `HideFlags.HideAndDontSave`,
- das Feld `_activeRowTex` hält die Referenz,
- `DisposeStyles()` zerstört sie und setzt `_stylesInited` zurück, sodass Stile
  und Textur immer gemeinsam neu entstehen und kein Stil auf eine zerstörte
  Textur zeigen kann,
- Aufruf aus `OnDisable`.

### Korrektur einer Review-Aussage zu `_expressQuietMode`

Der ursprüngliche Review behauptete, ein früher Return in `ExpressSetupAsync`
lasse das Flag auf `true` stehen. **Das ist falsch.** Jeder Standalone-Aufruf
übergibt `skipConfirm: false` und setzt das Flag damit selbst zurück, und der
Gate-Loop in `StartBatchWithSetupAsync` verwendet ausschließlich
`continue`, erreicht sein abschließendes `_expressQuietMode = false` also immer.

Ein echter Defekt liegt an derselben Stelle in der Gegenrichtung: das Flag ist
ein einfaches Feld und überlebt keinen Domain Reload. Löst ein Express-Vorgang
innerhalb des Upload-All-Gates einen Reload aus, setzt der fortgesetzte Upload
in `ContinueExpressUploadAsync` den Bestätigungston pro Outfit ab — genau das,
was der Quiet-Modus verhindern soll. Behoben über den neuen SessionState-Key
`Shiro_Express_Quiet`, geschrieben zusammen mit dem übrigen Resume-Record,
gelesen in `ContinueExpressUploadAsync` und in `ClearExpressState` entfernt.

### Dabei aufgefallen, nicht behoben

Der Gate-Loop in `OutfitBatchSetupGate.StartBatchWithSetupAsync` ist als Ganzes
nicht domain-reload-fest. Anders als die Batch-Queue und der Express-Record
liegt sein Fortschritt nur im laufenden Task. Löst ein Express-Vorgang mitten
im Loop einen Reload aus, wird der einzelne Upload zwar korrekt fortgesetzt,
die verbleibenden unkonfigurierten Outfits werden aber nie eingerichtet und die
bereits konfigurierten nie gebatcht. Das ist ein eigener, größerer Schritt.

### Prüfstand

Wie oben: statisch geprüft (Delimiterbilanz aller elf Dateien identisch zu
`HEAD`, neue Symbole genau einmal deklariert, kein `ShaderUtil`-Rest,
`MakeTex`-Ergebnis wird nirgends mehr verworfen). **Weiterhin nicht in Unity
kompiliert oder bedient.**

---

## Nachtrag 2: Dry-Run-Check und Dateiaufteilung

### Dry Run erkennt doppelte Avatarnamen

Der Dry Run prüfte bisher doppelte Outfitnamen, aber nicht doppelte
Avatarnamen. Der Projektstore ist über den Avatarnamen verschlüsselt, zwei
gleichnamige Avatare in der Szene teilen sich also **einen** Datensatz samt
Blueprint-IDs, Blendshapes, Items und FaceEmo. Die Quick-Pick-Leiste im
Kopfbereich macht genau diesen Fall leicht erreichbar.

Neu in `OutfitDryRun.cs` direkt nach der Outfitnamen-Prüfung. Die Meldung ist
ein Fehler, sobald der aktuell gewählte Avatar zu den Doppelungen gehört, sonst
eine Warnung — ohne Auswahl ist es noch keine akute Gefahr.

Das ist bewusst nur ein Wächter, keine Lösung. Die eigentliche Ursache bleibt
die namensbasierte Identität, die weiterhin offen ist.

### `AvatarVersionManager` in eigene Datei

Die Klasse lag am Ende von `OutfitBatchUploader.cs` und war damit weder Teil
der `partial class` noch bei den übrigen Stores. Sie ist jetzt
`Editor/AvatarVersionManager.cs`. Der Klassenrumpf wurde zeichengenau
übernommen und gegen die HEAD-Fassung verglichen; nur Dateikopf und `using`
sind neu, `using UnityEditor` entfiel als unbenutzt. Eine passende `.meta` mit
neuer GUID wurde im Format der bestehenden MonoImporter-Dateien angelegt;
bestehende GUIDs bleiben unberührt.

`OutfitBatchUploader.cs` schrumpft dadurch von 2182 auf 2062 Zeilen. Das ist
ein erster Schritt gegen die Dateigröße, nicht die Lösung: Scroll- und
Motion-Blur-Engine, `OutfitEntry` und die übrige UI liegen weiter dort.

### Architekturkarte nachgeführt

`project-memory/ARCHITECTURE_MAP.md` kannte `SdkCompat.cs` nicht — die Datei kam
mit `54a0bb6`, ohne dass die Karte ergänzt wurde. Eingetragen sind jetzt
`SdkCompat.cs` und `AvatarVersionManager.cs`; die Beschreibung von
`OutfitApiTools.cs` nennt nicht mehr Reflection als ihre Aufgabe, da die dort
verbliebenen Aufrufe jetzt über `SdkCompat` laufen.

Unter „Externe Grenzen" steht neu, dass neue Reflection nach `SdkCompat.cs`
gehört, mit den zwei bewusst dort belassenen Ausnahmen: die reine Typerkennung
in `OutfitContacts.cs` (Namensvergleich ohne Methodenaufruf) und die
UI-Automatisierung der SDK-Panels in `OutfitNewSetup.cs` (Auto-Fixes und
Copyright-Modal, die an SDK-UI-Elementen statt an API-Methoden hängen).

### Prüfstand

Statisch: verschobener Klassenrumpf zeichengleich zu `HEAD`, Delimiterbilanz
der Split-Dateien in Summe unverändert, neue `.meta` strukturgleich zu den
bestehenden und GUID projektweit eindeutig. **Weiterhin nicht in Unity
kompiliert.** Der Nutzer führt Kompilierung und Bedienung zu Hause durch und
stellt das Ergebnis anschließend bereit.
