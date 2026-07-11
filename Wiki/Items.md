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

In **New Outfit Defaults → Items (accessories)** you set:

- the **Items parent** name, and
- a per-item **"included on every outfit by default"** toggle.

Outfits you haven't set per-item yet inherit these defaults; toggling an item on an outfit overrides the default for that outfit.

## Notes

- Item inclusion is stored per avatar **and** per outfit (EditorPrefs).
- Items count toward an outfit's [[Budget counters|Budget-Counters]] (contacts/lights) only when included for that outfit.
