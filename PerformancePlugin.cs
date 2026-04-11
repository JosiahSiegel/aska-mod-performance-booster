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

/// <summary>
/// Soft dependencies ensure we load AFTER these mods so our read-before-write
/// logic sees their changes rather than the game's defaults. If any of these
/// mods are not installed, BepInEx simply ignores the attribute.
/// </summary>
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency("com.community.askafirstperson", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("Fisst.AskaPlus", BepInDependency.DependencyFlags.SoftDependency)]
public class PerformancePlugin : BasePlugin
{
    public const string PluginGuid = "com.community.askaperformancebooster";
    public const string PluginName = "Aska Performance Booster";
    public const string PluginVersion = "1.0.0";

    internal static new ManualLogSource Log;

    // =====================================================================
    //  Preset & mode selection
    // =====================================================================
    public static ConfigEntry<PerformancePreset> CfgPreset;
    public static ConfigEntry<bool> CfgDebugLogging;
    public static ConfigEntry<bool> CfgDiagnosticScan;

    // =====================================================================
    //  1. Draw Calls & Batching
    // =====================================================================
    public static ConfigEntry<bool> CfgForceSRPBatcher;
    public static ConfigEntry<int> CfgGPUResidentDrawer;

    // =====================================================================
    //  2. Shadows
    // =====================================================================
    public static ConfigEntry<float> CfgShadowDistance;
    public static ConfigEntry<int> CfgShadowCascades;
    public static ConfigEntry<bool> CfgDisableContactShadows;
    public static ConfigEntry<bool> CfgDisableMicroShadows;
    public static ConfigEntry<bool> CfgDisableScreenSpaceShadows;
    public static ConfigEntry<bool> CfgDisableSmallShadowCasters;
    public static ConfigEntry<float> CfgSmallShadowCasterThreshold;

    // =====================================================================
    //  3. Textures & Streaming
    // =====================================================================
    public static ConfigEntry<bool> CfgStreamingMipmapsActive;
    public static ConfigEntry<int> CfgStreamingMipmapsMemoryBudget;
    public static ConfigEntry<int> CfgStreamingMipmapsMaxLevelReduction;
    public static ConfigEntry<int> CfgAnisotropicFiltering;
    public static ConfigEntry<int> CfgGlobalTextureMipmapLimit;

    // =====================================================================
    //  4. Post-Processing (Volume Overrides)
    // =====================================================================
    public static ConfigEntry<bool> CfgDisableFilmGrain;
    public static ConfigEntry<bool> CfgDisableChromaticAberration;
    public static ConfigEntry<bool> CfgDisableLensDistortion;
    public static ConfigEntry<bool> CfgDisableMotionBlur;
    public static ConfigEntry<bool> CfgReduceBloomIterations;
    public static ConfigEntry<int> CfgBloomMaxIterations;

    // =====================================================================
    //  5. Culling & Overdraw
    // =====================================================================
    public static ConfigEntry<float> CfgShadowDistanceFallback;

    // =====================================================================
    //  6. LOD & Mesh
    // =====================================================================
    public static ConfigEntry<float> CfgLODBias;
    public static ConfigEntry<int> CfgMaximumLODLevel;
    public static ConfigEntry<int> CfgSkinWeights;

    // =====================================================================
    //  7. HDRP Pipeline (pipeline-level support flag overrides)
    // =====================================================================
    // HDRP Asset support flag overrides -- the PRIMARY optimization mechanism.
    // These write directly to RenderPipelineSettings via NativeFieldInfoPtr,
    // bypassing IL2CPP managed wrappers. HDRP checks them per-frame during
    // Frame Settings aggregation. Confirmed 4-5+ FPS improvement.
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

    // Cosmetic Volume component disables (VolumeComponent.active = false).
    // These disable the Volume components so HDRP does not read their parameters.
    // Works for cosmetic post-processing and may work for rendering effects.
    public static ConfigEntry<bool> CfgDisableVolumetricClouds;
    public static ConfigEntry<bool> CfgDisableSubsurfaceScattering;

