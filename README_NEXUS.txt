[size=6][b]Aska Performance Booster[/b][/size]

A BepInEx 6 IL2CPP plugin that squeezes extra GPU performance out of Aska by disabling [b]expensive HDRP rendering features that the game's settings menu does not expose[/b].

Every optimization in this mod has been confirmed working with measurable FPS impact. Nothing speculative, nothing unverified.

[line]

[size=5][b]Benchmarks[/b][/size]

[size=4][b]Ultra quality: 35 FPS to 59 FPS (+69%)[/b][/size]

The biggest win. Same scene, same location, same time of day -- VSync ON, no DLSS:

[center][b]Without Mod -- 35 FPS[/b]
[img]https://raw.githubusercontent.com/JosiahSiegel/aska-mod-performance-booster/main/media/ultra-no_mod-vsync.png[/img]

[b]With Mod -- 59 FPS[/b]
[img]https://raw.githubusercontent.com/JosiahSiegel/aska-mod-performance-booster/main/media/ultra-mod-vsync.png[/img][/center]

[size=4][b]High quality: 47 FPS to 59 FPS (+26%)[/b][/size]

VSync ON, no DLSS:

[center][b]Without Mod -- 47 FPS[/b]
[img]https://raw.githubusercontent.com/JosiahSiegel/aska-mod-performance-booster/main/media/high-no_mod-vsync.png[/img]

[b]With Mod -- 59 FPS[/b]
[img]https://raw.githubusercontent.com/JosiahSiegel/aska-mod-performance-booster/main/media/high-mod-vsync.png[/img][/center]

[size=4][b]Medium quality: 80 FPS to 92 FPS (+15%)[/b][/size]

VSync OFF (uncapped), no DLSS:

[center][b]Without Mod -- 80 FPS[/b]
[img]https://raw.githubusercontent.com/JosiahSiegel/aska-mod-performance-booster/main/media/medium-no_mod.png[/img]

[b]With Mod -- 92 FPS[/b]
[img]https://raw.githubusercontent.com/JosiahSiegel/aska-mod-performance-booster/main/media/medium-mod.png[/img][/center]

[size=4][b]Full results[/b][/size]

All screenshots captured with MSI Afterburner overlay. Tested on the same scene, same location, same time of day.

[list]
[*][b]Ultra[/b] / DLSS Off / VSync ON: 35 -> 59 FPS ([b]+24 FPS, +69%[/b])
[*][b]Ultra[/b] / DLSS Balanced / VSync ON: 35 -> 54 FPS ([b]+19 FPS, +54%[/b])
[*][b]High[/b] / DLSS Off / VSync ON: 47 -> 59 FPS ([b]+12 FPS, +26%[/b])
[*][b]High[/b] / DLSS Quality / VSync OFF: 57 -> 68 FPS ([b]+11 FPS, +19%[/b])
[*][b]Medium[/b] / DLSS Off / VSync OFF: 80 -> 92 FPS ([b]+12 FPS, +15%[/b])
[*][b]Medium[/b] / DLSS Off / VSync ON: 60 FPS / 79% GPU -> 60 FPS / 66% GPU ([b]13% less GPU load[/b])
[/list]

[b]Key takeaways:[/b]
[list]
[*][b]Ultra quality sees the largest gains[/b] (54-69% improvement) because the disabled effects (SSAO, volumetrics, decals, SSS) are most expensive at high resolution and detail levels
[*][b]At Medium with VSync ON[/b], both hit 60 FPS -- but the mod frees 13 percentage points of GPU headroom (79% down to 66%), leaving room for other work and reducing heat/power
[*][b]The 60 FPS cap removal[/b] alone is worth it for anyone GPU-bound above 60 -- Medium jumps from 80 to 92 FPS uncapped
[*][b]DLSS stacks with the mod[/b] -- High + DLSS Quality goes from 57 to 68 FPS
[/list]

[spoiler]
[size=4][b]All benchmark screenshots[/b][/size]

[b]Ultra -- No DLSS -- VSync ON[/b]
[center][b]Without Mod (35 FPS)[/b]
[img]https://raw.githubusercontent.com/JosiahSiegel/aska-mod-performance-booster/main/media/ultra-no_mod-vsync.png[/img]

[b]With Mod (59 FPS)[/b]
[img]https://raw.githubusercontent.com/JosiahSiegel/aska-mod-performance-booster/main/media/ultra-mod-vsync.png[/img][/center]

[b]Ultra -- DLSS Balanced -- VSync ON[/b]
[center][b]Without Mod (35 FPS)[/b]
[img]https://raw.githubusercontent.com/JosiahSiegel/aska-mod-performance-booster/main/media/ultra-no-mod-dlss-vsync.png[/img]

[b]With Mod (54 FPS)[/b]
[img]https://raw.githubusercontent.com/JosiahSiegel/aska-mod-performance-booster/main/media/ultra-mod-dlss-vsync.png[/img][/center]

