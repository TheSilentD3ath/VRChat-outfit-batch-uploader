# Blendshapes (per-outfit overrides)

Different outfits often need different body shape settings — heel offsets, hidden chest/hips, shrunk hands, etc. Each outfit can store its own blendshape values for the avatar's skin mesh, applied automatically when that outfit is activated/uploaded.

## Setup

1. Make sure **Avatar skin** in the top bar points to the `SkinnedMeshRenderer` that has the blendshapes (usually the body — auto-detected).
2. Expand an outfit's **Blendshapes** foldout.

## Capturing values

- Set the body blendshapes the way you want for this outfit (in the Inspector), then click **"Capture current skin values as overrides"** — every non-zero blendshape is saved as that outfit's override.
- Or tick individual blendshapes and set their value with the slider.
- Use the **Search** box to find a blendshape; the list is scrollable so long lists don't overflow.
- **Clear all overrides** removes them for that outfit.

## How it's applied

When you Select / Upload / Express / Batch an outfit, its saved blendshape values are written to the skin mesh. After a batch finishes, the skin's original values are **restored**, so your working scene isn't left modified.

## Notes

- Only blendshapes you've pinned/captured are applied — others are left untouched.
- Overrides are stored per outfit in EditorPrefs.
