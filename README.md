# Aska Performance Booster

A BepInEx 6 IL2CPP plugin that squeezes extra GPU performance out of Aska by disabling **expensive HDRP rendering features that the game's settings menu does not expose**. The philosophy is "read before write, only improve."

## Headline Features

- **Removes Aska's 60 FPS cap** -- Aska hard-codes `Application.targetFrameRate = 60`, capping all VSync-off users to 60 FPS regardless of GPU headroom. This mod removes that cap so your GPU can run at its full potential. VSync users are unaffected (Unity ignores `targetFrameRate` when VSync is on). A Harmony patch prevents the game from re-applying the cap.
- **Disables shadow casting on ~1000 small objects per scene** -- Scene analysis found 992 shadow-casting renderers with bounds < 1m (small rocks, plants, debris). Each one costs a draw call in the shadow depth pass but produces barely visible shadows. Disabling them saves 3-8% GPU time.
- **Disables expensive HDRP screen-space effects** -- SSR, SSAO, volumetric fog, contact shadows, subsurface scattering, and cosmetic post-processing that the game's settings menu does not expose.

## Realistic Expectations

This mod gives you **~5-15 extra FPS for free** by removing the frame rate cap, disabling small shadow casters, disabling screen-space effects, and reducing shadow overhead that the game's own settings menu cannot reach.

**Why ~5-15 FPS and not more:** Aska is primarily **geometry and lighting bound** at High quality. The base deferred rendering pass (G-buffer, lighting, shadows) is the majority of GPU time -- typically 80-90% of each frame. This mod removes the game's 60 FPS hard cap (immediate win for anyone GPU-bound below their monitor's refresh rate), disables ~1000 unnecessary small shadow casters (3-8% GPU savings), and disables **screen-space effects** (SSR, SSAO, volumetric fog, contact shadows, subsurface scattering, post-processing) which account for 10-20% of total GPU frame time.

**Where this mod shines:**
- Uncap frame rate beyond 60 FPS for high-refresh-rate monitors
- Push from just-under-60 to a stable 60 FPS
- Push from 50 to 55, or 45 to 50 -- every frame counts in a survival game
- Disable cosmetic post-processing effects (film grain, chromatic aberration, motion blur) that have no in-game toggle
- Complement the game's own quality settings and DLSS/FSR for maximum performance

**For the best results, combine all three levers:**
1. **In-game quality** -- dropping from Ultra to High changes things no mod can access (mesh LOD tiers, terrain detail, shader complexity)
2. **DLSS or FSR** -- Aska supports hardware dynamic resolution; enable it in the game's graphics menu
3. **This mod's Moderate preset** (default) -- handles the hidden rendering wins the game menu misses

If you need 60 FPS and are currently below it on Ultra, the most effective approach is: **Moderate preset + High quality in-game + DLSS Balanced**.

## Requirements

