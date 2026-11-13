# VRChat Outfit Batch Uploader – Projektstatus

Letzte Aktualisierung: 13. November 2026

Diese Datei beschreibt nur den **aktuellen Stand**. Wie es dazu kam, steht in
den Handoffs unter `project-memory/handoffs/` (Liste unten). Die frühere
ausführliche Chronik dieser Datei ist über `git show e9958f9:PROJECT_STATE.md`
abrufbar.

## Umgebung

- Unity 2022.3.22f1 mit VRChat SDK Base und Avatars **3.10.6** (Stand des
  Nutzerprojekts am 13.11.2026, laut Creator Companion). Was das Tool vom SDK
  benutzt, steht in `project-memory/SDK_NOTES.md`.
- Freigegebenes Unity-Testziel: der Pluginordner im Projekt `Main avi` auf `D:`
  (genauer Pfad und Regeln in `AGENTS.md`). Der innere Ordnername `…-2.0.0`
  ist ein falscher Altname und sagt nichts über die Version.
- In den Cloud-Sitzungen gibt es keinen C#-Compiler und kein Unity. Alles, was
  dort entsteht, ist nur statisch geprüft, bis der Nutzer es in Unity importiert.

## Veröffentlicht

| Version | Tag auf | Asset | Größe | SHA-256 |
| --- | --- | --- | --- | --- |
| 3.2.0 | `79ae6b0` | `VRChat-outfit-batch-uploader-3.2.0.unitypackage` | 113351 | `2AA0E1969CD8546341CC9CE597212C42F06F552FAB58B9854C7C25D51E59281E` |
| 3.3.0 | `5aca50e` | `VRChat-outfit-batch-uploader-3.3.0.unitypackage` | 127687 | `3CF8A1D07E4A9354965E671FAE3FA8D34F3DB4238733C5842E4B571B720A323E` |

Ältere Tags: `3.0.0`, `3.1.0`; `main` enthielt danach den v3.1.1-Fix `75943ee`.
v3.3.0 (Source-Commit `3c28eb7`) ist der letzte Stand, der in Unity kompiliert
**und** im Editor sichtgeprüft wurde. Das GitHub-Wiki wurde dafür mit `ca3659c`
aktualisiert.

## Unveröffentlicht (Branch `claude/youthful-gates-udtpmv`)

Seit v3.3.0, jeweils mit Handoff:

- **SdkCompat, Zustands- und Performance-Fixes, VRAM-Umfang** (`54a0bb6` bis
  `2e74c29`, Compile-Fix `f7c5989`) →
  `2026-09-16-sdkcompat-aktivierung-und-zustandsfixes.md`
- **Erststart-Onboarding** (`43079ce`) → `2026-09-27-onboarding.md`
- **Outfit-Varianten** (`e9958f9`) → `2026-09-27-outfit-varianten.md`
- **Review-Fixes** (13.11., ab `522501e`): SDK-Interna-Typ per Name statt `typeof`,
  Aufklapp-Zustand überlebt den Listen-Neuaufbau, „rig"-Fehlererkennung nur
  noch als ganzes Wort, Select ohne Blueprint-ID leert die ID am Avatar,
  Build-Skript im Repo, Doku nachgezogen → `2026-11-13-review-fixes.md`

- **Avatar-Liste repariert, Zeitmessung** (13.11.): „☁ IDs" und ▾ holen die
  Liste jetzt über `VRCApi.Get`; jeder Batch-Upload schreibt eine `TIME`-Zeile
  ins Upload-Log, am Ende steht eine Zusammenfassung; neuer Knopf „Log" →
  `2026-11-13-avatarliste-und-zeitmessung.md`

Nutzersicht dazu: `CHANGELOG.md`, Abschnitt `[Unreleased]`.

## Prüfstand

