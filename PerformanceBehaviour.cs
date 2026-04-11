using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace AskaPerformanceBooster;

/// <summary>
/// Core MonoBehaviour that applies and maintains performance settings at runtime.
///
/// Only confirmed-working optimizations:
///   1. HDRP Asset pipeline support flags (the PRIMARY optimization, 4-5+ FPS)
///   2. targetFrameRate = -1 (uncap from Aska's 60 FPS hard cap)
///   3. Small shadow caster disable (831+ objects per session)
///   4. SRP Batcher force-on (confirmed false -> true)
///   5. HDRP Asset stale check (detects quality level changes)
///
/// CRITICAL IL2CPP CONSTRAINT:
/// ClassInjector processes all INSTANCE methods on this class. Methods must NOT have
/// System.Object, System.Type, Dictionary, PropertyInfo, FieldInfo, etc. as
/// parameter types or return types. All such methods live in HDRPReflectionHelper.
/// </summary>
public class PerformanceBehaviour : MonoBehaviour
{
    // ------------------------------------------------------------------
    //  State
    // ------------------------------------------------------------------
    private bool _globalSettingsApplied;
    private float _reapplyTimer;

    // Scene transition tracking
    private bool _wasInGameplay;

    // Small shadow caster optimization state
    private bool _smallShadowCastersDone;
    private float _smallShadowCasterTimer;
    private int _smallShadowCastersDisabledCount;
    private float _gameplayTimer;

    // ------------------------------------------------------------------
    //  Debug logging helper
    // ------------------------------------------------------------------
    private static bool Debug => PerformancePlugin.CfgDebugLogging.Value;

    private static void DebugLog(string msg)
    {
        if (Debug)
            PerformancePlugin.Log.LogInfo($"[Debug] {msg}");
    }

    // ------------------------------------------------------------------
    //  Unity callbacks
    // ------------------------------------------------------------------

    private void Update()
    {
        bool inGameplay = IsInGameplay();

        // Scene transition detection
        if (_wasInGameplay && !inGameplay)
        {
            ClearCachedReferences();
            PerformancePlugin.Log.LogInfo("Left gameplay -- cleared optimization cache.");
        }
        else if (!_wasInGameplay && inGameplay)
        {
            _globalSettingsApplied = false;
            _gameplayTimer = 0f;
            _smallShadowCastersDone = false;
            _smallShadowCasterTimer = 0f;
            _smallShadowCastersDisabledCount = 0;
            PerformancePlugin.Log.LogInfo("Entered gameplay -- will re-apply optimizations.");
        }
        _wasInGameplay = inGameplay;

        // Detect quality level changes (can happen in any scene)
        if (QualityLevelPatch.QualityLevelChanged)
        {
            QualityLevelPatch.QualityLevelChanged = false;
            int newLevel = QualityLevelPatch.LastSetLevel;

            PerformancePlugin.Log.LogInfo(
                $"Quality level changed to {newLevel} -- invalidating HDRP cache " +
                "and reapplying all optimizations.");

            HDRPReflectionHelper.InvalidateCache();
            _globalSettingsApplied = false;
            _reapplyTimer = 0f;
        }

        // Track gameplay time for delayed small shadow caster scan
        if (inGameplay)
            _gameplayTimer += Time.unscaledDeltaTime;

        // Apply global settings (pipeline flags, SRP Batcher, frame rate)
        if (!_globalSettingsApplied)
        {
            ApplyGlobalSettings();
            _globalSettingsApplied = true;
            PerformancePlugin.Log.LogInfo(
                $"Performance optimizations applied (scene={GetActiveSceneName()}).");
        }

        // Small shadow caster optimization (gameplay only, after 5s delay)
        if (inGameplay && PerformancePlugin.CfgDisableSmallShadowCasters.Value)
        {
            if (!_smallShadowCastersDone)
            {
                if (_gameplayTimer >= 5f)
                {
                    ApplySmallShadowCasterOptimization();
                    _smallShadowCastersDone = true;
                    _smallShadowCasterTimer = 0f;
                }
            }
            else
            {
                // Re-scan periodically for newly streamed objects
                _smallShadowCasterTimer += Time.unscaledDeltaTime;
                if (_smallShadowCasterTimer >= 30f)
                {
                    _smallShadowCasterTimer = 0f;
                    ApplySmallShadowCasterOptimization();
                }
            }
        }

        // Periodic reapply timer
        float reapplyInterval = PerformancePlugin.CfgReapplyInterval.Value;
        if (reapplyInterval <= 0f)
            return;

        _reapplyTimer += Time.unscaledDeltaTime;
        if (_reapplyTimer >= reapplyInterval)
        {
            _reapplyTimer = 0f;
            ApplyGlobalSettings();
        }
    }

    // ==================================================================
    //  Apply all confirmed-working optimizations
    // ==================================================================

    private void ApplyGlobalSettings()
    {
        try
        {
            ApplyTargetFrameRate();
        }
        catch (Exception ex)
        {
            PerformancePlugin.Log.LogWarning($"Error applying targetFrameRate: {ex.Message}");
        }

        try
        {
            ApplyHDRPAssetSettings();
        }
        catch (Exception ex)
        {
            PerformancePlugin.Log.LogWarning($"Error applying HDRP Asset settings: {ex.Message}");
        }
    }