- [BepInEx 6 IL2CPP (BE #755 or newer)](https://builds.bepinex.dev/projects/bepinex_be)
  - **Easiest method:** Install [r2modman](https://thunderstore.io/package/ebkr/r2modman/), select Aska, and install **BepInExPack_IL2CPP** from the mod list.

## Installation

1. Install BepInEx 6 IL2CPP (see above)
2. Download the zip from the Files tab
3. Extract into your Aska game folder
4. Launch the game

## Quick Start

The mod works immediately with the **Moderate** preset (default). No configuration needed.

### First Time Setup (Recommended)

1. Set `DiagnosticScan = true` in the config file
2. Launch the game, enter gameplay, then exit
3. Check `BepInEx/LogOutput.log` for "DIAGNOSTIC SCAN" -- this shows all of Aska's current rendering settings
4. Set `DiagnosticScan = false` and relaunch to apply optimizations
5. Optionally set `DebugLogging = true` to see exactly what the mod changes vs. what it skips

## Presets

There are three presets. The default (Moderate) is the only optimization preset -- it gives you the full set of safe optimizations. Vanilla exists to disable the mod without uninstalling. Custom exists for manual tweaking.

| Preset | What It Does | When to Use |
|--------|-------------|-------------|
| **Vanilla** | Nothing -- stock game | Disable the mod without uninstalling |
| **Moderate** (default) | Disables expensive screen-space effects, reduces shadows, removes cosmetic post-processing | Always -- this is the recommended preset |
| **Custom** | Uses your manually edited config values | Advanced users who want per-setting control |

To change preset, edit `BepInEx/config/com.community.askaperformancebooster.cfg`:

```ini
[0. Preset]
Preset = Moderate
```

## What the Moderate Preset Does

Aska uses **HDRP** (High Definition Render Pipeline), and this mod targets HDRP-specific optimization paths:

- **Frame rate uncap** -- removes Aska's hard-coded 60 FPS cap for VSync-off users
- **Small shadow caster removal** -- disables shadow casting on ~1000 small objects (rocks, plants, debris) per scene
- **HDRP Pipeline Support Flags** -- disables expensive rendering features (SSAO, volumetrics, decals, etc.) at the pipeline level. Confirmed 4-5+ FPS improvement.
- **HDShadowSettings Volume** -- reduces shadow distance and cascade count
- **Post-processing Volume disables** -- removes cosmetic effects (film grain, chromatic aberration, motion blur, etc.)
- **QualitySettings** -- LOD, textures, async upload (engine-global, HDRP respects)
- **SRP Batcher verification**

### Pipeline & Draw Call Optimizations

| Optimization | What It Does | Visual Impact |
|-------------|-------------|---------------|
| **60 FPS cap removal** | Removes Aska's hard-coded `targetFrameRate = 60` | None (VSync users unaffected) |
| **SRP Batcher verification** | Ensures SRP Batcher is enabled | None |
| **Async Upload Increase** | Faster texture loading (4ms/frame, 32MB buffer) | None |
| **Input Lag Reduction** | Cap GPU frame queue to 2 | None |

### HDRP Pipeline Support Flags (biggest GPU savings)

Pipeline support flags are the primary optimization mechanism. They set `supportSSAO=false`, `supportVolumetrics=false`, etc. on the HDRP Asset, which HDRP reads every frame during Frame Settings aggregation. This is the **only** approach confirmed to produce measurable FPS gains in Aska's IL2CPP build.

| Feature Disabled | What It Was | GPU Savings |
|-----------------|------------|-------------|
| **SSAO** (Ambient Occlusion) | Subtle shadow in crevices | Significant |
| **Volumetric Fog** | 3D voxel atmospheric scattering | Major |
| **Volumetric Clouds** | Volumetric cloud rendering | Moderate |
| **Subsurface Scattering** | Skin/foliage translucency | Moderate |
| **Decals** | DBuffer decal rendering | Minor |
| **Screen-Space Lens Flare** | Lens flare effects | Minor |
| **Data-Driven Lens Flare** | Lens flare effects | Minor |

Note: SSR, Distortion, and Transparent SSR are intentionally NOT disabled at the pipeline level because doing so causes a stale-texture artifact (the screen image gets "stamped" onto reflective objects).

### Shadow Reduction

| Setting | Stock Value | Moderate Value |
|---------|------------|----------------|
| **Shadow distance** (HDShadowSettings Volume) | 500m | 60m |
| **Shadow cascades** (HDShadowSettings Volume) | 4 | 2 |
| **Micro shadows** | On | Off |
| **Small shadow casters** | ~992 casting | Shadows disabled (< 1m bounds) |

### Post-Processing Removed

| Effect | Why It Is Safe to Remove |
|--------|-------------------------|
| **Film Grain** | Cosmetic noise most players dislike |
| **Chromatic Aberration** | Aska already exposes this in its menu |
| **Lens Distortion** | Very few players notice |
| **Motion Blur** | Many players disable voluntarily |
| **Depth of Field** | Cosmetic blur effect |
| **Vignette** | Most players don't notice |
| **Panini Projection** | Rarely used in gameplay |
| **Data-Driven Lens Flare** | Cosmetic, GPU cost per flare |
| **Screen-Space Lens Flare** | Full-screen pass for cosmetic effect |
| **Bloom** | Kept, but iterations reduced (8 to 3) |

### What We Do NOT Touch (and why)

| Setting | Why We Leave It Alone |
|---------|----------------------|
| **DLSS/FSR/Dynamic Resolution** | Aska already has DRS enabled (hardware mode, 33-100%). We never interfere with upscaling. |
| **TAA settings** | HDRP's anti-aliasing is integral to the pipeline |
| **In-game quality preset** | Mesh LOD tiers, texture resolution levels, HDRP quality level, terrain detail -- these are controlled by the game's Ultra/High/Medium setting and cannot be changed by a BepInEx mod |
| **Feature support flags** | Cannot enable features at runtime in HDRP |
| **Terrain settings** | Game tunes these for its world |
| **Per-layer culling** | Don't know Aska's layer assignments |
| **Motion vectors** | Needed for TAA and DLSS temporal accumulation |

## High + Mod vs Low: Why This Mod Beats Dropping Quality

A common question: "Why not just set in-game graphics to Low instead of running High with this mod?"

The short answer: **High + this mod keeps the things you actually see (detailed meshes, sharp textures, long draw distances) and removes the things you don't consciously notice (screen-space effects, post-processing passes).** Low in-game degrades everything indiscriminately.

### What each approach changes

| Rendering Aspect | In-Game Low | High + Moderate Preset |
|-----------------|-------------|----------------------|
| **Mesh detail / polygons** | Reduced (lower LOD tiers) | Full High quality |
| **Texture resolution** | Reduced | Full High quality |
| **Draw distance (objects)** | Shorter | Full High distance |
| **Terrain detail / grass** | Sparse | Full High density |
| **Shader quality variants** | Simplified shaders | Full High shaders |
| **Shadow distance** | Short | 60m (vs stock 500m) |
| **Shadow quality** | Low resolution | Reduced atlas, 2 cascades |
| **Screen-space reflections** | Possibly reduced | Disabled |
| **Ambient occlusion (SSAO)** | Possibly reduced | Disabled |
| **Volumetric fog** | Possibly reduced | Disabled |
| **Subsurface scattering** | Possibly reduced | Disabled |
| **Post-processing effects** | Still present | Cosmetic effects removed |
| **DLSS / TAA compatibility** | Works | Works |

### Why High + Mod looks better

- **Objects look detailed up close** -- High-quality meshes and textures are preserved. Low uses coarser geometry and blurrier textures that are visible at any distance.
- **The world renders at full range** -- Trees, buildings, and terrain detail stay visible far into the distance. Low pulls the draw distance in, making the world feel empty.
- **Materials and shaders are high quality** -- Surface detail, normal maps, and material complexity stay at High tier. Low switches to simplified shader variants.
- **What is missing are "screen effects"** -- Reflections, ambient occlusion, volumetric fog, and subsurface scattering are screen-space passes computed after the base scene is rendered. Their absence makes the scene look slightly flatter or clearer, but the underlying detail is still there.

### Recommended approach

For the best visual quality per frame of GPU work:

1. **Set in-game quality to High** (keeps mesh, texture, terrain, and shader quality)
2. **Use Moderate preset** (default -- disables hidden GPU-heavy features the game menu does not expose)
3. **Enable DLSS Balanced** (handles raw pixel count reduction)

## Diagnostic Scan Mode

Set `DiagnosticScan = true` in the config to log all current rendering settings without changing anything. The output includes:

- All QualitySettings values (shadows, LOD, textures, etc.)
- HDRP Asset properties and pipeline support flags
- All Volume components and their active state (with IL2CPP type names)
- Camera settings
- System info (GPU, VRAM, capabilities)

Check `BepInEx/LogOutput.log` after entering gameplay.

## Configuration Reference

After first launch, the config file is generated at:
```
BepInEx/config/com.community.askaperformancebooster.cfg
```

Every setting has an inline description. Settings with value `0`, `-1`, or `false` mean "don't override -- respect the game's value."

### Config Sections (13 categories)

1. **Draw Calls** -- SRP Batcher
2. **Shadows** -- Distance, cascades (via HDShadowSettings Volume), contact shadows, micro shadows, screen-space shadows, small shadow caster culling
3. **Textures** -- Mipmap streaming, anisotropic filtering, memory budget
4. **Post-Processing** -- Film grain, chromatic aberration, bloom
5. **Culling** -- Shadow distance QualitySettings override
6. **LOD** -- Bias, max level, skin weights
7. **HDRP Pipeline** -- Pipeline support flags (SSAO, volumetrics, decals, etc.), Volume component disables
8. **Lighting** -- Reflection probes
9. **Async Upload** -- Time slice, buffer size
10. **Misc** -- VSync, frame rate (uncaps Aska's 60 FPS limit)
11. **Advanced GPU** -- Shader warmup, max queued frames
12. **Post-Processing Extra** -- Depth of field, vignette, panini projection, lens flares
13. **Mod Compatibility** -- Master toggles, reapply interval, respect external changes

## Compatibility with Other Mods

This mod is designed to coexist with other BepInEx mods. It uses soft dependencies to load after known Aska mods, and includes several mechanisms to avoid conflicts.

### What This Mod Touches (Global State)

| Category | Settings Modified | Conflict Risk |
|----------|------------------|--------------|
| **Draw Calls** | SRP Batcher | Low -- only enables, never disables |
| **Shadows** | Shadow distance, cascades (Volume), small casters | Medium -- other mods could want different values |
| **Textures** | Mipmap streaming, anisotropic filtering | Low -- only enables or reduces |
| **Post-Processing** | Film grain, bloom, vignette, etc. | High -- visual mods may want these ON |
| **HDRP Pipeline** | Pipeline support flags (SSAO, volumetrics, etc.) | Medium -- disables rendering features |
| **Lighting** | Reflection probes | Medium |

### How to Avoid Conflicts

**With visual enhancement mods** (ReShade, post-processing mods):
- Set `EnablePostProcessingOptimizations = false` in the Mod Compatibility section of the config

**With shadow/lighting mods:**
- Set `EnableShadowOptimizations = false` and/or `EnableLightingOptimizations = false`

**With HDRP-specific mods:**
- Set `EnableHDRPPipelineOptimizations = false` for pipeline support flag changes

**With camera mods (e.g., AskaFirstPerson):**
- No conflict. This mod never touches cameras, player transforms, renderers, or input.

### Compatibility Config Options (Section 13)

| Setting | Default | What It Does |
|---------|---------|-------------|
| `ReapplyIntervalSeconds` | 10 | How often to re-apply settings (0 = apply once only) |
| `ReapplyPostProcessing` | false | Whether to re-apply Volume changes on the timer |
| `RespectExternalChanges` | true | Skip settings another mod changed since our last write |
| `EnableDrawCallOptimizations` | true | Master toggle for SRP Batcher |
| `EnableShadowOptimizations` | true | Master toggle for all shadow settings |
| `EnableTextureOptimizations` | true | Master toggle for texture streaming, filtering |
| `EnablePostProcessingOptimizations` | true | Master toggle for Volume effects, lens flares |
| `EnableLODOptimizations` | true | Master toggle for LOD bias, skin weights |
| `EnableHDRPPipelineOptimizations` | true | Master toggle for pipeline support flags, Volume disables |
| `EnableLightingOptimizations` | true | Master toggle for probes |
| `EnableAdvancedGPUOptimizations` | true | Master toggle for shader warmup |

### Load Order

This mod uses `[BepInDependency(..., SoftDependency)]` for known Aska mods, ensuring it loads AFTER them. This means:
- Other mods' changes are already applied when we run
- Our read-before-write logic sees their values, not game defaults
- If they set a better value, we respect it

### Interaction with Aska's In-Game Settings

This mod detects when you change graphics settings in Aska's options menu and reacts appropriately:

**When you change the quality preset:**
- A Harmony patch on `QualitySettings.SetQualityLevel` detects the change instantly
- The mod invalidates its HDRP Asset cache (quality level changes can swap the entire pipeline asset)
- All optimizations are reapplied on the very next frame against the new quality level's baseline

**When you change an individual setting:**
- If the game changes a value to be MORE expensive, we reapply our optimization on the next cycle
- If the game changes a value to be LESS expensive, we treat this as intentional and respect it
- Post-processing overrides are only applied once on scene entry by default

## Sunshine / Moonlight (Remote Play / Steam Deck)

If you use [Sunshine](https://github.com/LizardByte/Sunshine) and [Moonlight](https://moonlight-stream.org/) to stream, copy your r2modman profile to the game folder:

```bash
# Git Bash / MSYS2
cp -r "$APPDATA/r2modmanPlus-local/ASKA/profiles/Default/"* \
  "C:/Program Files (x86)/Steam/steamapps/common/ASKA/"
```

Then launch Aska normally from Steam. Re-run the copy after updating mods.

## Troubleshooting

- **Mod not loading:** Make sure you have BepInEx **6** IL2CPP (not BepInEx 5).
- **Settings not applying:** Enable `DebugLogging = true` and check the log. The mod shows what it changed vs. what it skipped (and why).
- **Want to see what Aska has before the mod touches it:** Set `DiagnosticScan = true`, enter gameplay, check the log.
- **Want stock visuals:** Set preset to `Vanilla` or delete the config file.
- **Changed in-game settings but mod overrides them:** This is by design. Your in-game quality preset sets the baseline; the mod improves from there.
- **Config file changes not taking effect:** BepInEx reads config at plugin load time. You must restart the game.
- **Another mod's settings keep getting overwritten:** Enable `DebugLogging = true` and look for "External check" messages. Disable the overlapping category with the master toggles.
- **Visual mod's effects keep disappearing:** Set `EnablePostProcessingOptimizations = false`.
- **Screen image "stamped" onto objects / reflection artifacts:** This happens if `PipelineDisableSSR` or `PipelineDisableDistortion` is set to `true` in the config. These pipeline-level flags stop the SSR/distortion render pass but leave the texture with stale screen data that shaders still sample. The Moderate preset does NOT enable these flags. If you manually enabled them, set `PipelineDisableSSR = false` and `PipelineDisableDistortion = false` in the config to fix the artifact.

## Uninstallation

Delete `AskaPerformanceBooster.dll` from `BepInEx/plugins/` and optionally delete `BepInEx/config/com.community.askaperformancebooster.cfg`.

### Is it safe to uninstall? Will my game settings revert?

**Yes, it is 100% safe to uninstall.** All changes made by this mod are runtime-only and revert automatically when you remove the DLL and restart the game. Here is exactly why:

**QualitySettings**: Unity reloads from baked defaults on every launch. Runtime changes are in-memory only.

**HDRP Asset properties**: The HDRP Asset is a ScriptableObject. In a built game, runtime modifications are never written to disk. On restart, Unity deserializes the original asset.

**Volume component states**: We modify `volume.sharedProfile` in memory. In a built player, the original profile is deserialized from disk on restart.

**BepInEx config file**: The only thing that persists on disk. BepInEx ignores config files for absent plugins. Delete it or leave it.

**In summary**: remove the DLL, restart the game, and everything is back to stock.
