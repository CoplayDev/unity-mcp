using System;
using System.Collections.Generic;
using System.Reflection;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MCPForUnity.Editor.Tools.Graphics
{
    internal static class RenderPipelineOps
    {
        // === pipeline_get_info ===
        // Returns: active pipeline, quality level, renderer info, key settings
        internal static object GetInfo(JObject @params)
        {
            var pipeline = RenderPipelineUtility.GetActivePipeline();
            var pipelineAsset = GraphicsSettings.currentRenderPipeline;

            // Quality level info
            int currentQuality = QualitySettings.GetQualityLevel();
            string[] qualityNames = QualitySettings.names;

            var data = new Dictionary<string, object>
            {
                ["pipeline"] = pipeline.ToString(),
                ["pipelineName"] = GraphicsHelpers.GetPipelineName(),
                ["qualityLevel"] = currentQuality,
                ["qualityLevelName"] = currentQuality < qualityNames.Length ? qualityNames[currentQuality] : "Unknown",
                ["qualityLevels"] = qualityNames,
                ["colorSpace"] = QualitySettings.activeColorSpace.ToString(),
                ["hasVolumeSystem"] = GraphicsHelpers.HasVolumeSystem,
            };

            // If SRP, add pipeline asset info
            if (pipelineAsset != null)
            {
                data["pipelineAsset"] = pipelineAsset.name;
                data["pipelineAssetPath"] = AssetDatabase.GetAssetPath(pipelineAsset);
                data["pipelineAssetType"] = pipelineAsset.GetType().Name;

                // Read common public properties via reflection
                var settings = new Dictionary<string, object>();
                TryReadProperty(pipelineAsset, "renderScale", settings);
                TryReadProperty(pipelineAsset, "supportsHDR", settings);
                TryReadProperty(pipelineAsset, "msaaSampleCount", settings);
                TryReadProperty(pipelineAsset, "shadowDistance", settings);
                TryReadProperty(pipelineAsset, "shadowCascadeCount", settings);
                TryReadProperty(pipelineAsset, "maxAdditionalLightsCount", settings);
                TryReadProperty(pipelineAsset, "supportsSoftShadows", settings);
                TryReadProperty(pipelineAsset, "colorGradingMode", settings);

                if (settings.Count > 0)
                    data["settings"] = settings;
            }

            return new
            {
                success = true,
                message = $"Pipeline: {GraphicsHelpers.GetPipelineName()}, Quality: {(currentQuality < qualityNames.Length ? qualityNames[currentQuality] : "?")}",
                data
            };
        }

        // === pipeline_set_quality ===
        // Params: level (int or string name)
        internal static object SetQuality(JObject @params)
        {
            var p = new ToolParams(@params);
            string levelName = p.Get("level");
            int? levelIndex = p.GetInt("level");

            string[] names = QualitySettings.names;
            int targetIndex = -1;

            if (levelIndex.HasValue)
            {
                targetIndex = levelIndex.Value;
            }
            else if (!string.IsNullOrEmpty(levelName))
            {
                // Try exact match first
                for (int i = 0; i < names.Length; i++)
                {
                    if (string.Equals(names[i], levelName, StringComparison.OrdinalIgnoreCase))
                    {
                        targetIndex = i;
                        break;
                    }
                }
                // Try parse as int
                if (targetIndex < 0 && int.TryParse(levelName, out int parsed))
                    targetIndex = parsed;
            }
            else
            {
                return new ErrorResponse($"'level' parameter required. Available: {string.Join(", ", names)}");
            }

            if (targetIndex < 0 || targetIndex >= names.Length)
                return new ErrorResponse(
                    $"Invalid quality level. Available: {string.Join(", ", names)} (0-{names.Length - 1})");

            QualitySettings.SetQualityLevel(targetIndex, true);

            return new
            {
                success = true,
                message = $"Quality level set to '{names[targetIndex]}' (index {targetIndex}).",
                data = new
                {
                    level = targetIndex,
                    name = names[targetIndex],
                    allLevels = names
                }
            };
        }

        // === pipeline_get_settings ===
        // Detailed read of pipeline asset settings via public properties + SerializedObject fallback
        internal static object GetSettings(JObject @params)
        {
            var pipelineAsset = GraphicsSettings.currentRenderPipeline;
            if (pipelineAsset == null)
                return new ErrorResponse("No render pipeline asset found (Built-in pipeline has no asset).");

            var settings = new Dictionary<string, object>();

            // Public properties (URP)
            string[] publicProps = {
                "renderScale", "supportsHDR", "msaaSampleCount", "shadowDistance",
                "shadowCascadeCount", "mainLightShadowmapResolution",
                "additionalLightsShadowmapResolution", "maxAdditionalLightsCount",
                "supportsSoftShadows", "colorGradingMode", "colorGradingLutSize"
            };
            foreach (var propName in publicProps)
                TryReadProperty(pipelineAsset, propName, settings);

            // SerializedObject for non-public settings
            var serializedSettings = new Dictionary<string, object>();
            string[] serializedPaths = {
                "m_DefaultRendererIndex", "m_MainLightRenderingMode",
                "m_AdditionalLightsRenderingMode", "m_SupportsOpaqueTexture",
                "m_SupportsDepthTexture"
            };

            using (var so = new SerializedObject(pipelineAsset))
            {
                foreach (var path in serializedPaths)
                {
                    var prop = so.FindProperty(path);
                    if (prop != null)
                        serializedSettings[path] = GraphicsHelpers.ReadSerializedValue(prop);
                }
            }

            if (serializedSettings.Count > 0)
                settings["_serialized"] = serializedSettings;

            return new
            {
                success = true,
                message = $"Pipeline settings for '{pipelineAsset.name}'.",
                data = new
                {
                    assetName = pipelineAsset.name,
                    assetPath = AssetDatabase.GetAssetPath(pipelineAsset),
                    assetType = pipelineAsset.GetType().Name,
                    settings
                }
            };
        }

        // === pipeline_set_settings ===
        // Write pipeline asset settings via public properties + SerializedObject fallback
        internal static object SetSettings(JObject @params)
        {
            var p = new ToolParams(@params);
            var settingsToken = p.GetRaw("settings") as JObject;
            if (settingsToken == null)
                return new ErrorResponse("'settings' dict is required.");

            var pipelineAsset = GraphicsSettings.currentRenderPipeline;
            if (pipelineAsset == null)
                return new ErrorResponse("No render pipeline asset found.");

            var changed = new List<string>();
            var failed = new List<string>();

            using (var so = new SerializedObject(pipelineAsset))
            {
                foreach (var prop in settingsToken.Properties())
                {
                    string propName = prop.Name;
                    JToken value = prop.Value;

                    // Try public property first
                    var publicProp = pipelineAsset.GetType().GetProperty(propName,
                        BindingFlags.Public | BindingFlags.Instance);
                    if (publicProp != null && publicProp.CanWrite)
                    {
                        try
                        {
                            object converted = ConvertPropertyValue(value, publicProp.PropertyType);
                            publicProp.SetValue(pipelineAsset, converted);
                            changed.Add(propName);
                            continue;
                        }
                        catch (Exception ex)
                        {
                            McpLog.Warn($"[RenderPipelineOps] Failed to set '{propName}' via property: {ex.Message}");
                        }
                    }

                    // Try SerializedObject fallback (for m_ prefixed properties)
                    string serializedPath = propName.StartsWith("m_") ? propName : $"m_{char.ToUpper(propName[0])}{propName.Substring(1)}";
                    var sProp = so.FindProperty(serializedPath);
                    if (sProp == null && !propName.StartsWith("m_"))
                        sProp = so.FindProperty(propName);

                    if (sProp != null)
                    {
                        if (GraphicsHelpers.SetSerializedValue(sProp, value))
                        {
                            changed.Add(propName);
                            continue;
                        }
                    }

                    failed.Add(propName);
                }
                so.ApplyModifiedProperties();
            }

            EditorUtility.SetDirty(pipelineAsset);
            AssetDatabase.SaveAssets();

            var msg = $"Updated {changed.Count} pipeline setting(s)";
            if (failed.Count > 0)
                msg += $". Failed: {string.Join(", ", failed)}";

            return new
            {
                success = true,
                message = msg,
                data = new { changed, failed }
            };
        }

        // --- Helper: Convert JToken to target property type ---
        private static object ConvertPropertyValue(JToken value, Type targetType)
        {
            if (targetType == typeof(bool)) return ParamCoercion.CoerceBool(value, false);
            if (targetType == typeof(int)) return ParamCoercion.CoerceInt(value, 0);
            if (targetType == typeof(float)) return ParamCoercion.CoerceFloat(value, 0f);
            if (targetType == typeof(string)) return value.ToString();
            if (targetType.IsEnum)
            {
                string str = value.ToString();
                if (Enum.TryParse(targetType, str, true, out object enumVal))
                    return enumVal;
                if (int.TryParse(str, out int intVal))
                    return Enum.ToObject(targetType, intVal);
            }
            return Convert.ChangeType(value.ToObject<object>(), targetType);
        }

        // --- Helper: Try to read a property value via reflection ---
        private static void TryReadProperty(object obj, string propertyName, Dictionary<string, object> target)
        {
            if (obj == null) return;
            var prop = obj.GetType().GetProperty(propertyName,
                BindingFlags.Public | BindingFlags.Instance);
            if (prop != null)
            {
                try
                {
                    var val = prop.GetValue(obj);
                    target[propertyName] = val is Enum e ? e.ToString() : val;
                }
                catch { /* skip unreadable properties */ }
            }
        }
    }
}
