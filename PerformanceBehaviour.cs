using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace AskaPerformanceBooster;

/// <summary>
/// Core MonoBehaviour that applies and maintains performance settings at runtime.
///
/// Philosophy: "read before write, only improve."
///
/// Aska uses HDRP (High Definition Render Pipeline). Optimizations are applied
/// in two tiers based on what is available in the current scene:
///
///   Tier 1 (Global, scene-independent -- applies IMMEDIATELY, even at menu):
///     - QualitySettings: LOD, textures, async upload, VSync, frame rate
///     - HDRP Asset pipeline support flags: the PRIMARY optimization mechanism
///       (supportSSAO=false, supportVolumetrics=false, etc.)
///     - SRP Batcher verification
///
///   Tier 2 (Volume overrides -- applies in any scene with Camera.main):
///     - Cosmetic post-processing disables: film grain, chromatic aberration,
///       vignette, motion blur, lens distortion, bloom reduction, DOF, panini
///     - HDShadowSettings Volume: shadow distance and cascade count
///     - Volume component disables for contact shadows, volumetric clouds, SSS
///
/// Scene transition detection uses edge-triggered logic (_wasInGameplay flag)
/// so caches are cleared ONCE when leaving gameplay, not every frame at the menu.
///
/// CRITICAL IL2CPP CONSTRAINT:
/// ClassInjector processes all INSTANCE methods on this class. Methods must NOT have
/// System.Object, System.Type, Dictionary&lt;&gt;, PropertyInfo, FieldInfo, etc. as
/// parameter types or return types. All such methods live in HDRPReflectionHelper
/// (static class, invisible to ClassInjector) or ExternalChangeTracker.
///
/// All interop calls are wrapped in try/catch for IL2CPP safety.
/// Settings are re-applied periodically to counteract game resets.
/// </summary>
public class PerformanceBehaviour : MonoBehaviour
{
    // ------------------------------------------------------------------
    //  State
    // ------------------------------------------------------------------
    private bool _settingsApplied;
    private bool _diagnosticScanDone;
    private float _reapplyTimer;

    // Delayed scene bottleneck scan state
    private bool _sceneBottleneckScanDone;
    private float _gameplayTimer; // seconds spent in gameplay since last scene entry

    // Tiered application state -- global settings can apply before gameplay
    private bool _globalSettingsApplied;

    // Scene transition tracking -- only clear caches on actual transitions,
    // not every frame while at the menu
    private bool _wasInGameplay;

    // ------------------------------------------------------------------
    //  External change tracking (Dictionary lives on static helper)
    // ------------------------------------------------------------------
    private readonly Dictionary<string, object> _lastSetValues = new();

    // Quality level change snapshot (set on change, consumed after reapply)
    private bool _qualityLevelJustChanged;
    private int _qualityLevelChangedTo;

    // Shader warmup state
    private bool _shaderWarmupDone;

    // Small shadow caster optimization state
    private bool _smallShadowCastersDone;
    private float _smallShadowCasterTimer;
    private int _smallShadowCastersDisabledCount;

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

        // ---------------------------------------------------------------
        //  Scene transition detection: clear caches and re-apply when
        //  transitioning between gameplay and non-gameplay scenes.
        // ---------------------------------------------------------------
        if (_wasInGameplay && !inGameplay)
        {
            ClearCachedReferences();
            _settingsApplied = false;
            _diagnosticScanDone = false;
            _sceneBottleneckScanDone = false;
            _gameplayTimer = 0f;
            _lastSetValues.Clear();
            PerformancePlugin.Log.LogInfo("Left gameplay -- cleared optimization cache.");
        }
        else if (!_wasInGameplay && inGameplay)
        {
            // Entering gameplay from menu -- new Volumes may exist.
            _settingsApplied = false;
            _gameplayTimer = 0f;
            _sceneBottleneckScanDone = false;
            _diagnosticScanDone = false;
            _smallShadowCastersDone = false;
            _smallShadowCasterTimer = 0f;
            _smallShadowCastersDisabledCount = 0;
            PerformancePlugin.Log.LogInfo("Entered gameplay -- re-applying volume optimizations.");
        }
        _wasInGameplay = inGameplay;

        // ---------------------------------------------------------------
        //  Detect quality level changes (can happen in any scene)
        // ---------------------------------------------------------------
        if (QualityLevelPatch.QualityLevelChanged)
        {
            QualityLevelPatch.QualityLevelChanged = false;
            int newLevel = QualityLevelPatch.LastSetLevel;

            PerformancePlugin.Log.LogInfo(
                $"Quality level changed to {newLevel} -- invalidating HDRP cache " +
                "and reapplying all optimizations.");

            HDRPReflectionHelper.InvalidateCache();
            _lastSetValues.Clear();
            _globalSettingsApplied = false;
            _settingsApplied = false;
            _reapplyTimer = 0f;
            _qualityLevelJustChanged = true;
            _qualityLevelChangedTo = newLevel;
            // Fall through to reapply below
        }

        // ---------------------------------------------------------------
        //  Diagnostic scan (non-blocking -- optimizations always apply)
        // ---------------------------------------------------------------
        if (inGameplay)
            _gameplayTimer += Time.unscaledDeltaTime;

        if (inGameplay && PerformancePlugin.CfgDiagnosticScan.Value)
        {
            if (!_diagnosticScanDone)
            {
                _diagnosticScanDone = true;
                RunHDRPPipelineScan();
            }

            if (!_sceneBottleneckScanDone && _gameplayTimer >= 30f)
            {
                _sceneBottleneckScanDone = true;
                PerformancePlugin.Log.LogInfo(
                    "Running GPU bottleneck scan after 30s gameplay delay...");
                RunSceneBottleneckScan();
            }
        }

        // ---------------------------------------------------------------
        //  Tier 1: Global settings (scene-independent)
        //  QualitySettings, HDRP Asset pipeline support flags, SRP Batcher.
        // ---------------------------------------------------------------
        if (!_globalSettingsApplied)
        {
            ApplyGlobalSettings();
            _globalSettingsApplied = true;
            PerformancePlugin.Log.LogInfo(
                "Tier 1 (global) performance optimizations applied " +
                $"(scene={GetActiveSceneName()}).");
        }

