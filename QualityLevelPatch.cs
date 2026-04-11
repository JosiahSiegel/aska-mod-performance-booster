using System;
using System.Reflection;
using HarmonyLib;

namespace AskaPerformanceBooster;

/// <summary>
/// Harmony postfix on QualitySettings.SetQualityLevel to detect when Aska's
/// settings menu (or any code) changes the active quality level.
///
/// In HDRP, changing the quality level can swap the HDRP Asset (each quality
/// level can reference a different pipeline asset). This means:
///   1. Our cached HDRP Asset reflection becomes stale (wrong object).
///   2. Our pipeline support flag writes need re-application.
///
/// This patch sets a flag that PerformanceBehaviour reads on its next Update
/// to invalidate caches and trigger an immediate full reapplication. We do NOT
/// call ApplyAllSettings directly from the patch because Harmony postfixes
/// run on the caller's thread and the quality level change may not have fully
/// propagated yet (HDRP Asset swap, pipeline rebuild, etc.).
///
/// Uses string-based type resolution + TargetMethod for IL2CPP interop
/// compatibility. We target the two-parameter overload (int, bool) which is
/// what the single-parameter overload delegates to internally.
/// </summary>
[HarmonyPatch]
internal static class QualityLevelPatch
{
    /// <summary>
    /// Set to true by the postfix. PerformanceBehaviour reads and clears this
    /// flag each Update. Volatile because Harmony postfix and Update may run
    /// on the same thread but we want the write to be immediately visible.
    /// </summary>
    internal static volatile bool QualityLevelChanged;

    /// <summary>
    /// The quality level that was set, for logging.
    /// </summary>
    internal static int LastSetLevel;

    /// <summary>
    /// Resolve the target method at patch time using string-based type lookup.
    /// This is the IL2CPP-safe way to specify both the type and overload.
    /// </summary>
    [HarmonyTargetMethod]
    public static MethodBase TargetMethod()
    {
        // Resolve the IL2CPP-interop type by its original CLR name
        Type qualitySettingsType = AccessTools.TypeByName("UnityEngine.QualitySettings");
        if (qualitySettingsType == null)
        {
            PerformancePlugin.Log?.LogWarning(
                "[QualityLevelPatch] Could not resolve UnityEngine.QualitySettings type.");
            return null;
        }

        // Target the two-parameter overload: SetQualityLevel(int index, bool applyExpensiveChanges)
        // The single-parameter overload delegates to this one.
        // NOTE: IL2CPP may rename parameters vs the original C# source. The actual
        // interop signature uses "index" (not "qualityLevel" or "level").
        MethodInfo method = AccessTools.Method(qualitySettingsType, "SetQualityLevel",
            new[] { typeof(int), typeof(bool) });

        if (method == null)
        {
            // Fallback: try any SetQualityLevel overload
            method = AccessTools.Method(qualitySettingsType, "SetQualityLevel",
                new[] { typeof(int) });
        }

        if (method == null)
        {
            PerformancePlugin.Log?.LogWarning(
                "[QualityLevelPatch] Could not resolve SetQualityLevel method.");
        }

        return method;
    }

    /// <summary>
    /// Postfix that fires after SetQualityLevel completes.
    /// IMPORTANT: Parameter name must EXACTLY match the IL2CPP method signature.
    /// The IL2CPP interop assembly exposes the parameter as "index", not
    /// "qualityLevel" (which is the name in the original C# source). HarmonyX
    /// injects postfix parameters by name, so a mismatch causes:
    ///   "Parameter 'qualityLevel' not found in method SetQualityLevel(int index, ...)"
    /// </summary>
    [HarmonyPostfix]
    public static void Postfix(int index)
    {
        try
        {
            QualityLevelChanged = true;
            LastSetLevel = index;

            PerformancePlugin.Log.LogInfo(
                $"[QualityLevelPatch] Game changed quality level to {index}. " +
                "Will invalidate caches and reapply optimizations on next frame.");
        }
        catch { }
    }
}
