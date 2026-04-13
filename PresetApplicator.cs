namespace AskaPerformanceBooster;

/// <summary>
/// Applies preset configurations to all config entries.
///
/// Presets:
///   Vanilla  -- all overrides disabled, stock game behavior
///   Moderate -- (default) pipeline support flags + frame rate uncap +
///               small shadow caster disable + SRP Batcher.
///               Every setting confirmed working with measurable FPS impact.
///   Custom   -- use your manually edited values below
/// </summary>
internal static class PresetApplicator
{
    internal static void Apply(PerformancePreset preset)
    {
        switch (preset)
        {
            case PerformancePreset.Vanilla:
                ApplyVanilla();
                break;
            case PerformancePreset.Moderate:
                ApplyModerate();
                break;
            default:
                return;
        }
    }

    private static void ApplyVanilla()
    {
        // Pipeline -- all stock
        PerformancePlugin.CfgPipelineDisableSSR.Value = false;
        PerformancePlugin.CfgPipelineDisableSSAO.Value = false;
        PerformancePlugin.CfgPipelineDisableVolumetrics.Value = false;
        PerformancePlugin.CfgPipelineDisableVolumetricClouds.Value = false;
        PerformancePlugin.CfgPipelineDisableSubsurfaceScattering.Value = false;
        PerformancePlugin.CfgPipelineDisableDecals.Value = false;
        PerformancePlugin.CfgPipelineDisableDistortion.Value = false;
        PerformancePlugin.CfgPipelineDisableSSRTransparent.Value = false;
        PerformancePlugin.CfgPipelineDisableScreenSpaceLensFlare.Value = false;
        PerformancePlugin.CfgPipelineDisableDataDrivenLensFlare.Value = false;

        // Shadows -- stock
        PerformancePlugin.CfgDisableSmallShadowCasters.Value = false;
        PerformancePlugin.CfgSmallShadowCasterThreshold.Value = 1.0f;
        PerformancePlugin.CfgDisableResourceShadowCasters.Value = false;
        PerformancePlugin.CfgResourceShadowCasterThreshold.Value = 5.0f;
        PerformancePlugin.CfgDisableEnvironmentShadowCasters.Value = false;
        PerformancePlugin.CfgEnvironmentShadowCasterThreshold.Value = 5.0f;
        PerformancePlugin.CfgShadowMaxShadowRequests.Value = 0;          // don't override
        PerformancePlugin.CfgShadowMaxDirectionalResolution.Value = 0;   // don't override
        PerformancePlugin.CfgShadowMaxAreaResolution.Value = 0;          // don't override
        PerformancePlugin.CfgShadowAreaFilteringQuality.Value = -1;      // don't override

        // Draw calls -- stock
        PerformancePlugin.CfgForceSRPBatcher.Value = false;

        // Frame rate -- don't override
        PerformancePlugin.CfgTargetFrameRate.Value = 0;

        // LOD bias -- don't override
        PerformancePlugin.CfgLodBias.Value = 0f;

        // Diagnostics -- off
        PerformancePlugin.CfgEnableDiagnostics.Value = false;
        PerformancePlugin.CfgLogObjectBreakdown.Value = false;
        PerformancePlugin.CfgLogFrameTimings.Value = false;
    }

    private static void ApplyModerate()
    {
        // Pipeline support flags -- THE PRIMARY OPTIMIZATION (4-5+ FPS confirmed).
        // SSR, Distortion, and Transparent SSR are intentionally LEFT ENABLED
        // to avoid stale-texture artifacts.
        // Decals are LEFT ENABLED because Aska uses HDRP DecalProjectors for
        // the terraforming grid overlay (green/red placement squares).
        PerformancePlugin.CfgPipelineDisableSSR.Value = false;
        PerformancePlugin.CfgPipelineDisableSSAO.Value = true;
        PerformancePlugin.CfgPipelineDisableVolumetrics.Value = true;
        PerformancePlugin.CfgPipelineDisableVolumetricClouds.Value = true;
        PerformancePlugin.CfgPipelineDisableSubsurfaceScattering.Value = true;
        PerformancePlugin.CfgPipelineDisableDecals.Value = false;
        PerformancePlugin.CfgPipelineDisableDistortion.Value = false;
        PerformancePlugin.CfgPipelineDisableSSRTransparent.Value = false;
        PerformancePlugin.CfgPipelineDisableScreenSpaceLensFlare.Value = true;
        PerformancePlugin.CfgPipelineDisableDataDrivenLensFlare.Value = true;

        // Small shadow casters -- 831+ objects confirmed disabled per session
        PerformancePlugin.CfgDisableSmallShadowCasters.Value = true;
        PerformancePlugin.CfgSmallShadowCasterThreshold.Value = 1.0f;

        // Resource shadow casters -- 5,100+ accumulable objects (sticks, firewood, stones, etc.)
        PerformancePlugin.CfgDisableResourceShadowCasters.Value = true;
        PerformancePlugin.CfgResourceShadowCasterThreshold.Value = 5.0f;

        // Environment shadow casters -- grass, cave flora (~1,500+ objects)
        PerformancePlugin.CfgDisableEnvironmentShadowCasters.Value = true;
        PerformancePlugin.CfgEnvironmentShadowCasterThreshold.Value = 5.0f;

        // HDRP Shadow init params -- reduce shadow system capacity/quality
        // maxShadowRequests: 128 -> 48 (covers typical Aska scenes)
        // maxDirectionalShadowMapResolution: 2048 -> 1024 (75% fill rate reduction)
        // maxAreaShadowMapResolution: 2048 -> 1024 (area shadows are soft by design)
        // areaShadowFilteringQuality: Medium -> Low (match punctual/directional)
        PerformancePlugin.CfgShadowMaxShadowRequests.Value = 48;
        PerformancePlugin.CfgShadowMaxDirectionalResolution.Value = 1024;
        PerformancePlugin.CfgShadowMaxAreaResolution.Value = 1024;
        PerformancePlugin.CfgShadowAreaFilteringQuality.Value = 0;       // Low

        // SRP Batcher -- log confirmed false -> true
        PerformancePlugin.CfgForceSRPBatcher.Value = true;

        // Frame rate -- remove Aska's 60 FPS hard cap
        PerformancePlugin.CfgTargetFrameRate.Value = -1;

        // LOD bias -- force earlier LOD transitions for 9,000+ renderers
        PerformancePlugin.CfgLodBias.Value = 0.75f;

        // Diagnostics -- off by default even in Moderate
        PerformancePlugin.CfgEnableDiagnostics.Value = false;
        PerformancePlugin.CfgLogObjectBreakdown.Value = false;
        PerformancePlugin.CfgLogFrameTimings.Value = false;
    }
}
