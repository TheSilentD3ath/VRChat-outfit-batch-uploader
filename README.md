# VRC Outfit Batch Uploader

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

## Requirements

- Unity 2022.x (tested on 2022.3.x)
- VRChat Avatar SDK (`com.vrchat.avatars`) installed via the VRChat Creator Companion

## Installation

1. Copy the `ShiroTools` folder into your project's `Assets` folder
2. Unity will compile the script automatically
3. Open the window via **Tools → Shiro → Outfit Batch Uploader**

The tool works in any VRChat avatar project that has the Avatar SDK installed.

## Setup

1. **Avatar root** — drag your avatar's root GameObject into the field (auto-detected if only one avatar is in the scene)
2. **Avatar skin** — the SkinnedMeshRenderer with blendshapes (auto-detected)
3. **Outfits parent** — name of the GameObject that contains all outfit prefabs as direct children (default: `Outfits`)
4. For each outfit, paste its **Blueprint ID** (`avtr_...`) — this is saved per-machine in EditorPrefs
5. **Base Version** — (Optional) Enter a version number (e.g., `v1.2`) to automatically append it to the description of all uploaded outfits.
6. Use **"Capture current skin values"** inside each outfit's blendshape foldout to save the current skin state as that outfit's overrides

## Usage

- **Select** — activates a single outfit (sets tags, pipeline ID, blendshapes) without uploading
- **Upload** — activates + uploads a single outfit
- **Batch Upload All** — uploads every outfit that has a Blueprint ID and "Include in batch" checked, in order

## License

MIT — see [LICENSE](LICENSE)
