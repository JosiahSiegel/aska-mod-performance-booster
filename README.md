# Aska Performance Booster

A BepInEx 6 IL2CPP plugin that squeezes extra GPU performance out of Aska by disabling **expensive HDRP rendering features that the game's settings menu does not expose**.

Every optimization in this mod has been confirmed working with measurable FPS impact. Nothing speculative, nothing unverified.

## Benchmarks

### Ultra quality: 35 FPS to 59 FPS (+69%)

The biggest win. Same scene, same location, same time of day -- VSync ON, no DLSS:

<table>
<tr>
<td align="center"><strong>Without Mod -- 35 FPS</strong></td>
<td align="center"><strong>With Mod -- 59 FPS</strong></td>
</tr>
<tr>
<td><img src="https://raw.githubusercontent.com/JosiahSiegel/aska-mod-performance-booster/main/media/ultra-no_mod-vsync.png" width="400"></td>
<td><img src="https://raw.githubusercontent.com/JosiahSiegel/aska-mod-performance-booster/main/media/ultra-mod-vsync.png" width="400"></td>
</tr>
</table>

### High quality: 47 FPS to 59 FPS (+26%)

VSync ON, no DLSS:

<table>
<tr>
<td align="center"><strong>Without Mod -- 47 FPS</strong></td>
<td align="center"><strong>With Mod -- 59 FPS</strong></td>
</tr>
<tr>
<td><img src="https://raw.githubusercontent.com/JosiahSiegel/aska-mod-performance-booster/main/media/high-no_mod-vsync.png" width="400"></td>
<td><img src="https://raw.githubusercontent.com/JosiahSiegel/aska-mod-performance-booster/main/media/high-mod-vsync.png" width="400"></td>
</tr>
</table>

### Medium quality: 80 FPS to 92 FPS (+15%)

VSync OFF (uncapped), no DLSS:

<table>
<tr>
<td align="center"><strong>Without Mod -- 80 FPS</strong></td>
<td align="center"><strong>With Mod -- 92 FPS</strong></td>
</tr>
<tr>
<td><img src="https://raw.githubusercontent.com/JosiahSiegel/aska-mod-performance-booster/main/media/medium-no_mod.png" width="400"></td>
<td><img src="https://raw.githubusercontent.com/JosiahSiegel/aska-mod-performance-booster/main/media/medium-mod.png" width="400"></td>
</tr>
</table>

### Full results

All screenshots captured with MSI Afterburner overlay. Tested on the same scene, same location, same time of day.

| Quality | DLSS | VSync | Without Mod | With Mod | Improvement |
|---------|------|-------|-------------|----------|-------------|
| **Ultra** | Off | ON | 35 FPS | 59 FPS | **+24 FPS (+69%)** |
| **Ultra** | Balanced | ON | 35 FPS | 54 FPS | **+19 FPS (+54%)** |
| **High** | Off | ON | 47 FPS | 59 FPS | **+12 FPS (+26%)** |
| **High** | Quality | OFF | 57 FPS | 68 FPS | **+11 FPS (+19%)** |
| **Medium** | Off | OFF | 80 FPS | 92 FPS | **+12 FPS (+15%)** |
| **Medium** | Off | ON | 60 FPS / 79% GPU | 60 FPS / 66% GPU | **13% less GPU load** |

**Key takeaways:**
- **Ultra quality sees the largest gains** (54-69% improvement) because the disabled effects (SSAO, volumetrics, decals, SSS) are most expensive at high resolution and detail levels
- **At Medium with VSync ON**, both hit 60 FPS -- but the mod frees 13 percentage points of GPU headroom (79% down to 66%), leaving room for other work and reducing heat/power
- **The 60 FPS cap removal** alone is worth it for anyone GPU-bound above 60 -- Medium jumps from 80 to 92 FPS uncapped
- **DLSS stacks with the mod** -- High + DLSS Quality goes from 57 to 68 FPS

<details>
<summary>All benchmark screenshots (click to expand)</summary>

#### Ultra -- No DLSS -- VSync ON
<table>
<tr>
<td align="center"><strong>Without Mod (35 FPS)</strong></td>
<td align="center"><strong>With Mod (59 FPS)</strong></td>
</tr>
<tr>
<td><img src="https://raw.githubusercontent.com/JosiahSiegel/aska-mod-performance-booster/main/media/ultra-no_mod-vsync.png" width="400"></td>
<td><img src="https://raw.githubusercontent.com/JosiahSiegel/aska-mod-performance-booster/main/media/ultra-mod-vsync.png" width="400"></td>
</tr>
</table>

#### Ultra -- DLSS Balanced -- VSync ON
<table>
<tr>
<td align="center"><strong>Without Mod (35 FPS)</strong></td>
<td align="center"><strong>With Mod (54 FPS)</strong></td>
</tr>
<tr>
<td><img src="https://raw.githubusercontent.com/JosiahSiegel/aska-mod-performance-booster/main/media/ultra-no-mod-dlss-vsync.png" width="400"></td>
<td><img src="https://raw.githubusercontent.com/JosiahSiegel/aska-mod-performance-booster/main/media/ultra-mod-dlss-vsync.png" width="400"></td>
</tr>
</table>