| Stand | In Unity kompiliert | Im Editor benutzt |
| --- | --- | --- |
| v3.3.0 | ja (Exitcode 0) | ja, drei Sichtprüfungen per Video |
| Paket von `2e74c29` | **nein** — `SdkCompat.cs` CS0246 | — |
| `f7c5989` | laut Nutzer ja („Fehler scheint weg"), Editor.log nicht geprüft | nein |
| Testpaket `a9d3ce4` (Onboarding, Varianten, alle Review-Fixes inkl. Select-Fix) | laut Nutzer ja: „läuft" auf SDK 3.10.6 / Unity 2022.3.22f1 (13.11., Editor.log nicht geprüft) | läuft; welche Funktionen ausprobiert wurden, ist noch offen |

| Avatar-Liste und Zeitmessung (nach `a9d3ce4`) | **nein** | **nein** |

Wichtig: Der 34-Outfit-Batch vom 26.09. (33/34 OK) lief auf dem **alten**
geladenen Code, weil der Compile des Testpakets fehlgeschlagen war. Er belegt
nichts über den unveröffentlichten Stand.

## Vor dem nächsten Release

1. Aktuelles Testpaket importieren, Console auf Compilerfehler prüfen.
2. Sichtprüfung: Onboarding-Karte (über **?**), dabei auf die Höhe achten —
   Kopf, Karte und Batch-Fußzeile lassen der Outfitliste bei normaler
   Fensterhöhe möglicherweise wenig Platz. Varianten anlegen, Items ändern,
   **Select** zwischen Outfit und Variante wechseln.
3. Blueprint-ID am Avatar: **Select** auf eine Variante ohne ID → das Feld
   „Blueprint ID" im Pipeline Manager ist leer; **Select** zurück auf das
   Outfit → seine ID steht wieder drin. Die Zeile „Base Version" bleibt dabei
   sichtbar und behält ihren Wert.
4. Batch auf dem neuen Code wiederholen (es darf weiterhin nur **eine**
   Eigentumsabfrage kommen); Variante per Express hochladen. Danach steht in
   der Statusbox eine Zeile „Timing: …", und **Log** öffnet das Upload-Log mit
   einer `TIME`-Zeile pro Upload. Zeigt eine Phase „?", hat das SDK das
   Ereignis nicht gesendet — bitte melden.
5. **☁ IDs** und **▾** drücken: Es muss die Liste der eigenen Avatare kommen.
   Kommt eine Fehlermeldung, deren Wortlaut zurückmelden.
6. Avatare in VRChat prüfen (vom Nutzer angekündigt).
7. Erst dann, auf ausdrücklichen Auftrag: Versionsnummer (README-Kopfzeile
   steht noch auf v3.3), `[Unreleased]` im Changelog abschließen, Paket, Tag,
   Release, Wiki-Abgleich.

## Bekannte offene Punkte

- **Select** auf einen Eintrag ohne Blueprint-ID nimmt jede ID vom Avatar,
  auch eine, die das Tool keinem Outfit zuordnen kann (etwa von einem Upload
  vor der Tool-Nutzung). Die wird in die Console geschrieben und ist per
  Strg+Z wieder holbar, steht danach aber nicht mehr im Inspector.
- **„☁ IDs" und ▾ sind repariert, aber noch nie gelaufen.** Bis zum 13.11.
  riefen sie `VRCApi.GetAvatars` auf, das es in keinem SDK gibt. Die neue
  Abfrage ist aus dem SDK-Quelltext abgeleitet (`SDK_NOTES.md`), ob der Server
  so antwortet wie erwartet, zeigt erst der Test beim Nutzer.
- **Zeitmessung nur im Batch.** Express-Uploads neuer Avatare werden nicht
  gemessen. Was die Messung ergibt, entscheidet über die nächsten Schritte bei
  der Upload-Zeit (Outfits vor dem Build ausblenden, Upload und Build
  überlappen, unnötiges Szenenspeichern).

- Kein `.asmdef`, keine Tests, keine CI — siehe Abschnitt Umgebung.
- Identität über Namen: Umbenennen eines Outfits verliert seine Einstellungen
  und versteckt seine Varianten (Dry Run warnt). Varianten sind nicht
  umbenennbar, nur entfernen und neu anlegen.
- `OutfitBatchUploader.cs` (rund 2150 Zeilen) mischt GUI, Batch-Ablauf und
  Scroll-Animation.
- Die Schleife des Upload-All-Gates ist nicht gegen einen Domain Reload
  zwischen zwei Express-Setups gesichert.
- ↺ (`ScanScene`) klappt weiterhin alle Zeilen zu; nur `RebuildOutfitList`
  erhält den Aufklapp-Zustand.
- Kleineres: `FlushScene` speichert die Szene bei jedem Upload,
  Versionsstempel-Regex, keine Dauer-Schätzung im Dry Run.

## Paketbau

`python3 tools/build-unitypackage.py <ziel.unitypackage>` baut das Paket aus
einem sauberen Arbeitsbaum (braucht nur Python 3 und git) und gibt Assetzahl,
Größe und SHA-256 aus. Derselbe Commit ergibt bei gleicher Python-/zlib-Version
dieselben Bytes. Das Format ist am veröffentlichten v3.3.0-Asset abgeglichen. Ein `.unitypackage` ist ein
gzip-Tar mit je Asset einem Ordner `<GUID>/` (GUID aus der `.meta`), darin
`asset` (fehlt bei Ordnern), `asset.meta` und `pathname` (Zielpfad ohne
Zeilenumbruch am Ende). Zielpfad ist das historische Doppelordner-Layout
`Assets/VRChat-outfit-batch-uploader-3.0.0/VRChat-outfit-batch-uploader-2.0.0/…`,
damit ein Import bestehende Installationen über die GUIDs an Ort und Stelle
aktualisiert. Enthalten: `Editor/` samt `Sounds/`, `Wiki/`, `LICENSE`,
`README.md`, `CHANGELOG.md` (34 Assets). Nicht enthalten: `AGENTS.md`,
`PROJECT_STATE.md`, `PROMO_KIT.md`, `project-memory/`, `tools/` und jeder
projektspezifische Zustand.

## Handoffs

- `2026-08-23-claude-migration.md` — Übernahme aus Claude Cowork
- `2026-08-23-item-vram-git-vorbereitung.md` — Item-VRAM, Release v3.2.0
- `2026-08-23-stabilitaetsreview.md` — Stabilitäts-Pass nach v3.2.0
- `2026-08-23-ui-neustrukturierung.md` — drei Arbeitsbereiche, kompakte Karten
- `2026-08-23-release-v3.3.0.md` — Release v3.3.0
- `2026-09-16-sdkcompat-aktivierung-und-zustandsfixes.md`
- `2026-09-27-onboarding.md`
- `2026-09-27-outfit-varianten.md`
- `2026-11-13-review-fixes.md`
- `2026-11-13-avatarliste-und-zeitmessung.md`
- Nachschlagewerk, kein Handoff: `project-memory/SDK_NOTES.md`
