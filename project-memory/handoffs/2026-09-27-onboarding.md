# Handoff: Erststart-Onboarding

Datum: 27. September 2026

## Anlass

Nach der Installation bekam ein neuer Nutzer weder Erklärung noch Führung.
Das Erste, was er sah, war die gelbe Warnung „No child named "Outfits"
found…". Wunsch des Nutzers: ein Onboarding nur für Leute, die das Tool noch
nie benutzt haben. Es soll Schritt für Schritt führen und die leeren
Outfit-/Item-Objekte auf Wunsch selbst anlegen. Bestehende Nutzer sehen
nichts davon.

Designgrundlage: Apple Design Skill (github.com/dickwu/apple-design-skill),
`references/hig/onboarding.md` — „Teach through interactivity", kontextbezogene
Tipps statt langer Einführung, optional und später wieder aufrufbar.

## Umgesetzt

- Neue Partial-Datei `Editor/OutfitOnboarding.cs` (+ `.meta`, neue GUID).
- **Erkennung, je Rechner (EditorPrefs `ShiroOutfitUploader_Onboarding`):**
  Beim ersten Öffnen wird einmal entschieden. Als erfahren gilt, wer in diesem
  Projekt ein Outfit mit Blueprint-ID oder aufgezeichnetem Upload hat oder
  ein `ShiroOutfit_upload.log` besitzt; das prüft
  `OutfitProjectData.HasUsageHistory()`. Erfahrene Nutzer sehen weder Karte
  noch Tipps, auch nicht in einem späteren leeren Projekt. Bloßes Öffnen
  zählt nicht, weil `RebuildOutfitList` sofort Datensätze anlegt.
- **Karte** statt Assistenten-Dialog, an der Stelle der alten Warnung und
  über der Liste. Sie bleibt, bis das erste Outfit eine Blueprint-ID hat und
  „Done" gedrückt wird. Die Schritte lesen den echten Zustand:
  1. Avatar gewählt (`_avatarRoot`)
  2. Outfits-Objekt vorhanden. Knopf „Create "Outfits" and "Items""
     legt fehlende leere Objekte unter dem Avatar an, als ein Undo-Schritt.
  3. Mindestens ein Outfit. „Show in Hierarchy" pingt das Objekt; fehlt nur
     das Items-Objekt, wird es angeboten.
  4. Blueprint-ID vorhanden. Führt zu New Outfit/Express oder zum Einfügen
     einer vorhandenen ID.
- Während die Karte sichtbar ist, baut `OnHierarchyChangedInvalidate` die
  Liste neu auf (sonst nur über ↺), damit sich die Schritte selbst abhaken.
  Außerhalb des Onboardings ändert sich daran nichts.
- „Skip Introduction" und „Done" beenden die Karte. Das **?** im Header
  startet sie neu und setzt die Tipps zurück.
- **Tipps** (`DrawTip`): nur für neue Nutzer nach der Karte, je einmal
  wegklickbar, höchstens einmal pro GUI-Durchlauf gezeichnet. Stellen:
  Items-Foldout, Kopf von Batch Upload, unter der Budgetzeile (VRAM).
- Aktionen, die das Fensterlayout ändern, laufen über `AfterGui`
  (`EditorApplication.delayCall`). So bleiben Layout- und Input-Durchlauf
  von IMGUI konsistent.
- Wiki `Getting-Started.md`: Abschnitt „First launch".
  `CHANGELOG.md`: neuer Abschnitt `[Unreleased]` mit allen Änderungen seit
  v3.3.0.

## Verifikation

Nur statisch, da kein Unity oder Compiler verfügbar war: Klammerbilanz aller
geänderten Dateien und Auflösung aller verwendeten Symbole (jedes genau
einmal deklariert).

## Offene Punkte / Risiken

- **Unity-Compile und Sichtprüfung offen.** Besonders prüfen: Header-Zeile mit
  **?** (vorher `LabelField`, jetzt `Label` in einer horizontalen Zeile) und
  die Darstellung der Glyphen ✓ ● ○.
- Testweg für den Neuzustand: EditorPrefs-Key
  `ShiroOutfitUploader_Onboarding` löschen **und** ein Projekt ohne
  Blueprint-IDs oder Upload-Log verwenden, oder einfach das **?** drücken.
- Nächster Block: Outfit-Varianten nach Entscheidung (a). Bekannter
  Stolperstein: `ActivateOutfit` schaltet je Eintrag und muss auf
  GameObject-Vergleich umgestellt werden, sobald mehrere Einträge dasselbe
  GameObject teilen.
