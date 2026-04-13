using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine.Rendering;
using HarmonyLogger = HarmonyLib.Tools.Logger;

namespace AskaPerformanceBooster;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency("com.community.askafirstperson", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("Fisst.AskaPlus", BepInDependency.DependencyFlags.SoftDependency)]
public class PerformancePlugin : BasePlugin
{
    public const string PluginGuid = "com.community.askaperformancebooster";
    public const string PluginName = "Aska Performance Booster";
    public const string PluginVersion = "1.2.0";

    internal static new ManualLogSource Log;

    // =====================================================================
    //  0. Preset & debug
    // =====================================================================
    public static ConfigEntry<PerformancePreset> CfgPreset;
    public static ConfigEntry<bool> CfgDebugLogging;

    // =====================================================================
    //  1. Pipeline support flags (the PRIMARY optimization)
    // =====================================================================
    public static ConfigEntry<bool> CfgPipelineDisableSSR;
    public static ConfigEntry<bool> CfgPipelineDisableSSAO;
    public static ConfigEntry<bool> CfgPipelineDisableVolumetrics;
    public static ConfigEntry<bool> CfgPipelineDisableVolumetricClouds;
    public static ConfigEntry<bool> CfgPipelineDisableSubsurfaceScattering;
    public static ConfigEntry<bool> CfgPipelineDisableDecals;
    public static ConfigEntry<bool> CfgPipelineDisableDistortion;
    public static ConfigEntry<bool> CfgPipelineDisableSSRTransparent;
    public static ConfigEntry<bool> CfgPipelineDisableScreenSpaceLensFlare;
    public static ConfigEntry<bool> CfgPipelineDisableDataDrivenLensFlare;

    // =====================================================================
    //  2. Shadows
    // =====================================================================
    public static ConfigEntry<bool> CfgDisableSmallShadowCasters;
    public static ConfigEntry<float> CfgSmallShadowCasterThreshold;
    public static ConfigEntry<bool> CfgDisableResourceShadowCasters;
    public static ConfigEntry<float> CfgResourceShadowCasterThreshold;

    public static ConfigEntry<bool> CfgDisableEnvironmentShadowCasters;
    public static ConfigEntry<float> CfgEnvironmentShadowCasterThreshold;

    // HDRP Shadow Init Parameters (hdShadowInitParams on HDRP Asset)
    public static ConfigEntry<int> CfgShadowMaxShadowRequests;
    public static ConfigEntry<int> CfgShadowMaxDirectionalResolution;
    public static ConfigEntry<int> CfgShadowMaxAreaResolution;
    public static ConfigEntry<int> CfgShadowAreaFilteringQuality;

    // =====================================================================
    //  3. Draw Calls
    // =====================================================================
    public static ConfigEntry<bool> CfgForceSRPBatcher;

    // =====================================================================
    //  4. Frame Rate
    // =====================================================================
    public static ConfigEntry<int> CfgTargetFrameRate;

    // =====================================================================
    //  5. Misc
    // =====================================================================
    public static ConfigEntry<float> CfgReapplyInterval;
    public static ConfigEntry<float> CfgLodBias;

    // =====================================================================
    //  6. Diagnostics
    // =====================================================================
    public static ConfigEntry<bool> CfgEnableDiagnostics;
    public static ConfigEntry<float> CfgDiagnosticIntervalSeconds;
    public static ConfigEntry<bool> CfgLogObjectBreakdown;
    public static ConfigEntry<bool> CfgLogFrameTimings;

