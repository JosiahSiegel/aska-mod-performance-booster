using System;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Rendering;

namespace AskaPerformanceBooster;

/// <summary>
/// Static diagnostics helper that logs runtime performance metrics to BepInEx log.
///
/// CRITICAL: This MUST be a static class (same pattern as HDRPReflectionHelper).
/// IL2CPP's ClassInjector processes all instance methods on MonoBehaviour subclasses
/// and chokes on System.Object/System.Type/Dictionary/PropertyInfo parameter types.
/// Static methods on non-MonoBehaviour classes are invisible to ClassInjector.
///
/// All public entry points called from PerformanceBehaviour use only primitive
/// or void parameter/return types to stay ClassInjector-safe.
///
/// Capabilities:
///   1. Core frame stats (FPS, frame time, renderer/rigidbody counts)
///   2. FrameTimingManager CPU/GPU split (Unity 6 runtime API)
///   3. Object category breakdown (expensive, on-demand)
///   4. FixedUpdate-per-frame tracking
///   5. Small shadow caster threshold stats
/// </summary>
internal static class DiagnosticsHelper
{
    // ------------------------------------------------------------------
    //  FPS smoothing (exponential moving average)
    // ------------------------------------------------------------------
    private static float _smoothedFps;
    private static float _smoothedFrameTime;
    private const float FpsSmoothFactor = 0.05f; // lower = smoother
    private static bool _fpsInitialized;

    // ------------------------------------------------------------------
    //  Diagnostic interval timer
    // ------------------------------------------------------------------
    private static float _diagnosticTimer;

    // ------------------------------------------------------------------
    //  FixedUpdate tracking
    // ------------------------------------------------------------------
    private static int _fixedUpdateCountThisFrame;
    private static int _fixedUpdateCountLastFrame;

    // ------------------------------------------------------------------
    //  FrameTimingManager warmup
    // ------------------------------------------------------------------
    private static int _captureWarmupFrames;
    private const int RequiredWarmupFrames = 120;
    private static bool _frameTimingFeatureChecked;
    private static bool _frameTimingFeatureAvailable;

    // ------------------------------------------------------------------
    //  Gameplay state
    // ------------------------------------------------------------------
    private static bool _inGameplay;

    // ------------------------------------------------------------------
    //  Regex for object name grouping (compiled once)
    // ------------------------------------------------------------------
    private static readonly Regex CloneAndSuffixRegex =
        new(@"[\s_]*\(Clone\)$|[\s_]*\(\d+\)$|[\s_]*\d+$", RegexOptions.Compiled);

    // ==================================================================
    //  Entry points called from PerformanceBehaviour
    //  (all void/primitive params -- ClassInjector-safe)
    // ==================================================================

    /// <summary>
    /// Called from PerformanceBehaviour.Update() every frame.
    /// Gates all work behind EnableDiagnostics config.
    /// </summary>
    internal static void OnUpdate()
    {
        if (!PerformancePlugin.CfgEnableDiagnostics.Value)
            return;

        try
        {
            float dt = Time.unscaledDeltaTime;

            // -- Per-frame: update FPS EMA --
            UpdateFpsSmoothing(dt);

            // -- Per-frame: capture frame timings (cheap) --
            if (PerformancePlugin.CfgLogFrameTimings.Value && _frameTimingFeatureAvailable)
            {
                try
                {
                    FrameTimingManager.CaptureFrameTimings();
                    _captureWarmupFrames++;
                }
                catch { }
            }

            // -- Snapshot FixedUpdate count for this frame, then reset --
            _fixedUpdateCountLastFrame = _fixedUpdateCountThisFrame;
            _fixedUpdateCountThisFrame = 0;

            // -- Diagnostic interval --
            float interval = PerformancePlugin.CfgDiagnosticIntervalSeconds.Value;
            _diagnosticTimer += dt;
            if (_diagnosticTimer >= interval)
            {
                _diagnosticTimer = 0f;
                LogDiagnostics();
            }
        }
        catch (Exception ex)
        {
            // Swallow to avoid spamming; log once at debug level
            if (PerformancePlugin.CfgDebugLogging.Value)
                PerformancePlugin.Log.LogWarning($"[Diag] OnUpdate error: {ex.Message}");
        }
    }

