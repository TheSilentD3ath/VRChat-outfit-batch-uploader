# Uploading

## Per-outfit buttons

The compact card header keeps **Select**, **Ping**, **Upload**, batch inclusion, and the platform summary visible. Expand the card to edit the Blueprint ID and platform targets or access VRAM, thumbnail, blendshape, item, and FaceEmo tools.

- **Select** — activates a single outfit in the scene (sets tags, Blueprint ID, blendshapes, items, FaceEmo) **without** uploading. Great for previewing what would be uploaded. The avatar's Blueprint ID always follows the active entry: for an outfit or variant that has none yet, it is cleared, so an upload from the VRChat SDK window cannot overwrite another outfit's avatar. If the cleared ID is not used by any outfit in the tool, it is written to the Console, and **Ctrl+Z** undoes the Select.
- **Upload** — activates + uploads that single outfit (only shown once it has a Blueprint ID).
- **⚡ Express / ⚙ Advanced** — shown when the outfit has no Blueprint ID yet. See [[New Outfit Setup|New-Outfit-Setup]].

## Upload All (guided batch)

Tick **Include in batch** on the outfits you want, then press **Upload All**. Use the **All / None** buttons at the top of the outfit list to toggle every outfit at once.

If some selected outfits aren't set up yet (no Blueprint ID), the tool first walks you through them:

1. A prompt asks: **Express all** / **Ask me per outfit** / **Cancel**.
2. If you chose per outfit, each unconfigured outfit asks: **Express setup** / **Skip** / **Configure…** (a popup with name, description, content tags and thumbnail).
3. Each set-up outfit is created & uploaded on the **current platform**.

Then the already-configured outfits run through the normal platform-grouped batch.

> New outfits are uploaded on the current platform during setup. To also build them for other platforms (Android/iOS), run **Upload All** again — they now have IDs and will be included.

The status line shows e.g. `12 selected — 9 ready, 3 need setup`.

## Cross-platform batching

For each outfit you can tick **Win / And / iOS**. The batch groups outfits by platform and switches the Unity build target as few times as possible (Windows → Android → iOS), starting with whatever platform you're already on.

Progress is shown with a platform-tinted bar (Blue = Windows, Green = Android, Silver = iOS) and per-step sub-progress.

## Domain Reload Survival

Switching the Unity build target forces a **domain reload** (scripts recompile, running code is wiped). The batch queue is stored in `SessionState`, so it **pauses and automatically resumes** after each platform switch / recompile. It also waits for the VRChat SDK builder to re-initialise and for you to be logged in before resuming.

## Safety & convenience

- **Ownership confirmation** is asked once up front (not per outfit mid-batch).
- The SDK's **copyright/ownership dialog** for new-avatar uploads is auto-confirmed (best-effort; you still confirm once in the tool).
- **Login & platform checks** run before building to avoid mid-batch failures.
- A **sound** plays when the whole queue is done (toggle: "🔔 Sound when done").
- **Validation errors** on one outfit are logged and skipped; the summary at the end lists what was skipped so you can fix and re-upload those.
- Blendshape values are **restored** to their original state after the batch.
- If an outfit disappears after the queue is created, it is recorded as failed rather than counted as a successful upload.
- Queue and resume state are cleaned up after completion or cancellation.

## Upload log and timing

Every upload is recorded in `ProjectSettings/ShiroOutfit_upload.log`. The **Log** button next to **Dry run** opens it.

Besides the result (`OK` / `FAIL`), each batch upload writes a `TIME` line showing where its time went:

| Phase | What it covers |
| --- | --- |
| `activate` | switching outfit, items, blendshapes and FaceEmo |
| `save` | saving the scene and assets before the build |
| `fetch` | reading the avatar record from VRChat |
| `build` | the SDK processing the avatar and packing its file |
| `upload` | the SDK sending the file |
| `sdk other` | the SDK's checks before the build and its refresh afterwards |
| `tool wait` | this tool's own fixed pauses |

`file` is the size of the uploaded avatar file. A platform switch gets its own `TIME` line. When the batch ends, a summary with the average and share of each phase is written to the log and the Unity Console, and one line of it appears in the status box.

`build` and `upload` come from events the VRChat SDK sends. If the SDK does not send one, the phase is shown as `?` and its time is counted under `sdk other`. Express uploads of new avatars are not measured.

## Blueprint IDs from your account

Instead of pasting an `avtr_…` ID you can take it from your VRChat account:

- **▾** next to an outfit's Blueprint ID field lists your avatars; picking one assigns its ID to that outfit.
- **☁ IDs** in the top bar fetches your avatars and offers to assign IDs to all outfits that have none yet, matched by name (the outfit's name, or the name your Express template would give it). Nothing is assigned before you confirm the list.

Only avatars that belong to the logged-in account are ever offered.