    // =====================================================================
    //  8. Reflection Probes & Lighting
    // =====================================================================
    public static ConfigEntry<bool> CfgRealtimeReflectionProbes;

    // =====================================================================
    //  9. Async & Threading
    // =====================================================================
    public static ConfigEntry<int> CfgAsyncUploadTimeSlice;
    public static ConfigEntry<int> CfgAsyncUploadBufferSize;

    // =====================================================================
    //  10. Miscellaneous
    // =====================================================================
    public static ConfigEntry<int> CfgVSyncCount;
    public static ConfigEntry<int> CfgTargetFrameRate;

    // =====================================================================
    //  11. Advanced GPU
    // =====================================================================
    public static ConfigEntry<bool> CfgShaderWarmup;
    public static ConfigEntry<int> CfgMaxQueuedFrames;

    // =====================================================================
    //  12. Post-Processing Extra (Volume Overrides)
    // =====================================================================
    public static ConfigEntry<bool> CfgDisableDepthOfField;
    public static ConfigEntry<bool> CfgDisableVignette;
    public static ConfigEntry<bool> CfgDisablePaniniProjection;
    public static ConfigEntry<bool> CfgDisableDataDrivenLensFlare;
    public static ConfigEntry<bool> CfgDisableScreenSpaceLensFlare;

    // =====================================================================
    //  13. Mod Compatibility
    // =====================================================================
    public static ConfigEntry<float> CfgReapplyInterval;
    public static ConfigEntry<bool> CfgReapplyPostProcessing;
    public static ConfigEntry<bool> CfgRespectExternalChanges;
    public static ConfigEntry<bool> CfgEnableDrawCallOptimizations;
    public static ConfigEntry<bool> CfgEnableShadowOptimizations;
    public static ConfigEntry<bool> CfgEnableTextureOptimizations;
    public static ConfigEntry<bool> CfgEnablePostProcessingOptimizations;
    public static ConfigEntry<bool> CfgEnableLODOptimizations;
    public static ConfigEntry<bool> CfgEnableHDRPPipelineOptimizations;
    public static ConfigEntry<bool> CfgEnableLightingOptimizations;
    public static ConfigEntry<bool> CfgEnableAdvancedGPUOptimizations;

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

        // Detect other loaded mods that might interact with rendering
        DetectKnownMods();

        AddComponent<PerformanceBehaviour>();

        // Early render pipeline probe -- logs the detected pipeline type at
        // startup so we can verify HDRP detection and IL2CPP type resolution
        // even from a menu-only log (before entering gameplay).
        ProbeRenderPipeline();

