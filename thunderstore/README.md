# Aska Performance Booster

A BepInEx 6 IL2CPP plugin that squeezes **~5-10 extra FPS** out of Aska by disabling expensive HDRP rendering features that the game's settings menu does not expose.

**Philosophy: "Read before write, only improve."** Every setting is checked against the game's current value before being overridden.

## What This Mod Does

Aska uses HDRP (High Definition Render Pipeline). This mod targets HDRP-specific optimization paths that the game's graphics menu cannot reach:

- **HDRP Frame Settings** -- disable SSR, SSAO, volumetric fog, contact shadows, subsurface scattering, decals, distortion
- **Shadow reduction** -- distance 60m (stock 500m), atlas halved, cascades 4 to 2
- **Post-processing removal** -- film grain, chromatic aberration, motion blur, lens distortion, depth of field, vignette, lens flares
- **SSR/SSAO quality reduction** -- fewer ray steps and samples via Volume overrides
- **Pipeline verification** -- SRP Batcher, GPU occlusion culling, async upload buffers
- **Mipmap streaming** -- progressive texture loading, identical at gameplay distances

## Realistic Expectations

This mod gives **~5-10 FPS for free** by disabling screen-space effects that account for 10-20% of GPU frame time. Aska is primarily geometry/lighting bound -- the base deferred pass is the majority of each frame. For bigger gains, combine this mod with the game's own quality settings (High recommended) and DLSS/FSR.

## Installation

### With r2modman (recommended)

1. Install [r2modman](https://thunderstore.io/package/ebkr/r2modman/)
2. Select Aska as your game
3. Search for **AskaPerformanceBooster** and install

### Manual

1. Install [BepInEx 6 IL2CPP (BE #755+)](https://builds.bepinex.dev/projects/bepinex_be) for Aska
2. Copy `AskaPerformanceBooster.dll` into `BepInEx/plugins/` inside your Aska game folder
3. Launch the game

## Presets

The mod works immediately with the **Moderate** preset (default). No configuration needed.

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

## Configuration

After first launch, a config file is created at:
```
BepInEx/config/com.community.askaperformancebooster.cfg
```

### Config Sections (14 categories)

1. **Draw Calls** -- SRP Batcher, GPU Resident Drawer
2. **Shadows** -- Distance, cascades, atlas resolution, contact shadows, micro shadows, screen-space shadows
3. **Textures** -- Mipmap streaming, anisotropic filtering, memory budget
4. **Post-Processing** -- Film grain, chromatic aberration, bloom, color grading LUT
5. **Culling** -- Shadow distance QualitySettings override
6. **LOD** -- Bias, max level, skin weights
7. **HDRP Pipeline** -- SSR quality, SSAO quality, volumetric fog quality, volumetric clouds, subsurface scattering, max lights, LUT size
8. **Frame Settings** -- Per-camera toggles: SSR, SSAO, contact shadows, volumetrics, SSGI, subsurface scattering, transparent SSR, decals, distortion
9. **Lighting** -- Reflection probes
10. **Async Upload** -- Time slice, buffer size
11. **Misc** -- VSync, frame rate
12. **Advanced GPU** -- Shader warmup, max queued frames, GPU occlusion culling, small mesh culling
13. **Post-Processing Extra** -- Depth of field, vignette, panini projection, lens flares
14. **Mod Compatibility** -- Master toggles, reapply interval, respect external changes

Every setting has a detailed description explaining what it does and its visual impact.

## Compatibility

- Client-side only -- does not affect network state in co-op
- Works in both singleplayer and multiplayer sessions
- Compatible with other BepInEx IL2CPP mods
- Master toggles per category to avoid conflicts with other rendering mods
- May need updates after major Aska patches (delete `BepInEx/interop/` and relaunch)

## Troubleshooting

- **Plugin not loading:** Ensure BepInEx **6** IL2CPP (not BepInEx 5), DLL in `BepInEx/plugins/`
- **Settings not taking effect:** Enable `DebugLogging = true` in config, check `BepInEx/LogOutput.log`
- **Want stock visuals:** Set preset to `Vanilla` or delete the config file
- **Reset to defaults:** Delete `BepInEx/config/com.community.askaperformancebooster.cfg`