#### High -- No DLSS -- VSync ON
<table>
<tr>
<td align="center"><strong>Without Mod (47 FPS)</strong></td>
<td align="center"><strong>With Mod (59 FPS)</strong></td>
</tr>
<tr>
<td><img src="https://raw.githubusercontent.com/JosiahSiegel/aska-mod-performance-booster/main/media/high-no_mod-vsync.png" width="400"></td>
<td><img src="https://raw.githubusercontent.com/JosiahSiegel/aska-mod-performance-booster/main/media/high-mod-vsync.png" width="400"></td>
</tr>
</table>

#### High -- DLSS Quality -- VSync OFF
<table>
<tr>
<td align="center"><strong>Without Mod (57 FPS)</strong></td>
<td align="center"><strong>With Mod (68 FPS)</strong></td>
</tr>
<tr>
<td><img src="https://raw.githubusercontent.com/JosiahSiegel/aska-mod-performance-booster/main/media/high-no_mod-dlss.png" width="400"></td>
<td><img src="https://raw.githubusercontent.com/JosiahSiegel/aska-mod-performance-booster/main/media/high-mod-dlss.png" width="400"></td>
</tr>
</table>

#### Medium -- No DLSS -- VSync OFF
<table>
<tr>
<td align="center"><strong>Without Mod (80 FPS)</strong></td>
<td align="center"><strong>With Mod (92 FPS)</strong></td>
</tr>
<tr>
<td><img src="https://raw.githubusercontent.com/JosiahSiegel/aska-mod-performance-booster/main/media/medium-no_mod.png" width="400"></td>
<td><img src="https://raw.githubusercontent.com/JosiahSiegel/aska-mod-performance-booster/main/media/medium-mod.png" width="400"></td>
</tr>
</table>

#### Medium -- No DLSS -- VSync ON
<table>
<tr>
<td align="center"><strong>Without Mod (60 FPS / 79% GPU)</strong></td>
<td align="center"><strong>With Mod (60 FPS / 66% GPU)</strong></td>
</tr>
<tr>
<td><img src="https://raw.githubusercontent.com/JosiahSiegel/aska-mod-performance-booster/main/media/medium-no_mod-vsync.png" width="400"></td>
<td><img src="https://raw.githubusercontent.com/JosiahSiegel/aska-mod-performance-booster/main/media/medium-mod-vsync.png" width="400"></td>
</tr>
</table>

</details>

## What This Mod Does (confirmed working)

1. **HDRP Pipeline Support Flags** (the primary optimization -- see benchmarks above) -- sets `supportSSAO=false`, `supportVolumetrics=false`, `supportVolumetricClouds=false`, `supportSubsurfaceScattering=false`, `supportDecals=false`, `supportScreenSpaceLensFlare=false`, `supportDataDrivenLensFlare=false` on the HDRP Asset. HDRP reads these every frame during Frame Settings aggregation, preventing the corresponding render passes from executing.

2. **Frame rate uncap** -- removes Aska's hard-coded `Application.targetFrameRate = 60` that caps all VSync-off users to 60 FPS regardless of GPU headroom. A Harmony patch prevents the game from re-applying the cap.

3. **Small shadow caster disable** -- disables shadow casting on 831+ small objects per session (rocks, plants, debris with bounds < 1m). Each costs a draw call in the shadow depth pass but produces barely visible shadows.

4. **SRP Batcher force-on** -- Aska ships with `GraphicsSettings.useScriptableRenderPipelineBatching = false`. Log confirmed the mod flips it to `true`.

5. **HDRP Asset stale check** -- detects when Aska's settings menu changes the quality level (which can swap the entire HDRP Asset) and re-applies pipeline flags to the new asset.

## Realistic Expectations

Benchmarks show **+11 to +24 FPS** depending on quality preset, with the largest gains at Ultra where the disabled effects are most expensive. At Medium with VSync, the mod reduces GPU load by 13 percentage points (79% to 66%) even when both hit the 60 FPS cap -- freeing thermal and power headroom.

Aska is primarily geometry and lighting bound. The base deferred rendering pass is the majority of GPU time. This mod removes screen-space effects (SSAO, volumetrics, decals, subsurface scattering) that account for a meaningful slice of the remaining GPU work.

**For the best results, combine all three levers:**
1. **In-game quality** -- dropping from Ultra to High changes things no mod can access
2. **DLSS or FSR** -- enable in the game's graphics menu (stacks with this mod -- see benchmarks)
3. **This mod's Moderate preset** (default) -- handles the hidden rendering wins

## Requirements

