# Handoff: Outfit-Varianten

Datum: 27. September 2026

## Anlass

Ein neuer Tester (Malone) lädt dasselbe Outfit gern mehrfach hoch, mit
jeweils anderen Accessories. Ein Accessory-Tab existierte schon (Items, pro
Outfit wählbar). Es fehlte, dasselbe Outfit-Objekt mehrfach mit eigener
Blueprint-ID hochzuladen.

**Entscheidung des Nutzers: Variante (a).** Eigen sind nur Name, Items und
Blueprint-ID, außerdem Batch-Haken und Upload-Historie. Blendshapes, FaceEmo
und Plattformen kommen vom Basis-Outfit.

## Umgesetzt

- `OutfitProjectData.OutfitData`: neue Felder `baseOutfit` und `variantName`.
  Leer oder `null` (ältere Dateien) bedeutet normales Outfit. Neue Zugriffe:
  `HasOutfit`, `GetVariants`, `GetOrphanedVariants`, `AddVariant`,
  `RemoveOutfit`.
- Datensatzname `"<Basis> – <Variante>"` (Gedankenstrich). Dadurch
  funktioniert alles, was am Outfitnamen hängt, ohne Sonderfall: Items,
  Blueprint-ID, Batch-Warteschlange samt Domain-Reload-Resume, Express-Name
  über `{outfit}`, ID-Zuordnung in „☁ IDs", VRAM-/Budget-Caches.
- `OutfitEntry`: `BaseName`, `VariantName`, `IsVariant`, `SettingsName`
  (Basisname für geerbte Einstellungen). `RebuildOutfitList` hängt die
  Varianten direkt hinter ihre Basis. Das `Go` ist dasselbe, und
  `BlendShapes` ist dieselbe Instanz wie bei der Basis.
- **Zwei vorhandene Fehlerquellen umgestellt:**
  - `ActivateOutfit` verglich Einträge (`entry == target`). Bei einem
    geteilten GameObject hätte der letzte Listeneintrag es wieder
    ausgeschaltet. Jetzt wird `entry.Go == target.Go` verglichen.
  - `ApplyFaceEmoStates`: gleiches Muster. Zusätzlich hätte eine Variante
    ohne eigenen FaceEmo-Eintrag das FaceEmo beim Upload abgeschaltet. Jetzt
    gelten `SettingsName` und der Go-Vergleich. Der Dry Run nutzt ebenfalls
    `SettingsName`.
- Neue Partial-Datei `Editor/OutfitVariants.cs` (+ `.meta`):
  - UI: in den Details der Basis „New variant: [Name] [Add Variant]"; in den
    Details einer Variante ein Hinweis auf das Geerbte und „Remove Variant"
    (mit Dialog; auf VRChat wird nichts gelöscht).
  - Die Karte der Variante wird über den Style-Margin eingerückt, nicht über
    einen Wrapper. Die Motion-Blur-Geister lesen `GetLastRect()` der Karte.
  - Die Variantenstile werden in `DisposeStyles` genullt, weil sie die
    zerstörte Zeilentextur mitkopieren.
  - „Active"-Plakette: Bei geteilten Objekten trägt sie der zuletzt aktivierte
    Eintrag (`_activatedName`, `[SerializeField]`, überlebt einen Reload),
    sonst die Basis.
  - Eine neue Variante übernimmt die aktuelle Item-Auswahl der Basis und
    öffnet sich mit aufgeklappten Items.
  - Plattform-Umschalter der Basis geben den Wert an die Varianten weiter.
- Dry Run warnt vor verwaisten Varianten (Basis umbenannt oder entfernt).
- Wiki `Items.md`: Abschnitt „Variants". `CHANGELOG.md`: Eintrag unter
  `[Unreleased]`.

## Verifikation

Nur statisch: Klammerbilanz aller geänderten Dateien und Symbolauflösung.
Alle Schleifen über `_outfits` wurden auf die Annahme „ein Eintrag je
GameObject" geprüft. Außer den zwei oben genannten arbeiten alle über
`o.Name` und sind variantenfest.

## Offene Punkte / Risiken

- **Unity-Compile und Bedienprüfung offen.** Testweg: Variante anlegen,
  Items ändern, Select auf Basis und Variante im Wechsel (Items und
  Active-Plakette), Express-Upload der Variante (Name `… – …`), danach Batch
  mit Basis und Variante gemischt.
- Eine Variante lässt sich nicht umbenennen, nur entfernen und neu anlegen,
  weil ihr Name der Datensatzschlüssel ist.
- Namensbasierte Zuordnung wie bei Outfits (alter Punkt #3): Wird die Basis
  umbenannt, sind ihre Varianten verborgen. Der Dry Run meldet das.
