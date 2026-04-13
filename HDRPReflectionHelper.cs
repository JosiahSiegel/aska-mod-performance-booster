using System;
using System.Collections.Generic;
using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;
using UnityEngine.Rendering;

namespace AskaPerformanceBooster;

/// <summary>
/// Static helper for all HDRP reflection operations.
///
/// CRITICAL: These methods MUST NOT live on the MonoBehaviour class.
/// IL2CPP's ClassInjector processes all instance methods on MonoBehaviour
/// subclasses and chokes on System.Object / System.Type parameters.
/// Static methods on non-MonoBehaviour classes are invisible to ClassInjector.
///
/// This class handles:
///   - HDRP Asset reflection (finding properties on RenderPipelineSettings)
///   - Pipeline support flag writes (the PRIMARY optimization mechanism)
///   - HDRP Asset staleness detection (quality level changes)
/// </summary>
internal static class HDRPReflectionHelper
{
    // ------------------------------------------------------------------
    //  HDRP Asset reflection cache
    // ------------------------------------------------------------------
    internal static bool HdrpReflectionCached;
    internal static object HdrpAssetRef;
    internal static Type HdrpAssetType;

    internal static object RenderPipelineSettingsRef;
    internal static Type RenderPipelineSettingsType;

    internal static PropertyInfo PropCurrentPlatformRenderPipelineSettings;

    // Pipeline support flag properties (the PRIMARY optimization)
    internal static PropertyInfo PropSupportSSR;
    internal static PropertyInfo PropSupportSSAO;
    internal static PropertyInfo PropSupportVolumetrics;
    internal static PropertyInfo PropSupportVolumetricClouds;
    internal static PropertyInfo PropSupportSubsurfaceScattering;
    internal static PropertyInfo PropSupportDecals;
    internal static PropertyInfo PropSupportDistortion;
    internal static PropertyInfo PropSupportSSRTransparent;
    internal static PropertyInfo PropSupportDataDrivenLensFlare;
    internal static PropertyInfo PropSupportScreenSpaceLensFlare;

    // ------------------------------------------------------------------
    //  HDRP Shadow Init Parameters (hdShadowInitParams on RenderPipelineSettings)
    // ------------------------------------------------------------------
    internal static object HdShadowInitParamsRef;
    internal static Type HdShadowInitParamsType;
    internal static PropertyInfo PropHdShadowInitParams;

    // Writable shadow properties on HDShadowInitParameters
    internal static PropertyInfo PropMaxShadowRequests;
    internal static PropertyInfo PropMaxDirectionalShadowMapResolution;
    internal static PropertyInfo PropMaxPunctualShadowMapResolution;
    internal static PropertyInfo PropMaxAreaShadowMapResolution;
    internal static PropertyInfo PropAreaShadowFilteringQuality;

    // Track which HDRP Asset the reflection cache was built for.
    private static int _cachedHdrpAssetInstanceId = -1;

    // ------------------------------------------------------------------
    //  Debug logging helper
    // ------------------------------------------------------------------
    private static bool Debug => PerformancePlugin.CfgDebugLogging.Value;

    private static void DebugLog(string msg)
    {
        if (Debug)
            PerformancePlugin.Log.LogInfo($"[Debug] {msg}");
    }

    // ==================================================================
    //  Cache HDRP reflection
    // ==================================================================

