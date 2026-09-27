# Getting Started

## The core concept

You have **one avatar** with **many outfits**, and you want each outfit to be its own uploaded VRChat avatar.

Put your outfits as GameObjects under a parent named **`Outfits`** (configurable). The tool, for any given outfit, sets that outfit to the `Untagged` tag (so it's included in the build) and every *other* outfit to `EditorOnly` (so VRChat strips it at build). Each outfit remembers its own **Blueprint ID** (`avtr_...`), which is what makes it a separate avatar on VRChat.

```
Avatar (VRCAvatarDescriptor + PipelineManager)
├── Body (SkinnedMeshRenderer with blendshapes)
├── Outfits
│   ├── Outfit_A      ← Untagged when active  → uploaded
│   ├── Outfit_B      ← EditorOnly            → stripped
│   └── Outfit_C      ← EditorOnly            → stripped
└── Items             ← optional accessories (see [[Items]])
```

## First launch

The first time you open the tool on a computer — and only if you've never used it before — a short guide appears in the window. It takes you through four steps: choose your avatar, add the Outfits and Items objects (it can create both for you), add your first outfit, and connect it to VRChat. Each step ticks itself off as you do it. You can skip it at any time; the **?** at the top right brings it back.

After the guide, a few one-time tips appear next to Items, Batch Upload and the VRAM counter. If you have used the tool before, you won't see any of this.

## The top bar

1. **Avatar root** — drag your avatar's root GameObject here (auto-detected if there's only one avatar in the scene).
2. **Avatar skin** — the `SkinnedMeshRenderer` that has the blendshapes (auto-detected). Used for [[Blendshapes]] overrides.
3. **Outfits parent** — the name of the GameObject holding your outfits as direct children (default `Outfits`).
4. **Base Version** — *(optional)* a version string (e.g. `v1.2`) that gets stamped into the VRChat description of every uploaded outfit.

## The three workspaces

- **Outfits** contains every detected outfit in a compact list. Expand a card to edit its Blueprint ID, platforms, blendshapes, items, FaceEmo, thumbnail, or VRAM settings.
- **New Outfit** filters the list to outfits that still need their first Blueprint ID.
- **Defaults** contains the shared Express Setup, thumbnail, optimization, item, and backup settings.

The batch upload controls stay below the scrolling outfit list so they remain accessible even on avatars with many outfits. See [[Interface|User-Interface]].

## Your first upload

**Already-uploaded outfit (you have its `avtr_...` ID):**
1. Paste the Blueprint ID into the outfit's field.
2. Press **Upload** on that row (or tick **Include in batch** and use **Upload All**).

**Brand-new outfit (no ID yet):**
1. Just press **⚡ Express setup** on the outfit row — the tool creates a new avatar, uploads it, and fills in the new ID for you. See [[New Outfit Setup|New-Outfit-Setup]].

That's it. From there, explore per-outfit [[Items]], [[FaceEmo]], [[Budget counters|Budget-Counters]], and [[Texture / VRAM optimization|VRAM-Optimization]].

## Where settings live

- **Blueprint IDs & per-outfit selections** → `ProjectSettings/ShiroOutfit_data.json`, scoped by avatar + outfit name.
- **Defaults** (templates, tags, thumbnail, optimization) → EditorPrefs.
- **Base Version** → `ProjectSettings/ShiroOutfit_versions.json`.
- **Tags / active state** → your scene.
