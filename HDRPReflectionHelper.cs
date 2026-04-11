using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Rendering;

namespace AskaPerformanceBooster;

/// <summary>
/// Static helper for all HDRP reflection operations that use System.Object,
/// System.Type, System.Reflection.* parameter/return types.
///
/// CRITICAL: These methods MUST NOT live on the MonoBehaviour class.
/// IL2CPP's ClassInjector processes all instance methods on MonoBehaviour
/// subclasses and chokes on System.Object / System.Type parameters and
/// return types. Static methods and methods on non-MonoBehaviour classes
/// are invisible to ClassInjector.
///
/// This class handles:
///   - HDRP Asset reflection (finding properties on RenderPipelineSettings)
///   - Pipeline support flag writes (the PRIMARY optimization mechanism)
///   - Diagnostic property/field enumeration
/// </summary>
internal static class HDRPReflectionHelper
{
    // ------------------------------------------------------------------
    //  HDRP Asset reflection cache
    // ------------------------------------------------------------------
    internal static bool HdrpReflectionCached;
    internal static object HdrpAssetRef;
    internal static Type HdrpAssetType;

    // The RenderPipelineSettings struct, accessed via currentPlatformRenderPipelineSettings
    internal static object RenderPipelineSettingsRef;
    internal static Type RenderPipelineSettingsType;

    // Properties on HDRenderPipelineAsset
    internal static PropertyInfo PropCurrentPlatformRenderPipelineSettings;
    internal static PropertyInfo PropGpuResidentDrawerMode;

    // Properties on RenderPipelineSettings (managed wrapper exposes them as
    // PROPERTIES, not fields -- the managed wrapper is IsClass=True despite
    // wrapping a native struct, and all members are accessed via property
    // getters/setters that route through NativeFieldInfoPtr_* IntPtrs).
    internal static PropertyInfo PropSupportSSR;
    internal static PropertyInfo PropSupportSSAO;
    internal static PropertyInfo PropSupportVolumetrics;
    internal static PropertyInfo PropSupportVolumetricClouds;
    internal static PropertyInfo PropSupportSubsurfaceScattering;
    internal static PropertyInfo PropSupportMotionVectors;
    internal static PropertyInfo PropSupportDecals;
    internal static PropertyInfo PropSupportDistortion;
    internal static PropertyInfo PropSupportSSRTransparent;
    internal static PropertyInfo PropSupportDataDrivenLensFlare;
    internal static PropertyInfo PropSupportScreenSpaceLensFlare;
    internal static PropertyInfo PropSupportDitheringCrossFade;

    // Nested struct objects accessed via properties on RenderPipelineSettings
    // (kept for diagnostic reads -- writes to these are non-functional)
    internal static object ShadowInitParamsRef;
    internal static Type ShadowInitParamsType;

    internal static object LightLoopSettingsRef;
    internal static Type LightLoopSettingsType;

    internal static object PostProcessSettingsRef;
    internal static Type PostProcessSettingsType;

    internal static object DynamicResolutionSettingsRef;
    internal static Type DynamicResolutionSettingsType;

    internal static object GpuResidentDrawerSettingsRef;
    internal static Type GpuResidentDrawerSettingsType;

    internal static object LightingQualitySettingsRef;
    internal static Type LightingQualitySettingsType;

