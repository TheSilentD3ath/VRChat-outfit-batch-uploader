# New Outfit Setup (Express / Advanced)

Any outfit **without a Blueprint ID** is treated as new and shows inline **⚡ Express setup** and **⚙ Advanced** buttons in its row.

## ⚡ Express setup (one click)

Express does the whole first-time dance automatically:

1. **Activates** the outfit (this outfit `Untagged`, others `EditorOnly`) so what's measured/uploaded matches reality.
2. *(optional)* **Optimizes textures** — see [[Texture / VRAM optimization|VRAM-Optimization]].
3. **Clears** the `PipelineManager` blueprint ID so the SDK registers a brand-new avatar.
4. *(optional)* **Accepts the SDK's proposed auto-fixes** (best-effort).
5. **Captures a thumbnail** — scene camera with a solid filled background, or a default image.
6. **Builds + uploads** the new avatar with your default name, description, tags and release status.
7. **Writes the new Blueprint ID back** into the tool — no copy-pasting.

The SDK copyright dialog is auto-confirmed during this (you still confirm once in the tool).

## ⚙ Advanced

Same flow, but opens a panel to review/override **name, description, content tags, release status and thumbnail** for that one outfit before uploading.

## Defaults

Configured once in the dedicated **Defaults** tab (saved in EditorPrefs) and applied by Express:

- **Avatar name template** & **Description template** — support `{outfit}` and `{avatar}` tokens.
- **Release status** — `private` or `public`.
- **Content warnings** — the five VRChat tags: Sexually Suggestive, Adult Language and Themes, Graphic Violence, Excessive Gore, Extreme Horror.
- **Auto-detect SPS/DPS** — if VRChat SPS (VRCFury Haptic Plug/Socket) or DPS markers that will actually upload are found, the **Sexually Suggestive** tag is added automatically. It ignores components under `EditorOnly` or inside other outfits. Turn off to use only your default tag set.
- **Thumbnail** — a fixed default image, or auto-capture from a scene camera against a solid background colour.
- **Auto-accept SDK fixes** — best-effort invoke of the SDK's build-alert auto-fixes.
- **Auto-confirm copyright dialog** — auto-clicks the SDK's ownership modal during creation.
- Plus the **Items defaults** and **Texture optimization** settings (see those pages).

## Notes

- Keep the **VRChat SDK Control Panel open and logged in** while using Express.
- If an auto-fix triggers a script recompile / domain reload, the upload **resumes automatically** when the editor settles.
- Express uploads on the **current platform**. For other platforms, run [[Uploading|Uploading]] → Upload All again afterward.
