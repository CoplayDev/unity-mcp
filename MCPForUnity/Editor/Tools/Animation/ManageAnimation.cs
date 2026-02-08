using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Helpers;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.Animation
{
    [McpForUnityTool("manage_animation", AutoRegister = false)]
    public static class ManageAnimation
    {
        private static readonly Dictionary<string, string> ParamAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "clip_path", "clipPath" },
            { "controller_path", "controllerPath" },
            { "state_name", "stateName" },
            { "from_state", "fromState" },
            { "to_state", "toState" },
            { "parameter_name", "parameterName" },
            { "parameter_type", "parameterType" },
            { "property_path", "propertyPath" },
            { "default_value", "defaultValue" },
            { "has_exit_time", "hasExitTime" },
            { "exit_time", "exitTime" },
            { "layer_index", "layerIndex" },
            { "is_default", "isDefault" },
            { "relative_path", "relativePath" },
            { "function_name", "functionName" },
            { "string_parameter", "stringParameter" },
            { "float_parameter", "floatParameter" },
            { "int_parameter", "intParameter" },
            { "event_index", "eventIndex" },
            { "layer_name", "layerName" },
            { "blending_mode", "blendingMode" },
            { "blend_parameter", "blendParameter" },
            { "blend_parameter_x", "blendParameterX" },
            { "blend_parameter_y", "blendParameterY" },
            { "blend_type", "blendType" },
        };

        private static JObject NormalizeParams(JObject source)
        {
            if (source == null)
            {
                return new JObject();
            }

            var normalized = new JObject();
            var properties = ExtractProperties(source);
            if (properties != null)
            {
                foreach (var prop in properties.Properties())
                {
                    normalized[NormalizeKey(prop.Name, true)] = NormalizeToken(prop.Value);
                }
            }

            foreach (var prop in source.Properties())
            {
                if (string.Equals(prop.Name, "properties", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                normalized[NormalizeKey(prop.Name, true)] = NormalizeToken(prop.Value);
            }

            return normalized;
        }

        private static JObject ExtractProperties(JObject source)
        {
            if (source == null)
            {
                return null;
            }

            if (!source.TryGetValue("properties", StringComparison.OrdinalIgnoreCase, out var token))
            {
                return null;
            }

            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }

            if (token is JObject obj)
            {
                return obj;
            }

            if (token.Type == JTokenType.String)
            {
                try
                {
                    return JToken.Parse(token.ToString()) as JObject;
                }
                catch (JsonException ex)
                {
                    throw new JsonException(
                        $"Failed to parse 'properties' JSON string. Raw value: {token}",
                        ex);
                }
            }

            return null;
        }

        private static string NormalizeKey(string key, bool allowAliases)
        {
            if (string.IsNullOrEmpty(key))
            {
                return key;
            }
            if (string.Equals(key, "action", StringComparison.OrdinalIgnoreCase))
            {
                return "action";
            }
            if (allowAliases && ParamAliases.TryGetValue(key, out var alias))
            {
                return alias;
            }
            if (key.IndexOf('_') >= 0)
            {
                return StringCaseUtility.ToCamelCase(key);
            }
            return key;
        }

        private static JToken NormalizeToken(JToken token)
        {
            if (token == null)
            {
                return null;
            }

            if (token is JObject obj)
            {
                var normalized = new JObject();
                foreach (var prop in obj.Properties())
                {
                    normalized[NormalizeKey(prop.Name, false)] = NormalizeToken(prop.Value);
                }
                return normalized;
            }

            if (token is JArray array)
            {
                var normalized = new JArray();
                foreach (var item in array)
                {
                    normalized.Add(NormalizeToken(item));
                }
                return normalized;
            }

            return token;
        }

        public static object HandleCommand(JObject @params)
        {
            JObject normalizedParams = NormalizeParams(@params);
            string action = normalizedParams["action"]?.ToString();
            if (string.IsNullOrEmpty(action))
            {
                return new { success = false, message = "Action is required" };
            }

            try
            {
                string actionLower = action.ToLowerInvariant();

                if (actionLower.StartsWith("animator_"))
                {
                    return HandleAnimatorAction(normalizedParams, actionLower.Substring(9));
                }

                if (actionLower.StartsWith("controller_"))
                {
                    return HandleControllerAction(normalizedParams, actionLower.Substring(11));
                }

                if (actionLower.StartsWith("clip_"))
                {
                    return HandleClipAction(normalizedParams, actionLower.Substring(5));
                }

                return new { success = false, message = $"Unknown action: {action}. Actions must be prefixed with: animator_, controller_, or clip_" };
            }
            catch (Exception e)
            {
                McpLog.Error($"[ManageAnimation] Action '{action}' failed: {e}");
                return new ErrorResponse($"Internal error processing action '{action}': {e.Message}");
            }
        }

        private static object HandleAnimatorAction(JObject @params, string action)
        {
            switch (action)
            {
                case "get_info": return AnimatorRead.GetInfo(@params);
                case "get_parameter": return AnimatorRead.GetParameter(@params);
                case "play": return AnimatorControl.Play(@params);
                case "crossfade": return AnimatorControl.Crossfade(@params);
                case "set_parameter": return AnimatorControl.SetParameter(@params);
                case "set_speed": return AnimatorControl.SetSpeed(@params);
                case "set_enabled": return AnimatorControl.SetEnabled(@params);
                default:
                    return new { success = false, message = $"Unknown animator action: {action}. Valid: get_info, get_parameter, play, crossfade, set_parameter, set_speed, set_enabled" };
            }
        }

        private static object HandleControllerAction(JObject @params, string action)
        {
            switch (action)
            {
                case "create": return ControllerCreate.Create(@params);
                case "add_state": return ControllerCreate.AddState(@params);
                case "add_transition": return ControllerCreate.AddTransition(@params);
                case "add_parameter": return ControllerCreate.AddParameter(@params);
                case "get_info": return ControllerCreate.GetInfo(@params);
                case "assign": return ControllerCreate.AssignToGameObject(@params);
                case "add_layer": return ControllerLayers.AddLayer(@params);
                case "remove_layer": return ControllerLayers.RemoveLayer(@params);
                case "set_layer_weight": return ControllerLayers.SetLayerWeight(@params);
                case "create_blend_tree_1d": return ControllerBlendTrees.CreateBlendTree1D(@params);
                case "create_blend_tree_2d": return ControllerBlendTrees.CreateBlendTree2D(@params);
                case "add_blend_tree_child": return ControllerBlendTrees.AddBlendTreeChild(@params);
                default:
                    return new { success = false, message = $"Unknown controller action: {action}. Valid: create, add_state, add_transition, add_parameter, get_info, assign, add_layer, remove_layer, set_layer_weight, create_blend_tree_1d, create_blend_tree_2d, add_blend_tree_child" };
            }
        }

        private static object HandleClipAction(JObject @params, string action)
        {
            switch (action)
            {
                case "create": return ClipCreate.Create(@params);
                case "get_info": return ClipCreate.GetInfo(@params);
                case "add_curve": return ClipCreate.AddCurve(@params);
                case "set_curve": return ClipCreate.SetCurve(@params);
                case "set_vector_curve": return ClipCreate.SetVectorCurve(@params);
                case "create_preset": return ClipPresets.CreatePreset(@params);
                case "assign": return ClipCreate.Assign(@params);
                case "add_event": return ClipCreate.AddEvent(@params);
                case "remove_event": return ClipCreate.RemoveEvent(@params);
                default:
                    return new { success = false, message = $"Unknown clip action: {action}. Valid: create, get_info, add_curve, set_curve, set_vector_curve, create_preset, assign, add_event, remove_event" };
            }
        }
    }
}