    internal static void CacheHDRPReflection()
    {
        if (HdrpReflectionCached) return;
        HdrpReflectionCached = true;

        try
        {
            var pipelineAsset = GraphicsSettings.currentRenderPipeline;
            if (pipelineAsset == null)
            {
                PerformancePlugin.Log.LogWarning(
                    "GraphicsSettings.currentRenderPipeline is null -- HDRP settings unavailable.");
                return;
            }

            HdrpAssetRef = pipelineAsset;

            // IL2CPP interop: resolve the concrete HDRenderPipelineAsset type
            Type t = pipelineAsset.GetType();
            Type concreteType = ResolveIl2CppConcreteType(pipelineAsset);
            if (concreteType != null && concreteType != t)
            {
                PerformancePlugin.Log.LogInfo(
                    $"IL2CPP type resolution: GetType()={t.Name}, " +
                    $"concrete IL2CPP type={concreteType.Name}. Using concrete type.");
                t = concreteType;

                var castAsset = RuntimeCastToConcreteType(pipelineAsset, concreteType);
                if (castAsset != null && castAsset != (object)pipelineAsset)
                {
                    HdrpAssetRef = castAsset;
                    PerformancePlugin.Log.LogInfo(
                        $"HDRP Asset re-wrapped: managed type now {castAsset.GetType().Name}");
                }
            }

            HdrpAssetType = t;

            try
            {
                _cachedHdrpAssetInstanceId = pipelineAsset.GetInstanceID();
                PerformancePlugin.Log.LogInfo(
                    $"HDRP Asset cached: '{pipelineAsset.name}' (instanceID={_cachedHdrpAssetInstanceId})");
            }
            catch
            {
                _cachedHdrpAssetInstanceId = -1;
            }

            // Get currentPlatformRenderPipelineSettings
            PropCurrentPlatformRenderPipelineSettings = FindProp(t, "currentPlatformRenderPipelineSettings");
            if (PropCurrentPlatformRenderPipelineSettings != null)
            {
                try
                {
                    RenderPipelineSettingsRef = PropCurrentPlatformRenderPipelineSettings.GetValue(HdrpAssetRef);
                    if (RenderPipelineSettingsRef != null)
                    {
                        RenderPipelineSettingsType = RenderPipelineSettingsRef.GetType();
                        PerformancePlugin.Log.LogInfo(
                            $"RenderPipelineSettings resolved: type={RenderPipelineSettingsType.FullName}");
                        CacheSupportFlagProperties();
                        CacheShadowInitProperties();
                    }
                    else
                    {
                        PerformancePlugin.Log.LogWarning(
                            "currentPlatformRenderPipelineSettings returned null.");
                    }
                }
                catch (Exception ex)
                {
                    PerformancePlugin.Log.LogWarning(
                        $"Failed to read currentPlatformRenderPipelineSettings: {ex.Message}");
                }
            }
            else
            {
                PerformancePlugin.Log.LogWarning(
                    "currentPlatformRenderPipelineSettings property not found on " + t.FullName);
                TryFindSettingsViaInternalField(t);
            }

            LogReflectionSummary();
        }
        catch (Exception ex)
        {
            PerformancePlugin.Log.LogWarning($"Failed to cache HDRP reflection: {ex.Message}");
        }
    }

    private static void CacheSupportFlagProperties()
    {
        if (RenderPipelineSettingsType == null || RenderPipelineSettingsRef == null) return;

        Type st = RenderPipelineSettingsType;

        PropSupportSSR = FindProp(st, "supportSSR");
        PropSupportSSAO = FindProp(st, "supportSSAO");
        PropSupportVolumetrics = FindProp(st, "supportVolumetrics");
        PropSupportVolumetricClouds = FindProp(st, "supportVolumetricClouds");
        PropSupportSubsurfaceScattering = FindProp(st, "supportSubsurfaceScattering");
        PropSupportDecals = FindProp(st, "supportDecals");
        PropSupportDistortion = FindProp(st, "supportDistortion");
        PropSupportSSRTransparent = FindProp(st, "supportSSRTransparent");
        PropSupportDataDrivenLensFlare = FindProp(st, "supportDataDrivenLensFlare");
        PropSupportScreenSpaceLensFlare = FindProp(st, "supportScreenSpaceLensFlare");
    }

