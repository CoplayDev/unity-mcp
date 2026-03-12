using System;
using System.Collections.Generic;
using System.Reflection;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// MCP tool for Unity render pipeline management (URP/HDRP).
    /// Actions: get_pipeline_info, list_volumes, get_volume, set_volume_override,
    ///          list_renderer_features, get_render_pipeline_asset, list_post_processing,
    ///          toggle_volume_override.
    /// </summary>
    [McpForUnityTool("manage_render_pipeline", AutoRegister = true)]
    public static class ManageRenderPipeline
    {
        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
                return new ErrorResponse("Parameters cannot be null.");

            var p = new ToolParams(@params);
            var actionResult = p.GetRequired("action");
            if (!actionResult.IsSuccess)
                return new ErrorResponse(actionResult.ErrorMessage);

            string action = actionResult.Value.ToLowerInvariant();

            try
            {
                return action switch
                {
                    "get_pipeline_info"          => GetPipelineInfo(),
                    "list_volumes"               => ListVolumes(p),
                    "get_volume"                 => GetVolume(p),
                    "set_volume_override"        => SetVolumeOverride(p),
                    "list_renderer_features"     => ListRendererFeatures(),
                    "get_render_pipeline_asset"  => GetRenderPipelineAsset(),
                    "list_post_processing"       => ListPostProcessing(),
                    "toggle_volume_override"     => ToggleVolumeOverride(p),
                    _ => new ErrorResponse(
                        $"Unknown action: '{action}'. Supported: get_pipeline_info, list_volumes, get_volume, " +
                        "set_volume_override, list_renderer_features, get_render_pipeline_asset, " +
                        "list_post_processing, toggle_volume_override")
                };
            }
            catch (Exception e)
            {
                McpLog.Error($"[ManageRenderPipeline] Action '{action}' failed: {e}");
                return new ErrorResponse($"Internal error: {e.Message}");
            }
        }

        #region Pipeline Info

        private static object GetPipelineInfo()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            string pipelineType = "Built-in";
            string pipelineName = "Built-in Render Pipeline";

            if (pipeline != null)
            {
                pipelineName = pipeline.name;
                string typeName = pipeline.GetType().Name;
                if (typeName.Contains("Universal"))
                    pipelineType = "URP";
                else if (typeName.Contains("HD"))
                    pipelineType = "HDRP";
                else
                    pipelineType = typeName;
            }

            return new SuccessResponse("Render pipeline info.", new Dictionary<string, object>
            {
                ["pipeline_type"] = pipelineType,
                ["pipeline_name"] = pipelineName,
                ["pipeline_asset_type"] = pipeline != null ? pipeline.GetType().FullName : null,
            });
        }

        private static object GetRenderPipelineAsset()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline == null)
                return new SuccessResponse("Built-in render pipeline (no SRP asset).", new Dictionary<string, object>
                {
                    ["pipeline_type"] = "Built-in",
                });

            var data = new Dictionary<string, object>
            {
                ["name"] = pipeline.name,
                ["type"] = pipeline.GetType().Name,
            };

            // Read public properties via reflection
            var props = pipeline.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var settings = new Dictionary<string, object>();
            foreach (var prop in props)
            {
                if (!prop.CanRead) continue;
                try
                {
                    var val = prop.GetValue(pipeline);
                    if (val != null && (val is int || val is float || val is bool || val is string || val is Enum))
                        settings[prop.Name] = val.ToString();
                }
                catch { /* skip unreadable properties */ }
            }
            data["settings"] = settings;

            return new SuccessResponse($"Render pipeline asset '{pipeline.name}'.", data);
        }

        #endregion

        #region Volume Operations

        private static object ListVolumes(ToolParams p)
        {
            var volumes = UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsSortMode.InstanceID);
            var pagination = PaginationRequest.FromParams(
                new JObject { ["page_size"] = p.GetInt("page_size"), ["cursor"] = p.GetInt("cursor") });

            var allItems = new List<Dictionary<string, object>>();
            foreach (var vol in volumes)
            {
                allItems.Add(new Dictionary<string, object>
                {
                    ["name"] = vol.gameObject.name,
                    ["instance_id"] = vol.gameObject.GetInstanceID(),
                    ["is_global"] = vol.isGlobal,
                    ["weight"] = Math.Round(vol.weight, 4),
                    ["priority"] = vol.priority,
                    ["has_profile"] = vol.profile != null,
                    ["override_count"] = vol.profile != null ? vol.profile.components.Count : 0,
                    ["enabled"] = vol.enabled,
                });
            }

            var page = PaginationResponse<Dictionary<string, object>>.Create(allItems, pagination);
            return new SuccessResponse($"Found {volumes.Length} Volume component(s).", page);
        }

        private static object GetVolume(ToolParams p)
        {
            var targetResult = p.GetRequired("target");
            if (!targetResult.IsSuccess)
                return new ErrorResponse(targetResult.ErrorMessage);

            var go = ObjectResolver.ResolveGameObject(new JValue(targetResult.Value));
            if (go == null)
                return new ErrorResponse($"GameObject '{targetResult.Value}' not found.");

            var vol = go.GetComponent<Volume>();
            if (vol == null)
                return new ErrorResponse($"No Volume on '{go.name}'.");

            if (vol.profile == null)
                return new ErrorResponse($"Volume '{go.name}' has no profile assigned.");

            var overrides = new List<Dictionary<string, object>>();
            foreach (var component in vol.profile.components)
            {
                var overrideData = new Dictionary<string, object>
                {
                    ["type"] = component.GetType().Name,
                    ["active"] = component.active,
                };

                // Read override parameters
                var fields = component.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance);
                var parameters = new Dictionary<string, object>();
                foreach (var field in fields)
                {
                    if (typeof(VolumeParameter).IsAssignableFrom(field.FieldType))
                    {
                        try
                        {
                            var param = field.GetValue(component) as VolumeParameter;
                            if (param != null)
                            {
                                parameters[field.Name] = new Dictionary<string, object>
                                {
                                    ["value"] = param.GetValue<object>()?.ToString(),
                                    ["overrideState"] = param.overrideState,
                                };
                            }
                        }
                        catch { /* skip unreadable */ }
                    }
                }
                overrideData["parameters"] = parameters;
                overrides.Add(overrideData);
            }

            return new SuccessResponse($"Volume '{go.name}' profile.", new Dictionary<string, object>
            {
                ["name"] = go.name,
                ["profile_name"] = vol.profile.name,
                ["is_global"] = vol.isGlobal,
                ["weight"] = Math.Round(vol.weight, 4),
                ["overrides"] = overrides,
            });
        }

        private static object SetVolumeOverride(ToolParams p)
        {
            var targetResult = p.GetRequired("target");
            if (!targetResult.IsSuccess) return new ErrorResponse(targetResult.ErrorMessage);
            var overrideTypeResult = p.GetRequired("override_type");
            if (!overrideTypeResult.IsSuccess) return new ErrorResponse(overrideTypeResult.ErrorMessage);
            var propertyResult = p.GetRequired("property");
            if (!propertyResult.IsSuccess) return new ErrorResponse(propertyResult.ErrorMessage);
            var valueResult = p.GetRequired("value");
            if (!valueResult.IsSuccess) return new ErrorResponse(valueResult.ErrorMessage);

            var go = ObjectResolver.ResolveGameObject(new JValue(targetResult.Value));
            if (go == null) return new ErrorResponse($"GameObject '{targetResult.Value}' not found.");

            var vol = go.GetComponent<Volume>();
            if (vol == null || vol.profile == null)
                return new ErrorResponse($"No Volume with profile on '{go.name}'.");

            foreach (var component in vol.profile.components)
            {
                if (string.Equals(component.GetType().Name, overrideTypeResult.Value, StringComparison.OrdinalIgnoreCase))
                {
                    var field = component.GetType().GetField(propertyResult.Value,
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                    if (field == null)
                        return new ErrorResponse($"Property '{propertyResult.Value}' not found on {overrideTypeResult.Value}.");

                    try
                    {
                        var param = field.GetValue(component) as VolumeParameter;
                        if (param != null)
                        {
                            Undo.RecordObject(vol.profile, "MCP SetVolumeOverride");
                            param.overrideState = true;
                            // Try to set value based on parameter type
                            SetVolumeParamValue(param, valueResult.Value);
                            EditorUtility.SetDirty(vol.profile);
                            return new SuccessResponse($"Set {overrideTypeResult.Value}.{propertyResult.Value} = {valueResult.Value}.");
                        }
                    }
                    catch (Exception ex)
                    {
                        return new ErrorResponse($"Failed to set value: {ex.Message}");
                    }
                }
            }

            return new ErrorResponse($"Override '{overrideTypeResult.Value}' not found on volume '{go.name}'.");
        }

        private static object ToggleVolumeOverride(ToolParams p)
        {
            var targetResult = p.GetRequired("target");
            if (!targetResult.IsSuccess) return new ErrorResponse(targetResult.ErrorMessage);
            var overrideTypeResult = p.GetRequired("override_type");
            if (!overrideTypeResult.IsSuccess) return new ErrorResponse(overrideTypeResult.ErrorMessage);

            bool enabled = p.GetBool("enabled", true);

            var go = ObjectResolver.ResolveGameObject(new JValue(targetResult.Value));
            if (go == null) return new ErrorResponse($"GameObject '{targetResult.Value}' not found.");

            var vol = go.GetComponent<Volume>();
            if (vol == null || vol.profile == null)
                return new ErrorResponse($"No Volume with profile on '{go.name}'.");

            foreach (var component in vol.profile.components)
            {
                if (string.Equals(component.GetType().Name, overrideTypeResult.Value, StringComparison.OrdinalIgnoreCase))
                {
                    Undo.RecordObject(vol.profile, "MCP ToggleVolumeOverride");
                    component.active = enabled;
                    EditorUtility.SetDirty(vol.profile);
                    return new SuccessResponse($"{overrideTypeResult.Value} {(enabled ? "enabled" : "disabled")} on '{go.name}'.");
                }
            }

            return new ErrorResponse($"Override '{overrideTypeResult.Value}' not found on volume '{go.name}'.");
        }

        #endregion

        #region Renderer Features & Post Processing

        private static object ListRendererFeatures()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline == null)
                return new ErrorResponse("No SRP active — renderer features are a URP concept.");

            // Try to get renderer data via reflection (URP-specific)
            var features = new List<Dictionary<string, object>>();
            try
            {
                // Get default renderer
                var renderersProp = pipeline.GetType().GetProperty("rendererDataList",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (renderersProp == null)
                    renderersProp = pipeline.GetType().GetField("m_RendererDataList",
                        BindingFlags.NonPublic | BindingFlags.Instance)?.FieldType == null ? null : null;

                // Alternative: try scriptable renderer features
                var so = new SerializedObject(pipeline);
                var rendererList = so.FindProperty("m_RendererDataList");
                if (rendererList != null && rendererList.isArray)
                {
                    for (int i = 0; i < rendererList.arraySize; i++)
                    {
                        var rendererRef = rendererList.GetArrayElementAtIndex(i).objectReferenceValue;
                        if (rendererRef == null) continue;

                        var rendererSO = new SerializedObject(rendererRef);
                        var featuresProp = rendererSO.FindProperty("m_RendererFeatures");
                        if (featuresProp != null && featuresProp.isArray)
                        {
                            for (int j = 0; j < featuresProp.arraySize; j++)
                            {
                                var feature = featuresProp.GetArrayElementAtIndex(j).objectReferenceValue;
                                if (feature != null)
                                {
                                    features.Add(new Dictionary<string, object>
                                    {
                                        ["name"] = feature.name,
                                        ["type"] = feature.GetType().Name,
                                        ["renderer_index"] = i,
                                    });
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"[ManageRenderPipeline] Could not read renderer features: {ex.Message}");
            }

            return new SuccessResponse($"Found {features.Count} renderer feature(s).", new Dictionary<string, object>
            {
                ["features"] = features,
            });
        }

        private static object ListPostProcessing()
        {
            var volumes = UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsSortMode.InstanceID);
            var effects = new List<Dictionary<string, object>>();

            foreach (var vol in volumes)
            {
                if (vol.profile == null) continue;
                foreach (var component in vol.profile.components)
                {
                    if (!component.active) continue;
                    effects.Add(new Dictionary<string, object>
                    {
                        ["volume_name"] = vol.gameObject.name,
                        ["effect_type"] = component.GetType().Name,
                        ["is_global"] = vol.isGlobal,
                        ["priority"] = vol.priority,
                    });
                }
            }

            return new SuccessResponse($"Found {effects.Count} active post-processing effect(s).", new Dictionary<string, object>
            {
                ["effects"] = effects,
            });
        }

        #endregion

        #region Helpers

        private static void SetVolumeParamValue(VolumeParameter param, string valueStr)
        {
            var paramType = param.GetType();

            // Handle common VolumeParameter<T> types
            if (paramType.IsGenericType)
            {
                var genericArg = paramType.GetGenericArguments()[0];
                var valueProp = paramType.GetProperty("value");

                if (genericArg == typeof(float))
                    valueProp?.SetValue(param, float.Parse(valueStr, System.Globalization.CultureInfo.InvariantCulture));
                else if (genericArg == typeof(int))
                    valueProp?.SetValue(param, int.Parse(valueStr));
                else if (genericArg == typeof(bool))
                    valueProp?.SetValue(param, bool.Parse(valueStr));
                else
                    McpLog.Warn($"[ManageRenderPipeline] Unsupported param type: {genericArg.Name}");
            }
        }

        #endregion
    }
}
