namespace AskaPerformanceBooster;

/// <summary>
/// Applies preset configurations to all config entries.
///
/// Philosophy: "read before write, only improve."
/// Preset names describe OPTIMIZATION intensity, not graphics quality.
///
/// Aska uses HDRP (High Definition Render Pipeline), so presets target
/// HDRP-specific features: pipeline support flags (the PRIMARY optimization
/// mechanism), HDShadowSettings Volume overrides, post-processing Volume
/// disables, and QualitySettings.
///
/// Many settings now use sentinel values (0, -1, false) that mean "don't override."
/// The PerformanceBehaviour reads the game's current value first and only writes
/// if our value would be an actual improvement.
///
/// Presets:
///   Vanilla  -- all overrides disabled, stock game behavior
///   Moderate -- (default) disables expensive HDRP rendering features via pipeline
///               support flags (the ONLY approach that produces measurable FPS gains
///               in Aska's IL2CPP build); gives a real ~5-10+ FPS improvement.
///               Also applies shadow reduction, post-processing cleanup, and
///               small shadow caster optimization.
///   Custom   -- use your manually edited values below
///
/// Presets are "sticky": if the user sets Preset = Moderate, it stays Moderate
/// across launches. On each launch the mod re-applies the preset values
/// (a no-op when nothing changed). If the user wants full manual control,
/// they set Preset = Custom and edit individual values.
///
/// INVARIANT: Presets NEVER touch DebugLogging, DiagnosticScan, or any
/// Mod Compatibility settings (section 13: ReapplyInterval, RespectExternalChanges,
/// Enable*Optimizations toggles). Those are user preferences that must persist
/// across preset changes.
/// </summary>
internal static class PresetApplicator
{
    internal static void Apply(PerformancePreset preset, PerformancePlugin plugin)
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

    // ==================================================================
    //  Vanilla -- stock game, zero modifications
    //
    //  NOTE: Presets NEVER touch DebugLogging, DiagnosticScan, or
    //  Mod Compatibility settings (section 13). Those are user
    //  preferences that persist across preset changes.
    // ==================================================================
    private static void ApplyVanilla()
    {
        // 1. Draw Calls -- don't override
        PerformancePlugin.CfgForceSRPBatcher.Value = false;
        PerformancePlugin.CfgGPUResidentDrawer.Value = 0;

        // 2. Shadows -- don't override (sentinel values)
        PerformancePlugin.CfgShadowDistance.Value = 0f;
        PerformancePlugin.CfgShadowCascades.Value = 4;
        PerformancePlugin.CfgDisableContactShadows.Value = false;
        PerformancePlugin.CfgDisableMicroShadows.Value = false;
        PerformancePlugin.CfgDisableScreenSpaceShadows.Value = false;
        PerformancePlugin.CfgDisableSmallShadowCasters.Value = false;
        PerformancePlugin.CfgSmallShadowCasterThreshold.Value = 1.0f;

        // 3. Textures -- don't override
        PerformancePlugin.CfgStreamingMipmapsActive.Value = false;
        PerformancePlugin.CfgStreamingMipmapsMemoryBudget.Value = 512;
        PerformancePlugin.CfgStreamingMipmapsMaxLevelReduction.Value = 2;
        PerformancePlugin.CfgAnisotropicFiltering.Value = -1;
        PerformancePlugin.CfgGlobalTextureMipmapLimit.Value = 0;

        // 4. Post-Processing -- don't override
        PerformancePlugin.CfgDisableFilmGrain.Value = false;
        PerformancePlugin.CfgDisableChromaticAberration.Value = false;
        PerformancePlugin.CfgDisableLensDistortion.Value = false;
        PerformancePlugin.CfgDisableMotionBlur.Value = false;
        PerformancePlugin.CfgReduceBloomIterations.Value = false;
        PerformancePlugin.CfgBloomMaxIterations.Value = 8;

        // 5. Culling -- don't override
        PerformancePlugin.CfgShadowDistanceFallback.Value = 0f;

        // 6. LOD -- don't override
        PerformancePlugin.CfgLODBias.Value = 0f;
        PerformancePlugin.CfgMaximumLODLevel.Value = 0;
        PerformancePlugin.CfgSkinWeights.Value = 0;

        // 7. HDRP Pipeline -- don't override (restore stock)
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
        PerformancePlugin.CfgDisableVolumetricClouds.Value = false;
        PerformancePlugin.CfgDisableSubsurfaceScattering.Value = false;

        // 8. Lighting -- don't change
        PerformancePlugin.CfgRealtimeReflectionProbes.Value = true;

        // 9. Async -- don't override
        PerformancePlugin.CfgAsyncUploadTimeSlice.Value = 1;
        PerformancePlugin.CfgAsyncUploadBufferSize.Value = 2;

        // 10. Misc -- don't override
        PerformancePlugin.CfgVSyncCount.Value = -1;
        PerformancePlugin.CfgTargetFrameRate.Value = 0;

        // 11. Advanced GPU -- all disabled
        PerformancePlugin.CfgShaderWarmup.Value = false;
        PerformancePlugin.CfgMaxQueuedFrames.Value = -1;

        // 12. Post-Processing Extra -- don't disable
        PerformancePlugin.CfgDisableDepthOfField.Value = false;
        PerformancePlugin.CfgDisableVignette.Value = false;
        PerformancePlugin.CfgDisablePaniniProjection.Value = false;
        PerformancePlugin.CfgDisableDataDrivenLensFlare.Value = false;
        PerformancePlugin.CfgDisableScreenSpaceLensFlare.Value = false;
    }