    public override void Load()
    {
        Log = base.Log;

        BindConfig();

        var harmony = new Harmony(PluginGuid);
        var active = new List<string>();

        var savedHarmonyChannels = HarmonyLogger.ChannelFilter;
        HarmonyLogger.ChannelFilter = savedHarmonyChannels & ~HarmonyLogger.LogChannel.Warn;
        try
        {
            if (TryPatchClass(harmony, typeof(QualityLevelPatch)))
                active.Add("QualityLevel");
            if (TryPatchClass(harmony, typeof(TargetFrameRatePatch)))
                active.Add("TargetFrameRate");
        }
        finally
        {
            HarmonyLogger.ChannelFilter = savedHarmonyChannels;
        }

        Log.LogInfo($"Harmony patches active: [{string.Join(", ", active)}]");

        AddComponent<PerformanceBehaviour>();

        ProbeRenderPipeline();

        Log.LogInfo($"{PluginName} v{PluginVersion} loaded (preset: {CfgPreset.Value}" +
                    $", reapply={CfgReapplyInterval.Value}s)");
    }

    /// <summary>
    /// Probes GraphicsSettings.currentRenderPipeline at plugin load time to
    /// confirm HDRP detection and IL2CPP type resolution are working.
    /// </summary>
    private void ProbeRenderPipeline()
    {
        try
        {
            var pipelineAsset = GraphicsSettings.currentRenderPipeline;
            if (pipelineAsset == null)
            {
                Log.LogInfo("[Pipeline] No render pipeline asset detected (Built-in RP or not yet loaded).");
                return;
            }

            string managedTypeName = pipelineAsset.GetType().FullName;
            string il2cppTypeName = "unknown";
            try
            {
                il2cppTypeName = pipelineAsset.GetIl2CppType()?.FullName ?? "unknown";
            }
            catch { }

            bool isHDRP = il2cppTypeName.Contains("HighDefinition") ||
                          managedTypeName.Contains("HighDefinition") ||
                          il2cppTypeName.Contains("HDRenderPipeline") ||
                          managedTypeName.Contains("HDRenderPipeline");

            Log.LogInfo($"[Pipeline] Detected: {pipelineAsset.name} " +
                        $"(managed={managedTypeName}, il2cpp={il2cppTypeName}). " +
                        (isHDRP
                            ? "HDRP confirmed."
                            : "WARNING: Expected HDRP but type does not match. Optimizations may not apply."));
        }
        catch (Exception ex)
        {
            Log.LogWarning($"[Pipeline] Early probe failed: {ex.Message}");
        }
    }

