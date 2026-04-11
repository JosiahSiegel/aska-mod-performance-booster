# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A BepInEx 6 IL2CPP plugin for **Aska** (Unity 6, IL2CPP backend, HDRP) that boosts GPU performance through smart, non-destructive optimizations. Targets `net6.0`. Single DLL output: `AskaPerformanceBooster.dll`.

### Aska's rendering pipeline

Aska uses **HDRP** (High Definition Render Pipeline). The IL2CPP type resolved via `GetIl2CppType()` is `UnityEngine.Rendering.HighDefinition.HDRenderPipelineAsset`. The managed wrapper returned by `GetType()` may only show `RenderPipelineAsset` (the base class); the `ResolveIl2CppConcreteType()` helper resolves the real concrete type from loaded IL2CPP interop assemblies.

### Philosophy

**"Read before write, only improve."** Every setting is checked against the game's current value before being overridden. Settings are only changed when our value would be a genuine improvement.

## Build & package

Project references BepInEx/interop DLLs from the local r2modman profile via the `R2ModManDir` MSBuild property (default: `%APPDATA%\r2modmanPlus-local\ASKA\profiles\Default\BepInEx`). **CI cannot build** -- interop assemblies are generated per-machine at first game launch.

```bash
dotnet build -c Release                              # build plugin
dotnet build -c Release -p:R2ModManDir="C:\path"     # override BepInEx location
./package.sh                                         # build + zip for Thunderstore/Nexus/GitHub
./package.sh 1.0.0                                   # override version
```

Fast iterate: build, then copy `bin/Release/net6.0/AskaPerformanceBooster.dll` into `$APPDATA/r2modmanPlus-local/ASKA/profiles/Default/BepInEx/plugins/` and relaunch via r2modman. Logs land in `BepInEx/LogOutput.log`.

## Architecture

Five files do all the work:

- **`PerformancePlugin.cs`** -- `BasePlugin.Load()` binds config entries, runs Harmony patches, and calls `AddComponent<PerformanceBehaviour>()`. Defines the `PerformancePreset` enum. Config entries use sentinel values (0, -1, false) to mean "don't override."

- **`PresetApplicator.cs`** -- Sets all config values for a given preset. `Moderate` (default) enables pipeline support flag disables (the PRIMARY optimization mechanism, confirmed 4-5+ FPS), shadow reduction via HDShadowSettings Volume, small shadow caster disable, and cosmetic post-processing cleanup. `Vanilla` sets all sentinels to "don't override." `Custom` skips preset application.

- **`PerformanceBehaviour.cs`** -- The `MonoBehaviour` that applies settings at runtime with read-before-write logic. Operates in two tiers: Tier 1 (global -- QualitySettings, pipeline support flags, SRP Batcher) and Tier 2 (Volume overrides -- cosmetic post-processing, HDShadowSettings, Volume component disables). Also contains the `ExternalChangeTracker` static helper class. **CRITICAL: Instance methods on this class must ONLY use IL2CPP-safe parameter/return types.**

- **`HDRPReflectionHelper.cs`** -- Static helper class for ALL HDRP reflection operations. Handles: HDRP Asset reflection caching, `RenderPipelineSettings` struct access (the actual location of `supportSSR`, `supportSSAO`, etc.), pipeline support flag batch writes, diagnostic dump, and type resolution helpers.

- **`QualityLevelPatch.cs`** -- Harmony postfix on `QualitySettings.SetQualityLevel(int, bool)` that detects quality level changes and triggers HDRP Asset cache invalidation.

- **`TargetFrameRatePatch.cs`** -- Harmony prefix on `Application.targetFrameRate` setter that prevents Aska from re-capping frame rate after our override.

### What actually works (confirmed via testing)

1. **Pipeline support flags** (`supportSSR=false`, `supportSSAO=false`, etc.) -- writes via NativeFieldInfoPtr to native memory. Confirmed 4-5+ FPS improvement. This is the primary optimization.
2. **QualitySettings static API** -- lodBias, skinWeights, asyncUpload, etc. Minor but real.
3. **SRP Batcher** (`GraphicsSettings.useScriptableRenderPipelineBatching`) -- static API, works.
4. **targetFrameRate uncap** + Harmony patch -- removes Aska's 60 FPS cap.
5. **Small shadow caster disable** (`renderer.shadowCastingMode`) -- component property, works. ~1000 objects per scene.
6. **HDShadowSettings Volume parameters** (maxShadowDistance, cascadeShadowSplitCount) -- managed Volume system, works.
7. **Volume component `active = false` for cosmetic post-processing** (film grain, chromatic aberration, vignette, motion blur, lens distortion, bloom reduction, DOF, panini) -- managed Volume system, works.
8. **Quality level change detection** (QualityLevelPatch) -- Harmony patch, works.
9. **HDRP Asset stale check** -- instance ID comparison, works.

