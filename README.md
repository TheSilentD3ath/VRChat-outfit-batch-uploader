# VRC Outfit Batch Uploader

**v3.2** · Unity 2022.x · VRChat Avatars SDK · MIT

A Unity Editor tool for VRChat avatar creators who manage multiple outfits under a single avatar and need to upload each one separately.

## What it does

- Detects all outfit GameObjects under a configurable **Outfits** parent in your scene
- For each outfit: switches tags (`Untagged` / `EditorOnly`), toggles `SetActive`, and sets the correct `PipelineManager` blueprint ID
- Applies **per-outfit blendshape overrides** on the avatar skin mesh (useful for heels offsets, body shape adjustments, etc.)
- **Cross-Platform Batching:** Groups outfits by target platform (Windows, Android, iOS) to minimize Unity platform switching.
- **Domain Reload Survival:** Safely pauses and automatically resumes the upload queue across Unity platform switches and script recompilations.
- **Avatar Versioning:** Set a base version number that is saved to your project and automatically stamped into the VRChat description of every uploaded outfit.
- **Visual Feedback:** Platform-specific colored progress bars (Blue/Green/Silver) with sub-step progress tracking during the build and upload phases.
- Asks for ownership confirmation once at the start — no repeated SDK consent dialogs mid-batch
- Plays a sound when all uploads are done
- Restores blendshape values to their original state after the batch completes
- **Robust Safety Checks:** Automatically verifies VRChat SDK login status and confirms platform targets before building to prevent errors.
- **New Outfit Setup (Express / Advanced):** Turns the tedious first-time setup of a new outfit into one click — clears the blueprint, creates & uploads a brand-new avatar, applies your default name/description/tags/thumbnail/release, and writes the new Blueprint ID back automatically.
- **Guided "Upload All":** Selecting a mix of ready and not-yet-set-up outfits and pressing Upload walks you through setting up the new ones (Express all, or per-outfit Express / Skip / Configure) before batching the rest.
- **Per-outfit Items (accessories):** Pick, per outfit, which accessory objects upload with it.
- **Per-outfit FaceEmo:** Make [FaceEmo](https://suzuryg.github.io/face-emo/) face-expression menus per outfit via capture + tag-swap.
- **Texture / VRAM optimizer:** One-click texture compression + resolution capping using Thry's recommendations, optionally including the items selected for that outfit.
- **Live budget counters:** Per outfit, see Contacts (rank + 256 cap), realtime Lights, and expression Parameters (VRCFury-aware).
- **Quality-of-life:** All/None batch selection, search + scrolling on long lists, automatic copyright-dialog confirmation.

## New Outfit Setup (Express / Advanced)

Any outfit without a Blueprint ID is treated as **new** and listed in the **New Outfit Setup** section. Instead of manually unbinding the blueprint, copying the freshly generated ID, setting tags and accepting SDK fixes, you get two paths:

- **⚡ Express** — one click does everything: clears the `PipelineManager` blueprint ID (so the SDK registers a brand-new avatar), applies your configured defaults (name, description, release status, content-warning tags), captures a thumbnail, optionally accepts the VRChat SDK's proposed auto-fixes, builds + uploads the new avatar, and **writes the new Blueprint ID back into the tool automatically**.
- **⚙ Advanced** — same flow, but lets you review/override the name, description, content tags, release status and thumbnail per outfit before uploading.

**Defaults** (configured once in the section, saved in EditorPrefs):

- **Avatar name / description templates** — support `{outfit}` and `{avatar}` tokens
- **Release status** — `private` or `public`
- **Content warnings** — the five VRChat tags: Sexually Suggestive, Adult Language and Themes, Graphic Violence, Excessive Gore, Extreme Horror
- **Auto-detect SPS/DPS** — if VRChat SPS (VRCFury Haptic Plug/Socket) or DPS markers are found on the avatar, the *Sexually Suggestive* tag is added automatically (can be turned off to use only your default tag set)
- **Thumbnail** — either a fixed default image, or an auto-capture from a scene camera rendered against a solid (filled) background color
- **Auto-accept SDK fixes** — best-effort: invokes the auto-fix actions the SDK lists in its build alerts. If the SDK internals differ on your version it fails gracefully and you apply the fixes manually as usual.

> Keep the VRChat SDK Control Panel open and logged in while using Express setup. If an auto-fix triggers a script recompile / domain reload, the upload resumes automatically when the editor settles.

The Express/Advanced buttons appear inline on every outfit that has no Blueprint ID yet. The **New Outfit Defaults** section only holds the shared default settings.

## Texture optimization (VRAM)

Each outfit row has a **VRAM** button that optimizes that outfit's textures using the same recommendations as [Thry's Avatar Performance Tools](https://github.com/Thryrallo/VRC-Avatar-Performance-Tools) (MIT):

- **Compression** — uncompressed textures are block-compressed: `BC7` for textures with alpha or normal maps, `DXT1` otherwise (PC platform override, quality 100).
- **Resolution** — textures larger than the configured cap (default **2048**) have their `maxTextureSize` reduced, with an optional "never reduce below" floor.

It shows a preview with the estimated VRAM saved and asks for confirmation before applying. When **Also optimize the outfit's selected items (accessories)** is enabled, the plan additionally scans every item currently included for that outfit. Textures shared by the outfit and its items are processed only once.

Changes are made to the texture import settings and are **not undo-able**, and because import settings are per-asset, optimizing a shared texture affects every outfit that uses it. Item optimization is therefore disabled by default and must be enabled explicitly.

In the **New Outfit Defaults → Texture optimization** settings you can enable running this automatically during Express (with a one-time "always / don't ask again" prompt), include selected items, and set the resolution cap and floor. The item setting applies to both the manual **VRAM** button and Express optimization.

## Items (accessories)

Besides outfits, you can keep accessory objects (props, weapons, jewelry, …) under a second configurable parent (default **Items**) and choose **per outfit** which of them upload with it.

Each outfit row has its own **Items** foldout listing every accessory with an "include with this outfit" checkbox (saved per outfit), plus All / None and per-item Ping. On activation (Select / Upload / Express / Batch) the active outfit's selection is applied:

- **included** items are set to `Untagged` (uploaded),
- **excluded** items are set to `EditorOnly` (stripped at build, not uploaded).

Set the items parent name and the per-name **"included on every outfit by default"** toggles in **New Outfit Defaults → Items (accessories)**. Each outfit starts from those defaults and you can override per outfit.

### Budget counters (per outfit)

Each outfit row shows a live one-line budget for what uploads with that outfit (its own components + its included items + shared body, ignoring `EditorOnly` and other outfits):

- **◆ Contacts** — VRChat Contacts (`VRCContactSender` / `VRCContactReceiver`). Networked contacts set the performance rank, colour-coded (green = Excellent/Good, yellow = Medium/Poor, red = Very Poor); thresholds PC 8/16/24/32, Quest 2/4/8/16. Local-only contacts (e.g. many SPS senders) don't count. Also shows `total / 256` — the hard cap above which VRChat disables contacts.
- **☀ Lights** — realtime `Light` components. Avatars should have **0** (PC: 1 = Poor, 2+ = Very Poor; Quest: any light = Very Poor).
- **⚙ Params** — expression-parameter memory (avatar-wide), `cost / 256`. If the avatar uses **VRCFury**, this is shown greyed as "(VRCFury)" because VRCFury's parameter compressor handles the 256-bit limit at build time, so the editor cost isn't the final synced cost.

## FaceEmo (per outfit)

[FaceEmo](https://suzuryg.github.io/face-emo/) (MIT) generates a single Modular-Avatar object named `FaceEmoPrefab` under the avatar and natively supports only one face-expression config per avatar. Each outfit row has a **FaceEmo** foldout that makes it per-outfit via *capture + tag-swap* — without touching FaceEmo's internals:

1. **Open FaceEmo**, build this outfit's expressions, and click **Generate** in FaceEmo (this creates `FaceEmoPrefab`).
2. Back in the outfit row, press **Capture** — the tool renames `FaceEmoPrefab` to `FaceEmo__<outfit>` and remembers it for that outfit.
3. Repeat per outfit (each Generate makes a fresh `FaceEmoPrefab` to capture).

On every activation (Select / Upload / Express / Batch) the active outfit's captured FaceEmo object is set `Untagged` (uploaded) and all other outfits' FaceEmo objects `EditorOnly` (stripped), so each outfit uploads only its own face expressions. **Clear** unassigns (the object stays in the scene); **Ping** selects it. If an uncaptured `FaceEmoPrefab` is left around, the foldout warns you (it would otherwise merge onto every outfit).

## Requirements

- Unity 2022.x (tested on 2022.3.x)
- VRChat Avatar SDK (`com.vrchat.avatars`) installed via the VRChat Creator Companion

### Optional integrations (none are required)

The tool references no third-party types directly, so it compiles and runs without any of these — the related features simply activate when the package is present:

- **VRCFury** — SPS/DPS auto-tag detection and the VRCFury-aware Parameters counter.
- **FaceEmo** — the per-outfit FaceEmo capture workflow.
- **Modular Avatar** — required by FaceEmo to merge the captured face menus at build.
- **Thry's Avatar Performance Tools** — not needed; the VRAM optimizer reimplements its recommendations.

## Installation

1. Copy this package's folder (the one containing the `Editor` folder) into your project's `Assets` folder
2. Unity compiles the scripts automatically
3. Open the window via **Tools → Shiro → Outfit Batch Uploader**

The tool works in any VRChat avatar project that has the Avatar SDK installed, regardless of the folder name you install it under.

## Setup

1. **Avatar root** — drag your avatar's root GameObject into the field (auto-detected if only one avatar is in the scene)
2. **Avatar skin** — the SkinnedMeshRenderer with blendshapes (auto-detected)
3. **Outfits parent** — name of the GameObject that contains all outfit prefabs as direct children (default: `Outfits`)
4. For each outfit, paste its **Blueprint ID** (`avtr_...`) — the field validates the format, and everything is saved per-project in `ProjectSettings/ShiroOutfit_data.json` (survives plugin updates; settings from older versions are migrated automatically on first read)
5. **Base Version** — (Optional) Enter a version number (e.g., `v1.2`) to stamp it into the description of all uploaded outfits. The dropdown next to it chooses whether the version **replaces** the description (classic behavior) or is **appended** as a `v…` line while keeping the description text.
6. Use **"Capture current skin values"** inside each outfit's blendshape foldout to save the current skin state as that outfit's overrides

## Usage

- **Select** — activates a single outfit (sets tags, pipeline ID, blendshapes) without uploading
- **Upload** — activates + uploads a single outfit
- **Upload All** — processes every "Include in batch" outfit in order. If some selected outfits aren't set up yet (no Blueprint ID), it first asks whether to **Express-setup all** of them or **decide per outfit** (Express / Skip / Configure… in a popup with name, description, tags and thumbnail). Each set-up outfit is created & uploaded on the current platform; the already-configured outfits then run through the normal platform-grouped batch. (New outfits are uploaded on the current platform during setup — run Upload All again to also build them for other platforms.)
- **Retry failed** — after a batch, any failed or skipped outfits can be re-queued with one click (or dismissed) instead of hunting them down manually.

## Where settings are stored

All per-avatar data (Blueprint IDs, batch & platform toggles, blendshape overrides, item selections, FaceEmo captures) lives in `ProjectSettings/ShiroOutfit_data.json`; avatar versions live in `ProjectSettings/ShiroOutfit_versions.json`. Both are outside the plugin folder, so **deleting/replacing the plugin folder on updates never loses your settings**, and the files can be backed up or versioned with the project. Settings created by older versions (stored in EditorPrefs) are migrated automatically the first time they're read. Global defaults (templates, thumbnail settings, sound toggle) remain in EditorPrefs.

## License

MIT — see [LICENSE](LICENSE)