    private void BindConfig()
    {
        // -- 0. Preset --
        CfgPreset = Config.Bind("0. Preset", "Preset", PerformancePreset.Moderate,
            "Select a performance optimization preset to auto-configure all settings below.\n" +
            "  Moderate (default) = disables expensive HDRP pipeline features the game menu\n" +
            "    does not expose, removes the 60 FPS cap, disables shadows on small objects,\n" +
            "    and forces SRP Batcher on. Confirmed 4-5+ FPS gain.\n" +
            "  Vanilla = all optimizations disabled, stock game behavior.\n" +
            "  Custom = use your manually edited values below.\n" +
            "Presets are sticky: the chosen preset re-applies its values on every launch.\n" +
            "To tweak individual settings manually, set this to Custom first.");

        CfgDebugLogging = Config.Bind("0. Preset", "DebugLogging", false,
            "Enable verbose debug logging showing every setting applied, " +
            "including the game's current value vs the value we're setting.");

        // -- 1. Pipeline --
        CfgPipelineDisableSSR = Config.Bind("1. Pipeline", "PipelineDisableSSR", false,
            "PIPELINE-LEVEL disable of Screen Space Reflections. " +
            "Sets supportSSR=false on the HDRP Asset. " +
            "WARNING: Causes a stale-texture artifact (screen image stamped onto " +
            "reflective objects). Kept false in all presets.");

        CfgPipelineDisableSSAO = Config.Bind("1. Pipeline", "PipelineDisableSSAO", false,
            "PIPELINE-LEVEL disable of Screen Space Ambient Occlusion. " +
            "Sets supportSSAO=false on the HDRP Asset. " +
            "Removes all ambient occlusion. Noticeable visual change but saves GPU time.");

        CfgPipelineDisableVolumetrics = Config.Bind("1. Pipeline", "PipelineDisableVolumetrics", false,
            "PIPELINE-LEVEL disable of volumetric fog/lighting. " +
            "Sets supportVolumetrics=false on the HDRP Asset. " +
            "Removes all fog and volumetric lighting. Major visual change.");

        CfgPipelineDisableVolumetricClouds = Config.Bind("1. Pipeline", "PipelineDisableVolumetricClouds", false,
            "PIPELINE-LEVEL disable of volumetric clouds. " +
            "Sets supportVolumetricClouds=false on the HDRP Asset.");

        CfgPipelineDisableSubsurfaceScattering = Config.Bind("1. Pipeline", "PipelineDisableSubsurfaceScattering", false,
            "PIPELINE-LEVEL disable of subsurface scattering. " +
            "Sets supportSubsurfaceScattering=false on the HDRP Asset. " +
            "Affects skin and foliage translucency rendering.");

        CfgPipelineDisableDecals = Config.Bind("1. Pipeline", "PipelineDisableDecals", false,
            "PIPELINE-LEVEL disable of decal rendering. " +
            "Sets supportDecals=false on the HDRP Asset. " +
            "WARNING: Aska uses HDRP DecalProjectors for the terraforming grid overlay " +
            "(green/red placement squares). Enabling this WILL hide that grid. " +
            "Kept false in all presets.");

        CfgPipelineDisableDistortion = Config.Bind("1. Pipeline", "PipelineDisableDistortion", false,
            "PIPELINE-LEVEL disable of distortion effects (heat haze, refraction). " +
            "Sets supportDistortion=false on the HDRP Asset. " +
            "WARNING: Like PipelineDisableSSR, can cause stale-texture artifacts. Kept false.");

        CfgPipelineDisableSSRTransparent = Config.Bind("1. Pipeline", "PipelineDisableSSRTransparent", false,
            "PIPELINE-LEVEL disable of SSR on transparent objects. " +
            "Sets supportSSRTransparent=false on the HDRP Asset. " +
            "WARNING: Like PipelineDisableSSR, can cause stale-texture artifacts. Kept false.");

        CfgPipelineDisableScreenSpaceLensFlare = Config.Bind("1. Pipeline", "PipelineDisableScreenSpaceLensFlare", false,
            "PIPELINE-LEVEL disable of screen-space lens flares. " +
            "Sets supportScreenSpaceLensFlare=false on the HDRP Asset.");

        CfgPipelineDisableDataDrivenLensFlare = Config.Bind("1. Pipeline", "PipelineDisableDataDrivenLensFlare", false,
            "PIPELINE-LEVEL disable of data-driven lens flares. " +
            "Sets supportDataDrivenLensFlare=false on the HDRP Asset.");

        // -- 2. Shadows --
        CfgDisableSmallShadowCasters = Config.Bind("2. Shadows", "DisableSmallShadowCasters", true,
            "Disable shadow casting on small renderers (rocks, plants, debris). " +
            "831+ objects confirmed disabled per session. Each costs a draw call in " +
            "the shadow depth pass but produces barely visible shadows.");

        CfgSmallShadowCasterThreshold = Config.Bind("2. Shadows", "SmallShadowCasterThreshold", 1.0f,
            new ConfigDescription(
                "Bounds size magnitude threshold (metres) below which renderers " +
                "have their shadow casting disabled. Only used when " +
                "DisableSmallShadowCasters is true.",
                new AcceptableValueRange<float>(0.1f, 5.0f)));

        CfgDisableResourceShadowCasters = Config.Bind("2. Shadows", "DisableResourceShadowCasters", true,
            "Disable shadow casting on player-accumulable resource objects " +
            "(sticks, firewood, stones, resin, bark, logs, bone fragments). " +
            "These pile up in the hundreds/thousands as the player chops and gathers, " +
            "and their shadows are imperceptible. 5,100+ objects confirmed in late-game sessions. " +
            "Uses a higher bounds threshold than small shadow casters since many " +
            "resource objects (e.g. logs) exceed 1.0m but still don't need shadows.");

        CfgResourceShadowCasterThreshold = Config.Bind("2. Shadows", "ResourceShadowCasterThreshold", 5.0f,
            new ConfigDescription(
                "Bounds size magnitude threshold (metres) for resource shadow caster " +
                "disabling. Resource objects below this size have shadows disabled. " +
                "Higher than SmallShadowCasterThreshold because resource objects like " +
                "logs can be large but still don't produce meaningful shadows. " +
                "Only used when DisableResourceShadowCasters is true.",
                new AcceptableValueRange<float>(0.5f, 15.0f)));

        CfgDisableEnvironmentShadowCasters = Config.Bind("2. Shadows", "DisableEnvironmentShadowCasters", true,
            "Disable shadow casting on environment objects that produce imperceptible " +
            "shadows (grass clumps, cave flora). Grass shadows fall on other grass " +
            "creating uniform darkening that looks nearly identical without them. " +
            "~1,500+ objects in typical scenes.");

        CfgEnvironmentShadowCasterThreshold = Config.Bind("2. Shadows", "EnvironmentShadowCasterThreshold", 5.0f,
            new ConfigDescription(
                "Bounds size magnitude threshold (metres) for environment shadow caster " +
                "disabling. Environment objects below this size have shadows disabled. " +
                "Only used when DisableEnvironmentShadowCasters is true.",
                new AcceptableValueRange<float>(0.5f, 15.0f)));

        CfgShadowMaxShadowRequests = Config.Bind("2. Shadows", "ShadowMaxShadowRequests", 0,
            new ConfigDescription(
                "HDRP Asset: maximum shadow map render requests per frame. " +
                "Each shadow-casting light consumes one or more requests (directional " +
                "cascades use 4). Game default is 128. Reducing to 48 cuts GPU shadow " +
                "workload while covering typical Aska scenes. " +
                "0 = don't override (use game default).",
                new AcceptableValueRange<int>(0, 128)));

        CfgShadowMaxDirectionalResolution = Config.Bind("2. Shadows", "ShadowMaxDirectionalResolution", 0,
            new ConfigDescription(
                "HDRP Asset: maximum resolution per directional shadow cascade. " +
                "Game default is 2048. Reducing to 1024 cuts directional shadow fill " +
                "rate by 75% (4 cascades x 2048^2 -> 4 x 1024^2). Shadow edges " +
                "become softer but filtering is already Low. " +
                "0 = don't override. Must be power of 2.",
                new AcceptableValueRange<int>(0, 4096)));

        CfgShadowMaxAreaResolution = Config.Bind("2. Shadows", "ShadowMaxAreaResolution", 0,
            new ConfigDescription(
                "HDRP Asset: maximum area light shadow map resolution. " +
                "Game default is 2048. Reducing to 1024 saves GPU fill rate on " +
                "area light shadows. Area shadows are inherently soft so the " +
                "quality loss is minimal. 0 = don't override. Must be power of 2.",
                new AcceptableValueRange<int>(0, 4096)));

        CfgShadowAreaFilteringQuality = Config.Bind("2. Shadows", "ShadowAreaFilteringQuality", -1,
            new ConfigDescription(
                "HDRP Asset: area light shadow filtering quality. " +
                "Game default is Medium (1). Low (0) reduces PCF filter taps, " +
                "matching the punctual and directional filtering which are already Low. " +
                "-1 = don't override. Values: 0=Low, 1=Medium, 2=High.",
                new AcceptableValueRange<int>(-1, 2)));

        // -- 3. Draw Calls --
        CfgForceSRPBatcher = Config.Bind("3. Draw Calls", "ForceSRPBatcher", true,
            "Force the SRP Batcher on. Log confirmed Aska ships with it off " +
            "(false -> true). No visual change.");

        // -- 4. Frame Rate --
        CfgTargetFrameRate = Config.Bind("4. Frame Rate", "TargetFrameRate", -1,
            new ConfigDescription(
                "Target frame rate when VSync is off. " +
                "Aska sets Application.targetFrameRate = 60 at startup, hard-capping " +
                "all VSync-off users to 60 FPS regardless of GPU headroom. " +
                "A Harmony patch blocks the game from overriding this value. " +
                "-1 = unlimited (removes the 60 FPS cap), 0 = don't override, " +
                ">0 = cap at that FPS. " +
                "When VSync is on, Unity ignores targetFrameRate entirely.",
                new AcceptableValueRange<int>(-1, 300)));

        // -- 5. Misc --
        CfgLodBias = Config.Bind("5. Misc", "LodBias", 0f,
            new ConfigDescription(
                "Override LOD bias to force lower-poly LOD levels sooner. " +
                "Reduces vertex count for distant objects across 9,000-10,000+ renderers. " +
                "Game default is typically 1.0-2.0. Reducing to 0.75 forces LOD transitions " +
                "closer to the camera with subtle visual change at medium distance. " +
                "0 = don't override (use game default).",
                new AcceptableValueRange<float>(0f, 5.0f)));

        CfgReapplyInterval = Config.Bind("5. Misc", "ReapplyIntervalSeconds", 10f,
            new ConfigDescription(
                "How often (seconds) to re-apply settings as a safety net. " +
                "Quality level changes are detected instantly via Harmony patch. " +
                "0 = apply once only.",
                new AcceptableValueRange<float>(0f, 120f)));

        // -- 6. Diagnostics --
        CfgEnableDiagnostics = Config.Bind("6. Diagnostics", "EnableDiagnostics", false,
            "Master toggle for runtime diagnostics logging. " +
            "When enabled, logs FPS, renderer counts, rigidbody counts, and " +
            "shadow stats to BepInEx/LogOutput.log at a configurable interval. " +
            "Zero cost when disabled.");

        CfgDiagnosticIntervalSeconds = Config.Bind("6. Diagnostics", "DiagnosticIntervalSeconds", 10f,
            new ConfigDescription(
                "How often (seconds) to log diagnostic stats. " +
                "Lower values give more granular data but produce more log output.",
                new AcceptableValueRange<float>(5f, 120f)));

        CfgLogObjectBreakdown = Config.Bind("6. Diagnostics", "LogObjectBreakdown", false,
            "EXPENSIVE: Categorize all renderers by GameObject name and log " +
            "the top 10 categories by count. Useful for identifying which object " +
            "types dominate the scene. Only runs on the diagnostic interval.");

        CfgLogFrameTimings = Config.Bind("6. Diagnostics", "LogFrameTimings", false,
            "Use Unity's FrameTimingManager to log CPU vs GPU frame times. " +
            "Shows whether the game is CPU-bound or GPU-bound. " +
            "CaptureFrameTimings() is called every frame (cheap); " +
            "timings are only read and logged on the diagnostic interval.");

        // -- Apply preset if not Custom --
        Log.LogInfo($"Config preset on load: {CfgPreset.Value}");
        if (CfgPreset.Value != PerformancePreset.Custom)
        {
            var requestedPreset = CfgPreset.Value;
            PresetApplicator.Apply(requestedPreset);
            Log.LogInfo($"Applied \"{requestedPreset}\" preset.");
        }
    }

    private static bool TryPatchClass(Harmony harmony, Type type)
    {
        try
        {
            harmony.CreateClassProcessor(type).Patch();
            return true;
        }
        catch (Exception ex)
        {
            Log.LogWarning($"Skipping Harmony patch {type.Name}: {ex.Message}");
            return false;
        }
    }
}

public enum PerformancePreset
{
    /// <summary>Use manual config values.</summary>
    Custom,
    /// <summary>All optimizations disabled, stock game behavior.</summary>
    Vanilla,
    /// <summary>Pipeline flags + frame rate uncap + small shadow casters + SRP Batcher. Confirmed 4-5+ FPS.</summary>
    Moderate
}
