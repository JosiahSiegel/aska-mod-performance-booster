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

        // Draw calls -- stock
        PerformancePlugin.CfgForceSRPBatcher.Value = false;

        // Frame rate -- don't override
        PerformancePlugin.CfgTargetFrameRate.Value = 0;
    }

    private static void ApplyModerate()
    {
        // Pipeline support flags -- THE PRIMARY OPTIMIZATION (4-5+ FPS confirmed).
        // SSR, Distortion, and Transparent SSR are intentionally LEFT ENABLED
        // to avoid stale-texture artifacts.
        PerformancePlugin.CfgPipelineDisableSSR.Value = false;
        PerformancePlugin.CfgPipelineDisableSSAO.Value = true;
        PerformancePlugin.CfgPipelineDisableVolumetrics.Value = true;
        PerformancePlugin.CfgPipelineDisableVolumetricClouds.Value = true;
        PerformancePlugin.CfgPipelineDisableSubsurfaceScattering.Value = true;
        PerformancePlugin.CfgPipelineDisableDecals.Value = true;
        PerformancePlugin.CfgPipelineDisableDistortion.Value = false;
        PerformancePlugin.CfgPipelineDisableSSRTransparent.Value = false;
        PerformancePlugin.CfgPipelineDisableScreenSpaceLensFlare.Value = true;
        PerformancePlugin.CfgPipelineDisableDataDrivenLensFlare.Value = true;

        // Small shadow casters -- 831+ objects confirmed disabled per session
        PerformancePlugin.CfgDisableSmallShadowCasters.Value = true;
        PerformancePlugin.CfgSmallShadowCasterThreshold.Value = 1.0f;

        // SRP Batcher -- log confirmed false -> true
        PerformancePlugin.CfgForceSRPBatcher.Value = true;

        // Frame rate -- remove Aska's 60 FPS hard cap
        PerformancePlugin.CfgTargetFrameRate.Value = -1;
    }
}