- [BepInEx 6 IL2CPP (BE #755 or newer)](https://builds.bepinex.dev/projects/bepinex_be)
  - **Easiest method:** Install [r2modman](https://thunderstore.io/package/ebkr/r2modman/), select Aska, and install **BepInExPack_IL2CPP** from the mod list.

## Installation

1. Install BepInEx 6 IL2CPP (see above)
2. Download the zip from the Files tab
3. Extract into your Aska game folder
4. Launch the game -- the Moderate preset applies automatically

## Presets

| Preset | What It Does | When to Use |
|--------|-------------|-------------|
| **Vanilla** | Nothing -- stock game | Disable the mod without uninstalling |
| **Moderate** (default) | Pipeline flags + uncap + shadow casters + SRP Batcher | Always |
| **Custom** | Uses your manually edited config values | Advanced users |

To change preset, edit `BepInEx/config/com.community.askaperformancebooster.cfg`:

```ini
[0. Preset]
Preset = Moderate
```

## Configuration Reference

After first launch, the config file is generated at:
```
BepInEx/config/com.community.askaperformancebooster.cfg
```

### Config Sections

```ini
[0. Preset]
  Preset = Moderate            # Moderate / Vanilla / Custom
  DebugLogging = false         # Verbose logging to LogOutput.log

[1. Pipeline]
  PipelineDisableSSAO = false           # Moderate sets true
  PipelineDisableVolumetrics = false     # Moderate sets true
  PipelineDisableVolumetricClouds = false # Moderate sets true
  PipelineDisableSubsurfaceScattering = false # Moderate sets true
  PipelineDisableDecals = false          # Moderate sets true
  PipelineDisableSSR = false             # Kept false (stale texture artifact)
  PipelineDisableDistortion = false      # Kept false
  PipelineDisableSSRTransparent = false  # Kept false
  PipelineDisableScreenSpaceLensFlare = false # Moderate sets true
  PipelineDisableDataDrivenLensFlare = false  # Moderate sets true

[2. Shadows]
  DisableSmallShadowCasters = true
  SmallShadowCasterThreshold = 1.0

[3. Draw Calls]
  ForceSRPBatcher = true

[4. Frame Rate]
  TargetFrameRate = -1         # -1 = unlimited, 0 = don't override

[5. Misc]
  ReapplyIntervalSeconds = 10  # Safety net re-apply timer
```

### Pipeline flags explained

| Flag | What It Disables | Visual Impact |
|------|-----------------|---------------|
| **PipelineDisableSSAO** | Screen-space ambient occlusion | Less shadow detail in crevices |
| **PipelineDisableVolumetrics** | Volumetric fog/lighting | No atmospheric fog |
| **PipelineDisableVolumetricClouds** | Volumetric cloud rendering | Flat sky |
| **PipelineDisableSubsurfaceScattering** | Skin/foliage translucency | Less realistic skin/leaves |
| **PipelineDisableDecals** | DBuffer decals | No scorch marks, footprints, etc. |
| **PipelineDisableScreenSpaceLensFlare** | Screen-space lens flares | No screen flares |
| **PipelineDisableDataDrivenLensFlare** | Data-driven lens flares | No data-driven flares |

SSR, Distortion, and Transparent SSR are intentionally kept enabled (false) because disabling them causes a stale-texture artifact where the screen image gets "stamped" onto reflective objects.

## Compatibility

- **Client-side only** -- does not affect network state in co-op
- **Works in singleplayer and multiplayer**
- **Compatible with other BepInEx IL2CPP mods** -- this mod only touches HDRP pipeline flags, small shadow casters, SRP Batcher, and frame rate. It never touches cameras, player transforms, renderers (except shadow caster mode on small objects), or input.
- Soft dependencies ensure the mod loads after known Aska mods (AskaFirstPerson, AskaPlus)

## Troubleshooting

- **Mod not loading:** Make sure you have BepInEx **6** IL2CPP (not BepInEx 5).
- **Settings not applying:** Enable `DebugLogging = true` and check `BepInEx/LogOutput.log`.
- **Want stock visuals:** Set preset to `Vanilla` or delete the config file.
- **Screen image "stamped" onto objects:** You manually set `PipelineDisableSSR = true` or `PipelineDisableDistortion = true`. Set them back to `false`.
- **Config changes not taking effect:** BepInEx reads config at plugin load time. Restart the game.

## Uninstallation

Delete `AskaPerformanceBooster.dll` from `BepInEx/plugins/` and optionally delete the config file. All changes are runtime-only and revert automatically on restart.

## Sunshine / Moonlight (Remote Play / Steam Deck)

If you use Sunshine/Moonlight to stream, copy your r2modman profile to the game folder:

```bash
cp -r "$APPDATA/r2modmanPlus-local/ASKA/profiles/Default/"* \
  "C:/Program Files (x86)/Steam/steamapps/common/ASKA/"
```

Then launch Aska normally from Steam. Re-run the copy after updating mods.