    // ==================================================================
    //  Moderate -- the DEFAULT and only optimization preset.
    //
    //  PRIMARY MECHANISM: Pipeline support flags on the HDRP Asset's
    //  RenderPipelineSettings struct. These write through NativeFieldInfoPtr
    //  directly to native memory. HDRP checks them per-frame during
    //  Frame Settings aggregation (aggregate.enableSSR = ... && supportSSR).
    //  This is the ONLY approach confirmed to produce measurable FPS gains
    //  in Aska's IL2CPP build (4-5 FPS from SSR+SSAO alone).
    //
    //  What it does (in priority order):
    //    1. Pipeline support flags: SSAO, volumetrics, volumetric clouds,
    //       subsurface scattering, decals, screen-space lens flare,
    //       data-driven lens flare disabled. SSR, distortion, and transparent
    //       SSR are NOT disabled at the pipeline level (stale-texture artifact).
    //    2. Shadow reduction: 60m distance, 2 cascades (via HDShadowSettings Volume)
    //    3. Small shadow caster disable: ~1000 objects per scene
    //    4. Post-processing cleanup: film grain, chromatic aberration,
    //       motion blur, lens distortion, vignette, DOF, lens flares OFF
    //    5. SRP Batcher verification, async buffers
    //
    //  Best combined with the game's own quality settings (High recommended)
    //  and DLSS/FSR.
    //
    //  Self-contained -- does not call any other preset method.
    // ==================================================================
    private static void ApplyModerate()
    {
        // 1. Draw Calls -- SRP Batcher should already be on, but verify.
        //    GPU Resident Drawer is forced to 0 (disabled) because changing
        //    gpuResidentDrawerMode at runtime causes a Blitter crash loop.
        PerformancePlugin.CfgForceSRPBatcher.Value = true;
        PerformancePlugin.CfgGPUResidentDrawer.Value = 0;

        // 2. Shadows -- significant reduction from stock 500m.
        // 60m covers all gameplay-relevant shadow casters while cutting
        // the shadow volume significantly. Applied via HDShadowSettings Volume.
        PerformancePlugin.CfgShadowDistance.Value = 60f;
        PerformancePlugin.CfgShadowCascades.Value = 2;
        PerformancePlugin.CfgDisableContactShadows.Value = true;
        PerformancePlugin.CfgDisableMicroShadows.Value = true;
        PerformancePlugin.CfgDisableScreenSpaceShadows.Value = true;
        PerformancePlugin.CfgDisableSmallShadowCasters.Value = true;
        PerformancePlugin.CfgSmallShadowCasterThreshold.Value = 1.0f;

        // 3. Textures -- enable streaming if not already on. Don't touch AF.
        PerformancePlugin.CfgStreamingMipmapsActive.Value = true;
        PerformancePlugin.CfgStreamingMipmapsMemoryBudget.Value = 512;
        PerformancePlugin.CfgStreamingMipmapsMaxLevelReduction.Value = 2;
        PerformancePlugin.CfgAnisotropicFiltering.Value = -1; // don't override
        PerformancePlugin.CfgGlobalTextureMipmapLimit.Value = 0; // don't override

        // 4. Post-Processing -- disable cosmetic effects, reduce quality
        PerformancePlugin.CfgDisableFilmGrain.Value = true;
        PerformancePlugin.CfgDisableChromaticAberration.Value = true;
        PerformancePlugin.CfgDisableLensDistortion.Value = true;
        PerformancePlugin.CfgDisableMotionBlur.Value = true;
        PerformancePlugin.CfgReduceBloomIterations.Value = true;
        PerformancePlugin.CfgBloomMaxIterations.Value = 3;

        // 5. Culling -- reduce shadow distance in QualitySettings too
        PerformancePlugin.CfgShadowDistanceFallback.Value = 60f;

        // 6. LOD -- reduce if game's values are higher
        PerformancePlugin.CfgLODBias.Value = 1.0f;
        PerformancePlugin.CfgMaximumLODLevel.Value = 0;
        PerformancePlugin.CfgSkinWeights.Value = 2;

        // 7. HDRP Pipeline Support Flags -- THE PRIMARY OPTIMIZATION MECHANISM.
        // SSR, Distortion, and Transparent SSR are intentionally LEFT ENABLED
        // at the pipeline support flag level. Setting supportSSR=false prevents
        // the SSR render pass from running but does NOT clear the SSR texture
        // allocated at pipeline init. Shaders that sample the SSR texture read
        // stale screen data from the previous frame, producing a visible artifact.
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
        PerformancePlugin.CfgDisableVolumetricClouds.Value = true;
        PerformancePlugin.CfgDisableSubsurfaceScattering.Value = true;

        // 8. Lighting -- disable realtime probes
        PerformancePlugin.CfgRealtimeReflectionProbes.Value = false;

        // 9. Async -- increase if game has lower values
        PerformancePlugin.CfgAsyncUploadTimeSlice.Value = 4;
        PerformancePlugin.CfgAsyncUploadBufferSize.Value = 32;

        // 10. Misc -- uncap frame rate, don't touch VSync.
        PerformancePlugin.CfgVSyncCount.Value = -1;
        PerformancePlugin.CfgTargetFrameRate.Value = -1;

        // 11. Advanced GPU -- proven invisible wins
        PerformancePlugin.CfgShaderWarmup.Value = false;
        PerformancePlugin.CfgMaxQueuedFrames.Value = 2;

        // 12. Post-Processing Extra -- disable
        PerformancePlugin.CfgDisableDepthOfField.Value = true;
        PerformancePlugin.CfgDisableVignette.Value = true;
        PerformancePlugin.CfgDisablePaniniProjection.Value = true;
        PerformancePlugin.CfgDisableDataDrivenLensFlare.Value = true;
        PerformancePlugin.CfgDisableScreenSpaceLensFlare.Value = true;
    }
}