    /// <summary>
    /// Called from PerformanceBehaviour.FixedUpdate() every physics tick.
    /// </summary>
    internal static void OnFixedUpdate()
    {
        if (!PerformancePlugin.CfgEnableDiagnostics.Value)
            return;

        _fixedUpdateCountThisFrame++;
    }

    /// <summary>
    /// Called when PerformanceBehaviour detects entry into gameplay scene.
    /// </summary>
    internal static void OnGameplayEntered()
    {
        _inGameplay = true;
        _diagnosticTimer = 0f;
        _fpsInitialized = false;
        _fixedUpdateCountThisFrame = 0;
        _fixedUpdateCountLastFrame = 0;
        _captureWarmupFrames = 0;
        CheckFrameTimingFeature();
    }

    /// <summary>
    /// Called when PerformanceBehaviour detects exit from gameplay scene.
    /// </summary>
    internal static void OnGameplayExited()
    {
        _inGameplay = false;
    }

    // ==================================================================
    //  FPS smoothing
    // ==================================================================

    private static void UpdateFpsSmoothing(float unscaledDeltaTime)
    {
        if (unscaledDeltaTime <= 0f)
            return;

        float instantFps = 1f / unscaledDeltaTime;
        float instantFrameTime = unscaledDeltaTime * 1000f; // ms

        if (!_fpsInitialized)
        {
            _smoothedFps = instantFps;
            _smoothedFrameTime = instantFrameTime;
            _fpsInitialized = true;
        }
        else
        {
            // Exponential moving average
            _smoothedFps += FpsSmoothFactor * (instantFps - _smoothedFps);
            _smoothedFrameTime += FpsSmoothFactor * (instantFrameTime - _smoothedFrameTime);
        }
    }

    // ==================================================================
    //  Main diagnostic log (runs once per interval)
    // ==================================================================

    private static void LogDiagnostics()
    {
        LogCoreStats();

        if (PerformancePlugin.CfgLogFrameTimings.Value)
            LogFrameTimings();

        if (_inGameplay)
        {
            LogShadowThresholdStats();

            if (PerformancePlugin.CfgLogObjectBreakdown.Value)
                LogObjectBreakdown();
        }
    }

    // ==================================================================
    //  1. Core frame stats
    // ==================================================================

    private static void LogCoreStats()
    {
        try
        {
            int rendererCount = 0;
            int visibleCount = 0;
            int shadowCasterCount = 0;

            try
            {
                var renderers = UnityEngine.Object.FindObjectsOfType<Renderer>();
                if (renderers != null)
                {
                    rendererCount = renderers.Length;
                    for (int i = 0; i < renderers.Length; i++)
                    {
                        try
                        {
                            var rend = renderers[i];
                            if (rend == null) continue;

                            if (rend.isVisible)
                                visibleCount++;

                            if (rend.shadowCastingMode != ShadowCastingMode.Off)
                                shadowCasterCount++;
                        }
                        catch { }
                    }
                }
            }
            catch { }

            int rigidbodyCount = 0;
            int awakeCount = 0;

            try
            {
                var rigidbodies = UnityEngine.Object.FindObjectsOfType<Rigidbody>();
                if (rigidbodies != null)
                {
                    rigidbodyCount = rigidbodies.Length;
                    for (int i = 0; i < rigidbodies.Length; i++)
                    {
                        try
                        {
                            var rb = rigidbodies[i];
                            if (rb == null) continue;

                            if (!rb.IsSleeping())
                                awakeCount++;
                        }
                        catch { }
                    }
                }
            }
            catch { }

            PerformancePlugin.Log.LogInfo(
                $"[Diag] FPS={_smoothedFps:F1} frameTime={_smoothedFrameTime:F1}ms" +
                $" | renderers={rendererCount} visible={visibleCount} shadowCasters={shadowCasterCount}" +
                $" | rigidbodies={rigidbodyCount} awake={awakeCount}" +
                $" | fixedPerFrame={_fixedUpdateCountLastFrame}");
        }
        catch (Exception ex)
        {
            PerformancePlugin.Log.LogWarning($"[Diag] Core stats error: {ex.Message}");
        }
    }

