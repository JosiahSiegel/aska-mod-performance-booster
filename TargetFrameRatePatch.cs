using System;
using System.Reflection;
using HarmonyLib;

namespace AskaPerformanceBooster;

/// <summary>
/// Harmony prefix on Application.targetFrameRate setter to prevent Aska from
/// re-capping the frame rate after our override.
///
/// Aska sets Application.targetFrameRate = 60 at startup and on settings
/// changes, which hard-caps all VSync-off users to 60 FPS regardless of GPU
/// headroom. When VSync is on (vSyncCount >= 1), Unity ignores targetFrameRate
/// entirely, so this patch is a no-op for VSync users.
///
/// Behavior:
///   - When CfgTargetFrameRate == 0 (sentinel = "don't override"):
///     Pass through all writes -- the mod is not managing frame rate.
///   - When CfgTargetFrameRate != 0 AND the incoming value differs from ours:
///     Suppress the game's write and re-enter with our value instead.
///   - When the incoming value already matches our config:
///     Pass through (no-op suppression).
///
/// Uses [HarmonyTargetMethod] with AccessTools.PropertySetter for IL2CPP
/// interop compatibility. Application.targetFrameRate is a static property
/// with a native setter.
///
/// IMPORTANT: The prefix uses a static reentrancy guard (_inSelfWrite) to
/// prevent infinite recursion. When we call Application.targetFrameRate = ourValue
/// from inside the prefix, the patched setter fires again. The guard lets our
/// own write pass through to the original setter.
/// </summary>
[HarmonyPatch]
internal static class TargetFrameRatePatch
{
    /// <summary>
    /// Reentrancy guard. Set to true while we are writing our own value
    /// from inside the prefix, so the recursive prefix call passes through.
    /// </summary>
    private static bool _inSelfWrite;

    /// <summary>
    /// Resolve the target method at patch time using string-based type lookup.
    /// Application.targetFrameRate is a static property; we patch its setter.
    /// </summary>
    [HarmonyTargetMethod]
    public static MethodBase TargetMethod()
    {
        Type appType = AccessTools.TypeByName("UnityEngine.Application");
        if (appType == null)
        {
            PerformancePlugin.Log?.LogWarning(
                "[TargetFrameRatePatch] Could not resolve UnityEngine.Application type.");
            return null;
        }

        MethodInfo setter = AccessTools.PropertySetter(appType, "targetFrameRate");

        if (setter == null)
        {
            PerformancePlugin.Log?.LogWarning(
                "[TargetFrameRatePatch] Could not resolve Application.targetFrameRate setter.");
        }

        return setter;
    }

    /// <summary>
    /// Prefix that intercepts writes to Application.targetFrameRate.
    /// Returns false to suppress the original setter when we are managing
    /// the frame rate and the incoming value differs from our config.
    /// </summary>
    [HarmonyPrefix]
    public static bool Prefix(int value)
    {
        try
        {
            // Reentrancy: our own write from below -- let it through
            if (_inSelfWrite)
                return true;

            int ourValue = PerformancePlugin.CfgTargetFrameRate.Value;

            // Sentinel: 0 = "don't override" -- let all writes through
            if (ourValue == 0)
                return true;

            // If the game is trying to set the same value we want, let it through
            if (value == ourValue)
                return true;

            // The game is trying to set a different value than ours -- suppress
            // and apply our value instead.
            _inSelfWrite = true;
            try
            {
                UnityEngine.Application.targetFrameRate = ourValue;
            }
            finally
            {
                _inSelfWrite = false;
            }

            if (PerformancePlugin.CfgDebugLogging.Value)
            {
                PerformancePlugin.Log?.LogInfo(
                    $"[TargetFrameRatePatch] Blocked game write ({value}), " +
                    $"applied our value ({ourValue}).");
            }

            return false; // suppress the original setter
        }
        catch (Exception ex)
        {
            _inSelfWrite = false;
            // On any error, let the original write through to avoid breaking things
            PerformancePlugin.Log?.LogWarning(
                $"[TargetFrameRatePatch] Error in prefix: {ex.Message} -- passing through.");
            return true;
        }
    }
}
