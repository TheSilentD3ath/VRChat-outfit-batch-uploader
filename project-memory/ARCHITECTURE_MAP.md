# Architekturkarte

## Kernaufteilung

- `Editor/OutfitBatchUploader.cs`: Hauptfenster, Batch-Ablauf, Aktivierung,
  Plattformwechsel und übergreifende UI.
- `Editor/OutfitProjectData.cs`: projektbezogene Persistenz und Migration aus
  älteren EditorPrefs-Daten.
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
- `Editor/OutfitApiTools.cs`: reflektiver Zugriff auf VRChat-SDK-Funktionen.

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
