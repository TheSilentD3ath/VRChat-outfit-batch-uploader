# Items (accessories)

Keep accessory objects (props, weapons, jewelry, …) under a second configurable parent (default **Items**) and choose, **per outfit**, which of them upload with that outfit.

```
Avatar
├── Outfits
│   ├── Outfit_A
│   └── Outfit_B
└── Items
    ├── Sword
    ├── Glasses
    └── Tail
```

## How it works

Every outfit row has an **Items** foldout listing each child of the Items parent with an "include with this outfit" checkbox (saved per outfit). On activation (Select / Upload / Express / Batch) the active outfit's selection is applied:

- **included** items → tag `Untagged` (uploaded with this outfit)
- **excluded** items → tag `EditorOnly` (stripped at build)

So Outfit A can ship the Sword + Tail while Outfit B ships only the Tail.

## Per-outfit controls

- **Search** box to filter long item lists.
- **All / None** apply to the *currently filtered* items.
- **Ping** selects the item in the hierarchy.
- The list is scrollable (height-capped) so it never overflows the window.

## Defaults ("included on every outfit")

In **Defaults → Items (accessories)** you set:

- the **Items parent** name, and
- a per-item **"included on every outfit by default"** toggle.

Outfits you haven't set per-item yet inherit these defaults; toggling an item on an outfit overrides the default for that outfit.

## Variants: one outfit, several item sets

Want the same outfit on VRChat more than once — say *Black Dress* plain and *Black Dress* with a bag? Make a **variant** instead of duplicating the outfit object.

1. Open the outfit's details and type a name next to **New variant**, e.g. `With Bag`.
2. Press **Add Variant**. It appears indented under the outfit as `↳ With Bag`, with the outfit's current item selection already ticked.
3. Change its items, then upload it as a new avatar with **Express**. On VRChat it is named `Black Dress – With Bag`.

Each variant has its **own** name, item selection, Blueprint ID, batch tick and upload history. It **shares** blendshapes, FaceEmo and build platforms with its outfit: change those on the outfit and every variant follows.

**Remove Variant** in the variant's details forgets it in this tool. The avatar on VRChat is not deleted.

Variants are attached to the outfit's **name**. If you rename the outfit object, its variants are hidden until you rename it back; the dry run tells you when that happens.

## Notes

- Item inclusion is stored per avatar **and** per outfit in `ProjectSettings/ShiroOutfit_data.json`; legacy EditorPrefs values are migrated automatically when first read.
- Items count toward an outfit's [[Budget counters|Budget-Counters]] (contacts/lights) only when included for that outfit.
- The optional [[Texture / VRAM optimizer|VRAM-Optimization]] can include the textures of the items selected for the current outfit. This setting is disabled by default because texture-import changes affect the shared texture asset globally.
