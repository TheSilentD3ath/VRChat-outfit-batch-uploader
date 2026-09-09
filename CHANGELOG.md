# Changelog

All notable changes to the VRC Outfit Batch Uploader are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/). Detailed release notes for each version are on the [GitHub Releases page](https://github.com/TheSilentD3ath/VRChat-outfit-batch-uploader/releases).

## [3.3.0] — 2026-08-23 — UI & Backend Optimization

### Changed
- Three dedicated workspaces: Outfits, New Outfit, and Defaults; compact outfit cards with expandable details; fixed batch footer.
- Performance warnings (Very Poor Contacts/Lights/VRAM) use a quieter orange; red reserved for hard limits and blockers.

### Fixed
- Managed blendshapes reset before target overrides (no leaking between outfits); skin-renderer validated on avatar switch.
- Missing outfits in an active queue count as failures, not successes; session queue cleaned up after finish/cancel.
- Platform switches abort cleanly when Unity rejects the build target.
- Contact/light budgets correct for inactive outfits and selected items; baked lights no longer counted; nested EditorOnly subtrees respected.
- Project/version JSON written atomically with `.bak` recovery; damaged main files recover from backup on load.
- Express stores its resume state before potential domain reloads; thumbnail prepared before clearing the Blueprint ID; only plugin-created temp thumbnails are deleted.
- Texture optimizer always clears the progress bar and reports failed VRAM estimates.

## [3.2.0] — 2026-08-23 — Selected-item VRAM optimization

### Added
- Texture optimization (manual VRAM button and Express step) can optionally include the textures of items selected for an outfit. Opt-in (default off); shared textures deduplicated in one plan.

## [3.1.0] — 2026-07-11 — Update-proof settings, retry, validation & fixes

### Added
- Settings moved from EditorPrefs to `ProjectSettings/ShiroOutfit_data.json` (survives plugin updates; legacy values migrated automatically).
- Retry failed uploads button; Blueprint ID live validation; version-stamp mode (replace description / append v… line).
- Scene-view thumbnail source with preview window; Dry run report; upload log at `ProjectSettings/ShiroOutfit_upload.log` (~1 MB rotation).
- Fetch Blueprint IDs from VRChat (cloud button + per-outfit picker, via reflection); last-upload timestamps per platform.
- Thumbnail-only update for existing outfits; VRAM counter in the budget row; Quest shader check in the dry run; settings export/import bundle; taskbar flash on batch finish (Windows).

### Fixed
- Auto-consent loads before resume after a domain reload; no dead update-callbacks when closing the window mid-resume.
- JSON serialization for queue and blendshape snapshots (names with `|`/`:`/`;` no longer corrupt state); cancellation tokens disposed; main PipelineManager found anywhere under the avatar.
- DPS auto-detection matches "dps" only as a whole word (no more "HandPSprite" false positives), with the triggering object logged.

## [3.0.0] — 2026 — Outfit Setup, Items, FaceEmo, Budgets & Guided Upload

### Added
- Express / Advanced first-time setup for new outfits (clear blueprint, apply defaults, thumbnail, SDK auto-fixes, upload, write ID back).
- Guided "Upload All" for mixed new/existing selections; per-outfit Items (accessories); per-outfit FaceEmo via capture + tag-swap.
- Texture/VRAM optimizer (BC7/DXT1 + resolution cap, Thry-style recommendations); live budget counters (Contacts, Lights, Params, VRCFury-aware); SPS/DPS auto-tagging.
- All/None batch selection; search + scrolling on long lists; automatic copyright-dialog confirmation; confirm sound found by name regardless of install folder.
