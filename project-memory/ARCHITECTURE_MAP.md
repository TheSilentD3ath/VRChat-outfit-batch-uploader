# Architekturkarte

## Kernaufteilung

- `Editor/OutfitBatchUploader.cs`: Hauptfenster, Batch-Ablauf, Aktivierung,
  Plattformwechsel und übergreifende UI.
- `Editor/OutfitProjectData.cs`: projektbezogene Persistenz und Migration aus
  älteren EditorPrefs-Daten.
- `Editor/AvatarVersionManager.cs`: Basis-Versionsnummern je Blueprint-ID in
  `ProjectSettings/ShiroOutfit_versions.json`. Schreibt über
  `OutfitProjectData.WriteAtomically`.
- `Editor/OutfitNewSetup.cs`: Express-/Advanced-Erstanlage neuer Outfits.
- `Editor/OutfitBatchSetupGate.cs`: geführte Vorbereitung neuer Outfits vor
  einem Batch.
- `Editor/OutfitItems.cs`: outfitspezifische Auswahl von Zubehör-Objekten.
- `Editor/OutfitFaceEmo.cs`: outfitspezifischer Capture-/Tag-Swap für FaceEmo.
- `Editor/OutfitTextureOptimizer.cs`: Textursammlung, VRAM-Schätzung und
  Importer-Optimierung.
- `Editor/OutfitContacts.cs`: Budgetauswertung für Contacts, Lights und
  Parameter.
- `Editor/OutfitDryRun.cs`: nichtdestruktive Vorabprüfung eines Uploads.
- `Editor/OutfitApiTools.cs`: Avatarliste, Thumbnail-Update und
  Einstellungs-Export/Import über die VRChat-API.
- `Editor/SdkCompat.cs`: einziger Anlaufpunkt für Reflection in VRChat-SDK- und
  Unity-Interna. Lookups sind gecacht, fehlende Methoden warnen einmalig und
  deaktivieren nur das betroffene Feature.

## Zustands- und Datenfluss

Avatar- und Outfitzustand wird projektbezogen unter `ProjectSettings`
gespeichert. Globale Bedienvorgaben bleiben in EditorPrefs. Batch- und
Express-Abläufe müssen Domain Reloads und Plattformwechsel überleben. Der
aktive Outfitzustand steuert Tags, Blueprint-ID, Blendshapes, Items und
FaceEmo gemeinsam; Erweiterungen dürfen diese Schritte nicht getrennt und
widersprüchlich nachbauen.

## Externe Grenzen

VRChat SDK, VRCFury, FaceEmo und Modular Avatar sind externe Systeme. Optionale
Integrationen dürfen keine harte Compile-Abhängigkeit erzeugen, sofern das
bestehende Feature ausdrücklich reflektiv und optional gestaltet ist.

Neue Reflection auf SDK- oder Unity-Interna gehört nach `SdkCompat.cs` und nicht
in die aufrufende Datei. Die Ausnahmen sind bewusst dort geblieben, wo sie eng
zum Fachcode gehören: Typerkennung ohne Methodenaufruf (`OutfitContacts.cs`) und
die UI-Automatisierung der SDK-Panels (`OutfitNewSetup.cs`).
