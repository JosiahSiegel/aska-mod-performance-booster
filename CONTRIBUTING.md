# Contributing to Aska Performance Booster

Thanks for your interest in contributing! This guide covers everything you need to get the mod building, tested, and ready for a pull request.

## Prerequisites

| Tool | Version | Purpose |
|------|---------|---------|
| [.NET SDK](https://dotnet.microsoft.com/download) | 6.0+ | Build the plugin |
| [r2modman](https://thunderstore.io/package/ebkr/r2modman/) | Latest | Manage BepInEx and launch modded Aska |
| [Aska](https://store.steampowered.com/app/1898300/ASKA/) | Steam | The game |
| Git | Any | Source control |

Optional but recommended:
- **Visual Studio 2022+** or **Rider** for IDE support
- **dnSpyEx** for inspecting interop assemblies

## Initial Setup

### 1. Install BepInEx via r2modman

1. Open r2modman, select **Aska**
2. Install **BepInExPack_IL2CPP** from the online mod list
3. Click **Start modded** once and close the game -- this generates interop assemblies

### 2. Clone and build

```bash
git clone https://github.com/YOUR_USERNAME/aska-performance-booster.git
cd aska-performance-booster
dotnet build -c Release
```

The project references BepInEx DLLs from the default r2modman profile path:

```
%APPDATA%\r2modmanPlus-local\ASKA\profiles\Default\BepInEx\
```

If your r2modman installation is elsewhere, override the path:

```bash
dotnet build -c Release -p:R2ModManDir="C:\path\to\BepInEx"
```

### 3. Install the built DLL

Copy `bin\Release\net6.0\AskaPerformanceBooster.dll` to your r2modman plugins folder:

```
%APPDATA%\r2modmanPlus-local\ASKA\profiles\Default\BepInEx\plugins\
```

Launch via r2modman's **Start modded** button.

## Project Structure

```
aska-performance-booster/
  AskaPerformanceBooster.csproj  # Project file (references BepInEx/interop DLLs)
  PerformancePlugin.cs           # BepInEx BasePlugin entry point, config bindings
  PerformanceBehaviour.cs        # MonoBehaviour: applies all optimizations at runtime
  HDRPReflectionHelper.cs        # Static helper: HDRP Asset reflection, pipeline support flags
  PresetApplicator.cs            # Preset definitions (Vanilla/Moderate/Custom)
  QualityLevelPatch.cs           # Harmony postfix on QualitySettings.SetQualityLevel
  TargetFrameRatePatch.cs        # Harmony prefix on Application.targetFrameRate setter
  thunderstore/                  # Thunderstore package files
    manifest.json
    README.md
    icon.png
```

## Architecture Overview

### Design philosophy

**"Read before write, only improve."** Every setting is checked against the game's current value before being overridden. The mod never blindly forces values -- it respects that Aska is a shipped, actively-tuned game.

### Aska's Render Pipeline

Aska uses **HDRP** (High Definition Render Pipeline). The IL2CPP concrete type is `UnityEngine.Rendering.HighDefinition.HDRenderPipelineAsset`. All HDRP-specific types are accessed via reflection at runtime -- there is no compile-time dependency on the HDRP assembly.

### How the mod works

1. **`PerformancePlugin.Load()`** binds config entries, applies a preset if selected, registers Harmony patches via tolerant `TryPatchClass()`, and adds `PerformanceBehaviour` to a persistent GameObject. Config entries use sentinel values (0, -1, false) to mean "don't override."

2. **`PerformanceBehaviour.Update()`** detects gameplay state (`StreamingWorld` scene active). In diagnostic scan mode, it logs all current values without changing anything. In normal mode, it applies settings with read-before-write logic and re-applies periodically.

3. **Settings are applied in two tiers (all read-before-write):**
   - **Tier 1 (Global):** QualitySettings API (LOD bias, textures, async upload), HDRP Asset pipeline support flags (the PRIMARY optimization -- sets supportSSAO=false etc. via NativeFieldInfoPtr), SRP Batcher verification
   - **Tier 2 (Volume):** Cosmetic post-processing Volume disables (film grain, chromatic aberration, etc.), HDShadowSettings Volume (shadow distance, cascades), Volume component disables for contact shadows/volumetric clouds/SSS

4. **`QualityLevelPatch`** detects when the game changes quality level, triggering immediate cache invalidation and full reapplication.

5. **`PresetApplicator`** sets all config values for a selected preset, using sentinel values for settings that should not be overridden.

### Key design decisions

- **Rendering-only mod** -- never touches player transforms, input, or networked state
- **Pipeline support flags are the primary optimization** -- confirmed 4-5+ FPS improvement. These write directly to native memory via NativeFieldInfoPtr property setters on the HDRP Asset's RenderPipelineSettings struct
- **Graceful degradation** -- every interop call is wrapped in try/catch; missing properties silently skipped
- **Managed reflection for HDRP properties** -- System.Reflection on Il2CppInterop wrapper types
- **Type name substring matching** for volume components -- handles namespace variations
- **Periodic reapply** -- timer counteracts game quality level resets
- **Preset-then-Custom pattern** -- presets write to config entries and auto-reset

## Development Workflow

### Quick iteration

```bash
dotnet build -c Release && cp bin/Release/net6.0/AskaPerformanceBooster.dll "$APPDATA/r2modmanPlus-local/ASKA/profiles/Default/BepInEx/plugins/"
```

Then launch via r2modman. Check `BepInEx/LogOutput.log` for your plugin's log messages.

### Debug logging

Enable `DebugLogging = true` in the config file to see detailed per-setting application logs prefixed with `[Debug]`. This shows every property set, every reflection lookup, and every optimization applied.

### Debugging tips

- Check `LogOutput.log` for lines starting with `[Aska Performance Booster]`
- "HDRP Asset: ..." confirms the render pipeline asset was accessed and lists settable properties
- "Frame Setting X: set to false" confirms per-camera feature toggles are working
- Warnings about "Error applying ..." indicate which tiers are failing
- Delete `BepInEx/config/com.community.askaperformancebooster.cfg` to reset all settings
- Delete `BepInEx/interop/` after game updates to regenerate interop assemblies

## How to Contribute

### Reporting issues

- Include your `BepInEx/LogOutput.log` (the full file, not just errors)
- Note your GPU model and approximate FPS before/after changes
- Enable DebugLogging and include those lines
- Mention any other mods installed

### Submitting changes

**All contributions must be submitted via Pull Request.**

1. **Fork** the repository on GitHub
2. **Clone** your fork
3. **Create a feature branch:** `git checkout -b feature/my-change`
4. Make your changes
5. **Test** in both singleplayer and multiplayer if possible
6. **Verify the build:** `dotnet build -c Release`
7. **Commit** with a clear message describing what and why
8. **Push** and **Open a Pull Request** against `main`

### Code guidelines

- **Target .NET 6.0** -- BepInEx 6 IL2CPP plugins use this framework
- **Wrap all interop calls in try/catch** -- IL2CPP can throw at any time
- **Use managed System.Reflection for HDRP properties** -- not compile-time type references
- **Convert enums via `Enum.ToObject()`** -- IL2CPP wrapper types need the actual enum type
- **Match volume components by type name substring** -- not exact type comparison
- **Don't touch player state** -- this mod is rendering-only
- **Read before write** -- always check the game's current value before overriding
- **Only reduce, never increase** -- shadow settings, quality levels should only lower, not raise
- **Prefer reduction over removal where possible** -- the Moderate preset both reduces SSR/SSAO quality via Volume overrides and disables via Frame Settings
- **Use sentinel values** -- 0, -1, or false to mean "don't override this setting"
- **Test all 3 presets** -- Vanilla, Moderate, Custom should all produce valid results
- **Test diagnostic scan mode** -- verify it logs correctly without changing anything
- **Log sparingly** -- one info line per application cycle, debug lines gated behind config toggle
- **Add config descriptions** -- every setting needs a tooltip explaining what it does and its visual impact

### Areas where help is welcome

- **HDRP Frame Settings discovery** -- test which FrameSettingsField values exist in Aska's Unity 6 HDRP build
- **Performance profiling** -- quantify FPS gains per optimization on different GPUs
- **HDRP Volume parameter discovery** -- map exact field names for SSR/SSAO/fog quality parameters
- **Shadow atlas field discovery** -- the m_RenderPipelineSettings nested struct is hard to navigate via reflection
- **Aska-specific rendering features** -- find game-specific wasteful settings
- **Game updates** -- Aska updates may change HDRP settings or add new rendering features

## Releasing

> **Note:** The project cannot be built in CI because it depends on BepInEx interop DLLs
> generated at runtime on each machine. Builds are done locally.

1. Update **both** version locations -- they must match:
   - `PerformancePlugin.cs` -> `PluginVersion`
   - `thunderstore/manifest.json` -> `version_number`
2. Build and package: `./package.sh`
3. Commit, tag `vX.Y.Z`, push
4. Upload zip to Thunderstore and Nexus

## License

By contributing, you agree that your contributions will be licensed under the same license as the project.
