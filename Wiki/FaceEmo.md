# FaceEmo (per outfit)

[FaceEmo](https://suzuryg.github.io/face-emo/) (MIT) builds face-expression menus for VRChat avatars. It generates a single Modular-Avatar object named **`FaceEmoPrefab`** under the avatar and natively supports only **one** face config per avatar.

This tool makes FaceEmo **per outfit** with a *capture + tag-swap* approach — without touching FaceEmo's internals. Each outfit row has a **FaceEmo** foldout.

> Requires FaceEmo + Modular Avatar installed. Without them the foldout is harmless (nothing to capture).

## Workflow

1. **Open FaceEmo**, build this outfit's expressions, and click **Generate** in FaceEmo. This creates `FaceEmoPrefab` under your avatar.
2. Back in the outfit row, press **Capture**. The tool renames `FaceEmoPrefab` to `FaceEmo__<outfit>` and remembers it for that outfit.
3. Repeat per outfit — each FaceEmo **Generate** makes a fresh `FaceEmoPrefab` you then Capture for the next outfit.

## What happens on upload

On every activation (Select / Upload / Express / Batch):

- the active outfit's captured FaceEmo object → `Untagged` (uploaded), and
- all other outfits' FaceEmo objects → `EditorOnly` (stripped).

So each outfit uploads only its own face expressions, and Modular Avatar merges just that one.

## Buttons

- **Open FaceEmo** — opens FaceEmo (menu `FaceEmo/New Menu`).
- **Capture** — captures the current `FaceEmoPrefab` for this outfit (enabled only when one exists).
- **Clear** — unassigns FaceEmo from this outfit (the object stays in the scene).
- **Ping** — selects the assigned object.

## Notes

- If an **uncaptured `FaceEmoPrefab`** is left around, the foldout warns you — otherwise it would merge onto every outfit. Capture it for an outfit or delete it.
- The assigned object is found by name under the avatar; if you delete/rename it, the foldout shows a warning.