        Log.LogInfo($"{PluginName} v{PluginVersion} loaded (preset: {CfgPreset.Value}" +
                    $", reapply={CfgReapplyInterval.Value}s" +
                    $", respectExternal={CfgRespectExternalChanges.Value}" +
                    (CfgDiagnosticScan.Value ? ", diagnostic scan enabled" : "") + ")");
    }

    /// <summary>
    /// Probes GraphicsSettings.currentRenderPipeline at plugin load time to
    /// log the detected render pipeline type. This runs before gameplay so
    /// it appears even in a menu-only log, confirming HDRP detection and
    /// IL2CPP type resolution are working.
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
                            ? "HDRP confirmed. Full HDRP reflection will run on gameplay entry."
                            : "WARNING: Expected HDRP but type does not match. Optimizations may not apply."));
        }
        catch (Exception ex)
        {
            Log.LogWarning($"[Pipeline] Early probe failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Scans BepInEx's loaded plugin list for known mods that might interact
    /// with rendering settings and logs compatibility warnings.
    /// </summary>
    private void DetectKnownMods()
    {
        try
        {
            var chainloader = IL2CPPChainloader.Instance;
            if (chainloader == null) return;

            var plugins = chainloader.Plugins;
            if (plugins == null) return;

            foreach (var kvp in plugins)
            {
                if (kvp.Value == null) continue;
                string guid = kvp.Key;
                string name = kvp.Value.Metadata?.Name ?? guid;

                // Known mods that touch cameras or renderers
                if (guid == "com.community.askafirstperson")
                {
                    Log.LogInfo($"[Compat] Detected {name} -- camera mod. " +
                        "No conflict expected (we don't touch cameras or renderers).");
                }

                // Log any unrecognized mod as informational
                if (guid != PluginGuid)
                {
                    Log.LogInfo($"[Compat] Co-loaded with: {name} ({guid})");
                }
            }
        }
        catch (Exception ex)
        {
            Log.LogWarning($"[Compat] Could not enumerate loaded plugins: {ex.Message}");
        }
    }

    private void BindConfig()
    {
        // -- Preset --
        CfgPreset = Config.Bind("0. Preset", "Preset", PerformancePreset.Moderate,
            "Select a performance optimization preset to auto-configure all settings below.\n" +
            "  Moderate (default) = disables expensive screen-space effects the game menu\n" +
            "    does not expose. Gives ~5-10 FPS for free. Best combined with High quality\n" +
            "    in-game + DLSS/FSR.\n" +
            "  Vanilla = all optimizations disabled, stock game behavior.\n" +
            "  Custom = use your manually edited values below.\n" +
            "Presets are sticky: the chosen preset re-applies its values on every launch.\n" +
            "To tweak individual settings manually, set this to Custom first.");

        CfgDebugLogging = Config.Bind("0. Preset", "DebugLogging", false,
            "Enable verbose debug logging showing every setting applied, " +
            "including the game's current value vs the value we're setting. " +
            "Useful for troubleshooting and verifying the mod is helping.");

        CfgDiagnosticScan = Config.Bind("0. Preset", "DiagnosticScan", false,
            "GPU BOTTLENECK DISCOVERY: When enabled, runs two diagnostic scans " +
            "IN ADDITION to normal optimizations (does not block them). " +
            "Part 1: HDRP pipeline/asset scan runs immediately on gameplay entry. " +
            "Part 2: Scene bottleneck scan (lights, shadow casters, cameras, " +
            "reflection probes) runs after 30 seconds of gameplay to capture the " +
            "fully loaded world. Check BepInEx/LogOutput.log after playing for 30s.");

        // -- 1. Draw Calls & Batching --
        CfgForceSRPBatcher = Config.Bind("1. Draw Calls", "ForceSRPBatcher", true,
            "Force the SRP Batcher on. In HDRP, the SRP Batcher is the primary " +
            "draw call optimization. We verify it is enabled and force it on only " +
            "if it is off. No visual change.");

        CfgGPUResidentDrawer = Config.Bind("1. Draw Calls", "GPUResidentDrawer", 0,
            new ConfigDescription(
                "[NON-FUNCTIONAL] GPU Resident Drawer mode (Unity 6). " +
                "This setting is IGNORED at runtime. Changing gpuResidentDrawerMode " +
                "at runtime causes HDRP to reinitialize its internal Blitter, " +
                "producing a crash loop ('Blitter is already initialized'). " +
                "0 = Disabled (default), 1 = InstancedDrawing (ignored).",
                new AcceptableValueRange<int>(0, 1)));

        // -- 2. Shadows --
        CfgShadowDistance = Config.Bind("2. Shadows", "ShadowDistance", 120f,
            new ConfigDescription(
                "Maximum shadow render distance (metres) applied via HDShadowSettings " +
                "Volume override. Only applied if the game's current value is HIGHER " +
                "than this (i.e., we only reduce, never increase). Set to 0 to skip.",
                new AcceptableValueRange<float>(0f, 300f)));

        CfgShadowCascades = Config.Bind("2. Shadows", "ShadowCascades", 2,
            new ConfigDescription(
                "Number of shadow cascade splits (1/2/4) applied via HDShadowSettings " +
                "Volume override. Set to 4 to skip override.",
                new AcceptableValueList<int>(1, 2, 4)));

        CfgDisableContactShadows = Config.Bind("2. Shadows", "DisableContactShadows", true,
            "Disable contact shadows via Volume component deactivation. " +
            "Contact shadows add fine detail at close range but cost a full-screen pass.");

        CfgDisableMicroShadows = Config.Bind("2. Shadows", "DisableMicroShadows", true,
            "Disable micro shadows via Volume override. Micro shadows add " +
            "subtle self-shadowing from normal maps. Very subtle effect " +
            "with non-trivial GPU cost.");

        CfgDisableScreenSpaceShadows = Config.Bind("2. Shadows", "DisableScreenSpaceShadows", false,
            "Disable screen-space shadows via Volume component deactivation. " +
            "Only set to true if you see no visual difference.");

        CfgDisableSmallShadowCasters = Config.Bind("2. Shadows", "DisableSmallShadowCasters", true,
            "Disable shadow casting on small renderers (rocks, plants, debris). " +
            "The scene scan found ~1000 shadow-casting renderers with bounds < 1m. " +
            "Each one costs a draw call in the shadow depth pass but produces " +
            "barely visible shadows. Disabling them saves 3-8% GPU time.");

        CfgSmallShadowCasterThreshold = Config.Bind("2. Shadows", "SmallShadowCasterThreshold", 1.0f,
            new ConfigDescription(
                "Bounds size magnitude threshold (metres) below which renderers " +
                "have their shadow casting disabled. Only used when " +
                "DisableSmallShadowCasters is true. Larger values are more " +
                "aggressive (disable shadows on bigger objects).",
                new AcceptableValueRange<float>(0.1f, 5.0f)));

        // -- 3. Textures & Streaming --
        CfgStreamingMipmapsActive = Config.Bind("3. Textures", "StreamingMipmapsActive", true,
            "Enable mipmap streaming. Only applied if the game doesn't already " +
            "have it enabled. A pure win with zero visual change at gameplay distances.");

        CfgStreamingMipmapsMemoryBudget = Config.Bind("3. Textures", "StreamingMipmapsMemoryBudget", 512,
            new ConfigDescription(
                "Memory budget (MB) for mipmap streaming. Only applied if " +
                "streaming is being enabled by us (not if game already has it).",
                new AcceptableValueRange<int>(128, 4096)));

        CfgStreamingMipmapsMaxLevelReduction = Config.Bind("3. Textures", "StreamingMipmapsMaxLevelReduction", 2,
            new ConfigDescription(
                "Maximum mipmap levels the streaming system can reduce.",
                new AcceptableValueRange<int>(0, 4)));

        CfgAnisotropicFiltering = Config.Bind("3. Textures", "AnisotropicFiltering", -1,
            new ConfigDescription(
                "Anisotropic texture filtering mode. " +
                "0 = Disable, 1 = Enable (per-texture), 2 = ForceEnable. " +
                "-1 = Don't override (respect game's setting). " +
                "Only applied if the game's current value is more expensive.",
                new AcceptableValueRange<int>(-1, 2)));

        CfgGlobalTextureMipmapLimit = Config.Bind("3. Textures", "GlobalTextureMipmapLimit", 0,
            new ConfigDescription(
                "Skip the N highest-resolution mipmap levels globally. " +
                "0 = don't override. Only applied if higher than the game's current value.",
                new AcceptableValueRange<int>(0, 3)));

        // -- 4. Post-Processing (Volume Overrides) --
        CfgDisableFilmGrain = Config.Bind("4. Post-Processing", "DisableFilmGrain", true,
            "Disable Film Grain effect. A purely cosmetic noise overlay " +
            "that costs a full-screen pass. Most players prefer it off.");

        CfgDisableChromaticAberration = Config.Bind("4. Post-Processing", "DisableChromaticAberration", true,
            "Disable Chromatic Aberration. Aska exposes this in its own " +
            "settings menu, so some players already have it off.");

        CfgDisableLensDistortion = Config.Bind("4. Post-Processing", "DisableLensDistortion", true,
            "Disable Lens Distortion. Costs a full-screen pass. " +
            "Very few players notice its absence.");

        CfgDisableMotionBlur = Config.Bind("4. Post-Processing", "DisableMotionBlur", true,
            "Disable Motion Blur. Many players disable this voluntarily.");

        CfgReduceBloomIterations = Config.Bind("4. Post-Processing", "ReduceBloomIterations", true,
            "Reduce Bloom iteration count. Produces nearly identical results " +
            "with less GPU work.");

        CfgBloomMaxIterations = Config.Bind("4. Post-Processing", "BloomMaxIterations", 4,
            new ConfigDescription(
                "Maximum bloom downscale iterations when ReduceBloomIterations is on.",
                new AcceptableValueRange<int>(2, 8)));

        // -- 5. Culling & Overdraw --
        CfgShadowDistanceFallback = Config.Bind("5. Culling", "ShadowDistanceQuality", 0f,
            new ConfigDescription(
                "QualitySettings shadow distance override. 0 = don't override " +
                "(respect game's value). Only applied if the game's value is higher.",
                new AcceptableValueRange<float>(0f, 300f)));

        // -- 6. LOD & Mesh --
        CfgLODBias = Config.Bind("6. LOD", "LODBias", 0f,
            new ConfigDescription(
                "LOD bias multiplier. 0 = don't override (respect game's value). " +
                "Only applied if the game's current LOD bias is HIGHER than this " +
                "(we only reduce, never increase).",
                new AcceptableValueRange<float>(0f, 4.0f)));

        CfgMaximumLODLevel = Config.Bind("6. LOD", "MaximumLODLevel", 0,
            new ConfigDescription(
                "Force a minimum LOD level (0 = full detail). " +
                "0 = don't override.",
                new AcceptableValueRange<int>(0, 3)));

        CfgSkinWeights = Config.Bind("6. LOD", "SkinWeights", 0,
            new ConfigDescription(
                "Bone weights per vertex. 0 = don't override. " +
                "Only applied if the game's current value is higher.",
                new AcceptableValueList<int>(0, 1, 2, 4)));

        // -- 7. HDRP Pipeline --
        // Pipeline-level support flag overrides -- the PRIMARY optimization mechanism.
        // These modify the HDRP Asset's RenderPipelineSettings support booleans.
        // Effect: HDRP's per-frame FrameSettings aggregation ANDs the camera's
        // frame setting with the asset's support flag. Setting supportSSR=false
        // causes aggregate.enableSSR=false every frame, preventing the render
        // pass from executing regardless of camera or volume settings.
        // This is the DEEPEST disable available without a pipeline rebuild.
        CfgPipelineDisableSSR = Config.Bind("7. HDRP Pipeline", "PipelineDisableSSR", false,
            "PIPELINE-LEVEL disable of Screen Space Reflections. " +
            "Sets supportSSR=false on the HDRP Asset, which forces SSR off globally. " +
            "WARNING: Enabling this can cause a visual artifact where the screen image " +
            "is 'stamped' onto reflective objects. This happens because the SSR render " +
            "pass stops running but the SSR texture retains stale data from the previous " +
            "frame, and shaders that sample it read the old screen image instead of black. " +
            "The Moderate preset leaves this OFF to avoid the artifact.");

        CfgPipelineDisableSSAO = Config.Bind("7. HDRP Pipeline", "PipelineDisableSSAO", false,
            "PIPELINE-LEVEL disable of Screen Space Ambient Occlusion. " +
            "Sets supportSSAO=false on the HDRP Asset. " +
            "Removes all ambient occlusion. Noticeable visual change but saves GPU time.");

        CfgPipelineDisableVolumetrics = Config.Bind("7. HDRP Pipeline", "PipelineDisableVolumetrics", false,
            "PIPELINE-LEVEL disable of volumetric fog/lighting. " +
            "Sets supportVolumetrics=false on the HDRP Asset. " +
            "Removes all fog and volumetric lighting. Major visual change. " +
            "Off by default since Aska's atmosphere depends heavily on volumetrics.");

        CfgPipelineDisableVolumetricClouds = Config.Bind("7. HDRP Pipeline", "PipelineDisableVolumetricClouds", false,
            "PIPELINE-LEVEL disable of volumetric clouds. " +
            "Sets supportVolumetricClouds=false on the HDRP Asset. " +
            "Expensive effect; disabling saves significant GPU time.");

        CfgPipelineDisableSubsurfaceScattering = Config.Bind("7. HDRP Pipeline", "PipelineDisableSubsurfaceScattering", false,
            "PIPELINE-LEVEL disable of subsurface scattering. " +
            "Sets supportSubsurfaceScattering=false on the HDRP Asset. " +
            "Affects skin and foliage translucency rendering.");

        CfgPipelineDisableDecals = Config.Bind("7. HDRP Pipeline", "PipelineDisableDecals", false,
            "PIPELINE-LEVEL disable of decal rendering. " +
            "Sets supportDecals=false on the HDRP Asset. " +
            "Removes all decals (scorch marks, footprints, etc.).");

        CfgPipelineDisableDistortion = Config.Bind("7. HDRP Pipeline", "PipelineDisableDistortion", false,
            "PIPELINE-LEVEL disable of distortion effects (heat haze, refraction). " +
            "Sets supportDistortion=false on the HDRP Asset. " +
            "WARNING: Like PipelineDisableSSR, enabling this can cause stale-texture " +
            "artifacts. The Moderate preset leaves this OFF.");

        CfgPipelineDisableSSRTransparent = Config.Bind("7. HDRP Pipeline", "PipelineDisableSSRTransparent", false,
            "PIPELINE-LEVEL disable of SSR on transparent objects. " +
            "Sets supportSSRTransparent=false on the HDRP Asset. " +
            "WARNING: Like PipelineDisableSSR, this can cause stale-texture artifacts.");

        CfgPipelineDisableScreenSpaceLensFlare = Config.Bind("7. HDRP Pipeline", "PipelineDisableScreenSpaceLensFlare", false,
            "PIPELINE-LEVEL disable of screen-space lens flares. " +
            "Sets supportScreenSpaceLensFlare=false on the HDRP Asset.");

        CfgPipelineDisableDataDrivenLensFlare = Config.Bind("7. HDRP Pipeline", "PipelineDisableDataDrivenLensFlare", false,
            "PIPELINE-LEVEL disable of data-driven lens flares. " +
            "Sets supportDataDrivenLensFlare=false on the HDRP Asset.");

        // Cosmetic Volume component disables -- lower priority than pipeline flags
        // but useful for effects where pipeline flag causes stale-texture artifacts.
        CfgDisableVolumetricClouds = Config.Bind("7. HDRP Pipeline", "DisableVolumetricClouds", false,
            "Disable volumetric clouds via Volume component deactivation. " +
            "Expensive effect but visually important. Off by default.");

        CfgDisableSubsurfaceScattering = Config.Bind("7. HDRP Pipeline", "DisableSubsurfaceScattering", false,
            "Disable subsurface scattering via Volume component deactivation. " +
            "Affects skin and foliage translucency. Expensive GPU pass. " +
            "Off by default since it affects character appearance.");

        // -- 8. Reflection Probes & Lighting --
        CfgRealtimeReflectionProbes = Config.Bind("8. Lighting", "RealtimeReflectionProbes", true,
            "Keep realtime reflection probes as the game sets them. " +
            "Set to false only if you're sure the game doesn't need them. " +
            "Defaults to true (don't change) because blindly disabling " +
            "these can break indoor lighting.");

        // -- 9. Async & Threading --
        CfgAsyncUploadTimeSlice = Config.Bind("9. Async Upload", "AsyncUploadTimeSlice", 4,
            new ConfigDescription(
                "Milliseconds per frame for async uploads. " +
                "Only applied if the game's current value is LOWER (we only increase).",
                new AcceptableValueRange<int>(1, 33)));

        CfgAsyncUploadBufferSize = Config.Bind("9. Async Upload", "AsyncUploadBufferSize", 32,
            new ConfigDescription(
                "Async upload ring buffer size (MB). " +
                "Only applied if the game's current value is LOWER.",
                new AcceptableValueRange<int>(2, 512)));

        // -- 10. Miscellaneous --
        CfgVSyncCount = Config.Bind("10. Misc", "VSyncCount", -1,
            new ConfigDescription(
                "VSync: -1 = don't override, 0 = off, 1 = every vblank. " +
                "The game manages this itself; only override if you know " +
                "what you want.",
                new AcceptableValueRange<int>(-1, 4)));

        CfgTargetFrameRate = Config.Bind("10. Misc", "TargetFrameRate", -1,
            new ConfigDescription(
                "Target frame rate when VSync is off. " +
                "Aska sets Application.targetFrameRate = 60 at startup, which hard-caps " +
                "all VSync-off users to 60 FPS regardless of GPU headroom. " +
                "A Harmony patch blocks the game from overriding this value. " +
                "-1 = unlimited (removes the 60 FPS cap), 0 = don't override " +
                "(let the game set whatever it wants), >0 = cap at that FPS. " +
                "When VSync is on (vSyncCount >= 1), Unity ignores targetFrameRate " +
                "entirely so this setting has no effect.",
                new AcceptableValueRange<int>(-1, 300)));

        // -- 11. Advanced GPU --
        CfgShaderWarmup = Config.Bind("11. Advanced GPU", "ShaderWarmup", false,
            "Call Shader.WarmupAllShaders() once at session start. " +
            "Can prevent hitches when new effects first render. " +
            "WARNING: In Unity 6 with jobified rendering, this produces " +
            "hundreds of 'ShaderProgram is unsupported' warnings in the log. " +
            "Disabled by default for this reason.");

        CfgMaxQueuedFrames = Config.Bind("11. Advanced GPU", "MaxQueuedFrames", 2,
            new ConfigDescription(
                "Maximum GPU driver frame queue. " +
                "-1 = don't override. 2 = reduced input lag.",
                new AcceptableValueRange<int>(-1, 4)));

        // -- 12. Post-Processing Extra --
        CfgDisableDepthOfField = Config.Bind("12. Post-Processing Extra", "DisableDepthOfField", false,
            "Disable Depth of Field. Off by default since DOF is a key visual.");

        CfgDisableVignette = Config.Bind("12. Post-Processing Extra", "DisableVignette", true,
            "Disable Vignette. Most players don't notice its absence.");

        CfgDisablePaniniProjection = Config.Bind("12. Post-Processing Extra", "DisablePaniniProjection", true,
            "Disable Panini Projection. Rarely used in gameplay.");

        CfgDisableDataDrivenLensFlare = Config.Bind("12. Post-Processing Extra", "DisableDataDrivenLensFlare", false,
            "Disable Data Driven Lens Flare via Volume component deactivation.");

        CfgDisableScreenSpaceLensFlare = Config.Bind("12. Post-Processing Extra", "DisableScreenSpaceLensFlare", false,
            "Disable Screen Space Lens Flare via Volume component deactivation.");

        // -- 13. Mod Compatibility --
        CfgReapplyInterval = Config.Bind("13. Mod Compatibility", "ReapplyIntervalSeconds", 10f,
            new ConfigDescription(
                "How often (seconds) to re-apply settings as a safety net. " +
                "Quality level changes from the game's settings menu are detected " +
                "instantly via Harmony patch and trigger an immediate reapply -- " +
                "this timer is only for catching edge cases. " +
                "0 = apply once only (rely solely on quality level detection).",
                new AcceptableValueRange<float>(0f, 120f)));

        CfgReapplyPostProcessing = Config.Bind("13. Mod Compatibility", "ReapplyPostProcessing", false,
            "Whether to re-apply post-processing (Volume) changes on the " +
            "periodic timer. When false (default), post-processing overrides " +
            "are applied ONCE on scene entry, then left alone.");

        CfgRespectExternalChanges = Config.Bind("13. Mod Compatibility", "RespectExternalChanges", true,
            "When true, uses directional change detection to respect other mods: " +
            "if a setting moved FURTHER in the optimization direction than we set, " +
            "assume another mod pushed harder and respect it.");

        CfgEnableDrawCallOptimizations = Config.Bind("13. Mod Compatibility", "EnableDrawCallOptimizations", true,
            "Master toggle for Section 1 (SRP Batcher, GPU Resident Drawer). " +
            "Disable if another mod manages draw call settings.");

        CfgEnableShadowOptimizations = Config.Bind("13. Mod Compatibility", "EnableShadowOptimizations", true,
            "Master toggle for Section 2 (shadow distance, cascades, small casters). " +
            "Disable if another mod manages shadow settings.");

        CfgEnableTextureOptimizations = Config.Bind("13. Mod Compatibility", "EnableTextureOptimizations", true,
            "Master toggle for Section 3 (mipmap streaming, anisotropic filtering, mip limits). " +
            "Disable if another mod manages texture quality.");

        CfgEnablePostProcessingOptimizations = Config.Bind("13. Mod Compatibility", "EnablePostProcessingOptimizations", true,
            "Master toggle for Sections 4+12 (film grain, bloom, vignette, etc.). " +
            "Disable if you use a visual enhancement mod that customizes post-processing.");

        CfgEnableLODOptimizations = Config.Bind("13. Mod Compatibility", "EnableLODOptimizations", true,
            "Master toggle for Section 6 (LOD bias, max LOD level, skin weights). " +
            "Disable if another mod manages LOD settings.");

        CfgEnableHDRPPipelineOptimizations = Config.Bind("13. Mod Compatibility", "EnableHDRPPipelineOptimizations", true,
            "Master toggle for Section 7 (pipeline support flags, Volume disables). " +
            "Disable if another mod manages HDRP pipeline settings.");

        CfgEnableLightingOptimizations = Config.Bind("13. Mod Compatibility", "EnableLightingOptimizations", true,
            "Master toggle for Section 8 (reflection probes, additional lights). " +
            "Disable if another mod manages lighting settings.");

        CfgEnableAdvancedGPUOptimizations = Config.Bind("13. Mod Compatibility", "EnableAdvancedGPUOptimizations", true,
            "Master toggle for Section 11 (shader warmup, etc.). " +
            "Disable if another mod manages advanced GPU settings.");

        // -- Apply preset if not Custom --
        // Presets are "sticky": the chosen preset stays in the config and
        // re-applies its values on every launch (a no-op when nothing changed).
        // Users who want full manual control set Preset = Custom.
        Log.LogInfo($"Config preset on load: {CfgPreset.Value}");
        if (CfgPreset.Value != PerformancePreset.Custom)
        {
            var requestedPreset = CfgPreset.Value;
            PresetApplicator.Apply(requestedPreset, this);
            Log.LogInfo($"Applied \"{requestedPreset}\" preset (sticky -- will re-apply on next launch).");
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

/// <summary>
/// Performance optimization preset tiers. Philosophy: "read before write, only improve."
/// </summary>
public enum PerformancePreset
{
    /// <summary>Use manual config values.</summary>
    Custom,
    /// <summary>All optimizations disabled, stock game behavior.</summary>
    Vanilla,
    /// <summary>Disables expensive screen-space effects not exposed by the game menu. ~5-10 FPS gain.</summary>
    Moderate
}
