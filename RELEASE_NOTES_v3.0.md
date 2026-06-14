# v3.0 — Outfit Setup, Items, FaceEmo, Budgets & Guided Upload

A huge update that turns the uploader from a batch tool into a full multi-outfit workflow: it can now **set up brand-new outfits for you**, manage **per-outfit accessories and FaceEmo**, show **live performance budgets**, and **guide you through uploading a mix of new and existing outfits** in one go.

Everything new is built so that **no third-party package is required** — the tool references no external types directly, so it compiles on its own and the related features light up only when VRCFury / FaceEmo / Modular Avatar are present.

## ✨ Highlights

### New Outfit Setup — Express / Advanced (one click)
Any outfit without a Blueprint ID now gets inline **⚡ Express** and **⚙ Advanced** buttons. Express does the whole tedious first-time dance automatically:
- clears the `PipelineManager` blueprint so the SDK registers a brand-new avatar,
- applies your defaults (name & description templates with `{outfit}`/`{avatar}` tokens, release status, content-warning tags),
- captures a thumbnail (scene camera with a solid filled background, or a default image),
- best-effort accepts the SDK's proposed auto-fixes,
- builds + uploads the new avatar, and **writes the new Blueprint ID back into the tool** — no copy-pasting IDs.

**Advanced** opens the same flow with per-outfit overrides. A **New Outfit Defaults** section holds all the shared default settings.

### Guided "Upload All"
Select any mix of already-uploaded and brand-new outfits and press **Upload All**. If some aren't set up yet, it asks once whether to **Express-setup them all**, or to **decide per outfit** (Express / Skip / Configure… in a focused popup). New outfits are created on the current platform, then the already-configured ones run through the normal platform-grouped batch.

### Per-outfit Items (accessories)
Put props / weapons / jewelry under a configurable **Items** parent and choose, **per outfit**, which of them upload with it (included → `Untagged`, excluded → `EditorOnly`). A global "included on every outfit by default" list seeds each outfit, with per-outfit overrides.

### Per-outfit FaceEmo
Make [FaceEmo](https://suzuryg.github.io/face-emo/) face-expression menus **per outfit** via *capture + tag-swap*: generate in FaceEmo, press **Capture**, and on upload only the active outfit's face menu is included. No FaceEmo internals are touched.

### Texture / VRAM optimizer
A **VRAM** button per outfit (and an optional Express step) compresses textures and caps resolution using [Thry's Avatar Performance Tools](https://github.com/Thryrallo/VRC-Avatar-Performance-Tools) recommendations (BC7 for alpha/normal, DXT1 otherwise; cap to 2048 by default, with a "never below" floor). Shows estimated VRAM saved and confirms before applying.

### Live budget counters (per outfit)
Each outfit row shows, for what actually uploads with it:
- **◆ Contacts** — networked count → performance rank (colour-coded) + `total / 256` hard cap,
- **☀ Lights** — realtime light count,
- **⚙ Params** — expression-parameter cost / 256, shown as **(VRCFury)** when VRCFury's compressor will handle it.

### SPS/DPS auto-tagging
If VRCFury SPS / DPS components that will actually be uploaded are detected, the **Sexually Suggestive** content tag is added automatically (ignores `EditorOnly` and other outfits; can be turned off).

## 🧰 Quality of life
- **All / None** buttons to select/deselect every outfit for batch.
- **Search + scrolling** on long Items and Blendshape lists.
- Automatic confirmation of the SDK's copyright/ownership dialog during new-avatar uploads.
- Completion sound is now found by name, so it works regardless of the install folder.

## ⚠️ Notes
- Texture optimization edits import settings and is **not undo-able**; shared textures affect every outfit that uses them.
- Express uploads new outfits on the **current platform**; run Upload All again to also build them for other platforms.
- The "accept SDK fixes" and copyright auto-confirm steps are best-effort (they use SDK UI internals); if they can't act, the normal manual flow still works. Keep the VRChat SDK Control Panel open and logged in.

## 📦 Installation
Copy this package's folder (the one containing the `Editor` folder) into your project's `Assets` folder. Unity compiles it automatically. Open via **Tools → Shiro → Outfit Batch Uploader**.

**Requires:** Unity 2022.x and the VRChat Avatar SDK (`com.vrchat.avatars`). Optional: VRCFury, FaceEmo + Modular Avatar.

**License:** MIT
