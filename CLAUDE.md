# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A BepInEx 6 IL2CPP plugin for **Aska** (Unity 6, HDRP, IL2CPP backend) that boosts GPU performance by disabling expensive HDRP rendering features the game menu does not expose. Targets `net6.0`. Single DLL output: `AskaPerformanceBooster.dll`.

Every optimization is confirmed working with measurable FPS impact. Nothing speculative.

## Build & package

Project references BepInEx/interop DLLs from the local r2modman profile via the `R2ModManDir` MSBuild property (default: `%APPDATA%\r2modmanPlus-local\ASKA\profiles\Default\BepInEx`). **CI cannot build** -- interop assemblies are generated per-machine at first game launch.

```bash
dotnet build -c Release                              # build plugin
dotnet build -c Release -p:R2ModManDir="C:\path"     # override BepInEx location
./package.sh                                         # build + zip for Thunderstore/Nexus/GitHub
./package.sh 1.2.3                                   # override version
```

Fast iterate: build, then copy `bin/Release/net6.0/AskaPerformanceBooster.dll` into `$APPDATA/r2modmanPlus-local/ASKA/profiles/Default/BepInEx/plugins/` and relaunch via r2modman. Logs land in `BepInEx/LogOutput.log`.

## Architecture

Six files do all the work:

- **`PerformancePlugin.cs`** -- `BasePlugin.Load()` binds config entries, runs `Harmony.PatchAll()`, and calls `AddComponent<PerformanceBehaviour>()`. Config sections: Preset, Pipeline, Shadows, Draw Calls, Frame Rate, Misc.
- **`PerformanceBehaviour.cs`** -- the `MonoBehaviour` that applies optimizations at runtime: pipeline support flags, SRP Batcher, targetFrameRate, small shadow caster disable. Reapplies on timer and on quality level change.
- **`HDRPReflectionHelper.cs`** -- static helper for HDRP Asset reflection. Caches the pipeline asset, resolves IL2CPP concrete types, reads/writes pipeline support flag properties, detects stale assets after quality level changes.
- **`PresetApplicator.cs`** -- defines Vanilla (all false) and Moderate (confirmed-working flags enabled) presets.
- **`QualityLevelPatch.cs`** -- Harmony postfix on `QualitySettings.SetQualityLevel` that detects quality level changes and triggers cache invalidation + reapply.
- **`TargetFrameRatePatch.cs`** -- Harmony prefix on `Application.targetFrameRate` setter that prevents Aska from re-capping the frame rate after our override.

### What is confirmed working (and kept)

1. **Pipeline support flags** (7 features) -- `supportSSAO=false`, `supportVolumetrics=false`, `supportVolumetricClouds=false`, `supportSubsurfaceScattering=false`, `supportDecals=false`, `supportScreenSpaceLensFlare=false`, `supportDataDrivenLensFlare=false`. The primary optimization. 4-5+ FPS proven.

2. **targetFrameRate = -1** (uncap) + `TargetFrameRatePatch` Harmony patch -- removes Aska's 60 FPS hard cap.

3. **Small shadow caster disable** -- `renderer.shadowCastingMode = Off` on small objects. 831+ objects confirmed disabled per session.

4. **SRP Batcher force-on** -- `GraphicsSettings.useScriptableRenderPipelineBatching = true`. Log confirmed `false -> true`.

5. **HDRP Asset stale check** -- detects quality level changes and re-applies pipeline flags to new asset.

6. **Quality level change detection** -- `QualityLevelPatch` Harmony patch.

### Config sections

```
[0. Preset]
  Preset = Moderate
  DebugLogging = false

[1. Pipeline]
  PipelineDisableSSAO = false           (default, Moderate=true)
  PipelineDisableVolumetrics = false     (default, Moderate=true)
  PipelineDisableVolumetricClouds = false (default, Moderate=true)
  PipelineDisableSubsurfaceScattering = false (default, Moderate=true)
  PipelineDisableDecals = false          (default, Moderate=true)
  PipelineDisableSSR = false            (kept false -- stale texture artifact)
  PipelineDisableDistortion = false     (kept false)
  PipelineDisableSSRTransparent = false (kept false)
  PipelineDisableScreenSpaceLensFlare = false (default, Moderate=true)
  PipelineDisableDataDrivenLensFlare = false  (default, Moderate=true)

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

### Critical invariants

- **Pipeline support flags are the PRIMARY optimization.** These write directly to RenderPipelineSettings via NativeFieldInfoPtr property setters. HDRP checks them per-frame during Frame Settings aggregation. Confirmed 4-5+ FPS improvement.
- **SSR, Distortion, and Transparent SSR must stay enabled (false).** Disabling them stops the render pass but leaves the texture with stale screen data, causing visible artifacts.
- **`HDRPReflectionHelper` must be a static class, not on the MonoBehaviour.** IL2CPP's ClassInjector processes all instance methods on MonoBehaviour subclasses and chokes on System.Object/System.Type/PropertyInfo parameter types.
- **Harmony patches use the string overload** for IL2CPP interop compatibility (`[HarmonyTargetMethod]` with `AccessTools.TypeByName`).
- **`TargetFrameRatePatch` uses a reentrancy guard** (`_inSelfWrite`) to prevent infinite recursion when writing our own value from inside the prefix.
- **`QualityLevelPatch` sets a volatile flag** that `PerformanceBehaviour.Update()` reads and clears. We do not call settings application directly from the Harmony postfix because the quality level change may not have fully propagated yet.
- **`AddComponent<PerformanceBehaviour>()` from `BasePlugin.Load`** relies on BepInEx 6 IL2CPP's automatic `ClassInjector` registration. Do not add manual `ClassInjector.RegisterTypeInIl2Cpp<PerformanceBehaviour>()` -- that double-registers and crashes.
- **HDRP Asset stale check** compares `GetInstanceID()` of the cached asset vs `GraphicsSettings.currentRenderPipeline`. Quality level changes in Aska can swap the entire HDRP Asset.
- **Small shadow caster scan** waits 5 seconds after entering gameplay (for world to stream in), then re-scans every 30 seconds. Skips `ShadowsOnly` and `Off` renderers, and `Player`-tagged objects.
- **`try/catch` around all interop calls is load-bearing.** Under IL2CPP, property access can throw during scene transitions and before systems initialize.

## Releasing

Version lives in **two** places that must match: `PerformancePlugin.cs` -> `PluginVersion`, and `thunderstore/manifest.json` -> `version_number`. `package.sh` only **warns** on mismatch. Tag `vX.Y.Z` and push; manually upload `dist/AskaPerformanceBooster-X.Y.Z.zip` to Thunderstore and Nexus.

## Further reading in-repo

- `README.md` -- end-user install, config reference
- `CONTRIBUTING.md` -- dev setup, PR workflow, code guidelines
