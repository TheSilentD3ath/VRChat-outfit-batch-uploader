# v3.3.0 — UI & Backend Optimization Update

> A calmer workspace on the surface, stronger state handling underneath.

## ✨ A focused interface

- Three dedicated workspaces: **Outfits**, **New Outfit**, and **Defaults**.
- Compact outfit cards keep the actions and health information you need in view.
- Blueprint IDs, platform editing, blendshapes, items, FaceEmo, VRAM, and thumbnails move into expandable details.
- New outfits remain immediately actionable through **Express** and **Advanced**.
- The batch footer stays visible below the scrolling outfit list.
- Performance warnings are visually quieter; hard limits and actual blockers still stand out in red.

## ⚙️ More reliable outfit state

- Managed blendshapes are reset before the target outfit's overrides are applied, preventing values from leaking between outfits.
- Skin-renderer detection is validated when switching avatars.
- Missing outfits in an active queue are reported as failures instead of successes.
- Platform switches now abort cleanly when Unity rejects the requested build target.
- Batch and resume data are cleaned up after completion or cancellation.

## 📊 More accurate performance data

- Contact and light budgets are calculated for inactive outfits and their selected items without being confused by the plugin's temporary `EditorOnly` owner tags.
- Explicitly excluded nested `EditorOnly` subtrees remain ignored.
- Baked lights no longer count as runtime avatar lights.
- Item selection changes invalidate VRAM and budget caches immediately.
- Item **All / None** changes are persisted in one operation.

## 💾 Safer project data

- Outfit data and avatar-version JSON files are written atomically.
- The previous valid file is retained as a `.bak` recovery copy.
- A damaged main file can be recovered from its backup during loading.

## ⚡ Stronger Express and texture workflows

- Express stores its complete recovery state before scene saves, asset refreshes, SDK fixes, or other operations that may reload scripts.
- The thumbnail is prepared before the previous Blueprint ID is cleared.
- Temporary cleanup only removes thumbnails created by the plugin.
- Upload-relevant scanning includes the target outfit and its selected items while respecting explicitly excluded subtrees.
- Texture optimization always clears Unity's progress indicator and reports failed VRAM estimates instead of silently treating them as zero.

## 🧩 Selected-item VRAM optimization

The v3.2 selected-item optimization remains available and opt-in. Outfit and selected-item textures share one deduplicated plan, so shared texture assets are processed only once. Remember that Unity importer settings are asset-wide: optimizing a shared texture affects every outfit or item that uses it.

## Compatibility

- Unity 2022.x
- VRChat Avatars SDK
- Existing project data remains compatible; no manual migration is required.
- No third-party integration is required.
