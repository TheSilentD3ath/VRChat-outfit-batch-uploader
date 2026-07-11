# Texture / VRAM optimization

Reduces an outfit's texture VRAM using the same recommendations as [Thry's Avatar Performance Tools](https://github.com/Thryrallo/VRC-Avatar-Performance-Tools) (MIT). The logic is reimplemented, so **Thry's package is not required**.

## What it changes

- **Compression** — textures that aren't optimally block-compressed get a PC platform override at quality 100:
  - **BC7** for textures with alpha or normal maps,
  - **DXT1** otherwise.
- **Resolution** — textures larger than the configured cap (default **2048**) have their `maxTextureSize` reduced, never below an optional **floor**.

## Using it

- Each outfit row has a **VRAM** button. It scans that outfit's materials' textures, shows a preview (how many format/resolution changes and the estimated VRAM saved), and asks for confirmation before applying. Details are logged to the Console.
- It can also run automatically during **Express setup** — see defaults below.

## Defaults

In **New Outfit Defaults → Texture optimization**:

- **Optimize textures during Express** — on/off.
- **Ask before applying** — when on, Express shows a one-time prompt with **Optimize now / Skip / Always (don't ask again)**.
- **Max resolution (cap)** — 256 / 512 / 1024 / 2048 / 4096 (default 2048).
- **Never reduce below** — an optional floor so textures aren't shrunk too far.

## ⚠️ Important

- Changes are made to the **texture import settings** and are **NOT undo-able**.
- Import settings are **per-asset**: optimizing a texture shared by several outfits affects all of them.
- Only textures with a standard `TextureImporter` are touched (e.g. DDS / render textures are skipped).
