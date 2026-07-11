# v3.1 — Update-proof settings, retry, validation & fixes

## ✨ New

### Settings now survive plugin updates (and travel with the project)
All per-avatar data — Blueprint IDs, batch & platform toggles, blendshape overrides, item selections, FaceEmo captures — moved from machine-wide EditorPrefs into **`ProjectSettings/ShiroOutfit_data.json`**. That folder is never touched when the plugin folder is deleted/replaced during an update, and the file can be backed up or versioned with the project. Existing settings are **migrated automatically** the first time they're read — nothing is lost. Two projects with the same avatar name no longer share settings.

### Retry failed uploads
After a batch, failed or skipped outfits show a **↻ Retry failed (n)** button that re-queues exactly those uploads (with a Dismiss option).

### Blueprint ID validation
The ID field validates the `avtr_<GUID>` format live (red field + warning), and the batch refuses to start with an invalid ID — protecting you from uploading over the wrong avatar because of a paste error.

### Version stamping mode
The Base Version field got a dropdown: **replaces description** (classic behavior) or **appends a `v…` line** while keeping your description text (previous stamp lines are updated, not stacked).

### Scene-view thumbnails with preview
The thumbnail source is now a clear three-way choice: **Standard view** (the auto-framed front shot, as before), **Scene view camera** (captures exactly what you see in the Scene view — arrange the view, done), or **Default image**. A **👁 Preview thumbnail** button shows the exact result in a window before anything is uploaded. Available in the defaults, the Advanced panel and the per-outfit Configure window.

### Dry run
A **Dry run** button next to the Batch Upload header checks everything the batch would check — SDK/login, Blueprint ID format, duplicate IDs and outfit names, platform toggles, contact hard cap, realtime lights, missing FaceEmo objects — and shows a per-outfit report. Nothing is uploaded.

### Upload log
Every batch and Express result (start, per-outfit OK/FAIL with the error message, cancel, summary) is appended to `ProjectSettings/ShiroOutfit_upload.log`. Unlike the Unity console this survives the domain reloads that platform switches trigger mid-batch. Rotates at ~1 MB.

### Fetch Blueprint IDs from VRChat
No more copy-pasting IDs: the **☁ IDs** button in the top bar fetches your avatar list from your VRChat account and auto-matches them to outfits by name (with a confirmation dialog). Each Blueprint ID field also got a **▾** picker listing all your avatars. Reached via reflection, so it degrades gracefully on SDK versions where the API differs.

### "Last upload" per outfit
Every configured outfit shows when it was last uploaded per platform ("Win: 3d ago · And: never") — so you instantly see what still needs a re-upload after changes.

### Thumbnail update for existing outfits
The new **Thumb** button per outfit captures a fresh thumbnail (using your thumbnail settings, with the preview window) and uploads ONLY the image — no rebuild.

### VRAM in the budget counters
Next to Contacts/Lights/Params each outfit now shows **▦ VRAM ~n MiB** with the official texture-memory rank colors (PC and Quest thresholds) — computed from everything that uploads with that outfit.

### Quest shader check in the dry run
Outfits with Android/iOS enabled are checked for materials without a `VRChat/Mobile` shader — the most common mobile-build failure, caught before the platform switch.

### Settings backup
**Export/Import settings** buttons (in New Outfit Defaults) bundle all per-avatar data + versions into one JSON — for backups or moving your whole setup to another project.

### Taskbar flash
When a batch finishes, the Unity taskbar icon flashes until you focus the window — handy for long multi-platform batches while you're AFK (Windows only).

## 🐛 Fixes

- **Auto-consent after a domain reload**: the Express flow's copyright auto-confirm setting is now loaded before resuming, so it works even when an SDK auto-fix triggered a script reload mid-upload.
- **Window close during a pending resume** no longer leaves dead update-callbacks behind (console spam / stuck batch).
- **Robust internal serialization**: the batch queue and the blendshape snapshot are now JSON — outfit names containing `|` or blendshape names containing `:` / `;` can no longer corrupt the queue or your overrides.
- Cancellation tokens are disposed properly, the resume handler can no longer be registered twice, and the main Blueprint ID lookup now also finds a `PipelineManager` that isn't on the avatar root itself.
- **DPS auto-detection** now matches "dps" only as its own word — object names like "HandPSprite" can no longer add the "Sexually Suggestive" tag by accident. The console logs which object triggered a detection.
