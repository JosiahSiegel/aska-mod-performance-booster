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
  PerformanceBehaviour.cs        # MonoBehaviour: applies optimizations at runtime
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

**"Only ship what is confirmed working."** Every optimization in this mod has been tested and shown measurable FPS impact. Nothing speculative, nothing unverified.

### Confirmed optimizations (6 total)

1. **HDRP Pipeline Support Flags** (the primary optimization, +11 to +24 FPS) -- writes `supportSSAO=false`, `supportVolumetrics=false`, etc. on the HDRP Asset via NativeFieldInfoPtr property setters
2. **Frame rate uncap** -- removes Aska's hard-coded 60 FPS cap via Harmony patch on `Application.targetFrameRate` setter
3. **Small shadow caster disable** -- `shadowCastingMode = Off` on 831+ small renderers per session
4. **SRP Batcher force-on** -- log confirmed `false -> true`
5. **HDRP Asset stale check** -- detects quality level changes, re-applies pipeline flags
6. **Quality level change detection** -- Harmony postfix on `QualitySettings.SetQualityLevel`

### How the mod works

1. **`PerformancePlugin.Load()`** binds config entries, applies a preset if selected, registers Harmony patches, and adds `PerformanceBehaviour` to a persistent GameObject.

2. **`PerformanceBehaviour.Update()`** detects gameplay state (`StreamingWorld` scene), applies pipeline support flags and SRP Batcher, sets targetFrameRate, and runs the small shadow caster scan after 5 seconds of gameplay.

3. **`QualityLevelPatch`** detects when the game changes quality level, triggering cache invalidation and full reapplication.

4. **`TargetFrameRatePatch`** intercepts the game's writes to `Application.targetFrameRate` and replaces them with our configured value.

### Key design decisions

- **Rendering-only mod** -- never touches player transforms, input, or networked state
- **Pipeline support flags are the primary optimization** -- confirmed +11 to +24 FPS improvement
- **Graceful degradation** -- every interop call is wrapped in try/catch
- **HDRPReflectionHelper is a static class** -- IL2CPP ClassInjector can't handle System.Object/Type parameters on MonoBehaviour instance methods
- **Periodic reapply** -- timer counteracts any game resets

## Development Workflow

### Quick iteration

```bash
dotnet build -c Release && cp bin/Release/net6.0/AskaPerformanceBooster.dll "$APPDATA/r2modmanPlus-local/ASKA/profiles/Default/BepInEx/plugins/"
```

Then launch via r2modman. Check `BepInEx/LogOutput.log` for your plugin's log messages.

### Debug logging

Enable `DebugLogging = true` in the config file to see detailed per-setting application logs prefixed with `[Debug]`.

## How to Contribute

### Reporting issues

- Include your `BepInEx/LogOutput.log` (the full file, not just errors)
- Note your GPU model and approximate FPS before/after changes
- Enable DebugLogging and include those lines
- Mention any other mods installed

### Submitting changes

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
- **Don't touch player state** -- this mod is rendering-only
- **Only add confirmed-working optimizations** -- every feature must have proven FPS impact before merging
- **Log sparingly** -- one info line per application cycle, debug lines gated behind config toggle

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
