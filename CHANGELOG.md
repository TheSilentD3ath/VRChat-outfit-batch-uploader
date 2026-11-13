# Changelog

All notable changes to the VRC Outfit Batch Uploader are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/). Detailed release notes for each version are on the [GitHub Releases page](https://github.com/TheSilentD3ath/VRChat-outfit-batch-uploader/releases).

## [Unreleased]

### Added
- First-run guide for new users: a card in the window walks through choosing the avatar, creating the Outfits and Items objects (one click, undoable), adding the first outfit and connecting it to VRChat. Steps tick themselves off as you work; skippable, and the **?** in the header brings it back. People who have used the tool before never see it.
- One-time tips next to Items, Batch Upload and the VRAM counter, for new users after the guide.
- Outfit variants: upload the same outfit several times with different items, each as its own avatar with its own Blueprint ID (`Outfit – Variant`). Variants share blendshapes, FaceEmo and platforms with their outfit; the dry run warns when renaming an outfit hides its variants.
- VRAM counter splits body, outfit and items; texture optimization can optionally include the shared body textures (off by default, always confirmed).
- Dry run warns when two avatars in the scene share a name (their settings would overwrite each other).
- Upload timing: every batch upload writes a `TIME` line to the upload log showing how long activating, saving, building and uploading took and how large the file was; a batch ends with a summary of where the time went. A **Log** button next to Dry run opens the upload log.

### Fixed
- **☁ IDs** and the **▾** avatar picker never returned any avatars: they relied on an SDK function that does not exist in any SDK version. They now load the account's avatars through the SDK's public request API, and only ever offer avatars the logged-in account owns.
- Closing the window cancels a running batch; docking or a domain reload no longer breaks it.
- Express quiet mode survives a domain reload.
- Text fields for outfits parent, items parent, Blueprint ID and base version no longer save and rebuild on every keystroke.
- Switching avatars clears the previous avatar's drafts and cached values.
- Fetch-my-avatars reports an unsupported SDK instead of silently returning nothing.
- Texture memory leak in the outfit list background; faster texture scanning.
- **Select** on an outfit or variant without a Blueprint ID left the previously active outfit's ID on the avatar, so an upload from the VRChat SDK window overwrote that other avatar. The ID is now cleared in that case (undoable; an ID no outfit uses is written to the Console). The Base Version row stays available while the avatar has no ID.
- Upload errors whose text merely contains "rig" (as in "copyright" or "right") are no longer mistaken for avatar validation errors and skipped without asking.
- The copyright pre-consent no longer depends on an SDK class at compile time: if a VRChat SDK update moves or removes it, only that convenience switches off instead of the whole tool failing to compile.

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
