# VRC Outfit Batch Uploader

**v3.3** · Unity 2022.x · VRChat Avatars SDK · MIT

A Unity Editor tool for VRChat avatar creators who keep many outfits under a single avatar and upload each one as its own avatar. It handles the whole multi-outfit workflow: setting up brand-new outfits, per-outfit accessories and face expressions, performance budgets, and uploading a mix of new and existing outfits in one guided pass.

Open it via **Tools → Shiro → Outfit Batch Uploader**.

## Start here

- **[[Installation]]** — drop it in, what's required, optional integrations
- **[[Getting Started|Getting-Started]]** — the core concept and your first upload
- **[[Uploading]]** — Select / Upload / Upload All, batching, cross-platform

## Features

- **[[Interface|User-Interface]]** — focused tabs, compact outfit cards, expandable details, fixed batch footer
- **[[New Outfit Setup|New-Outfit-Setup]]** — one-click Express / Advanced creation of new outfits
- **[[Items (accessories)|Items]]** — per-outfit accessory selection
- **[[FaceEmo (per outfit)|FaceEmo]]** — per-outfit face-expression menus
- **[[Texture / VRAM optimization|VRAM-Optimization]]** — compress + cap outfit and optionally selected-item textures
- **[[Budget counters|Budget-Counters]]** — Contacts, Lights, Parameters per outfit
- **[[Blendshapes]]** — per-outfit blendshape overrides

## What changed in v3.3

v3.3 separates the window into **Outfits**, **New Outfit**, and **Defaults**, making large avatar projects much easier to scan. It also adds a broad backend reliability pass: deterministic blendshape switching, safer avatar changes, more accurate outfit/item budgets, atomic settings writes with backup recovery, stronger batch/domain-reload cleanup, and safer Express thumbnail/resume handling.

## Help

- **[[Troubleshooting]]** — common issues and fixes

## How it works (in one paragraph)

Your outfits live as GameObjects under an **Outfits** parent. Activating an outfit sets it to `Untagged` (uploaded) and every other outfit to `EditorOnly` (stripped at build). The same tag trick drives per-outfit **Items** and **FaceEmo**. Each outfit stores its own VRChat **Blueprint ID** (`avtr_...`), so one Unity project uploads many separate avatars.

> The tool references no third-party types directly — it compiles and runs on its own, and integrations (VRCFury, FaceEmo, Modular Avatar) light up only when those packages are installed. See [[Installation]].