    // ==================================================================
    //  Target frame rate
    // ==================================================================

    private static void ApplyTargetFrameRate()
    {
        int fpsCfg = PerformancePlugin.CfgTargetFrameRate.Value;
        if (fpsCfg == 0) return; // sentinel: don't override

        int prev = Application.targetFrameRate;
        if (prev != fpsCfg)
        {
            Application.targetFrameRate = fpsCfg;
            DebugLog($"Application.targetFrameRate: {prev} -> {fpsCfg}");
        }
    }

    // ==================================================================
    //  HDRP Asset settings (pipeline support flags + SRP Batcher)
    // ==================================================================

    private void ApplyHDRPAssetSettings()
    {
        HDRPReflectionHelper.CheckHdrpAssetStale();
        HDRPReflectionHelper.CacheHDRPReflection();
        if (HDRPReflectionHelper.HdrpAssetRef == null) return;

        // SRP Batcher -- confirmed false -> true in logs
        if (PerformancePlugin.CfgForceSRPBatcher.Value)
        {
            try
            {
                bool currentSRP = GraphicsSettings.useScriptableRenderPipelineBatching;
                if (!currentSRP)
                {
                    GraphicsSettings.useScriptableRenderPipelineBatching = true;
                    PerformancePlugin.Log.LogInfo("SRP Batcher: false -> true");
                }
                else
                {
                    DebugLog("SRP Batcher: already enabled.");
                }
            }
            catch (Exception ex)
            {
                DebugLog($"Could not access SRP Batcher: {ex.Message}");
            }
        }

        // Pipeline support flags -- THE PRIMARY OPTIMIZATION
        ApplyPipelineSupportFlags();

        DebugLog("HDRP Asset settings pass complete.");
    }

    private static void ApplyPipelineSupportFlags()
    {
        HDRPReflectionHelper.ApplyPipelineSupportFlagBatch(
            PerformancePlugin.CfgPipelineDisableSSR.Value,
            PerformancePlugin.CfgPipelineDisableSSAO.Value,
            PerformancePlugin.CfgPipelineDisableVolumetrics.Value,
            PerformancePlugin.CfgPipelineDisableVolumetricClouds.Value,
            PerformancePlugin.CfgPipelineDisableSubsurfaceScattering.Value,
            PerformancePlugin.CfgPipelineDisableDecals.Value,
            PerformancePlugin.CfgPipelineDisableDistortion.Value,
            PerformancePlugin.CfgPipelineDisableSSRTransparent.Value,
            PerformancePlugin.CfgPipelineDisableScreenSpaceLensFlare.Value,
            PerformancePlugin.CfgPipelineDisableDataDrivenLensFlare.Value);
    }

    // ==================================================================
    //  Small shadow caster optimization
    // ==================================================================

    private void ApplySmallShadowCasterOptimization()
    {
        try
        {
            float threshold = PerformancePlugin.CfgSmallShadowCasterThreshold.Value;
            int disabledThisPass = 0;

            var renderers = FindObjectsOfType<Renderer>();
            if (renderers == null || renderers.Length == 0)
            {
                DebugLog("SmallShadowCasters: no renderers found.");
                return;
            }

            foreach (var rend in renderers)
            {
                if (rend == null) continue;
                try
                {
                    if (rend.shadowCastingMode == ShadowCastingMode.Off)
                        continue;
                    if (rend.shadowCastingMode == ShadowCastingMode.ShadowsOnly)
                        continue;
                    if (rend.gameObject.CompareTag("Player"))
                        continue;

                    if (rend.bounds.size.magnitude < threshold)
                    {
                        rend.shadowCastingMode = ShadowCastingMode.Off;
                        disabledThisPass++;
                    }
                }
                catch { }
            }

            _smallShadowCastersDisabledCount += disabledThisPass;

            if (disabledThisPass > 0)
            {
                PerformancePlugin.Log.LogInfo(
                    $"Disabled shadows on {disabledThisPass} small renderers " +
                    $"(< {threshold:F1}m bounds). Total this session: {_smallShadowCastersDisabledCount}.");
            }
            else
            {
                DebugLog("SmallShadowCasters: no new small shadow casters found this pass.");
            }
        }
        catch (Exception ex)
        {
            PerformancePlugin.Log.LogWarning(
                $"Error in ApplySmallShadowCasterOptimization: {ex.Message}");
        }
    }

    // ==================================================================
    //  Cleanup
    // ==================================================================

    private void ClearCachedReferences()
    {
        HDRPReflectionHelper.InvalidateCache();
        _globalSettingsApplied = false;
        _smallShadowCastersDone = false;
        _smallShadowCasterTimer = 0f;
        _smallShadowCastersDisabledCount = 0;
        _gameplayTimer = 0f;
    }

    // ==================================================================
    //  Utility
    // ==================================================================

    private static bool IsInGameplay()
    {
        try
        {
            return SceneManager.GetActiveScene().name == "StreamingWorld";
        }
        catch
        {
            return false;
        }
    }

    private static string GetActiveSceneName()
    {
        try
        {
            return SceneManager.GetActiveScene().name ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }
}
