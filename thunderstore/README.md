# Aska Performance Booster

A BepInEx 6 IL2CPP plugin that gives **+11 to +24 extra FPS** by disabling expensive HDRP rendering features that Aska's settings menu does not expose. Up to **69% FPS improvement** at Ultra quality.

Every optimization is confirmed working with real benchmark data. Nothing speculative.

## Benchmarks

Real numbers from controlled testing -- same scene, same location, same time of day:

| Quality | DLSS | VSync | Without Mod | With Mod | Gain |
|---------|------|-------|-------------|----------|------|
| **Ultra** | Off | ON | 35 FPS | 59 FPS | **+69%** |
| **Ultra** | Balanced | ON | 35 FPS | 54 FPS | **+54%** |
| **High** | Off | ON | 47 FPS | 59 FPS | **+26%** |
| **High** | Quality | OFF | 57 FPS | 68 FPS | **+19%** |
| **Medium** | Off | OFF | 80 FPS | 92 FPS | **+15%** |
| **Medium** | Off | ON | 60 FPS (79% GPU) | 60 FPS (66% GPU) | **13% less GPU** |

## What This Mod Does

1. **HDRP Pipeline Support Flags** (primary optimization) -- disables SSAO, volumetrics, volumetric clouds, subsurface scattering, decals, and lens flares at the pipeline level
2. **Frame rate uncap** -- removes Aska's hard-coded 60 FPS cap
3. **Small shadow caster disable** -- disables shadows on 831+ small objects per session
4. **SRP Batcher force-on** -- Aska ships with it off; we turn it on

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

| Preset | What It Does |
|--------|-------------|
| **Vanilla** | Nothing -- stock game |
| **Moderate** (default) | Pipeline flags + uncap + shadow casters + SRP Batcher |
| **Custom** | Your manually edited config values |

## Configuration

Config file: `BepInEx/config/com.community.askaperformancebooster.cfg`

```ini
[0. Preset]
Preset = Moderate
DebugLogging = false

[1. Pipeline]
PipelineDisableSSAO = false           # Moderate = true
PipelineDisableVolumetrics = false     # Moderate = true
PipelineDisableVolumetricClouds = false # Moderate = true
PipelineDisableSubsurfaceScattering = false # Moderate = true
PipelineDisableDecals = false          # Moderate = true
PipelineDisableSSR = false             # Always false (artifact)
PipelineDisableDistortion = false      # Always false (artifact)
PipelineDisableSSRTransparent = false  # Always false (artifact)
PipelineDisableScreenSpaceLensFlare = false # Moderate = true
PipelineDisableDataDrivenLensFlare = false  # Moderate = true

[2. Shadows]
DisableSmallShadowCasters = true
SmallShadowCasterThreshold = 1.0

[3. Draw Calls]
ForceSRPBatcher = true

[4. Frame Rate]
TargetFrameRate = -1

[5. Misc]
ReapplyIntervalSeconds = 10
```

## Compatibility

- Client-side only -- does not affect network state in co-op
- Works in singleplayer and multiplayer
- Compatible with other BepInEx IL2CPP mods
- Never touches cameras, player transforms, or input

## Troubleshooting

- **Plugin not loading:** Ensure BepInEx **6** IL2CPP (not BepInEx 5)
- **Settings not taking effect:** Enable `DebugLogging = true`, check `BepInEx/LogOutput.log`
- **Want stock visuals:** Set preset to `Vanilla` or delete the config file
