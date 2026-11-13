# Handoff: Avatar-Liste repariert, Zeitmessung im Batch

Datum: 13. November 2026
Branch: `claude/youthful-gates-udtpmv`
Auftrag des Nutzers: „ja repariere beides und bau die messung ein".

## 1. Avatar-Liste („☁ IDs" und ▾)

**Befund.** Beide Funktionen suchten per Reflection `VRCApi.GetAvatars`. Die
Methode gibt es in keiner SDK-Version von 3.3.0 bis 3.10.6 (im SDK-Quelltext
nachgesehen, siehe `project-memory/SDK_NOTES.md`). Die Funktionen haben also
nie eine Liste geliefert.

**Reparatur** (`Editor/OutfitApiTools.cs`, `FetchAvatarListAsync`):

- Abfrage über `VRCApi.Get<List<VRCAvatar>>("avatars", query, forceRefresh: true)`
  mit `user=me`, `releaseStatus=all`, `n=50`, `offset`. `VRCApi.Get` ist mit
  `[PublicAPI]` markiert und in 3.8.0 und 3.10.6 gleich signiert; direkter
  Aufruf, keine Reflection.
- Es werden nur Avatare übernommen, deren `AuthorId` der ID des angemeldeten
  Kontos entspricht (`APIUser.CurrentUser?.id`, derselbe Vergleich wie im SDK).
  Eine fremde ID darf nie an einem Outfit landen; lieber eine leere Liste.
  Kommen Avatare zurück, aber kein eigener, gibt es eine eigene Fehlermeldung.
- Blättern endet bei einer leeren Seite oder wenn eine Seite nichts Neues
  bringt (Schutz, falls der Server `offset` ignoriert), spätestens bei 2000.

`Editor/SdkCompat.cs`: `GetAvatarsMethod`, `BuildGetAvatarsArgs` und
`InvokeTaskWithResultAsync` sind entfernt, ebenso `ExtractAvatars` in
`OutfitApiTools.cs`. Niemand sonst hat sie benutzt.

Damit stimmt auch der Onboarding-Text aus dem vorigen Handoff wieder, der auf
▾ verweist.

## 2. Zeitmessung (`Editor/OutfitTiming.cs`, neu)

Ziel: wissen, wie sich die rund 2:36 pro Outfit aufteilen, bevor an der
Upload-Zeit gearbeitet wird.

- Pro Batch-Upload eine Zeile im Upload-Log:
  `TIME  <Outfit> (<Plattform>): total … | activate … | save … | fetch … | build … | upload … | sdk other … | tool wait … | file … MB`
- Pro Plattformwechsel eine eigene `TIME`-Zeile (vom Wechsel bis das SDK
  wieder bereit ist; Startzeit als Wanduhr im SessionState, weil der Domain
  Reload jede Uhr im Speicher zurücksetzt).
- Am Batch-Ende (auch nach Abbruch) eine Zusammenfassung mit Durchschnitt und
  Anteil je Phase im Log und in der Console; eine Kurzzeile hängt an der
  Abschlussmeldung in der Statusbox.
- Neuer Knopf **Log** neben „Dry run" öffnet das Upload-Log
  (`OpenUploadLog` in `OutfitDryRun.cs`).

Woher die Zahlen kommen:

- `activate`, `save`, `fetch`, Gesamtzeit: Stoppuhr-Marken in
  `ProcessBatchQueueAsync`.
- `build`, `upload`, Dateigröße: öffentliche Ereignisse des Builders
  (`OnSdkBuildStart`, `OnSdkBuildSuccess` mit Dateipfad, `OnSdkBuildFinish`,
  `OnSdkUploadStart`, `OnSdkUploadFinish`). Die Handler haben die Signatur
  `(object, object)`; die passt per Kontravarianz auf `EventHandler`,
  `EventHandler<string>` und `EventHandler<object>`.
- `sdk other` = Zeit im `BuildAndUpload`-Aufruf abzüglich build und upload.
- `tool wait` = Rest, also die festen Pausen des Tools (1,5 s nach dem
  Aktivieren, 2 s vor dem nächsten Upload; letztere wird als Konstante
  übergeben, weil sie erst nach der Messung läuft).

Die Summen liegen als JSON im SessionState (`Shiro_BatchTiming`) und überleben
so den Plattformwechsel. Jede Messfunktion fängt ihre Ausnahmen selbst: Messen
darf einen Upload nicht abbrechen.

## Geändert

`Editor/OutfitApiTools.cs`, `Editor/SdkCompat.cs`, `Editor/OutfitTiming.cs`
(+ `.meta`, neue GUID), `Editor/OutfitBatchUploader.cs`,
`Editor/OutfitDryRun.cs`, `README.md`, `Wiki/Uploading.md`, `CHANGELOG.md`,
`project-memory/ARCHITECTURE_MAP.md`, `project-memory/SDK_NOTES.md`,
`PROJECT_STATE.md`.

## Geprüft

Nur statisch: Klammerbilanz aller geänderten C#-Dateien, keine Namenskollision
der neuen Bezeichner, keine verbliebenen Verweise auf die entfernten Helfer.
Alle benutzten SDK-Mitglieder gegen den Quelltext von 3.10.6 (Version des
Nutzers) und 3.8.0 gelesen: `VRCApi.Get<T>`, die fünf Builder-Ereignisse,
`VRCAvatar.AuthorId`/`ID`, `APIUser.CurrentUser?.id`.
**Kein Unity-Compile, kein Lauf.**

## Risiken

- **Die Avatar-Abfrage ist nie gelaufen.** Endpunkt und Parameter sind aus dem
  SDK abgeleitet, nicht ausprobiert. Antwortet der Server anders, kommt eine
  Fehlermeldung oder eine leere Liste — keine falsche Zuordnung, dafür sorgt
  der Eigentumsfilter.
- **Methodengruppen mit `(object, object)` auf die SDK-Ereignisse.** Nach den
  C#-Regeln zulässig; sollte der Compiler widersprechen, sind es fünf Zeilen
  in `AttachSdkTiming`/`DetachSdkTiming`.
- **Neuer Knopf in der Batch-Kopfzeile.** Bei sehr schmalem Fenster wird die
  Zeile enger.
- Feuert das SDK ein Ereignis nicht, steht bei der Phase „?" und die Zeit
  zählt unter `sdk other`; die Summen bleiben stimmig.

## Offen

- Compile und Lauf beim Nutzer; Testschritte in `PROJECT_STATE.md`.
- Nach dem ersten gemessenen Batch entscheiden, welcher Hebel sich lohnt:
  die anderen Outfits vor dem Build aus dem Avatar nehmen, Upload und Build
  überlappen, das Szenenspeichern vor jedem Build.