### What was removed (confirmed non-functional under IL2CPP)

1. **Frame Settings on HDAdditionalCameraData** -- customRenderingSettings, override mask, SetEnabled, BitArray128 indexer. All confirmed non-functional; managed writes don't reach the native pipeline.
2. **Volume component `active = false` for rendering effects** (SSR, SSAO, Fog, ContactShadows, VolumetricClouds, SSS, SSGI) -- doesn't disable the render pass, only changes parameter source.
3. **HDRP Asset nested struct modifications** (shadow atlas resolution, LUT size, max lights) -- likely cached at pipeline init, runtime writes ineffective.
4. **VolumeUpdatePatch** -- dead code from the start, never registered.
5. **GPU Occlusion Culling / Small Mesh Screen Percentage** on RenderPipelineGlobalSettings -- same IL2CPP native barrier.

### Critical invariants

- **Pipeline support flags are the ONLY confirmed path for disabling HDRP rendering features.** Do not add Frame Settings or Volume-based rendering disables back without new evidence that they work.
- **SSR, Distortion, and Transparent SSR cannot be disabled at the pipeline flag level** -- causes stale-texture artifacts where the screen image is stamped onto reflective objects. Leave `PipelineDisableSSR=false`, `PipelineDisableDistortion=false`, `PipelineDisableSSRTransparent=false`.
- **RenderPipelineSettings members are PROPERTIES, not fields.** `IsClass=True` despite wrapping a native struct. Use `GetProperty()` / `FindProp()`, NOT `GetField()`.
- **`AddComponent<PerformanceBehaviour>()` from `BasePlugin.Load`** relies on BepInEx 6 IL2CPP's automatic `ClassInjector` registration. Do NOT add manual `ClassInjector.RegisterTypeInIl2Cpp<PerformanceBehaviour>()`.
- **Instance methods on PerformanceBehaviour must NOT use System.Object/System.Type/reflection parameter types.** ClassInjector processes all instance methods. All reflection-typed code lives in HDRPReflectionHelper (static class).
- **`try/catch` around IL2CPP interop calls is load-bearing.** Under IL2CPP interop, property access can throw during scene transitions. Don't remove the catches.
- **Presets NEVER touch DebugLogging, DiagnosticScan, or Mod Compatibility settings.** Those are user preferences that must persist across preset changes.

### IL2CPP / interop notes

- **Harmony patches use string-based type resolution** (`[HarmonyTargetMethod]` with `AccessTools.TypeByName`), not `typeof()`. The Il2CppInterop-wrapped type lives under an `Il2Cpp` namespace prefix and attribute-time `typeof` resolution is unreliable.
- **`try/catch` around Fusion and Gamepad access is load-bearing, not defensive.**
- **Mixed input stacks are intentional.** Aska ships both legacy `UnityEngine.Input` and new Input System modules.

## Config sections (after dead code removal)

| Section | What it controls |
|---------|-----------------|
| 0. Preset | Preset selection, debug logging, diagnostic scan |
| 1. Draw Calls | SRP Batcher, GPU Resident Drawer (non-functional) |
| 2. Shadows | Shadow distance/cascades (via HDShadowSettings Volume), contact/micro/screen-space shadow disables, small shadow caster optimization |
| 3. Textures | Mipmap streaming, anisotropic filtering, mip limits |
| 4. Post-Processing | Cosmetic effect disables (film grain, bloom, etc.) |
| 5. Culling | QualitySettings shadow distance fallback |
| 6. LOD | LOD bias, max LOD level, skin weights |
| 7. HDRP Pipeline | Pipeline support flags (PRIMARY optimization), Volume component disables |
| 8. Lighting | Realtime reflection probes |
| 9. Async Upload | Async upload time slice and buffer size |
| 10. Misc | VSync, target frame rate |
| 11. Advanced GPU | Shader warmup, max queued frames |
| 12. Post-Processing Extra | DOF, vignette, panini, lens flares |
| 13. Mod Compatibility | Reapply interval, respect external changes, per-category toggles |

## Releasing

Version lives in **two** places that must match: `PerformancePlugin.cs` -> `PluginVersion`, and `thunderstore/manifest.json` -> `version_number`. `package.sh` only **warns** on mismatch. Tag `vX.Y.Z` and push; manually upload to Thunderstore and Nexus.
