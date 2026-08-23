# Uploading

## Per-outfit buttons

The compact card header keeps **Select**, **Ping**, **Upload**, batch inclusion, and the platform summary visible. Expand the card to edit the Blueprint ID and platform targets or access VRAM, thumbnail, blendshape, item, and FaceEmo tools.

- **Select** — activates a single outfit in the scene (sets tags, Blueprint ID, blendshapes, items, FaceEmo) **without** uploading. Great for previewing what would be uploaded.
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
