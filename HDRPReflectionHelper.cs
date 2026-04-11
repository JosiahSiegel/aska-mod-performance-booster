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

        DebugLog("HDRP reflection cache invalidated.");
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
}