        // ---------------------------------------------------------------
        //  Tier 2: Volume overrides (any scene with Camera.main)
        //  Cosmetic post-processing disables, HDShadowSettings Volume,
        //  and Volume component disables for rendering effects.
        // ---------------------------------------------------------------
        if (!_settingsApplied)
        {
            try
            {
                var cam = Camera.main;
                if (cam != null)
                {
                    ApplyVolumeSettings(true);
                    _settingsApplied = true;
                    _reapplyTimer = 0f;

                    if (inGameplay && PerformancePlugin.CfgShaderWarmup.Value && !_shaderWarmupDone)
                    {
                        ApplyShaderWarmup();
                        _shaderWarmupDone = true;
                    }

                    PerformancePlugin.Log.LogInfo(
                        "Tier 2 (volume) performance optimizations applied " +
                        $"(scene={GetActiveSceneName()}).");
                }
            }
            catch (Exception ex)
            {
                DebugLog($"Tier 2 volume check failed: {ex.Message}");
            }
        }

        // ---------------------------------------------------------------
        //  Small shadow caster optimization (gameplay only)
        // ---------------------------------------------------------------
        if (inGameplay && PerformancePlugin.CfgDisableSmallShadowCasters.Value
            && PerformancePlugin.CfgEnableShadowOptimizations.Value)
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
                _smallShadowCasterTimer += Time.unscaledDeltaTime;
                if (_smallShadowCasterTimer >= 30f)
                {
                    _smallShadowCasterTimer = 0f;
                    ApplySmallShadowCasterOptimization();
                }
            }
        }

        // ---------------------------------------------------------------
        //  Periodic reapply timer
        // ---------------------------------------------------------------
        float reapplyInterval = PerformancePlugin.CfgReapplyInterval.Value;
        if (reapplyInterval <= 0f)
            return;

        _reapplyTimer += Time.unscaledDeltaTime;
        if (_reapplyTimer >= reapplyInterval)
        {
            _reapplyTimer = 0f;

            // Tier 1: always reapply
            ApplyGlobalSettings();

            // Tier 2: reapply volume overrides in any scene with a camera.
            try
            {
                if (Camera.main != null)
                {
                    bool shouldReapplyVolumes = PerformancePlugin.CfgReapplyPostProcessing.Value;
                    if (shouldReapplyVolumes)
                        ApplyVolumeSettings(false);
                }
            }
            catch (Exception ex)
            {
                DebugLog($"Tier 2 reapply volume check failed: {ex.Message}");
            }
        }
    }

    // ==================================================================
    //  Diagnostic Scan
    // ==================================================================

    /// <summary>
    /// Part 1: HDRP Asset/Pipeline scan -- runs immediately on first gameplay entry.
    /// </summary>
    private void RunHDRPPipelineScan()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== ASKA PERFORMANCE BOOSTER - HDRP PIPELINE SCAN ===");
        sb.AppendLine("Current rendering settings (optimizations apply independently):");
        sb.AppendLine();

        // QualitySettings
        sb.AppendLine("--- QualitySettings ---");
        try
        {
            sb.AppendLine($"  shadowDistance       = {QualitySettings.shadowDistance}");
            sb.AppendLine($"  shadowCascades       = {QualitySettings.shadowCascades}");
            sb.AppendLine($"  lodBias              = {QualitySettings.lodBias}");
            sb.AppendLine($"  maximumLODLevel      = {QualitySettings.maximumLODLevel}");
            sb.AppendLine($"  enableLODCrossFade   = {QualitySettings.enableLODCrossFade}");
            sb.AppendLine($"  skinWeights          = {QualitySettings.skinWeights}");
            sb.AppendLine($"  globalTextureMipmapLimit = {QualitySettings.globalTextureMipmapLimit}");
            sb.AppendLine($"  streamingMipmapsActive   = {QualitySettings.streamingMipmapsActive}");
            sb.AppendLine($"  streamingMipmapsMemoryBudget = {QualitySettings.streamingMipmapsMemoryBudget}");
            sb.AppendLine($"  anisotropicFiltering = {QualitySettings.anisotropicFiltering}");
            sb.AppendLine($"  pixelLightCount      = {QualitySettings.pixelLightCount}");
            sb.AppendLine($"  antiAliasing         = {QualitySettings.antiAliasing}");
            sb.AppendLine($"  vSyncCount           = {QualitySettings.vSyncCount}");
            sb.AppendLine($"  realtimeReflectionProbes = {QualitySettings.realtimeReflectionProbes}");
            sb.AppendLine($"  asyncUploadTimeSlice = {QualitySettings.asyncUploadTimeSlice}");
            sb.AppendLine($"  asyncUploadBufferSize = {QualitySettings.asyncUploadBufferSize}");
            sb.AppendLine($"  maxQueuedFrames      = {QualitySettings.maxQueuedFrames}");
            int tfr = Application.targetFrameRate;
            sb.AppendLine($"  targetFrameRate      = {tfr}");
            if (tfr > 0)
                sb.AppendLine($"  NOTE: Game has targetFrameRate set to {tfr} -- this limits maximum FPS.");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  [ERROR reading QualitySettings: {ex.Message}]");
        }

        // HDRP Asset and RenderPipelineSettings
        sb.AppendLine();
        sb.AppendLine("--- HDRP Asset & RenderPipelineSettings ---");
        try
        {
            HDRPReflectionHelper.DumpHDRPAssetDiagnostics(sb);
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  [ERROR reading HDRP Asset: {ex.Message}]");
        }

        // Volume components
        sb.AppendLine();
        sb.AppendLine("--- Volume Components ---");
        try
        {
            var volumes = FindObjectsOfType<Volume>();
            if (volumes != null)
            {
                foreach (var vol in volumes)
                {
                    if (vol == null) continue;
                    try
                    {
                        var profile = vol.sharedProfile;
                        if (profile == null) continue;
                        sb.AppendLine($"  Volume: {vol.gameObject.name} (priority={vol.priority}, global={vol.isGlobal})");
                        foreach (var comp in profile.components)
                        {
                            if (comp == null) continue;
                            try
                            {
                                string name = comp.GetType().Name;
                                string il2cppName = "";
                                try { il2cppName = comp.GetIl2CppType()?.Name ?? ""; }
                                catch { }
                                string displayName = !string.IsNullOrEmpty(il2cppName) && il2cppName != name
                                    ? $"{name} (il2cpp={il2cppName})"
                                    : name;
                                sb.AppendLine($"    {displayName}: active={comp.active}");
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  [ERROR reading volumes: {ex.Message}]");
        }

        // Camera
        sb.AppendLine();
        sb.AppendLine("--- Camera ---");
        try
        {
            var cam = Camera.main;
            if (cam != null)
            {
                sb.AppendLine($"  farClipPlane     = {cam.farClipPlane}");
                sb.AppendLine($"  nearClipPlane    = {cam.nearClipPlane}");
                sb.AppendLine($"  fieldOfView      = {cam.fieldOfView}");
                sb.AppendLine($"  allowHDR         = {cam.allowHDR}");
                sb.AppendLine($"  allowMSAA        = {cam.allowMSAA}");
            }
            else
            {
                sb.AppendLine("  [Camera.main not available]");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  [ERROR reading camera: {ex.Message}]");
        }

        // System info
        sb.AppendLine();
        sb.AppendLine("--- System Info ---");
        try
        {
            sb.AppendLine($"  graphicsDeviceName  = {SystemInfo.graphicsDeviceName}");
            sb.AppendLine($"  graphicsDeviceType  = {SystemInfo.graphicsDeviceType}");
            sb.AppendLine($"  graphicsMemorySize  = {SystemInfo.graphicsMemorySize} MB");
            sb.AppendLine($"  systemMemorySize    = {SystemInfo.systemMemorySize} MB");
            sb.AppendLine($"  maxTextureSize      = {SystemInfo.maxTextureSize}");
            sb.AppendLine($"  supportsInstancing  = {SystemInfo.supportsInstancing}");
            sb.AppendLine($"  supportsComputeShaders = {SystemInfo.supportsComputeShaders}");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  [ERROR reading system info: {ex.Message}]");
        }

        sb.AppendLine();
        sb.AppendLine("=== END HDRP PIPELINE SCAN ===");
        sb.AppendLine("Scene bottleneck scan (lights, renderers, cameras, probes) will run after 30s of gameplay.");

        PerformancePlugin.Log.LogInfo(sb.ToString());
    }

    /// <summary>
    /// Part 2: Scene bottleneck scan -- runs after 30 seconds of gameplay.
    /// </summary>
    private void RunSceneBottleneckScan()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== ASKA PERFORMANCE BOOSTER - SCENE BOTTLENECK SCAN ===");
        sb.AppendLine("(Running after 30s gameplay delay to capture fully loaded world)");
        sb.AppendLine();

        // Light audit
        sb.AppendLine("=== LIGHT AUDIT ===");
        try
        {
            var lights = FindObjectsOfType<Light>();
            if (lights != null && lights.Length > 0)
            {
                int dirCount = 0, dirShadow = 0;
                int pointCount = 0, pointShadow = 0;
                float pointRangeSum = 0f;
                int spotCount = 0, spotShadow = 0;
                float spotRangeSum = 0f;
                int areaCount = 0, areaShadow = 0;
                int largePunctualShadow = 0;

                foreach (var light in lights)
                {
                    if (light == null) continue;
                    try
                    {
                        bool hasShadow = light.shadows != LightShadows.None;
                        switch (light.type)
                        {
                            case LightType.Directional:
                                dirCount++;
                                if (hasShadow) dirShadow++;
                                break;
                            case LightType.Point:
                                pointCount++;
                                if (hasShadow) pointShadow++;
                                pointRangeSum += light.range;
                                if (hasShadow && light.range > 50f) largePunctualShadow++;
                                break;
                            case LightType.Spot:
                                spotCount++;
                                if (hasShadow) spotShadow++;
                                spotRangeSum += light.range;
                                if (hasShadow && light.range > 50f) largePunctualShadow++;
                                break;
                            default:
                                areaCount++;
                                if (hasShadow) areaShadow++;
                                break;
                        }
                    }
                    catch { }
                }

                sb.AppendLine($"Total lights: {lights.Length}");
                sb.AppendLine($"  Directional: {dirCount} (shadow-casting: {dirShadow})");
                sb.AppendLine($"  Point: {pointCount} (shadow-casting: {pointShadow}, avg range: {(pointCount > 0 ? pointRangeSum / pointCount : 0f):F1}m)");
                sb.AppendLine($"  Spot: {spotCount} (shadow-casting: {spotShadow}, avg range: {(spotCount > 0 ? spotRangeSum / spotCount : 0f):F1}m)");
                sb.AppendLine($"  Area: {areaCount} (shadow-casting: {areaShadow})");
                sb.AppendLine($"Shadow-casting punctual lights with range > 50m: {largePunctualShadow}");
            }
            else
            {
                sb.AppendLine("Total lights: 0");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  [ERROR reading lights: {ex.Message}]");
        }

        // Shadow-caster renderer audit
        sb.AppendLine();
        sb.AppendLine("=== SHADOW CASTER AUDIT ===");
        try
        {
            var renderers = FindObjectsOfType<Renderer>();
            if (renderers != null && renderers.Length > 0)
            {
                int shadowOn = 0, shadowOff = 0, smallShadowCasters = 0;

                foreach (var rend in renderers)
                {
                    if (rend == null) continue;
                    try
                    {
                        if (rend.shadowCastingMode != ShadowCastingMode.Off)
                        {
                            shadowOn++;
                            if (rend.bounds.size.magnitude < 1f)
                                smallShadowCasters++;
                        }
                        else
                        {
                            shadowOff++;
                        }
                    }
                    catch { }
                }

                sb.AppendLine($"Total renderers: {renderers.Length}");
                sb.AppendLine($"  Shadow-casting: {shadowOn}");
                sb.AppendLine($"  Shadow-off: {shadowOff}");
                sb.AppendLine($"Small shadow-casters (bounds < 1m): {smallShadowCasters}");
            }
            else
            {
                sb.AppendLine("Total renderers: 0");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  [ERROR reading renderers: {ex.Message}]");
        }

        // Camera audit
        sb.AppendLine();
        sb.AppendLine("=== CAMERA AUDIT ===");
        try
        {
            var cameras = FindObjectsOfType<Camera>();
            if (cameras != null && cameras.Length > 0)
            {
                int activeCount = 0;
                foreach (var c in cameras)
                {
                    if (c != null && c.isActiveAndEnabled) activeCount++;
                }

                sb.AppendLine($"Total cameras: {cameras.Length}");
                sb.AppendLine($"  Active: {activeCount}");

                foreach (var c in cameras)
                {
                    if (c == null) continue;
                    try
                    {
                        sb.AppendLine($"    - \"{c.gameObject.name}\" (tag={c.tag}, active={c.isActiveAndEnabled}, depth={c.depth})");
                    }
                    catch { }
                }
            }
            else
            {
                sb.AppendLine("Total cameras: 0");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  [ERROR reading cameras: {ex.Message}]");
        }

        // Reflection probe inventory
        sb.AppendLine();
        sb.AppendLine("=== REFLECTION PROBES ===");
        try
        {
            var probes = FindObjectsOfType<ReflectionProbe>();
            if (probes != null && probes.Length > 0)
            {
                int realtime = 0, baked = 0, custom = 0;

                foreach (var probe in probes)
                {
                    if (probe == null) continue;
                    try
                    {
                        switch (probe.mode)
                        {
                            case ReflectionProbeMode.Realtime: realtime++; break;
                            case ReflectionProbeMode.Baked: baked++; break;
                            case ReflectionProbeMode.Custom: custom++; break;
                        }
                    }
                    catch { }
                }

                sb.AppendLine($"Total probes: {probes.Length}");
                sb.AppendLine($"  Realtime: {realtime}, Baked: {baked}, Custom: {custom}");
            }
            else
            {
                sb.AppendLine("Total probes: 0");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  [ERROR reading reflection probes: {ex.Message}]");
        }

        sb.AppendLine();
        sb.AppendLine("=== END SCENE BOTTLENECK SCAN ===");

        PerformancePlugin.Log.LogInfo(sb.ToString());
    }

    // ==================================================================
    //  Tier 1: Global settings (scene-independent, read-before-write)
    // ==================================================================

    private void ApplyGlobalSettings()
    {
        try
        {
            ApplyQualitySettings();
        }
        catch (Exception ex)
        {
            PerformancePlugin.Log.LogWarning($"Error in ApplyQualitySettings: {ex.Message}");
        }

        try
        {
            ApplyHDRPAssetSettings();
        }
        catch (Exception ex)
        {
            PerformancePlugin.Log.LogWarning($"Error in ApplyHDRPAssetSettings: {ex.Message}");
        }
    }

    // ==================================================================
    //  Small shadow caster optimization (gameplay only)
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
    //  Tier 2: Volume overrides (any scene with Camera.main)
    // ==================================================================

    private void ApplyVolumeSettings(bool isFirstApplication)
    {
        bool shouldApply = isFirstApplication ||
            PerformancePlugin.CfgReapplyPostProcessing.Value;

        if (shouldApply && PerformancePlugin.CfgEnablePostProcessingOptimizations.Value)
        {
            try
            {
                ApplyPostProcessingSettings();
            }
            catch (Exception ex)
            {
                PerformancePlugin.Log.LogWarning($"Error in ApplyPostProcessingSettings: {ex.Message}");
            }
        }

        if (shouldApply && PerformancePlugin.CfgEnableHDRPPipelineOptimizations.Value)
        {
            try
            {
                ApplyHDRPVolumeQualityReductions();
            }
            catch (Exception ex)
            {
                PerformancePlugin.Log.LogWarning($"Error in ApplyHDRPVolumeQualityReductions: {ex.Message}");
            }
        }
    }

    // ==================================================================
    //  1. QualitySettings (read-before-write)
    // ==================================================================

    private void ApplyQualitySettings()
    {
        try
        {
            // -- LOD (read-before-write) -- gated by LOD toggle
            if (PerformancePlugin.CfgEnableLODOptimizations.Value)
            {
                float lodBiasCfg = PerformancePlugin.CfgLODBias.Value;
                if (lodBiasCfg > 0f)
                {
                    float currentLod = QualitySettings.lodBias;
                    if (WasChangedExternally("QS.lodBias", currentLod))
                    {
                        DebugLog($"QS lodBias: {currentLod} (changed externally, respecting)");
                    }
                    else if (currentLod > lodBiasCfg)
                    {
                        QualitySettings.lodBias = lodBiasCfg;
                        RecordLastSet("QS.lodBias", lodBiasCfg);
                        DebugLog($"QS lodBias: {currentLod} -> {lodBiasCfg}");
                    }
                    else
                    {
                        DebugLog($"QS lodBias: {currentLod} (already <= {lodBiasCfg}, skipped)");
                    }
                }

                int maxLodCfg = PerformancePlugin.CfgMaximumLODLevel.Value;
                if (maxLodCfg > 0)
                {
                    int currentMaxLod = QualitySettings.maximumLODLevel;
                    if (WasChangedExternally("QS.maximumLODLevel", currentMaxLod))
                    {
                        DebugLog($"QS maximumLODLevel: {currentMaxLod} (changed externally, respecting)");
                    }
                    else if (currentMaxLod < maxLodCfg)
                    {
                        QualitySettings.maximumLODLevel = maxLodCfg;
                        RecordLastSet("QS.maximumLODLevel", maxLodCfg);
                        DebugLog($"QS maximumLODLevel: {currentMaxLod} -> {maxLodCfg}");
                    }
                }

                int skinWeightsCfg = PerformancePlugin.CfgSkinWeights.Value;
                if (skinWeightsCfg > 0)
                {
                    int currentSW = (int)QualitySettings.skinWeights;
                    SkinWeights target = skinWeightsCfg switch
                    {
                        1 => SkinWeights.OneBone,
                        2 => SkinWeights.TwoBones,
                        _ => SkinWeights.FourBones,
                    };
                    if (WasChangedExternally("QS.skinWeights", currentSW))
                    {
                        DebugLog($"QS skinWeights: {currentSW} (changed externally, respecting)");
                    }
                    else if (currentSW > skinWeightsCfg)
                    {
                        QualitySettings.skinWeights = target;
                        RecordLastSet("QS.skinWeights", skinWeightsCfg);
                        DebugLog($"QS skinWeights: -> {target}");
                    }
                }
            }

            // -- Textures (read-before-write) -- gated by texture toggle
            if (PerformancePlugin.CfgEnableTextureOptimizations.Value)
            {
                int mipLimitCfg = PerformancePlugin.CfgGlobalTextureMipmapLimit.Value;
                if (mipLimitCfg > 0)
                {
                    int currentMip = QualitySettings.globalTextureMipmapLimit;
                    if (WasChangedExternally("QS.mipLimit", currentMip))
                    {
                        DebugLog($"QS globalTextureMipmapLimit: {currentMip} (changed externally, respecting)");
                    }
                    else if (currentMip < mipLimitCfg)
                    {
                        QualitySettings.globalTextureMipmapLimit = mipLimitCfg;
                        RecordLastSet("QS.mipLimit", mipLimitCfg);
                        DebugLog($"QS globalTextureMipmapLimit: {currentMip} -> {mipLimitCfg}");
                    }
                }

                if (PerformancePlugin.CfgStreamingMipmapsActive.Value)
                {
                    if (!QualitySettings.streamingMipmapsActive)
                    {
                        QualitySettings.streamingMipmapsActive = true;
                        QualitySettings.streamingMipmapsMemoryBudget =
                            PerformancePlugin.CfgStreamingMipmapsMemoryBudget.Value;
                        QualitySettings.streamingMipmapsMaxLevelReduction =
                            PerformancePlugin.CfgStreamingMipmapsMaxLevelReduction.Value;
                        DebugLog("QS mipmap streaming enabled (was off).");
                    }
                    else
                    {
                        DebugLog("QS mipmap streaming: already active, not modifying.");
                    }
                }

                int anisoCfg = PerformancePlugin.CfgAnisotropicFiltering.Value;
                if (anisoCfg >= 0)
                {
                    int currentAniso = (int)QualitySettings.anisotropicFiltering;
                    if (WasChangedExternally("QS.aniso", currentAniso))
                    {
                        DebugLog($"QS anisotropicFiltering: {currentAniso} (changed externally, respecting)");
                    }
                    else if (currentAniso > anisoCfg)
                    {
                        QualitySettings.anisotropicFiltering = anisoCfg switch
                        {
                            0 => AnisotropicFiltering.Disable,
                            1 => AnisotropicFiltering.Enable,
                            _ => AnisotropicFiltering.ForceEnable,
                        };
                        RecordLastSet("QS.aniso", anisoCfg);
                        DebugLog($"QS anisotropicFiltering: {currentAniso} -> {anisoCfg}");
                    }
                }
            }

            // -- Realtime reflection probes -- gated by lighting toggle
            if (PerformancePlugin.CfgEnableLightingOptimizations.Value)
            {
                if (!PerformancePlugin.CfgRealtimeReflectionProbes.Value)
                {
                    if (QualitySettings.realtimeReflectionProbes)
                    {
                        QualitySettings.realtimeReflectionProbes = false;
                        DebugLog("QS realtimeReflectionProbes: true -> false");
                    }
                }
            }

            // -- Async upload (only increase, never decrease)
            int asyncTimeCfg = PerformancePlugin.CfgAsyncUploadTimeSlice.Value;
            if (QualitySettings.asyncUploadTimeSlice < asyncTimeCfg)
            {
                DebugLog($"QS asyncUploadTimeSlice: {QualitySettings.asyncUploadTimeSlice} -> {asyncTimeCfg}");
                QualitySettings.asyncUploadTimeSlice = asyncTimeCfg;
            }

            int asyncBufCfg = PerformancePlugin.CfgAsyncUploadBufferSize.Value;
            if (QualitySettings.asyncUploadBufferSize < asyncBufCfg)
            {
                DebugLog($"QS asyncUploadBufferSize: {QualitySettings.asyncUploadBufferSize} -> {asyncBufCfg}");
                QualitySettings.asyncUploadBufferSize = asyncBufCfg;
            }

            // -- Max queued frames
            int mqf = PerformancePlugin.CfgMaxQueuedFrames.Value;
            if (mqf >= 0)
            {
                int currentMqf = QualitySettings.maxQueuedFrames;
                if (currentMqf < 0 || currentMqf > mqf)
                {
                    QualitySettings.maxQueuedFrames = mqf;
                    DebugLog($"QS maxQueuedFrames: {currentMqf} -> {mqf}");
                }
            }

            // -- VSync
            int vsyncCfg = PerformancePlugin.CfgVSyncCount.Value;
            if (vsyncCfg >= 0)
            {
                if (QualitySettings.vSyncCount != vsyncCfg)
                {
                    DebugLog($"QS vSyncCount: {QualitySettings.vSyncCount} -> {vsyncCfg}");
                    QualitySettings.vSyncCount = vsyncCfg;
                }
            }

            // -- Target frame rate
            int fpsCfg = PerformancePlugin.CfgTargetFrameRate.Value;
            if (fpsCfg != 0)
            {
                int prev = Application.targetFrameRate;
                Application.targetFrameRate = fpsCfg;
                if (prev != fpsCfg)
                    DebugLog($"Application.targetFrameRate: {prev} -> {fpsCfg}");
            }
        }
        catch (Exception ex)
        {
            PerformancePlugin.Log.LogWarning($"Error applying QualitySettings: {ex.Message}");
        }
    }

    // ==================================================================
    //  2. HDRP Asset settings (pipeline support flags)
    // ==================================================================

    private void ApplyHDRPAssetSettings()
    {
        try
        {
            HDRPReflectionHelper.CheckHdrpAssetStale();
            HDRPReflectionHelper.CacheHDRPReflection();
            if (HDRPReflectionHelper.HdrpAssetRef == null) return;

            // Snapshot the NEW asset's properties BEFORE we apply optimizations.
            // This runs once per quality level change and captures the original
            // values so we can diagnose why Low might be slower than Medium.
            if (_qualityLevelJustChanged)
            {
                _qualityLevelJustChanged = false;
                try
                {
                    HDRPReflectionHelper.LogQualityLevelSnapshot(_qualityLevelChangedTo);
                }
                catch (Exception ex)
                {
                    DebugLog($"Quality level snapshot failed: {ex.Message}");
                }
            }

            // SRP Batcher -- gated by draw call toggle
            if (PerformancePlugin.CfgEnableDrawCallOptimizations.Value)
            {
                if (PerformancePlugin.CfgForceSRPBatcher.Value)
                {
                    try
                    {
                        bool currentSRP = GraphicsSettings.useScriptableRenderPipelineBatching;
                        if (!currentSRP)
                        {
                            GraphicsSettings.useScriptableRenderPipelineBatching = true;
                            DebugLog("HDRP SRP Batcher: false -> true");
                        }
                        else
                        {
                            DebugLog("HDRP SRP Batcher: already enabled, skipped.");
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLog($"Could not access SRP Batcher: {ex.Message}");
                    }
                }
            }

            // Pipeline support flags -- THE PRIMARY OPTIMIZATION MECHANISM
            if (PerformancePlugin.CfgEnableHDRPPipelineOptimizations.Value)
            {
                ApplyPipelineSupportFlags();
            }

            DebugLog("HDRP Asset settings pass complete.");
        }
        catch (Exception ex)
        {
            PerformancePlugin.Log.LogWarning($"Error applying HDRP Asset settings: {ex.Message}");
        }
    }

    // ==================================================================
    //  Pipeline support flags
    // ==================================================================

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
    //  3. Post-processing (Volume Overrides -- cosmetic effects)
    // ==================================================================

    private void ApplyPostProcessingSettings()
    {
        try
        {
            var volumes = FindObjectsOfType<Volume>();
            if (volumes == null || volumes.Length == 0) return;

            foreach (var volume in volumes)
            {
                if (volume == null) continue;
                try { ApplyVolumeOverrides(volume); }
                catch (Exception ex)
                {
                    PerformancePlugin.Log.LogWarning(
                        $"Error modifying Volume on '{volume.gameObject.name}': {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            PerformancePlugin.Log.LogWarning($"Error scanning for Volume components: {ex.Message}");
        }
    }

    private static void ApplyVolumeOverrides(Volume volume)
    {
        var profile = volume.sharedProfile;
        if (profile == null) return;

        foreach (var component in profile.components)
        {
            if (component == null) continue;

            try
            {
                string typeName = ResolveVolumeComponentTypeName(component);
                if (typeName == null) continue;

                // Disable cosmetic effects that are currently active
                if (typeName.Contains("FilmGrain") && PerformancePlugin.CfgDisableFilmGrain.Value)
                {
                    if (component.active)
                    {
                        component.active = false;
                        DebugLog("Disabled Film Grain (was active)");
                    }
                }
                else if (typeName.Contains("ChromaticAberration") && PerformancePlugin.CfgDisableChromaticAberration.Value)
                {
                    if (component.active)
                    {
                        component.active = false;
                        DebugLog("Disabled Chromatic Aberration (was active)");
                    }
                }
                else if (typeName.Contains("LensDistortion") && PerformancePlugin.CfgDisableLensDistortion.Value)
                {
                    if (component.active)
                    {
                        component.active = false;
                        DebugLog("Disabled Lens Distortion (was active)");
                    }
                }
                else if (typeName.Contains("MotionBlur") && PerformancePlugin.CfgDisableMotionBlur.Value)
                {
                    if (component.active)
                    {
                        component.active = false;
                        DebugLog("Disabled Motion Blur (was active)");
                    }
                }
                else if (typeName.Contains("Bloom") && PerformancePlugin.CfgReduceBloomIterations.Value)
                {
                    TryReduceBloomIterations(component);
                }
                else if (typeName.Contains("DepthOfField") && PerformancePlugin.CfgDisableDepthOfField.Value)
                {
                    if (component.active)
                    {
                        component.active = false;
                        DebugLog("Disabled Depth of Field (was active)");
                    }
                }
                else if (typeName.Contains("Vignette") && PerformancePlugin.CfgDisableVignette.Value)
                {
                    if (component.active)
                    {
                        component.active = false;
                        DebugLog("Disabled Vignette (was active)");
                    }
                }
                else if (typeName.Contains("PaniniProjection") && PerformancePlugin.CfgDisablePaniniProjection.Value)
                {
                    if (component.active)
                    {
                        component.active = false;
                        DebugLog("Disabled Panini Projection (was active)");
                    }
                }
                else if (typeName.Contains("MicroShadowing") && PerformancePlugin.CfgDisableMicroShadows.Value)
                {
                    if (component.active)
                    {
                        component.active = false;
                        DebugLog("Disabled Micro Shadowing (was active)");
                    }
                }
                // Contact shadows -- Volume component disable
                else if (typeName.Contains("ContactShadow") && PerformancePlugin.CfgDisableContactShadows.Value)
                {
                    if (component.active)
                    {
                        component.active = false;
                        DebugLog("Disabled Contact Shadows (was active)");
                    }
                }
                // Volumetric clouds -- Volume component disable
                else if (typeName.Contains("VolumetricClouds") && PerformancePlugin.CfgDisableVolumetricClouds.Value)
                {
                    if (component.active)
                    {
                        component.active = false;
                        DebugLog("Disabled Volumetric Clouds (was active)");
                    }
                }
                // Subsurface scattering -- Volume component disable
                else if (typeName.Contains("SubsurfaceScattering") && PerformancePlugin.CfgDisableSubsurfaceScattering.Value)
                {
                    if (component.active)
                    {
                        component.active = false;
                        DebugLog("Disabled Subsurface Scattering (was active)");
                    }
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// Resolve a VolumeComponent's type name using both managed and IL2CPP type info.
    /// </summary>
    private static string ResolveVolumeComponentTypeName(VolumeComponent component)
    {
        string managedName = null;
        try { managedName = component.GetType().Name; }
        catch { }

        string il2cppName = null;
        try { il2cppName = component.GetIl2CppType()?.Name; }
        catch { }

        if (!string.IsNullOrEmpty(managedName) && !string.IsNullOrEmpty(il2cppName))
            return managedName + "|" + il2cppName;
        if (!string.IsNullOrEmpty(managedName))
            return managedName;
        if (!string.IsNullOrEmpty(il2cppName))
            return il2cppName;
        return null;
    }

    /// <summary>
    /// Apply HDShadowSettings Volume overrides for shadow distance and cascade count.
    /// HDRP ignores QualitySettings.shadowDistance -- it reads from this Volume.
    /// </summary>
    private void ApplyHDRPVolumeQualityReductions()
    {
        try
        {
            var volumes = FindObjectsOfType<Volume>();
            if (volumes == null || volumes.Length == 0)
            {
                PerformancePlugin.Log.LogWarning(
                    "HDRP Volume reductions: no Volume objects found in scene.");
                return;
            }

            bool foundHDShadowSettings = false;
            int volumeCount = 0;

            foreach (var volume in volumes)
            {
                if (volume == null) continue;
                var profile = volume.sharedProfile;
                if (profile == null) continue;
                volumeCount++;

                foreach (var component in profile.components)
                {
                    if (component == null || !component.active) continue;

                    try
                    {
                        string typeName = null;
                        try { typeName = component.GetIl2CppType()?.Name; }
                        catch { }
                        if (string.IsNullOrEmpty(typeName))
                        {
                            try { typeName = component.GetType().Name; }
                            catch { }
                        }
                        if (string.IsNullOrEmpty(typeName)) continue;

                        // HDShadowSettings Volume: shadow distance and cascade count
                        if (typeName.Contains("HDShadowSettings") &&
                            PerformancePlugin.CfgEnableShadowOptimizations.Value)
                        {
                            foundHDShadowSettings = true;
                            TryApplyHDShadowSettings(component);
                        }
                    }
                    catch { }
                }
            }

            PerformancePlugin.Log.LogInfo(
                $"HDRP Volume reductions: scanned {volumeCount} volumes. " +
                $"HDShadowSettings {(foundHDShadowSettings ? "FOUND and applied" : "NOT FOUND")}.");

            if (!foundHDShadowSettings && PerformancePlugin.CfgEnableShadowOptimizations.Value)
            {
                PerformancePlugin.Log.LogWarning(
                    "HDShadowSettings: NOT FOUND in any volume -- " +
                    "shadow distance/cascade optimizations cannot be applied.");
            }
        }
        catch (Exception ex)
        {
            PerformancePlugin.Log.LogWarning($"Error applying HDRP volume quality reductions: {ex.Message}");
        }
    }

    /// <summary>
    /// Apply shadow distance and cascade count via HDShadowSettings Volume override.
    /// </summary>
    private static void TryApplyHDShadowSettings(VolumeComponent shadowSettings)
    {
        try
        {
            Type shadowType = shadowSettings.GetType();

            float shadowDistCfg = PerformancePlugin.CfgShadowDistance.Value;
            if (shadowDistCfg > 0f)
            {
                float currentShadowDist = -1f;
                try
                {
                    var distField = shadowType.GetField("maxShadowDistance",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (distField != null)
                    {
                        var param = distField.GetValue(shadowSettings);
                        if (param != null)
                        {
                            var valueProp = param.GetType().GetProperty("value",
                                BindingFlags.Public | BindingFlags.Instance);
                            if (valueProp != null)
                                currentShadowDist = Convert.ToSingle(valueProp.GetValue(param));
                        }
                    }
                }
                catch { }

                TryEnableVolumeParameterOverride(shadowType, shadowSettings, "maxShadowDistance");
                TrySetVolumeParameterFloat(shadowType, shadowSettings,
                    "maxShadowDistance", shadowDistCfg);

                PerformancePlugin.Log.LogInfo(
                    $"HDShadowSettings: maxShadowDistance set to {shadowDistCfg} (was {currentShadowDist})");
            }

            int cascadesCfg = PerformancePlugin.CfgShadowCascades.Value;
            if (cascadesCfg < 4)
            {
                TryEnableVolumeParameterOverride(shadowType, shadowSettings, "cascadeShadowSplitCount");
                TrySetVolumeParameterInt(shadowType, shadowSettings,
                    "cascadeShadowSplitCount", cascadesCfg);

                PerformancePlugin.Log.LogInfo(
                    $"HDShadowSettings: cascadeShadowSplitCount set to {cascadesCfg}");
            }
        }
        catch (Exception ex)
        {
            PerformancePlugin.Log.LogWarning(
                $"HDShadowSettings: FAILED to apply Volume override: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Enable the overrideState on a VolumeParameter.
    /// </summary>
    private static void TryEnableVolumeParameterOverride(Type componentType, VolumeComponent component,
        string fieldName)
    {
        try
        {
            var field = componentType.GetField(fieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            object param = null;

            if (field != null)
            {
                param = field.GetValue(component);
            }
            else
            {
                var prop = componentType.GetProperty(fieldName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop != null)
                    param = prop.GetValue(component);
            }

            if (param == null) return;

            var overrideProp = param.GetType().GetProperty("overrideState",
                BindingFlags.Public | BindingFlags.Instance);
            if (overrideProp != null && overrideProp.GetSetMethod() != null)
            {
                bool current = false;
                try { current = (bool)overrideProp.GetValue(param); }
                catch { }

                if (!current)
                {
                    overrideProp.SetValue(param, true);
                    DebugLog($"Volume param {fieldName}: overrideState enabled");
                }
            }
        }
        catch { }
    }

    private static void TryReduceBloomIterations(VolumeComponent bloom)
    {
        try
        {
            int maxIterations = PerformancePlugin.CfgBloomMaxIterations.Value;
            Type bloomType = bloom.GetType();

            var iterField = bloomType.GetField("maxIterations",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (iterField != null)
            {
                var param = iterField.GetValue(bloom);
                if (param != null)
                {
                    var valueProp = param.GetType().GetProperty("value",
                        BindingFlags.Public | BindingFlags.Instance);
                    if (valueProp != null && valueProp.GetSetMethod() != null)
                    {
                        int currentVal = 0;
                        try { currentVal = (int)valueProp.GetValue(param); }
                        catch { }

                        if (currentVal > maxIterations)
                        {
                            valueProp.SetValue(param, maxIterations);
                            DebugLog($"Bloom maxIterations: {currentVal} -> {maxIterations}");
                        }
                    }
                }
            }

            var skipField = bloomType.GetField("skipIterations",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (skipField != null)
            {
                var param = skipField.GetValue(bloom);
                if (param != null)
                {
                    var valueProp = param.GetType().GetProperty("value",
                        BindingFlags.Public | BindingFlags.Instance);
                    if (valueProp != null && valueProp.GetSetMethod() != null)
                    {
                        int skipCount = Math.Max(0, 6 - maxIterations);
                        valueProp.SetValue(param, skipCount);
                        DebugLog($"Bloom skipIterations set to {skipCount}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            DebugLog($"Could not reduce bloom iterations: {ex.Message}");
        }
    }

    /// <summary>
    /// Unconditionally set a float VolumeParameter.
    /// </summary>
    private static void TrySetVolumeParameterFloat(Type componentType, VolumeComponent component,
        string fieldName, float value)
    {
        try
        {
            var field = componentType.GetField(fieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null)
            {
                var prop = componentType.GetProperty(fieldName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop == null) return;

                var propParam = prop.GetValue(component);
                if (propParam == null) return;

                var propValueProp = propParam.GetType().GetProperty("value",
                    BindingFlags.Public | BindingFlags.Instance);
                if (propValueProp == null || propValueProp.GetSetMethod() == null) return;

                propValueProp.SetValue(propParam, value);
                DebugLog($"Volume param {fieldName} (prop): -> {value}");
                return;
            }

            var param = field.GetValue(component);
            if (param == null) return;

            var valueProp = param.GetType().GetProperty("value",
                BindingFlags.Public | BindingFlags.Instance);
            if (valueProp == null || valueProp.GetSetMethod() == null) return;

            valueProp.SetValue(param, value);
            DebugLog($"Volume param {fieldName}: -> {value}");
        }
        catch { }
    }

    /// <summary>
    /// Set an int VolumeParameter.
    /// </summary>
    private static void TrySetVolumeParameterInt(Type componentType, VolumeComponent component,
        string fieldName, int value)
    {
        try
        {
            var field = componentType.GetField(fieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null)
            {
                var prop = componentType.GetProperty(fieldName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop == null) return;

                var propParam = prop.GetValue(component);
                if (propParam == null) return;

                var propValueProp = propParam.GetType().GetProperty("value",
                    BindingFlags.Public | BindingFlags.Instance);
                if (propValueProp == null || propValueProp.GetSetMethod() == null) return;

                propValueProp.SetValue(propParam, value);
                DebugLog($"Volume param {fieldName} (prop): -> {value}");
                return;
            }

            var param = field.GetValue(component);
            if (param == null) return;

            var valueProp = param.GetType().GetProperty("value",
                BindingFlags.Public | BindingFlags.Instance);
            if (valueProp == null || valueProp.GetSetMethod() == null) return;

            valueProp.SetValue(param, value);
            DebugLog($"Volume param {fieldName}: -> {value}");
        }
        catch { }
    }

    // ==================================================================
    //  Shader Warmup
    // ==================================================================

    private void ApplyShaderWarmup()
    {
        try
        {
            Shader.WarmupAllShaders();
            PerformancePlugin.Log.LogInfo("Shader.WarmupAllShaders() completed.");
        }
        catch (Exception ex)
        {
            PerformancePlugin.Log.LogWarning($"Shader warmup failed: {ex.Message}");
        }
    }

    // ==================================================================
    //  External change detection
    // ==================================================================

    private bool WasChangedExternally(string settingKey, float currentValue)
        => ExternalChangeTracker.WasChangedExternally(_lastSetValues, settingKey, currentValue);

    private bool WasChangedExternally(string settingKey, int currentValue)
        => ExternalChangeTracker.WasChangedExternally(_lastSetValues, settingKey, currentValue);

    private void RecordLastSet(string settingKey, float value)
        => ExternalChangeTracker.RecordLastSet(_lastSetValues, settingKey, value);

    private void RecordLastSet(string settingKey, int value)
        => ExternalChangeTracker.RecordLastSet(_lastSetValues, settingKey, value);

    // ==================================================================
    //  Cleanup
    // ==================================================================

    private void ClearCachedReferences()
    {
        HDRPReflectionHelper.InvalidateCache();
        _shaderWarmupDone = false;
        _globalSettingsApplied = false;
        _smallShadowCastersDone = false;
        _smallShadowCasterTimer = 0f;
        _smallShadowCastersDisabledCount = 0;
        _lastSetValues.Clear();
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

/// <summary>
/// Static helper for external change tracking.
/// </summary>
internal static class ExternalChangeTracker
{
    internal static bool WasChangedExternally(
        Dictionary<string, object> lastSetValues, string settingKey, float currentValue)
    {
        if (!PerformancePlugin.CfgRespectExternalChanges.Value)
            return false;

        if (!lastSetValues.TryGetValue(settingKey, out object lastSetObj))
            return false;

        try
        {
            if (lastSetObj is float lf)
            {
                if (Math.Abs(currentValue - lf) <= 0.001f)
                    return false;

                bool movedFurtherDown = currentValue < lf;
                if (PerformancePlugin.CfgDebugLogging.Value)
                    PerformancePlugin.Log.LogInfo(
                        $"[Debug] External check {settingKey}: was {lf}, now {currentValue}, " +
                        $"furtherDown={movedFurtherDown} -> {(movedFurtherDown ? "respect" : "reapply")}");
                return movedFurtherDown;
            }
        }
        catch { }

        return false;
    }

    internal static bool WasChangedExternally(
        Dictionary<string, object> lastSetValues, string settingKey, int currentValue)
    {
        if (!PerformancePlugin.CfgRespectExternalChanges.Value)
            return false;

        if (!lastSetValues.TryGetValue(settingKey, out object lastSetObj))
            return false;

        try
        {
            if (lastSetObj is int li)
            {
                if (currentValue == li)
                    return false;

                bool isIncreaseGood = settingKey is "QS.maximumLODLevel" or "QS.mipLimit";
                bool movedFurther = isIncreaseGood ? (currentValue > li) : (currentValue < li);
                if (PerformancePlugin.CfgDebugLogging.Value)
                    PerformancePlugin.Log.LogInfo(
                        $"[Debug] External check {settingKey}: was {li}, now {currentValue}, " +
                        $"movedFurther={movedFurther} -> {(movedFurther ? "respect" : "reapply")}");
                return movedFurther;
            }
        }
        catch { }

        return false;
    }

    internal static void RecordLastSet(
        Dictionary<string, object> lastSetValues, string settingKey, float value)
    {
        lastSetValues[settingKey] = value;
    }

    internal static void RecordLastSet(
        Dictionary<string, object> lastSetValues, string settingKey, int value)
    {
        lastSetValues[settingKey] = value;
    }
}