    // ==================================================================
    //  2. FrameTimingManager integration
    // ==================================================================

    private static void LogFrameTimings()
    {
        if (!_frameTimingFeatureAvailable)
        {
            PerformancePlugin.Log.LogInfo(
                "[Diag/Timing] FrameTimingManager not available (game built without Frame Timing Stats).");
            return;
        }

        if (_captureWarmupFrames < RequiredWarmupFrames)
        {
            PerformancePlugin.Log.LogInfo(
                $"[Diag/Timing] Warming up ({_captureWarmupFrames}/{RequiredWarmupFrames} frames)...");
            return;
        }

        try
        {
            uint numTimings = FrameTimingManager.GetLatestTimings(1, _frameTimings);
            if (numTimings == 0)
            {
                PerformancePlugin.Log.LogInfo("[Diag/Timing] No frame timings available.");
                return;
            }

            double cpuMs = _frameTimings[0].cpuFrameTime;
            double gpuMs = _frameTimings[0].gpuFrameTime;

            // Both zero after warmup means the feature is not actually supported
            if (cpuMs <= 0.0 && gpuMs <= 0.0)
            {
                PerformancePlugin.Log.LogInfo(
                    "[Diag/Timing] cpu=0.0ms gpu=0.0ms -> NOT SUPPORTED (game likely built without Frame Timing Stats in Player Settings).");
                _frameTimingFeatureAvailable = false; // stop trying
                return;
            }

            string bound;
            if (gpuMs <= 0.0)
                bound = "GPU-TIME-UNAVAILABLE";
            else if (gpuMs > cpuMs * 1.1)
                bound = "GPU-BOUND";
            else if (cpuMs > gpuMs * 1.1)
                bound = "CPU-BOUND";
            else
                bound = "BALANCED";

            PerformancePlugin.Log.LogInfo(
                $"[Diag/Timing] cpu={cpuMs:F1}ms gpu={gpuMs:F1}ms -> {bound}");
        }
        catch (Exception ex)
        {
            PerformancePlugin.Log.LogWarning($"[Diag/Timing] Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Check if FrameTimingManager is available on this platform/build.
    /// Only needs to run once.
    /// </summary>
    private static void CheckFrameTimingFeature()
    {
        if (_frameTimingFeatureChecked) return;
        _frameTimingFeatureChecked = true;

        try
        {
            // IsFeatureEnabled() was added in Unity 2022.2+
            // If it exists and returns false, the game was built without frame timing support.
            bool enabled = FrameTimingManager.IsFeatureEnabled();
            _frameTimingFeatureAvailable = enabled;
            if (!enabled)
            {
                PerformancePlugin.Log.LogInfo(
                    "[Diag/Timing] FrameTimingManager.IsFeatureEnabled()=false. " +
                    "Game was likely built without 'Enable Frame Timing Stats' in Player Settings.");
            }
        }
        catch
        {
            // If the method doesn't exist in this IL2CPP interop, assume available
            // and let the warmup+zero check handle it.
            _frameTimingFeatureAvailable = true;
        }
    }

    // Pre-allocated array for FrameTimingManager (avoids per-call allocation)
    private static readonly FrameTiming[] _frameTimings = new FrameTiming[1];

    // ==================================================================
    //  3. Object category breakdown
    // ==================================================================

    private static void LogObjectBreakdown()
    {
        try
        {
            var renderers = UnityEngine.Object.FindObjectsOfType<Renderer>();
            if (renderers == null || renderers.Length == 0)
            {
                PerformancePlugin.Log.LogInfo("[Diag/Objects] No renderers found.");
                return;
            }

            // Count objects grouped by normalized name prefix.
            // We cannot use Dictionary as a parameter/return on MonoBehaviour methods,
            // but we CAN use it inside a static method on a static class -- ClassInjector
            // never sees this code.
            var categories = new System.Collections.Generic.Dictionary<string, int>(256);

            for (int i = 0; i < renderers.Length; i++)
            {
                try
                {
                    var rend = renderers[i];
                    if (rend == null) continue;

                    string rawName = rend.gameObject.name;
                    if (string.IsNullOrEmpty(rawName))
                        rawName = "unnamed";

                    string normalized = NormalizeObjectName(rawName);

                    if (categories.ContainsKey(normalized))
                        categories[normalized]++;
                    else
                        categories[normalized] = 1;
                }
                catch { }
            }

            // Sort by count descending, take top 10
            var sorted = new System.Collections.Generic.List<(string name, int count)>(categories.Count);
            foreach (var kvp in categories)
                sorted.Add((kvp.Key, kvp.Value));

            sorted.Sort((a, b) => b.count.CompareTo(a.count));

            var sb = new StringBuilder("[Diag/Objects] Top objects: ");
            int top = Math.Min(10, sorted.Count);
            for (int i = 0; i < top; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(sorted[i].name);
                sb.Append('(');
                sb.Append(sorted[i].count);
                sb.Append(')');
            }

            PerformancePlugin.Log.LogInfo(sb.ToString());
        }
        catch (Exception ex)
        {
            PerformancePlugin.Log.LogWarning($"[Diag/Objects] Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Normalize a GameObject name for category grouping:
    ///   - Strip "(Clone)" suffix
    ///   - Strip trailing numeric suffixes and underscores
    ///   - Repeat stripping to handle patterns like "Rock_Small_03 (Clone)"
    /// </summary>
    private static string NormalizeObjectName(string name)
    {
        // Quick strip of common suffixes. Regex is compiled once.
        string prev;
        string current = name.Trim();
        do
        {
            prev = current;
            current = CloneAndSuffixRegex.Replace(current, "").TrimEnd('_', ' ');
        }
        while (current != prev && current.Length > 0);

        return current.Length > 0 ? current : name.Trim();
    }

    // ==================================================================
    //  5. Small shadow caster threshold stats
    // ==================================================================

    private static void LogShadowThresholdStats()
    {
        try
        {
            float threshold = PerformancePlugin.CfgSmallShadowCasterThreshold.Value;

            var renderers = UnityEngine.Object.FindObjectsOfType<Renderer>();
            if (renderers == null || renderers.Length == 0)
                return;

            int belowThreshold = 0;
            int aboveThreshold = 0;

            for (int i = 0; i < renderers.Length; i++)
            {
                try
                {
                    var rend = renderers[i];
                    if (rend == null) continue;

                    // Only count renderers that are relevant to the shadow optimization:
                    // skip those already Off or ShadowsOnly, and skip Player-tagged.
                    if (rend.shadowCastingMode == ShadowCastingMode.Off)
                        continue;
                    if (rend.shadowCastingMode == ShadowCastingMode.ShadowsOnly)
                        continue;

                    try
                    {
                        if (rend.gameObject.CompareTag("Player"))
                            continue;
                    }
                    catch { }

                    if (rend.bounds.size.magnitude < threshold)
                        belowThreshold++;
                    else
                        aboveThreshold++;
                }
                catch { }
            }

            PerformancePlugin.Log.LogInfo(
                $"[Diag/Shadows] belowThreshold={belowThreshold}" +
                $" aboveThreshold={aboveThreshold}" +
                $" (threshold={threshold:F1}m)");
        }
        catch (Exception ex)
        {
            PerformancePlugin.Log.LogWarning($"[Diag/Shadows] Error: {ex.Message}");
        }
    }
}
