# Troubleshooting

### "VRC SDK builder not available" / "Not logged in"
Open the **VRChat SDK Control Panel** (VRChat SDK → Show Control Panel) and **log in**. Keep it open while uploading and during Express setup.

### The SDK copyright/ownership dialog still pops up
Auto-confirm is best-effort and needs the **SDK Control Panel open**. If it can't click the dialog, just click **OK** yourself — the upload continues. (For brand-new avatars VRChat reserves the ID mid-upload, so this dialog can't be pre-agreed like the batch one.)

### Express / "accept SDK fixes" didn't apply the fixes
This step uses SDK UI internals and is best-effort. If it can't act (different SDK version), apply the suggested fixes manually in the SDK panel "Review Any Alerts" section, then upload. Nothing breaks if it's skipped.

### A new outfit didn't get its other-platform builds
Express uploads on the **current platform** only. Run **Upload All** again — the outfit now has a Blueprint ID and will be included for its other ticked platforms.

### My facial expressions are wrong / doubled
You likely have an **uncaptured `FaceEmoPrefab`** (it merges onto every outfit), or two outfits' FaceEmo objects are both `Untagged`. Open the outfit's [[FaceEmo]] foldout — it warns about a stray prefab. Capture it for the right outfit or delete it, then **Select** the outfit to re-apply tags.

### The Parameters counter looks too high
If the avatar uses **VRCFury**, the shown value is **pre-compression** (marked "(VRCFury)"); VRCFury shrinks synced parameters at build, so the real synced cost is lower. See [[Budget counters|Budget-Counters]].

### Contacts count seems off vs. the SDK
The counter shows **networked** contacts for the rank (local-only contacts, e.g. SPS senders, are excluded) and `total/256` for the hard cap. It reflects the **currently active** outfit — press **Select** to refresh for a specific outfit.

### Texture optimization changed a texture I didn't want
Import-setting changes are **not undo-able** and are **per-asset** (shared textures affect every outfit). Re-set the texture's import settings manually if needed. Turn off "Optimize during Express" if you don't want it automatic.

### A batch stopped after switching platform
Platform switches cause a domain reload; the queue resumes automatically once Unity finishes compiling and the SDK builder is ready. If it seems stuck, make sure you're **logged in** — it waits for login before resuming.

### One outfit was skipped during batch
Validation errors (rig/humanoid/bones, etc.) on a single outfit are logged and skipped so the rest continue. The end-of-batch summary lists skipped outfits — fix those and upload them separately.

### Settings disappeared
Blueprint IDs, defaults and per-outfit selections are stored in **EditorPrefs** (per machine/per Unity). Switching machines or clearing EditorPrefs loses them; the avatars on VRChat are unaffected. Re-paste IDs or re-capture as needed.