    /// <summary>
    /// Resolve the hdShadowInitParams nested struct on RenderPipelineSettings
    /// and cache PropertyInfo handles for writable shadow properties.
    /// </summary>
    private static void CacheShadowInitProperties()
    {
        if (RenderPipelineSettingsType == null || RenderPipelineSettingsRef == null) return;

        try
        {
            // Locate hdShadowInitParams property on RenderPipelineSettings
            PropHdShadowInitParams = FindProp(RenderPipelineSettingsType, "hdShadowInitParams");
            if (PropHdShadowInitParams == null)
            {
                DebugLog("hdShadowInitParams property not found on RenderPipelineSettings.");
                return;
            }

            HdShadowInitParamsRef = PropHdShadowInitParams.GetValue(RenderPipelineSettingsRef);
            if (HdShadowInitParamsRef == null)
            {
                DebugLog("hdShadowInitParams returned null.");
                return;
            }

            HdShadowInitParamsType = HdShadowInitParamsRef.GetType();

            // Cache individual shadow properties
            PropMaxShadowRequests = FindProp(HdShadowInitParamsType, "maxShadowRequests");
            PropMaxDirectionalShadowMapResolution = FindProp(HdShadowInitParamsType, "maxDirectionalShadowMapResolution");
            PropMaxPunctualShadowMapResolution = FindProp(HdShadowInitParamsType, "maxPunctualShadowMapResolution");
            PropMaxAreaShadowMapResolution = FindProp(HdShadowInitParamsType, "maxAreaShadowMapResolution");
            PropAreaShadowFilteringQuality = FindProp(HdShadowInitParamsType, "areaShadowFilteringQuality");

            // Log discovery results
            string maxReqVal = PropMaxShadowRequests != null ? SafeGetInt(PropMaxShadowRequests, HdShadowInitParamsRef).ToString() : "N/A";
            string maxDirVal = PropMaxDirectionalShadowMapResolution != null ? SafeGetInt(PropMaxDirectionalShadowMapResolution, HdShadowInitParamsRef).ToString() : "N/A";
            string maxPuncVal = PropMaxPunctualShadowMapResolution != null ? SafeGetInt(PropMaxPunctualShadowMapResolution, HdShadowInitParamsRef).ToString() : "N/A";
            string maxAreaVal = PropMaxAreaShadowMapResolution != null ? SafeGetInt(PropMaxAreaShadowMapResolution, HdShadowInitParamsRef).ToString() : "N/A";
            string areaFilterVal = PropAreaShadowFilteringQuality != null ? SafeGetEnum(PropAreaShadowFilteringQuality, HdShadowInitParamsRef) : "N/A";

            PerformancePlugin.Log.LogInfo(
                $"HDShadowInitParams cached: maxShadowRequests={maxReqVal}, " +
                $"maxDirShadowRes={maxDirVal}, maxPuncShadowRes={maxPuncVal}, " +
                $"maxAreaShadowRes={maxAreaVal}, areaFilterQuality={areaFilterVal}");
        }
        catch (Exception ex)
        {
            PerformancePlugin.Log.LogWarning($"Failed to cache shadow init properties: {ex.Message}");
        }
    }

