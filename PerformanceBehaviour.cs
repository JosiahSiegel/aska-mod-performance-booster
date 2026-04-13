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
    private bool _shadowPropertyDumpDone;

    // Scene transition tracking
    private bool _wasInGameplay;

    // Small shadow caster optimization state
    private bool _smallShadowCastersDone;
    private float _smallShadowCasterTimer;
    private int _smallShadowCastersDisabledCount;
    private int _resourceShadowCastersDisabledCount;
    private int _environmentShadowCastersDisabledCount;
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
            DiagnosticsHelper.OnGameplayExited();
            PerformancePlugin.Log.LogInfo("Left gameplay -- cleared optimization cache.");
        }
        else if (!_wasInGameplay && inGameplay)
        {
            _globalSettingsApplied = false;
            _gameplayTimer = 0f;
            _smallShadowCastersDone = false;
            _smallShadowCasterTimer = 0f;
            _smallShadowCastersDisabledCount = 0;
            _resourceShadowCastersDisabledCount = 0;
            _environmentShadowCastersDisabledCount = 0;
            DiagnosticsHelper.OnGameplayEntered();
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

        // Shadow caster optimization (gameplay only, after 5s delay)
        if (inGameplay && (PerformancePlugin.CfgDisableSmallShadowCasters.Value ||
                           PerformancePlugin.CfgDisableResourceShadowCasters.Value ||
                           PerformancePlugin.CfgDisableEnvironmentShadowCasters.Value))
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
        if (reapplyInterval > 0f)
        {
            _reapplyTimer += Time.unscaledDeltaTime;
            if (_reapplyTimer >= reapplyInterval)
            {
                _reapplyTimer = 0f;
                ApplyGlobalSettings();
            }
        }

        // Diagnostics (gated internally -- zero cost when disabled)
        DiagnosticsHelper.OnUpdate();
    }

    private void FixedUpdate()
    {
        DiagnosticsHelper.OnFixedUpdate();
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
            ApplyLodBias();
        }
        catch (Exception ex)
        {
            PerformancePlugin.Log.LogWarning($"Error applying LOD bias: {ex.Message}");
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
    //  LOD bias
    // ==================================================================

    private static void ApplyLodBias()
    {
        float lodBias = PerformancePlugin.CfgLodBias.Value;
        if (lodBias <= 0f) return; // sentinel: don't override

        float prev = UnityEngine.QualitySettings.lodBias;

        // Never increase lodBias beyond what the game already has.
        // Higher lodBias = LODs switch at greater distance = more polygons = worse perf.
        // If the user's quality preset already has a lower (more aggressive) value,
        // keep it rather than overriding with our higher target.
        float effective = Math.Min(prev, lodBias);

        if (Math.Abs(prev - effective) > 0.01f)
        {
            UnityEngine.QualitySettings.lodBias = effective;
            PerformancePlugin.Log.LogInfo($"LOD bias: {prev:F2} -> {effective:F2} (target was {lodBias:F2})");
        }
        else
        {
            DebugLog($"LOD bias: already {prev:F2} (<= target {lodBias:F2}).");
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

        // HDRP Shadow init parameters (secondary shadow optimization)
        ApplyShadowInitParams();

        // One-time shadow property discovery (debug mode only)
        if (Debug && !_shadowPropertyDumpDone)
        {
            _shadowPropertyDumpDone = true;
            HDRPReflectionHelper.DumpShadowProperties();
        }

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
    //  HDRP Shadow init parameter optimization
    // ==================================================================

    private static void ApplyShadowInitParams()
    {
        HDRPReflectionHelper.ApplyShadowInitParams(
            PerformancePlugin.CfgShadowMaxShadowRequests.Value,
            PerformancePlugin.CfgShadowMaxDirectionalResolution.Value,
            PerformancePlugin.CfgShadowMaxAreaResolution.Value,
            PerformancePlugin.CfgShadowAreaFilteringQuality.Value);
    }

    // ==================================================================
    //  Small shadow caster optimization
    // ==================================================================

    /// <summary>
    /// Resource object name prefixes for targeted shadow disable.
    /// These are player-accumulable objects (sticks, firewood, stones, resin, bark,
    /// logs, bone fragments) that pile up in the thousands during gameplay.
    /// Hardcoded because they are specific to Aska's object naming conventions.
    /// Note: "small_stone" covers the typo "small_stone_reslource_LOD" in the game.
    /// </summary>
    private static readonly string[] ResourceNamePrefixes = new string[]
    {
        "stick",
        "resource_firewood",
        "small_stone",
        "resource_resin",
        "resource_bark",
        "long_stick",
        "log_raw",
        "os_fragments"
    };

    /// <summary>
    /// Environment object name prefixes for shadow disable.
    /// These are world-spawned objects whose shadows are imperceptible:
    /// grass clumps cast shadows on other grass (uniform darkening),
    /// and cave flora is in already-dark environments.
    /// </summary>
    private static readonly string[] EnvironmentShadowPrefixes = new string[]
    {
        "grass_highlands",
        "cave_creep"
    };

    /// <summary>
    /// Checks whether a GameObject name matches any known resource object prefix.
    /// </summary>
    private static bool IsResourceObject(string goName)
    {
        for (int i = 0; i < ResourceNamePrefixes.Length; i++)
        {
            if (goName.StartsWith(ResourceNamePrefixes[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Checks whether a GameObject name matches any known environment object prefix.
    /// </summary>
    private static bool IsEnvironmentObject(string goName)
    {
        for (int i = 0; i < EnvironmentShadowPrefixes.Length; i++)
        {
            if (goName.StartsWith(EnvironmentShadowPrefixes[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private void ApplySmallShadowCasterOptimization()
    {
        try
        {
            bool doSmall = PerformancePlugin.CfgDisableSmallShadowCasters.Value;
            bool doResources = PerformancePlugin.CfgDisableResourceShadowCasters.Value;
            bool doEnvironment = PerformancePlugin.CfgDisableEnvironmentShadowCasters.Value;
            float smallThreshold = PerformancePlugin.CfgSmallShadowCasterThreshold.Value;
            float resourceThreshold = PerformancePlugin.CfgResourceShadowCasterThreshold.Value;
            float environmentThreshold = PerformancePlugin.CfgEnvironmentShadowCasterThreshold.Value;

            int smallDisabledThisPass = 0;
            int resourceDisabledThisPass = 0;
            int environmentDisabledThisPass = 0;

            var renderers = FindObjectsOfType<Renderer>();
            if (renderers == null || renderers.Length == 0)
            {
                DebugLog("ShadowCasters: no renderers found.");
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

                    float boundsMag = rend.bounds.size.magnitude;

                    // Check 1: General small object shadow disable (bounds-only)
                    if (doSmall && boundsMag < smallThreshold)
                    {
                        rend.shadowCastingMode = ShadowCastingMode.Off;
                        smallDisabledThisPass++;
                        continue;
                    }

                    // Check 2 & 3 both need the name, so read it once
                    if ((doResources || doEnvironment) && boundsMag < Math.Max(resourceThreshold, environmentThreshold))
                    {
                        string goName = rend.gameObject.name;
                        if (goName == null) continue;

                        // Check 2: Resource object shadow disable
                        if (doResources && boundsMag < resourceThreshold && IsResourceObject(goName))
                        {
                            rend.shadowCastingMode = ShadowCastingMode.Off;
                            resourceDisabledThisPass++;
                            continue;
                        }

                        // Check 3: Environment object shadow disable (grass, cave flora)
                        if (doEnvironment && boundsMag < environmentThreshold && IsEnvironmentObject(goName))
                        {
                            rend.shadowCastingMode = ShadowCastingMode.Off;
                            environmentDisabledThisPass++;
                        }
                    }
                }
                catch { }
            }

            _smallShadowCastersDisabledCount += smallDisabledThisPass;
            _resourceShadowCastersDisabledCount += resourceDisabledThisPass;
            _environmentShadowCastersDisabledCount += environmentDisabledThisPass;

            if (smallDisabledThisPass > 0)
            {
                PerformancePlugin.Log.LogInfo(
                    $"Disabled shadows on {smallDisabledThisPass} small renderers " +
                    $"(< {smallThreshold:F1}m bounds). Total this session: {_smallShadowCastersDisabledCount}.");
            }

            if (resourceDisabledThisPass > 0)
            {
                PerformancePlugin.Log.LogInfo(
                    $"Disabled shadows on {resourceDisabledThisPass} resource objects " +
                    $"(< {resourceThreshold:F1}m bounds). Total this session: {_resourceShadowCastersDisabledCount}.");
            }

            if (environmentDisabledThisPass > 0)
            {
                PerformancePlugin.Log.LogInfo(
                    $"Disabled shadows on {environmentDisabledThisPass} environment objects " +
                    $"(< {environmentThreshold:F1}m bounds). Total this session: {_environmentShadowCastersDisabledCount}.");
            }

            if (smallDisabledThisPass == 0 && resourceDisabledThisPass == 0 && environmentDisabledThisPass == 0)
            {
                DebugLog("ShadowCasters: no new shadow casters found this pass.");
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
        _resourceShadowCastersDisabledCount = 0;
        _environmentShadowCastersDisabledCount = 0;
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
