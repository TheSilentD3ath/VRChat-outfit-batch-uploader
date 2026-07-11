# Budget counters (per outfit)

Each outfit row shows a live one-line budget for what actually uploads with that outfit — the outfit's own components + its included [[Items]] + shared/body components, **ignoring** `EditorOnly` objects and other outfits.

```
◆ Contacts 14 (Good) · 22/256    ☀ Lights 0    ⚙ Params 86/256
```

Everything is colour-coded green / yellow / red and recomputed about twice a second (one scene scan, bucketed per outfit, so it stays fast even with many outfits).

## ◆ Contacts

Counts VRChat Contacts (`VRCContactSender` / `VRCContactReceiver`).

- The **networked** count (contacts that aren't local-only) sets the **performance rank**:
  - **PC**: Excellent ≤8, Good ≤16, Medium ≤24, Poor ≤32, Very Poor above.
  - **Quest**: Excellent ≤2, Good ≤4, Medium ≤8, Poor ≤16, Very Poor above.
- Local-only contacts (e.g. many SPS senders) **don't** count toward the rank.
- Also shows `total / 256` — the hard cap. Above 256, VRChat **disables** contacts (shown in red with a warning).

## ☀ Lights

Counts realtime `Light` components. Avatars should have **0** (PC: 1 = Poor, 2+ = Very Poor; Quest: any light = Very Poor). Green at 0.

## ⚙ Params

Expression-parameter memory (avatar-wide), `cost / 256`.

- If the avatar uses **VRCFury**, it's shown greyed as **(VRCFury)** — VRCFury's parameter compressor handles the 256-bit limit at build time, so the editor cost isn't the final synced cost.
- Without VRCFury: green / yellow / red against the 256-bit limit.

## Notes

- The counter reflects the **currently active** outfit's selection. Press **Select** on an outfit to update the numbers for it.
- Thresholds and the 256 caps come straight from the VRChat SDK's own performance data.
