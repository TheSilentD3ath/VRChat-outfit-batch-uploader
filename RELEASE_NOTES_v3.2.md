# v3.2 — Selected-item VRAM optimization

## ✨ New

### Include selected items in texture optimization

The manual **VRAM** action and automatic Express optimization can now include the textures of the accessory items selected for the current outfit.

- Enable **Also optimize the outfit's selected items (accessories)** under **New Outfit Defaults → Texture optimization**.
- The preview reports how many selected items are included.
- A texture shared by the outfit and one or more items is processed only once per optimization plan.
- The setting is disabled by default so an existing automatic Express workflow cannot begin changing item textures without an explicit opt-in.

Texture optimization still changes shared importer settings and is not undo-able. If several outfits or items reference the same texture asset, the importer change affects every use of that asset.