[b]High -- No DLSS -- VSync ON[/b]
[center][b]Without Mod (47 FPS)[/b]
[img]https://raw.githubusercontent.com/JosiahSiegel/aska-mod-performance-booster/main/media/high-no_mod-vsync.png[/img]

[b]With Mod (59 FPS)[/b]
[img]https://raw.githubusercontent.com/JosiahSiegel/aska-mod-performance-booster/main/media/high-mod-vsync.png[/img][/center]

[b]High -- DLSS Quality -- VSync OFF[/b]
[center][b]Without Mod (57 FPS)[/b]
[img]https://raw.githubusercontent.com/JosiahSiegel/aska-mod-performance-booster/main/media/high-no_mod-dlss.png[/img]

[b]With Mod (68 FPS)[/b]
[img]https://raw.githubusercontent.com/JosiahSiegel/aska-mod-performance-booster/main/media/high-mod-dlss.png[/img][/center]

[b]Medium -- No DLSS -- VSync OFF[/b]
[center][b]Without Mod (80 FPS)[/b]
[img]https://raw.githubusercontent.com/JosiahSiegel/aska-mod-performance-booster/main/media/medium-no_mod.png[/img]

[b]With Mod (92 FPS)[/b]
[img]https://raw.githubusercontent.com/JosiahSiegel/aska-mod-performance-booster/main/media/medium-mod.png[/img][/center]

[b]Medium -- No DLSS -- VSync ON[/b]
[center][b]Without Mod (60 FPS / 79% GPU)[/b]
[img]https://raw.githubusercontent.com/JosiahSiegel/aska-mod-performance-booster/main/media/medium-no_mod-vsync.png[/img]

[b]With Mod (60 FPS / 66% GPU)[/b]
[img]https://raw.githubusercontent.com/JosiahSiegel/aska-mod-performance-booster/main/media/medium-mod-vsync.png[/img][/center]
[/spoiler]

[line]

[size=5][b]What This Mod Does (confirmed working)[/b][/size]

[list=1]
[*][b]HDRP Pipeline Support Flags[/b] (the primary optimization -- see benchmarks above) -- sets [i]supportSSAO=false[/i], [i]supportVolumetrics=false[/i], [i]supportVolumetricClouds=false[/i], [i]supportSubsurfaceScattering=false[/i], [i]supportDecals=false[/i], [i]supportScreenSpaceLensFlare=false[/i], [i]supportDataDrivenLensFlare=false[/i] on the HDRP Asset. HDRP reads these every frame during Frame Settings aggregation, preventing the corresponding render passes from executing.

[*][b]Frame rate uncap[/b] -- removes Aska's hard-coded [i]Application.targetFrameRate = 60[/i] that caps all VSync-off users to 60 FPS regardless of GPU headroom. A Harmony patch prevents the game from re-applying the cap.

[*][b]Small shadow caster disable[/b] -- disables shadow casting on 831+ small objects per session (rocks, plants, debris with bounds < 1m). Each costs a draw call in the shadow depth pass but produces barely visible shadows.

[*][b]SRP Batcher force-on[/b] -- Aska ships with [i]GraphicsSettings.useScriptableRenderPipelineBatching = false[/i]. Log confirmed the mod flips it to [i]true[/i].

[*][b]HDRP Asset stale check[/b] -- detects when Aska's settings menu changes the quality level (which can swap the entire HDRP Asset) and re-applies pipeline flags to the new asset.
[/list]

[line]

[size=5][b]Realistic Expectations[/b][/size]

Benchmarks show [b]+11 to +24 FPS[/b] depending on quality preset, with the largest gains at Ultra where the disabled effects are most expensive. At Medium with VSync, the mod reduces GPU load by 13 percentage points (79% to 66%) even when both hit the 60 FPS cap -- freeing thermal and power headroom.

Aska is primarily geometry and lighting bound. The base deferred rendering pass is the majority of GPU time. This mod removes screen-space effects (SSAO, volumetrics, decals, subsurface scattering) that account for a meaningful slice of the remaining GPU work.

[b]For the best results, combine all three levers:[/b]
[list=1]
[*][b]In-game quality[/b] -- dropping from Ultra to High changes things no mod can access
[*][b]DLSS or FSR[/b] -- enable in the game's graphics menu (stacks with this mod -- see benchmarks)
[*][b]This mod's Moderate preset[/b] (default) -- handles the hidden rendering wins
[/list]

[line]

[size=5][b]Requirements[/b][/size]