    internal static object PostProcessQualitySettingsRef;
    internal static Type PostProcessQualitySettingsType;

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
                    $"concrete IL2CPP type={concreteType.Name}. Using concrete type for reflection.");
                t = concreteType;

                var castAsset = RuntimeCastToConcreteType(pipelineAsset, concreteType);
                if (castAsset != null && castAsset != (object)pipelineAsset)
                {
                    HdrpAssetRef = castAsset;
                    PerformancePlugin.Log.LogInfo(
                        $"HDRP Asset re-wrapped: managed type now {castAsset.GetType().Name}");
                }
            }
            else
            {
                try
                {
                    string il2cppTypeName = pipelineAsset.GetIl2CppType()?.FullName ?? "unknown";
                    PerformancePlugin.Log.LogInfo(
                        $"IL2CPP type info: GetType()={t.FullName}, GetIl2CppType()={il2cppTypeName}");
                }
                catch { }
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
                        CacheRenderPipelineSettingsFields();
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

    private static void CacheRenderPipelineSettingsFields()
    {
        if (RenderPipelineSettingsType == null || RenderPipelineSettingsRef == null) return;

        // Diagnostic dump (gated behind BOTH DebugLogging AND DiagnosticScan)
        if (Debug && PerformancePlugin.CfgDiagnosticScan.Value)
            DumpFieldDiscovery();

        Type st = RenderPipelineSettingsType;

        // Support flags (the PRIMARY optimization mechanism)
        PropSupportSSR = FindProp(st, "supportSSR");
        PropSupportSSAO = FindProp(st, "supportSSAO");
        PropSupportVolumetrics = FindProp(st, "supportVolumetrics");
        PropSupportVolumetricClouds = FindProp(st, "supportVolumetricClouds");
        PropSupportSubsurfaceScattering = FindProp(st, "supportSubsurfaceScattering");
        PropSupportMotionVectors = FindProp(st, "supportMotionVectors");
        PropSupportDecals = FindProp(st, "supportDecals");
        PropSupportDistortion = FindProp(st, "supportDistortion");
        PropSupportSSRTransparent = FindProp(st, "supportSSRTransparent");
        PropSupportDataDrivenLensFlare = FindProp(st, "supportDataDrivenLensFlare");
        PropSupportScreenSpaceLensFlare = FindProp(st, "supportScreenSpaceLensFlare");
        PropSupportDitheringCrossFade = FindProp(st, "supportDitheringCrossFade");

        // HDRenderPipelineAsset direct properties
        if (HdrpAssetType != null)
        {
            PropGpuResidentDrawerMode = FindProp(HdrpAssetType, "gpuResidentDrawerMode");
        }

        // Nested struct objects (for diagnostic reads)
        CacheNestedStruct("hdShadowInitParams", ref ShadowInitParamsRef, ref ShadowInitParamsType, null);
        CacheNestedStruct("lightLoopSettings", ref LightLoopSettingsRef, ref LightLoopSettingsType, null);
        CacheNestedStruct("postProcessSettings", ref PostProcessSettingsRef, ref PostProcessSettingsType, null);
        CacheNestedStruct("dynamicResolutionSettings", ref DynamicResolutionSettingsRef,
            ref DynamicResolutionSettingsType, null);
        CacheNestedStruct("gpuResidentDrawerSettings", ref GpuResidentDrawerSettingsRef,
            ref GpuResidentDrawerSettingsType, null);
        CacheNestedStruct("lightingQualitySettings", ref LightingQualitySettingsRef,
            ref LightingQualitySettingsType, null);
        CacheNestedStruct("postProcessQualitySettings", ref PostProcessQualitySettingsRef,
            ref PostProcessQualitySettingsType, null);

        DumpNestedStructSummary();
        if (Debug)
            DumpNestedStructProperties();
    }

    private static void CacheNestedStruct(string propName, ref object nestRef, ref Type nestType,
        Action onCached)
    {
        var prop = FindProp(RenderPipelineSettingsType, propName);
        if (prop == null)
        {
            DebugLog($"Nested struct property '{propName}' not found on RenderPipelineSettings.");
            return;
        }

        try
        {
            nestRef = prop.GetValue(RenderPipelineSettingsRef);
            if (nestRef != null)
            {
                nestType = nestRef.GetType();
                DebugLog($"Nested struct '{propName}': type={nestType.Name}");
                onCached?.Invoke();
            }
            else
            {
                DebugLog($"Nested struct '{propName}' returned null.");
            }
        }
        catch (Exception ex)
        {
            DebugLog($"Could not read nested struct '{propName}': {ex.Message}");
        }
    }

    // ==================================================================
    //  Field Discovery Dump (diagnostic only)
    // ==================================================================

    private static void DumpFieldDiscovery()
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("=== HDRP FIELD DISCOVERY DUMP (runs once per session) ===");

        BindingFlags bf = BindingFlags.Public | BindingFlags.NonPublic |
                          BindingFlags.Instance | BindingFlags.Static |
                          BindingFlags.FlattenHierarchy;

        sb.AppendLine();
        sb.AppendLine("--- RenderPipelineSettings: Managed Reflection ---");
        sb.AppendLine($"  Type: {RenderPipelineSettingsType.FullName}");
        sb.AppendLine($"  IsValueType: {RenderPipelineSettingsType.IsValueType}");
        sb.AppendLine($"  IsClass: {RenderPipelineSettingsType.IsClass}");

        try
        {
            var props = RenderPipelineSettingsType.GetProperties(bf);
            sb.AppendLine($"  Property count: {props?.Length ?? 0}");
            if (props != null)
            {
                foreach (var p in props)
                {
                    string valStr = "[not read]";
                    try
                    {
                        if (p.GetIndexParameters().Length == 0)
                        {
                            var val = p.GetValue(RenderPipelineSettingsRef);
                            valStr = val?.ToString() ?? "null";
                            if (valStr.Length > 100) valStr = valStr.Substring(0, 97) + "...";
                        }
                    }
                    catch (Exception ex) { valStr = $"[read error: {ex.Message}]"; }

                    sb.AppendLine($"    PROP:  {p.Name,-50} Type={p.PropertyType.FullName,-60} Value={valStr}");
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  [ERROR enumerating properties: {ex.Message}]");
        }

        sb.AppendLine();
        sb.AppendLine("=== END HDRP FIELD DISCOVERY DUMP ===");
        PerformancePlugin.Log.LogInfo(sb.ToString());
    }

    private static void DumpNestedStructSummary()
    {
        PerformancePlugin.Log.LogInfo(
            $"Nested struct status: " +
            $"shadows={ShadowInitParamsRef != null}, " +
            $"lightLoop={LightLoopSettingsRef != null}, " +
            $"postProcess={PostProcessSettingsRef != null}, " +
            $"dynamicRes={DynamicResolutionSettingsRef != null}, " +
            $"gpuResidentDrawer={GpuResidentDrawerSettingsRef != null}, " +
            $"lightingQuality={LightingQualitySettingsRef != null}, " +
            $"postProcessQuality={PostProcessQualitySettingsRef != null}");
    }

    private static void DumpNestedStructProperties()
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("=== NESTED STRUCT PROPERTY DUMP (debug) ===");

        BindingFlags bf = BindingFlags.Public | BindingFlags.NonPublic |
                          BindingFlags.Instance | BindingFlags.FlattenHierarchy;

        void DumpNested(string label, object nestRef, Type nestType)
        {
            if (nestRef == null || nestType == null) return;

            sb.AppendLine();
            sb.AppendLine($"  --- {label} ({nestType.Name}) ---");
            try
            {
                var props = nestType.GetProperties(bf);
                if (props != null)
                {
                    foreach (var p in props)
                    {
                        string valStr = "[not read]";
                        try
                        {
                            if (p.GetIndexParameters().Length == 0)
                            {
                                var val = p.GetValue(nestRef);
                                valStr = val?.ToString() ?? "null";
                                if (valStr.Length > 100) valStr = valStr.Substring(0, 97) + "...";
                            }
                        }
                        catch (Exception ex) { valStr = $"[read error: {ex.Message}]"; }

                        sb.AppendLine($"      PROP: {p.Name,-50} Type={p.PropertyType.Name,-30} Value={valStr}");
                    }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"    [ERROR: {ex.Message}]");
            }
        }

        DumpNested("hdShadowInitParams", ShadowInitParamsRef, ShadowInitParamsType);
        DumpNested("lightLoopSettings", LightLoopSettingsRef, LightLoopSettingsType);
        DumpNested("postProcessSettings", PostProcessSettingsRef, PostProcessSettingsType);
        DumpNested("dynamicResolutionSettings", DynamicResolutionSettingsRef, DynamicResolutionSettingsType);
        DumpNested("gpuResidentDrawerSettings", GpuResidentDrawerSettingsRef, GpuResidentDrawerSettingsType);
        DumpNested("lightingQualitySettings", LightingQualitySettingsRef, LightingQualitySettingsType);
        DumpNested("postProcessQualitySettings", PostProcessQualitySettingsRef, PostProcessQualitySettingsType);

        sb.AppendLine();
        sb.AppendLine("=== END NESTED STRUCT PROPERTY DUMP ===");
        PerformancePlugin.Log.LogInfo(sb.ToString());
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
                    CacheRenderPipelineSettingsFields();
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

        string ssrVal = PropSupportSSR != null ? SafeGetBool(PropSupportSSR, RenderPipelineSettingsRef).ToString() : "N/A";
        string ssaoVal = PropSupportSSAO != null ? SafeGetBool(PropSupportSSAO, RenderPipelineSettingsRef).ToString() : "N/A";
        string volVal = PropSupportVolumetrics != null ? SafeGetBool(PropSupportVolumetrics, RenderPipelineSettingsRef).ToString() : "N/A";
        string cloudsVal = PropSupportVolumetricClouds != null ? SafeGetBool(PropSupportVolumetricClouds, RenderPipelineSettingsRef).ToString() : "N/A";
        string sssVal = PropSupportSubsurfaceScattering != null ? SafeGetBool(PropSupportSubsurfaceScattering, RenderPipelineSettingsRef).ToString() : "N/A";

        PerformancePlugin.Log.LogInfo(
            $"HDRP Asset: {assetName} ({HdrpAssetType?.Name}). " +
            $"RenderPipelineSettings: {(RenderPipelineSettingsRef != null ? "resolved" : "NOT FOUND")}. " +
            $"Key props: supportSSR={ssrVal}, " +
            $"supportSSAO={ssaoVal}, " +
            $"supportVolumetrics={volVal}, " +
            $"supportVolumetricClouds={cloudsVal}, " +
            $"supportSubsurfaceScattering={sssVal}");
    }

    // ==================================================================
    //  Quality-level-change diagnostic snapshot
    // ==================================================================

    /// <summary>
    /// Logs key HDRP Asset properties that differ between quality levels and
    /// could explain why "Low" is slower than "Medium". Called once per quality
    /// level change, AFTER the reflection cache has been rebuilt for the new asset.
    ///
    /// Properties logged:
    ///   - Asset name (confirms which HDRP Asset is active)
    ///   - enableSRPBatcher on the HDRP Asset (separate from GraphicsSettings toggle)
    ///   - supportedLitShaderMode (DeferredOnly / ForwardOnly / Both)
    ///   - gpuResidentDrawerMode (Disabled / InstancedDrawing)
    ///   - dynamicResolutionSettings.enabled (DRS on/off)
    ///   - GraphicsSettings.useScriptableRenderPipelineBatching (runtime toggle)
    /// </summary>
    internal static void LogQualityLevelSnapshot(int qualityLevel)
    {
        CacheHDRPReflection();

        var sb = new StringBuilder();
        sb.AppendLine($"=== QUALITY LEVEL SNAPSHOT (level={qualityLevel}) ===");

        // Asset name
        string assetName = "unknown";
        try { assetName = ((UnityEngine.Object)HdrpAssetRef)?.name ?? "null"; }
        catch { }
        sb.AppendLine($"  HDRP Asset name           = {assetName}");

        // GraphicsSettings.useScriptableRenderPipelineBatching (runtime toggle)
        try
        {
            bool srpGlobal = GraphicsSettings.useScriptableRenderPipelineBatching;
            sb.AppendLine($"  GraphicsSettings.srpBatch = {srpGlobal}");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  GraphicsSettings.srpBatch = [read error: {ex.Message}]");
        }

        // enableSRPBatcher on the HDRP Asset itself
        if (HdrpAssetType != null)
        {
            var propEnableSrp = FindProp(HdrpAssetType, "enableSRPBatcher");
            if (propEnableSrp != null)
            {
                try
                {
                    var val = propEnableSrp.GetValue(HdrpAssetRef);
                    sb.AppendLine($"  Asset.enableSRPBatcher    = {val}");
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"  Asset.enableSRPBatcher    = [read error: {ex.Message}]");
                }
            }
            else
            {
                sb.AppendLine("  Asset.enableSRPBatcher    = [property not found]");
            }

            // supportedLitShaderMode (Forward vs Deferred)
            var propLitMode = FindProp(HdrpAssetType, "supportedLitShaderMode");
            if (propLitMode != null)
            {
                try
                {
                    var val = propLitMode.GetValue(HdrpAssetRef);
                    sb.AppendLine($"  supportedLitShaderMode    = {val}");
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"  supportedLitShaderMode    = [read error: {ex.Message}]");
                }
            }
            else
            {
                sb.AppendLine("  supportedLitShaderMode    = [property not found]");
            }
        }

        // gpuResidentDrawerMode
        if (PropGpuResidentDrawerMode != null)
        {
            try
            {
                var val = PropGpuResidentDrawerMode.GetValue(HdrpAssetRef);
                sb.AppendLine($"  gpuResidentDrawerMode     = {val}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"  gpuResidentDrawerMode     = [read error: {ex.Message}]");
            }
        }
        else
        {
            sb.AppendLine("  gpuResidentDrawerMode     = [property not found]");
        }

        // Dynamic resolution settings
        if (DynamicResolutionSettingsRef != null && DynamicResolutionSettingsType != null)
        {
            var propEnabled = FindProp(DynamicResolutionSettingsType, "enabled");
            if (propEnabled != null)
            {
                try
                {
                    var val = propEnabled.GetValue(DynamicResolutionSettingsRef);
                    sb.AppendLine($"  dynamicResolution.enabled = {val}");
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"  dynamicResolution.enabled = [read error: {ex.Message}]");
                }
            }
            else
            {
                sb.AppendLine("  dynamicResolution.enabled = [property not found]");
            }
        }
        else
        {
            sb.AppendLine("  dynamicResolution         = [struct not resolved]");
        }

        // Key pipeline support flags (current values BEFORE our writes)
        sb.AppendLine("  -- Pipeline support flags (pre-optimization) --");
        string ssrVal = PropSupportSSR != null ? SafeGetBool(PropSupportSSR, RenderPipelineSettingsRef).ToString() : "N/A";
        string ssaoVal = PropSupportSSAO != null ? SafeGetBool(PropSupportSSAO, RenderPipelineSettingsRef).ToString() : "N/A";
        string volVal = PropSupportVolumetrics != null ? SafeGetBool(PropSupportVolumetrics, RenderPipelineSettingsRef).ToString() : "N/A";
        string decalVal = PropSupportDecals != null ? SafeGetBool(PropSupportDecals, RenderPipelineSettingsRef).ToString() : "N/A";
        string mvecVal = PropSupportMotionVectors != null ? SafeGetBool(PropSupportMotionVectors, RenderPipelineSettingsRef).ToString() : "N/A";
        sb.AppendLine($"    supportSSR={ssrVal}, supportSSAO={ssaoVal}, " +
                       $"supportVolumetrics={volVal}, supportDecals={decalVal}, " +
                       $"supportMotionVectors={mvecVal}");

        sb.AppendLine("=== END QUALITY LEVEL SNAPSHOT ===");
        PerformancePlugin.Log.LogInfo(sb.ToString());
    }

    // ==================================================================
    //  Diagnostic dump
    // ==================================================================

    internal static void DumpHDRPAssetDiagnostics(StringBuilder sb)
    {
        CacheHDRPReflection();

        if (HdrpAssetRef == null)
        {
            sb.AppendLine("  [HDRP Asset not available]");
            return;
        }

        try { sb.AppendLine($"  Asset name: {((UnityEngine.Object)HdrpAssetRef).name}"); }
        catch { sb.AppendLine("  Asset name: [could not read]"); }

        if (RenderPipelineSettingsRef != null && RenderPipelineSettingsType != null)
        {
            sb.AppendLine();
            sb.AppendLine($"  -- RenderPipelineSettings ({RenderPipelineSettingsType.Name}) --");

            DumpProp(sb, "supportSSR", PropSupportSSR, RenderPipelineSettingsRef);
            DumpProp(sb, "supportSSAO", PropSupportSSAO, RenderPipelineSettingsRef);
            DumpProp(sb, "supportVolumetrics", PropSupportVolumetrics, RenderPipelineSettingsRef);
            DumpProp(sb, "supportVolumetricClouds", PropSupportVolumetricClouds, RenderPipelineSettingsRef);
            DumpProp(sb, "supportSubsurfaceScattering", PropSupportSubsurfaceScattering, RenderPipelineSettingsRef);
            DumpProp(sb, "supportDecals", PropSupportDecals, RenderPipelineSettingsRef);
            DumpProp(sb, "supportDistortion", PropSupportDistortion, RenderPipelineSettingsRef);
            DumpProp(sb, "supportSSRTransparent", PropSupportSSRTransparent, RenderPipelineSettingsRef);
            DumpProp(sb, "supportMotionVectors", PropSupportMotionVectors, RenderPipelineSettingsRef);
            DumpProp(sb, "supportDataDrivenLensFlare", PropSupportDataDrivenLensFlare, RenderPipelineSettingsRef);
            DumpProp(sb, "supportScreenSpaceLensFlare", PropSupportScreenSpaceLensFlare, RenderPipelineSettingsRef);
        }
        else
        {
            sb.AppendLine("  [RenderPipelineSettings not resolved]");
        }

        // Nested struct diagnostics
        DumpNestedStructDiagnostic(sb, "Shadow Init Params", ShadowInitParamsRef, ShadowInitParamsType);
        DumpNestedStructDiagnostic(sb, "Light Loop Settings", LightLoopSettingsRef, LightLoopSettingsType);
        DumpNestedStructDiagnostic(sb, "Post Process Settings", PostProcessSettingsRef, PostProcessSettingsType);
    }

    private static void DumpProp(StringBuilder sb, string label, PropertyInfo prop, object target)
    {
        if (prop == null)
        {
            sb.AppendLine($"    {label,-38} = [property not found]");
            return;
        }
        try
        {
            var val = prop.GetValue(target);
            sb.AppendLine($"    {label,-38} = {val}");
        }
        catch
        {
            sb.AppendLine($"    {label,-38} = [read error]");
        }
    }

    private static void DumpNestedStructDiagnostic(StringBuilder sb, string label,
        object nestRef, Type nestType)
    {
        if (nestRef == null || nestType == null) return;

        sb.AppendLine();
        sb.AppendLine($"  -- {label} ({nestType.Name}) --");
        try
        {
            foreach (var p in nestType.GetProperties(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                try
                {
                    if (p.GetIndexParameters().Length > 0) continue;
                    var val = p.GetValue(nestRef);
                    string valStr = val?.ToString() ?? "null";
                    if (valStr.Length > 80) valStr = valStr.Substring(0, 77) + "...";
                    sb.AppendLine($"    {p.Name,-50} ({p.PropertyType.Name,-30}) = {valStr}");
                }
                catch { sb.AppendLine($"    {p.Name,-50} ({p.PropertyType.Name,-30}) = [read error]"); }
            }
        }
        catch (Exception ex) { sb.AppendLine($"    [Error: {ex.Message}]"); }
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
        PropGpuResidentDrawerMode = null;

        PropSupportSSR = null;
        PropSupportSSAO = null;
        PropSupportVolumetrics = null;
        PropSupportVolumetricClouds = null;
        PropSupportSubsurfaceScattering = null;
        PropSupportMotionVectors = null;
        PropSupportDecals = null;
        PropSupportDistortion = null;
        PropSupportSSRTransparent = null;
        PropSupportDataDrivenLensFlare = null;
        PropSupportScreenSpaceLensFlare = null;
        PropSupportDitheringCrossFade = null;

        ShadowInitParamsRef = null;
        ShadowInitParamsType = null;
        LightLoopSettingsRef = null;
        LightLoopSettingsType = null;
        PostProcessSettingsRef = null;
        PostProcessSettingsType = null;
        DynamicResolutionSettingsRef = null;
        DynamicResolutionSettingsType = null;
        GpuResidentDrawerSettingsRef = null;
        GpuResidentDrawerSettingsType = null;
        LightingQualitySettingsRef = null;
        LightingQualitySettingsType = null;
        PostProcessQualitySettingsRef = null;
        PostProcessQualitySettingsType = null;

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

    internal static FieldInfo FindField(Type type, string name)
    {
        if (type == null) return null;
        try
        {
            Type current = type;
            while (current != null && current != typeof(object))
            {
                var field = current.GetField(name,
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (field != null) return field;
                current = current.BaseType;
            }
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

    internal static object ResolveAndCast(Il2CppSystem.Object il2cppObj, out Type outType)
    {
        Type baseType = ((object)il2cppObj).GetType();
        outType = baseType;

        try
        {
            Type concreteType = ResolveIl2CppConcreteType(il2cppObj);
            if (concreteType != null && concreteType != baseType)
            {
                outType = concreteType;
                var castObj = RuntimeCastToConcreteType(
                    (Il2CppObjectBase)il2cppObj, concreteType);
                return castObj;
            }
        }
        catch (Exception ex)
        {
            DebugLog($"ResolveAndCast fallback: {ex.Message}");
        }

        return il2cppObj;
    }

    internal static Type ResolveType(string fullName)
    {
        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetType(fullName);
                    if (t != null) return t;
                }
                catch { }
            }
        }
        catch { }
        return null;
    }

    internal static void SafeSet(PropertyInfo prop, object target, object value)
    {
        if (prop == null) return;
        try
        {
            if (prop.GetSetMethod(true) != null)
                prop.SetValue(target, value);
        }
        catch { }
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

    internal static float SafeGetFloat(PropertyInfo prop, object target)
    {
        if (prop == null) return 0f;
        try
        {
            var val = prop.GetValue(target);
            if (val is float f) return f;
            return Convert.ToSingle(val);
        }
        catch { return 0f; }
    }
}