    private static void TryFindSettingsViaInternalField(Type assetType)
    {
        try
        {
            FieldInfo settingsField = null;
            Type walkType = assetType;
            while (walkType != null && walkType != typeof(object))
            {
                settingsField = walkType.GetField("m_RenderPipelineSettings",
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (settingsField != null) break;
                walkType = walkType.BaseType;
            }

            if (settingsField != null && HdrpAssetRef != null)
            {
                RenderPipelineSettingsRef = settingsField.GetValue(HdrpAssetRef);
                if (RenderPipelineSettingsRef != null)
                {
                    RenderPipelineSettingsType = RenderPipelineSettingsRef.GetType();
                    PerformancePlugin.Log.LogInfo(
                        $"RenderPipelineSettings resolved via m_RenderPipelineSettings field: " +
                        $"type={RenderPipelineSettingsType.FullName}");
                    CacheSupportFlagProperties();
                    CacheShadowInitProperties();
                }
            }
            else
            {
                PerformancePlugin.Log.LogWarning(
                    "Neither currentPlatformRenderPipelineSettings property nor " +
                    "m_RenderPipelineSettings field found on HDRP Asset.");
            }
        }
        catch (Exception ex)
        {
            DebugLog($"Fallback settings field search failed: {ex.Message}");
        }
    }

    private static void LogReflectionSummary()
    {
        string assetName = "unknown";
        try { assetName = ((UnityEngine.Object)HdrpAssetRef)?.name ?? "unknown"; }
        catch { }

        string ssaoVal = PropSupportSSAO != null ? SafeGetBool(PropSupportSSAO, RenderPipelineSettingsRef).ToString() : "N/A";
        string volVal = PropSupportVolumetrics != null ? SafeGetBool(PropSupportVolumetrics, RenderPipelineSettingsRef).ToString() : "N/A";
        string cloudsVal = PropSupportVolumetricClouds != null ? SafeGetBool(PropSupportVolumetricClouds, RenderPipelineSettingsRef).ToString() : "N/A";
        string sssVal = PropSupportSubsurfaceScattering != null ? SafeGetBool(PropSupportSubsurfaceScattering, RenderPipelineSettingsRef).ToString() : "N/A";
        string decalsVal = PropSupportDecals != null ? SafeGetBool(PropSupportDecals, RenderPipelineSettingsRef).ToString() : "N/A";

        PerformancePlugin.Log.LogInfo(
            $"HDRP Asset: {assetName} ({HdrpAssetType?.Name}). " +
            $"RenderPipelineSettings: {(RenderPipelineSettingsRef != null ? "resolved" : "NOT FOUND")}. " +
            $"Key flags: supportSSAO={ssaoVal}, " +
            $"supportVolumetrics={volVal}, " +
            $"supportVolumetricClouds={cloudsVal}, " +
            $"supportSubsurfaceScattering={sssVal}, " +
            $"supportDecals={decalsVal}");
    }

    // ==================================================================
    //  Pipeline support flag writes (PRIMARY optimization mechanism)
    // ==================================================================

    internal static int WriteSupportFlagBatch(
        List<(PropertyInfo prop, string name, bool value)> flags)
    {
        if (RenderPipelineSettingsRef == null) return 0;

        int changed = 0;
        foreach (var (prop, name, value) in flags)
        {
            if (prop == null) continue;
            try
            {
                bool current = SafeGetBool(prop, RenderPipelineSettingsRef);
                if (current == value) continue;

                prop.SetValue(RenderPipelineSettingsRef, value);
                changed++;

                PerformancePlugin.Log.LogInfo(
                    $"HDRP Asset support flag: {name} = {current} -> {value}");
            }
            catch (Exception ex)
            {
                PerformancePlugin.Log.LogWarning(
                    $"Could not write support flag {name}: {ex.Message}");
            }
        }

        if (changed > 0)
        {
            WriteSettingsBackToAsset();
            PerformancePlugin.Log.LogInfo(
                $"HDRP Asset support flags: {changed} flag(s) changed, wrote settings back to asset.");
        }

        return changed;
    }

    /// <summary>
    /// Convenience method callable from the MonoBehaviour.
    /// Each bool parameter means "the user wants to DISABLE this feature".
    /// </summary>
    internal static void ApplyPipelineSupportFlagBatch(
        bool disableSSR,
        bool disableSSAO,
        bool disableVolumetrics,
        bool disableVolumetricClouds,
        bool disableSubsurfaceScattering,
        bool disableDecals,
        bool disableDistortion,
        bool disableSSRTransparent,
        bool disableScreenSpaceLensFlare,
        bool disableDataDrivenLensFlare)
    {
        if (RenderPipelineSettingsRef == null) return;

        if (!disableSSR && !disableSSAO && !disableVolumetrics &&
            !disableVolumetricClouds && !disableSubsurfaceScattering &&
            !disableDecals && !disableDistortion && !disableSSRTransparent &&
            !disableScreenSpaceLensFlare && !disableDataDrivenLensFlare)
        {
            DebugLog("Pipeline support flags: no flags requested for disable, skipping.");
            return;
        }

        var flags = new List<(PropertyInfo prop, string name, bool value)>();

        if (disableSSR) flags.Add((PropSupportSSR, "supportSSR", false));
        if (disableSSAO) flags.Add((PropSupportSSAO, "supportSSAO", false));
        if (disableVolumetrics) flags.Add((PropSupportVolumetrics, "supportVolumetrics", false));
        if (disableVolumetricClouds) flags.Add((PropSupportVolumetricClouds, "supportVolumetricClouds", false));
        if (disableSubsurfaceScattering) flags.Add((PropSupportSubsurfaceScattering, "supportSubsurfaceScattering", false));
        if (disableDecals) flags.Add((PropSupportDecals, "supportDecals", false));
        if (disableDistortion) flags.Add((PropSupportDistortion, "supportDistortion", false));
        if (disableSSRTransparent) flags.Add((PropSupportSSRTransparent, "supportSSRTransparent", false));
        if (disableScreenSpaceLensFlare) flags.Add((PropSupportScreenSpaceLensFlare, "supportScreenSpaceLensFlare", false));
        if (disableDataDrivenLensFlare) flags.Add((PropSupportDataDrivenLensFlare, "supportDataDrivenLensFlare", false));

        int changed = WriteSupportFlagBatch(flags);

        if (changed == 0)
        {
            DebugLog("Pipeline support flags: all requested flags already at target values.");
        }
    }

    /// <summary>
    /// Write the modified RenderPipelineSettings back to the HDRP Asset.
    /// Safety net: the NativeFieldInfoPtr setters likely already write through.
    /// </summary>
    internal static void WriteSettingsBackToAsset()
    {
        if (HdrpAssetRef == null || RenderPipelineSettingsRef == null) return;

        try
        {
            if (PropCurrentPlatformRenderPipelineSettings != null &&
                PropCurrentPlatformRenderPipelineSettings.GetSetMethod(true) != null)
            {
                PropCurrentPlatformRenderPipelineSettings.SetValue(HdrpAssetRef, RenderPipelineSettingsRef);
                return;
            }

            if (HdrpAssetType != null)
            {
                Type walkType = HdrpAssetType;
                while (walkType != null && walkType != typeof(object))
                {
                    var field = walkType.GetField("m_RenderPipelineSettings",
                        BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                    if (field != null)
                    {
                        field.SetValue(HdrpAssetRef, RenderPipelineSettingsRef);
                        return;
                    }
                    walkType = walkType.BaseType;
                }
            }
        }
        catch (Exception ex)
        {
            DebugLog($"Could not write settings back to HDRP Asset: {ex.Message}");
        }
    }

    // ==================================================================
    //  Shadow init parameter writes
    // ==================================================================

    /// <summary>
    /// Apply shadow init parameter overrides to the HDRP Asset.
    /// Each parameter uses a sentinel value to indicate "don't override":
    ///   int  0 = don't override
    ///   int -1 = don't override (for areaShadowFilteringQuality)
    /// </summary>
    internal static void ApplyShadowInitParams(
        int maxShadowRequests,
        int maxDirectionalShadowMapResolution,
        int maxAreaShadowMapResolution,
        int areaShadowFilteringQuality)
    {
        if (HdShadowInitParamsRef == null || HdShadowInitParamsType == null)
        {
            DebugLog("Shadow init params: not cached, skipping.");
            return;
        }

        bool anyChanged = false;

        if (maxShadowRequests > 0)
            anyChanged |= WriteShadowInt(PropMaxShadowRequests, "maxShadowRequests", maxShadowRequests);

        if (maxDirectionalShadowMapResolution > 0)
            anyChanged |= WriteShadowInt(PropMaxDirectionalShadowMapResolution, "maxDirectionalShadowMapResolution", maxDirectionalShadowMapResolution);

        if (maxAreaShadowMapResolution > 0)
            anyChanged |= WriteShadowInt(PropMaxAreaShadowMapResolution, "maxAreaShadowMapResolution", maxAreaShadowMapResolution);

        if (areaShadowFilteringQuality >= 0)
            anyChanged |= WriteShadowEnum(PropAreaShadowFilteringQuality, "areaShadowFilteringQuality", areaShadowFilteringQuality);

        if (anyChanged)
        {
            WriteShadowInitParamsBack();
            WriteSettingsBackToAsset();
            PerformancePlugin.Log.LogInfo("Shadow init params: wrote changes back to HDRP Asset.");
        }
        else
        {
            DebugLog("Shadow init params: all values already at target.");
        }
    }

    /// <summary>
    /// Write an int property on HdShadowInitParamsRef. Returns true if the value changed.
    /// Uses Math.Min to ensure we never INCREASE the value beyond what the game already
    /// has -- if the user's quality preset already uses a lower (cheaper) value, we
    /// keep it rather than overriding with our higher target.
    /// </summary>
    private static bool WriteShadowInt(PropertyInfo prop, string name, int targetValue)
    {
        if (prop == null)
        {
            DebugLog($"Shadow param {name}: property not found, skipping.");
            return false;
        }

        try
        {
            int current = SafeGetInt(prop, HdShadowInitParamsRef);

            // Never increase: use the lesser of current and target.
            // If the game's quality preset already has a lower value (e.g. Low preset
            // with maxShadowRequests=32), writing our target of 48 would INCREASE cost.
            int effective = Math.Min(current, targetValue);

            if (current == effective)
            {
                DebugLog($"Shadow param {name}: already {current} (<= target {targetValue}), no change.");
                return false;
            }

            prop.SetValue(HdShadowInitParamsRef, effective);
            PerformancePlugin.Log.LogInfo($"Shadow param: {name} = {current} -> {effective} (target was {targetValue})");
            return true;
        }
        catch (Exception ex)
        {
            PerformancePlugin.Log.LogWarning($"Could not write shadow param {name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Write an enum property on HdShadowInitParamsRef using its int value.
    /// HDRP shadow filtering quality enums map to int values:
    ///   HDAreaShadowFilteringQuality: Low=0, Medium=1, High=2
    /// Returns true if the value changed.
    ///
    /// Uses Math.Min on the underlying int values to ensure we never INCREASE
    /// quality beyond what the game already has. For quality enums where
    /// lower int = lower quality (Low=0, Medium=1, High=2), Min picks the
    /// cheaper option.
    ///
    /// NOTE: IL2CPP enum wrappers may not convert correctly via Convert.ToInt32().
    /// We use ToString() comparison as a fallback to detect the current value,
    /// and always write if the string representation doesn't match the target.
    /// </summary>
    private static bool WriteShadowEnum(PropertyInfo prop, string name, int targetValue)
    {
        if (prop == null)
        {
            DebugLog($"Shadow param {name}: property not found, skipping.");
            return false;
        }

        try
        {
            object currentObj = prop.GetValue(HdShadowInitParamsRef);
            string currentStr = currentObj?.ToString() ?? "null";

            // Try to get the current int value for Min comparison.
            // For quality enums (Low=0, Medium=1, High=2), lower = cheaper.
            int currentInt = targetValue; // fallback: no Min applied
            try
            {
                currentInt = Convert.ToInt32(currentObj);
            }
            catch
            {
                // IL2CPP enum wrapper didn't convert -- fall through to string compare
            }

            // Never increase: use the lesser of current and target.
            int effectiveInt = Math.Min(currentInt, targetValue);

            object effectiveEnum = Enum.ToObject(prop.PropertyType, effectiveInt);
            string effectiveStr = effectiveEnum?.ToString() ?? "null";

            if (string.Equals(currentStr, effectiveStr, StringComparison.OrdinalIgnoreCase))
            {
                DebugLog($"Shadow param {name}: already {currentStr} (<= target {targetValue}), no change.");
                return false;
            }

            prop.SetValue(HdShadowInitParamsRef, effectiveEnum);
            PerformancePlugin.Log.LogInfo($"Shadow param: {name} = {currentStr} -> {effectiveStr} (target was {targetValue})");
            return true;
        }
        catch (Exception ex)
        {
            PerformancePlugin.Log.LogWarning($"Could not write shadow param {name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Write the modified HDShadowInitParameters back to RenderPipelineSettings.
    /// The NativeFieldInfoPtr setters on IL2CPP structs may already write through,
    /// but this is a safety net (same pattern as WriteSettingsBackToAsset).
    /// </summary>
    private static void WriteShadowInitParamsBack()
    {
        if (PropHdShadowInitParams == null || RenderPipelineSettingsRef == null ||
            HdShadowInitParamsRef == null)
            return;

        try
        {
            if (PropHdShadowInitParams.GetSetMethod(true) != null)
            {
                PropHdShadowInitParams.SetValue(RenderPipelineSettingsRef, HdShadowInitParamsRef);
                DebugLog("Wrote HDShadowInitParams back via property setter.");
                return;
            }

            // Fallback: try field write
            if (RenderPipelineSettingsType != null)
            {
                var field = RenderPipelineSettingsType.GetField("hdShadowInitParams",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    field.SetValue(RenderPipelineSettingsRef, HdShadowInitParamsRef);
                    DebugLog("Wrote HDShadowInitParams back via field.");
                }
            }
        }
        catch (Exception ex)
        {
            DebugLog($"Could not write HDShadowInitParams back: {ex.Message}");
        }
    }

    // ==================================================================
    //  HDRP Asset staleness detection
    // ==================================================================

    internal static bool CheckHdrpAssetStale()
    {
        if (!HdrpReflectionCached || _cachedHdrpAssetInstanceId == -1)
            return false;

        try
        {
            var currentAsset = GraphicsSettings.currentRenderPipeline;
            if (currentAsset == null) return false;

            int currentId = currentAsset.GetInstanceID();

            if (currentId == _cachedHdrpAssetInstanceId)
            {
                DebugLog($"[AssetStaleCheck] Cache valid (instanceID={currentId}).");
                return false;
            }

            string currentName = "unknown";
            try { currentName = currentAsset.name; } catch { }

            PerformancePlugin.Log.LogInfo(
                $"[AssetStaleCheck] HDRP Asset CHANGED! " +
                $"Cached instanceID={_cachedHdrpAssetInstanceId}, " +
                $"Current='{currentName}' (instanceID={currentId}). " +
                "Invalidating cache.");

            InvalidateCache();
            return true;
        }
        catch (Exception ex)
        {
            DebugLog($"CheckHdrpAssetStale failed: {ex.Message}");
            return false;
        }
    }

    // ==================================================================
    //  Invalidate / clear
    // ==================================================================

    internal static void InvalidateCache()
    {
        HdrpReflectionCached = false;
        HdrpAssetRef = null;
        HdrpAssetType = null;
        _cachedHdrpAssetInstanceId = -1;
        RenderPipelineSettingsRef = null;
        RenderPipelineSettingsType = null;
        PropCurrentPlatformRenderPipelineSettings = null;

        PropSupportSSR = null;
        PropSupportSSAO = null;
        PropSupportVolumetrics = null;
        PropSupportVolumetricClouds = null;
        PropSupportSubsurfaceScattering = null;
        PropSupportDecals = null;
        PropSupportDistortion = null;
        PropSupportSSRTransparent = null;
        PropSupportDataDrivenLensFlare = null;
        PropSupportScreenSpaceLensFlare = null;

        HdShadowInitParamsRef = null;
        HdShadowInitParamsType = null;
        PropHdShadowInitParams = null;
        PropMaxShadowRequests = null;
        PropMaxDirectionalShadowMapResolution = null;
        PropMaxPunctualShadowMapResolution = null;
        PropMaxAreaShadowMapResolution = null;
        PropAreaShadowFilteringQuality = null;

        DebugLog("HDRP reflection cache invalidated.");
    }

    // ==================================================================
    //  Shadow property discovery (debug-only)
    // ==================================================================

    /// <summary>
    /// Enumerates all shadow-related properties on RenderPipelineSettings
    /// and any nested shadow init parameter structs. Run once with
    /// DebugLogging=true to discover exact IL2CPP property names for
    /// shadow distance, cascade count, atlas resolution, etc.
    /// </summary>
    internal static void DumpShadowProperties()
    {
        if (RenderPipelineSettingsRef == null || RenderPipelineSettingsType == null)
        {
            PerformancePlugin.Log.LogInfo("[ShadowDiscover] RenderPipelineSettings not available.");
            return;
        }

        PerformancePlugin.Log.LogInfo("[ShadowDiscover] === RenderPipelineSettings shadow-related properties ===");

        try
        {
            foreach (var prop in RenderPipelineSettingsType.GetProperties(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (prop.Name.IndexOf("shadow", StringComparison.OrdinalIgnoreCase) < 0 &&
                    prop.Name.IndexOf("Shadow", StringComparison.Ordinal) < 0)
                    continue;

                string val = "?";
                try { val = prop.GetValue(RenderPipelineSettingsRef)?.ToString() ?? "null"; }
                catch (Exception ex) { val = $"<error: {ex.Message}>"; }

                PerformancePlugin.Log.LogInfo(
                    $"[ShadowDiscover] {RenderPipelineSettingsType.Name}.{prop.Name} " +
                    $"({prop.PropertyType.Name}) = {val}");

                // If the property returns a struct/object, enumerate its shadow-related props too
                if (!prop.PropertyType.IsPrimitive && prop.PropertyType != typeof(string) &&
                    prop.PropertyType != typeof(bool))
                {
                    try
                    {
                        object nested = prop.GetValue(RenderPipelineSettingsRef);
                        if (nested != null)
                            DumpNestedShadowProps(nested, prop.Name);
                    }
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            PerformancePlugin.Log.LogWarning($"[ShadowDiscover] Error enumerating properties: {ex.Message}");
        }

        PerformancePlugin.Log.LogInfo("[ShadowDiscover] === End shadow property dump ===");
    }

    private static void DumpNestedShadowProps(object obj, string parentName)
    {
        if (obj == null) return;
        Type t = obj.GetType();

        try
        {
            foreach (var prop in t.GetProperties(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                string val = "?";
                try { val = prop.GetValue(obj)?.ToString() ?? "null"; }
                catch (Exception ex) { val = $"<error: {ex.Message}>"; }

                PerformancePlugin.Log.LogInfo(
                    $"[ShadowDiscover]   {parentName}.{prop.Name} " +
                    $"({prop.PropertyType.Name}) = {val}");
            }
        }
        catch { }
    }

    // ==================================================================
    //  Reflection helpers (all static, safe for IL2CPP)
    // ==================================================================

    internal static PropertyInfo FindProp(Type type, string name)
    {
        if (type == null) return null;
        try
        {
            Type current = type;
            while (current != null && current != typeof(object))
            {
                var prop = current.GetProperty(name,
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (prop != null) return prop;
                current = current.BaseType;
            }

            var fallback = type.GetProperty(name,
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            return fallback;
        }
        catch { }
        return null;
    }

    internal static Type ResolveIl2CppConcreteType(Il2CppSystem.Object il2cppObj)
    {
        try
        {
            var il2cppType = il2cppObj.GetIl2CppType();
            string fullName = il2cppType.FullName;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var managedType = asm.GetType(fullName);
                    if (managedType != null)
                        return managedType;
                }
                catch { }
            }

            if (fullName.Contains('.'))
            {
                string shortName = fullName.Substring(fullName.LastIndexOf('.') + 1);
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        foreach (var t in asm.GetTypes())
                        {
                            if (t.Name == shortName && t.IsClass)
                                return t;
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }

        return null;
    }

    internal static object RuntimeCastToConcreteType(Il2CppObjectBase il2cppObj, Type concreteType)
    {
        if (il2cppObj == null || concreteType == null) return il2cppObj;
        if (concreteType.IsInstanceOfType(il2cppObj)) return il2cppObj;

        try
        {
            IntPtr pointer = il2cppObj.Pointer;
            if (pointer == IntPtr.Zero) return il2cppObj;

            var intPtrCtor = concreteType.GetConstructor(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, new[] { typeof(IntPtr) }, null);

            if (intPtrCtor == null) return il2cppObj;

            object castObj = intPtrCtor.Invoke(new object[] { pointer });
            DebugLog($"RuntimeCast: {il2cppObj.GetType().Name} -> {concreteType.Name}");
            return castObj;
        }
        catch (Exception ex)
        {
            PerformancePlugin.Log.LogWarning(
                $"RuntimeCast failed ({il2cppObj.GetType().Name} -> {concreteType.Name}): {ex.Message}");
            return il2cppObj;
        }
    }

    internal static bool SafeGetBool(PropertyInfo prop, object target)
    {
        if (prop == null) return false;
        try
        {
            var val = prop.GetValue(target);
            if (val is bool b) return b;
            return Convert.ToBoolean(val);
        }
        catch { return false; }
    }

    internal static int SafeGetInt(PropertyInfo prop, object target)
    {
        if (prop == null) return 0;
        try
        {
            var val = prop.GetValue(target);
            if (val is int i) return i;
            return Convert.ToInt32(val);
        }
        catch { return 0; }
    }

    internal static string SafeGetEnum(PropertyInfo prop, object target)
    {
        if (prop == null) return "N/A";
        try
        {
            var val = prop.GetValue(target);
            return val?.ToString() ?? "null";
        }
        catch { return "error"; }
    }
}