[list]
[*][url=https://builds.bepinex.dev/projects/bepinex_be]BepInEx 6 IL2CPP (BE #755 or newer)[/url]
[*][b]Easiest method:[/b] Install [url=https://thunderstore.io/package/ebkr/r2modman/]r2modman[/url], select Aska, and install [b]BepInExPack_IL2CPP[/b] from the mod list.
[/list]

[line]

[size=5][b]Installation[/b][/size]

[list=1]
[*]Install BepInEx 6 IL2CPP (see above)
[*]Download the zip from the Files tab
[*]Extract into your Aska game folder
[*]Launch the game -- the Moderate preset applies automatically
[/list]

[line]

[size=5][b]Presets[/b][/size]

[list]
[*][b]Vanilla[/b] -- Nothing, stock game. Use to disable the mod without uninstalling.
[*][b]Moderate[/b] (default) -- Pipeline flags + uncap + shadow casters + SRP Batcher. Use always.
[*][b]Custom[/b] -- Uses your manually edited config values. For advanced users.
[/list]

To change preset, edit [i]BepInEx/config/com.community.askaperformancebooster.cfg[/i]:

[code]
[0. Preset]
Preset = Moderate
[/code]

[line]

[size=5][b]Configuration Reference[/b][/size]

After first launch, the config file is generated at:

[code]BepInEx/config/com.community.askaperformancebooster.cfg[/code]

[size=4][b]Config Sections[/b][/size]

[code]
[0. Preset]
  Preset = Moderate            # Moderate / Vanilla / Custom
  DebugLogging = false         # Verbose logging to LogOutput.log

[1. Pipeline]
  PipelineDisableSSAO = false                 # Moderate sets true
  PipelineDisableVolumetrics = false          # Moderate sets true
  PipelineDisableVolumetricClouds = false     # Moderate sets true
  PipelineDisableSubsurfaceScattering = false # Moderate sets true
  PipelineDisableDecals = false               # Moderate sets true
  PipelineDisableSSR = false                  # Kept false (stale texture artifact)
  PipelineDisableDistortion = false           # Kept false
  PipelineDisableSSRTransparent = false       # Kept false
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
[/code]

[size=4][b]Pipeline flags explained[/b][/size]

[list]
[*][b]PipelineDisableSSAO[/b] -- Disables screen-space ambient occlusion. Less shadow detail in crevices.
[*][b]PipelineDisableVolumetrics[/b] -- Disables volumetric fog/lighting. No atmospheric fog.
[*][b]PipelineDisableVolumetricClouds[/b] -- Disables volumetric cloud rendering. Flat sky.
[*][b]PipelineDisableSubsurfaceScattering[/b] -- Disables skin/foliage translucency. Less realistic skin/leaves.
[*][b]PipelineDisableDecals[/b] -- Disables DBuffer decals. No scorch marks, footprints, etc.
[*][b]PipelineDisableScreenSpaceLensFlare[/b] -- Disables screen-space lens flares. No screen flares.
[*][b]PipelineDisableDataDrivenLensFlare[/b] -- Disables data-driven lens flares. No data-driven flares.
[/list]

SSR, Distortion, and Transparent SSR are intentionally kept enabled (false) because disabling them causes a stale-texture artifact where the screen image gets "stamped" onto reflective objects.

[line]

[size=5][b]Compatibility[/b][/size]

[list]
[*][b]Client-side only[/b] -- does not affect network state in co-op
[*][b]Works in singleplayer and multiplayer[/b]
[*][b]Compatible with other BepInEx IL2CPP mods[/b] -- this mod only touches HDRP pipeline flags, small shadow casters, SRP Batcher, and frame rate. It never touches cameras, player transforms, renderers (except shadow caster mode on small objects), or input.
[*]Soft dependencies ensure the mod loads after known Aska mods (AskaFirstPerson, AskaPlus)
[/list]

[line]

[size=5][b]Troubleshooting[/b][/size]

[list]
[*][b]Mod not loading:[/b] Make sure you have BepInEx [b]6[/b] IL2CPP (not BepInEx 5).
[*][b]Settings not applying:[/b] Enable [i]DebugLogging = true[/i] and check [i]BepInEx/LogOutput.log[/i].
[*][b]Want stock visuals:[/b] Set preset to [i]Vanilla[/i] or delete the config file.
[*][b]Screen image "stamped" onto objects:[/b] You manually set [i]PipelineDisableSSR = true[/i] or [i]PipelineDisableDistortion = true[/i]. Set them back to [i]false[/i].
[*][b]Config changes not taking effect:[/b] BepInEx reads config at plugin load time. Restart the game.
[/list]

[line]

[size=5][b]Uninstallation[/b][/size]

Delete [i]AskaPerformanceBooster.dll[/i] from [i]BepInEx/plugins/[/i] and optionally delete the config file. All changes are runtime-only and revert automatically on restart.

[line]

[size=5][b]Sunshine / Moonlight (Remote Play / Steam Deck)[/b][/size]

If you use Sunshine/Moonlight to stream, copy your r2modman profile to the game folder:

[code]
cp -r "$APPDATA/r2modmanPlus-local/ASKA/profiles/Default/"* \
  "C:/Program Files (x86)/Steam/steamapps/common/ASKA/"
[/code]

Then launch Aska normally from Steam. Re-run the copy after updating mods.
