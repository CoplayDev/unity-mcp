using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.Graphics
{
    internal static class VolumeOps
    {
        // === volume_create ===
        // Params: name (string), is_global (bool, default true), weight (float, default 1),
        //         priority (float, default 0), profile_path (string, optional - path to save VolumeProfile asset),
        //         effects (array of {type, ...params}, optional - effects to add immediately)
        internal static object CreateVolume(JObject @params)
        {
            var p = new ToolParams(@params);
            string name = p.Get("name") ?? "Volume";
            bool isGlobal = p.GetBool("is_global", "isGlobal") ?? true;
            float weight = p.GetFloat("weight") ?? 1.0f;
            float priority = p.GetFloat("priority") ?? 0f;
            string profilePath = p.Get("profile_path", "profilePath");

            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, $"Create Volume '{name}'");

            // Add Volume component via reflection
            var volumeComp = go.AddComponent(GraphicsHelpers.VolumeType);

            // Set properties via reflection
            SetProperty(volumeComp, "isGlobal", isGlobal);
            SetProperty(volumeComp, "weight", weight);
            SetProperty(volumeComp, "priority", priority);

            // Create or load VolumeProfile
            object profile;
            if (!string.IsNullOrEmpty(profilePath))
            {
                // Load existing or create new profile asset
                profile = AssetDatabase.LoadAssetAtPath(profilePath, GraphicsHelpers.VolumeProfileType);
                if (profile == null)
                {
                    profile = ScriptableObject.CreateInstance(GraphicsHelpers.VolumeProfileType);
                    // Ensure directory exists
                    var dir = System.IO.Path.GetDirectoryName(profilePath);
                    if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                        System.IO.Directory.CreateDirectory(dir);
                    AssetDatabase.CreateAsset((UnityEngine.Object)profile, profilePath);
                }
            }
            else
            {
                // Create embedded profile (not saved as asset)
                profile = ScriptableObject.CreateInstance(GraphicsHelpers.VolumeProfileType);
            }

            // Assign profile to volume's sharedProfile
            SetProperty(volumeComp, "sharedProfile", profile);

            // Add initial effects if provided
            var effectsToken = p.GetRaw("effects") as JArray;
            var addedEffects = new List<string>();
            if (effectsToken != null)
            {
                foreach (var effectDef in effectsToken)
                {
                    if (effectDef is JObject effectObj)
                    {
                        string effectType = ParamCoercion.CoerceString(effectObj["type"], null);
                        if (string.IsNullOrEmpty(effectType)) continue;

                        var type = GraphicsHelpers.ResolveVolumeComponentType(effectType);
                        if (type == null) continue;

                        // profile.Add(type, true)
                        var addMethod = GraphicsHelpers.VolumeProfileType.GetMethod("Add",
                            new[] { typeof(Type), typeof(bool) });
                        if (addMethod != null)
                        {
                            var component = addMethod.Invoke(profile, new object[] { type, true });
                            if (component != null)
                            {
                                // Set parameters from the effect definition
                                foreach (var prop in effectObj.Properties())
                                {
                                    if (prop.Name == "type") continue;
                                    SetVolumeParameter(component, prop.Name, prop.Value);
                                }
                                addedEffects.Add(effectType);
                            }
                        }
                    }
                }
                if (profile is UnityEngine.Object profileObj)
                    EditorUtility.SetDirty(profileObj);
            }

            GraphicsHelpers.MarkDirty(volumeComp);

            return new
            {
                success = true,
                message = $"Created {(isGlobal ? "global" : "local")} Volume '{name}'" +
                         (addedEffects.Count > 0 ? $" with effects: {string.Join(", ", addedEffects)}" : ""),
                data = new
                {
                    instanceID = go.GetInstanceID(),
                    isGlobal,
                    weight,
                    priority,
                    profilePath = profilePath ?? "(embedded)",
                    effects = addedEffects
                }
            };
        }

        // === volume_add_effect ===
        // Params: target (string/int), effect (string - type name like "Bloom")
        internal static object AddEffect(JObject @params)
        {
            var p = new ToolParams(@params);
            string effectName = p.Get("effect");
            if (string.IsNullOrEmpty(effectName))
                return new ErrorResponse("'effect' parameter is required (e.g., 'Bloom', 'Vignette').");

            var volume = GraphicsHelpers.FindVolume(@params);
            if (volume == null)
                return new ErrorResponse("Volume not found. Specify 'target' (name or instance ID).");

            var effectType = GraphicsHelpers.ResolveVolumeComponentType(effectName);
            if (effectType == null)
            {
                var available = GraphicsHelpers.GetAvailableEffectTypes()
                    .Select(t => t.Name).ToList();
                return new ErrorResponse(
                    $"Effect type '{effectName}' not found. Available: {string.Join(", ", available.Take(20))}");
            }

            // Get the volume's profile
            var profile = GetProperty(volume, "sharedProfile");
            if (profile == null)
                return new ErrorResponse("Volume has no profile assigned.");

            // profile.Add(effectType, true) -- 'true' means override all params
            var addMethod = GraphicsHelpers.VolumeProfileType.GetMethod("Add",
                new[] { typeof(Type), typeof(bool) });
            if (addMethod == null)
                return new ErrorResponse("Could not find VolumeProfile.Add method.");

            var component = addMethod.Invoke(profile, new object[] { effectType, true });
            if (component == null)
                return new ErrorResponse($"Failed to add effect '{effectName}'.");

            if (profile is UnityEngine.Object profileObj)
                EditorUtility.SetDirty(profileObj);
            GraphicsHelpers.MarkDirty(volume);

            return new
            {
                success = true,
                message = $"Added '{effectName}' to Volume '{(volume as Component)?.gameObject.name}'.",
                data = new { effect = effectName, volumeInstanceID = (volume as Component)?.gameObject.GetInstanceID() }
            };
        }

        // === volume_set_effect ===
        // Params: target (string/int), effect (string), parameters (dict of field->value)
        internal static object SetEffect(JObject @params)
        {
            var p = new ToolParams(@params);
            string effectName = p.Get("effect");
            if (string.IsNullOrEmpty(effectName))
                return new ErrorResponse("'effect' parameter is required.");

            var volume = GraphicsHelpers.FindVolume(@params);
            if (volume == null)
                return new ErrorResponse("Volume not found. Specify 'target'.");

            var profile = GetProperty(volume, "sharedProfile");
            if (profile == null)
                return new ErrorResponse("Volume has no profile assigned.");

            var effectType = GraphicsHelpers.ResolveVolumeComponentType(effectName);
            if (effectType == null)
                return new ErrorResponse($"Effect type '{effectName}' not found.");

            // Find the effect component in the profile
            var components = GetProperty(profile, "components") as System.Collections.IList;
            if (components == null)
                return new ErrorResponse("Could not read profile components.");

            object targetComponent = null;
            foreach (var comp in components)
            {
                if (comp != null && comp.GetType() == effectType)
                {
                    targetComponent = comp;
                    break;
                }
            }

            if (targetComponent == null)
                return new ErrorResponse($"Effect '{effectName}' not found on this Volume. Use volume_add_effect first.");

            // Set parameters
            var parameters = p.GetRaw("parameters") as JObject;
            if (parameters == null)
                return new ErrorResponse("'parameters' dict is required.");

            var setParams = new List<string>();
            var failedParams = new List<string>();
            foreach (var prop in parameters.Properties())
            {
                if (SetVolumeParameter(targetComponent, prop.Name, prop.Value))
                    setParams.Add(prop.Name);
                else
                    failedParams.Add(prop.Name);
            }

            if (profile is UnityEngine.Object profileObj)
                EditorUtility.SetDirty(profileObj);

            var msg = $"Set {setParams.Count} parameter(s) on '{effectName}'";
            if (failedParams.Count > 0)
                msg += $". Failed: {string.Join(", ", failedParams)}";

            return new
            {
                success = true,
                message = msg,
                data = new { effect = effectName, set = setParams, failed = failedParams }
            };
        }

        // === volume_remove_effect ===
        // Params: target, effect
        internal static object RemoveEffect(JObject @params)
        {
            var p = new ToolParams(@params);
            string effectName = p.Get("effect");
            if (string.IsNullOrEmpty(effectName))
                return new ErrorResponse("'effect' parameter is required.");

            var volume = GraphicsHelpers.FindVolume(@params);
            if (volume == null)
                return new ErrorResponse("Volume not found.");

            var effectType = GraphicsHelpers.ResolveVolumeComponentType(effectName);
            if (effectType == null)
                return new ErrorResponse($"Effect type '{effectName}' not found.");

            var profile = GetProperty(volume, "sharedProfile");
            if (profile == null)
                return new ErrorResponse("Volume has no profile.");

            var removeMethod = GraphicsHelpers.VolumeProfileType.GetMethod("Remove",
                new[] { typeof(Type) });
            if (removeMethod == null)
                return new ErrorResponse("Could not find VolumeProfile.Remove method.");

            removeMethod.Invoke(profile, new object[] { effectType });

            if (profile is UnityEngine.Object profileObj)
                EditorUtility.SetDirty(profileObj);
            GraphicsHelpers.MarkDirty(volume);

            return new
            {
                success = true,
                message = $"Removed '{effectName}' from Volume.",
                data = new { effect = effectName }
            };
        }

        // === volume_get_info ===
        // Params: target (optional -- if omitted, returns info for all volumes)
        internal static object GetInfo(JObject @params)
        {
            var volume = GraphicsHelpers.FindVolume(@params);
            if (volume == null)
                return new ErrorResponse("Volume not found.");

            var info = BuildVolumeInfo(volume);
            return new
            {
                success = true,
                message = $"Volume info for '{(volume as Component)?.gameObject.name}'.",
                data = info
            };
        }

        // === volume_set_properties ===
        // Params: target, weight, priority, is_global, blend_distance
        internal static object SetProperties(JObject @params)
        {
            var p = new ToolParams(@params);
            var volume = GraphicsHelpers.FindVolume(@params);
            if (volume == null)
                return new ErrorResponse("Volume not found.");

            var changed = new List<string>();

            var weight = p.GetFloat("weight");
            if (weight.HasValue) { SetProperty(volume, "weight", weight.Value); changed.Add("weight"); }

            var priority = p.GetFloat("priority");
            if (priority.HasValue) { SetProperty(volume, "priority", priority.Value); changed.Add("priority"); }

            var isGlobal = p.GetBool("is_global", "isGlobal");
            if (isGlobal.HasValue) { SetProperty(volume, "isGlobal", isGlobal.Value); changed.Add("isGlobal"); }

            var blendDist = p.GetFloat("blend_distance", "blendDistance");
            if (blendDist.HasValue) { SetProperty(volume, "blendDistance", blendDist.Value); changed.Add("blendDistance"); }

            if (changed.Count == 0)
                return new ErrorResponse("No properties specified. Use: weight, priority, is_global, blend_distance.");

            GraphicsHelpers.MarkDirty(volume);
            return new
            {
                success = true,
                message = $"Updated Volume properties: {string.Join(", ", changed)}",
                data = new { changed }
            };
        }

        // === volume_list_effects ===
        // No params needed -- lists all available VolumeComponent types
        internal static object ListEffects(JObject @params)
        {
            var types = GraphicsHelpers.GetAvailableEffectTypes();
            var effectList = types.Select(t => new
            {
                name = t.Name,
                fullName = t.FullName,
                ns = t.Namespace
            }).ToList();

            return new
            {
                success = true,
                message = $"Found {effectList.Count} available volume effects.",
                data = new { pipeline = GraphicsHelpers.GetPipelineName(), effects = effectList }
            };
        }

        // === volume_create_profile ===
        // Params: path (string -- asset path like "Assets/Settings/MyProfile.asset")
        internal static object CreateProfile(JObject @params)
        {
            var p = new ToolParams(@params);
            string path = p.Get("path");
            if (string.IsNullOrEmpty(path))
                return new ErrorResponse("'path' parameter is required (e.g., 'Assets/Settings/MyProfile.asset').");

            if (!path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                path += ".asset";

            // Ensure directory exists
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
            {
                // Create folders recursively
                var parts = dir.Replace("\\", "/").Split('/');
                string current = parts[0];
                for (int i = 1; i < parts.Length; i++)
                {
                    string next = current + "/" + parts[i];
                    if (!AssetDatabase.IsValidFolder(next))
                        AssetDatabase.CreateFolder(current, parts[i]);
                    current = next;
                }
            }

            var profile = ScriptableObject.CreateInstance(GraphicsHelpers.VolumeProfileType);
            AssetDatabase.CreateAsset(profile, path);
            AssetDatabase.SaveAssets();

            return new
            {
                success = true,
                message = $"Created VolumeProfile at '{path}'.",
                data = new { path }
            };
        }

        // === ListVolumes (used by VolumesResource) ===
        internal static object ListVolumes(JObject @params)
        {
            if (!GraphicsHelpers.HasVolumeSystem)
                return new { success = true, message = "Volume system not available.", data = new { volumes = new List<object>() } };

            var allVolumes = UnityEngine.Object.FindObjectsOfType(GraphicsHelpers.VolumeType);
            var volumeList = new List<object>();

            foreach (Component vol in allVolumes)
            {
                volumeList.Add(BuildVolumeInfo(vol));
            }

            return new
            {
                success = true,
                message = $"Found {volumeList.Count} volume(s).",
                data = new { pipeline = GraphicsHelpers.GetPipelineName(), volumes = volumeList }
            };
        }

        // --- Helper: Build info object for a single Volume ---
        private static object BuildVolumeInfo(object volumeComponent)
        {
            var comp = volumeComponent as Component;
            if (comp == null) return null;

            bool isGlobal = GetPropertyValue<bool>(volumeComponent, "isGlobal", true);
            float weight = GetPropertyValue<float>(volumeComponent, "weight", 1f);
            float priority = GetPropertyValue<float>(volumeComponent, "priority", 0f);
            float blendDistance = GetPropertyValue<float>(volumeComponent, "blendDistance", 0f);

            var profile = GetProperty(volumeComponent, "sharedProfile");
            string profileName = profile is UnityEngine.Object profileObj ? profileObj.name : null;
            string profilePath = profile is UnityEngine.Object po ? AssetDatabase.GetAssetPath(po) : null;

            var effectsList = new List<object>();
            if (profile != null)
            {
                var components = GetProperty(profile, "components") as System.Collections.IList;
                if (components != null)
                {
                    foreach (var effect in components)
                    {
                        if (effect == null) continue;
                        var effectType = effect.GetType();
                        bool active = GetPropertyValue<bool>(effect, "active", true);

                        // Collect overridden parameters
                        var overriddenParams = new List<string>();
                        foreach (var field in effectType.GetFields(BindingFlags.Public | BindingFlags.Instance))
                        {
                            var fieldValue = field.GetValue(effect);
                            if (fieldValue == null) continue;
                            var overrideProp = fieldValue.GetType().GetProperty("overrideState");
                            if (overrideProp != null)
                            {
                                bool overridden = (bool)overrideProp.GetValue(fieldValue);
                                if (overridden)
                                    overriddenParams.Add(field.Name);
                            }
                        }

                        effectsList.Add(new
                        {
                            type = effectType.Name,
                            active,
                            overridden_params = overriddenParams
                        });
                    }
                }
            }

            return new
            {
                name = comp.gameObject.name,
                instance_id = comp.gameObject.GetInstanceID(),
                is_global = isGlobal,
                weight,
                priority,
                blend_distance = blendDistance,
                profile = profileName,
                profile_path = profilePath ?? "",
                effects = effectsList
            };
        }

        // --- Helper: Set a VolumeParameter field value via reflection ---
        // VolumeParameter<T> has: overrideState (bool), value (T)
        internal static bool SetVolumeParameter(object component, string fieldName, JToken value)
        {
            if (component == null || string.IsNullOrEmpty(fieldName)) return false;

            var field = component.GetType().GetField(fieldName,
                BindingFlags.Public | BindingFlags.Instance);
            if (field == null)
            {
                // Try camelCase conversion from snake_case
                string camelCase = ConvertToCamelCase(fieldName);
                field = component.GetType().GetField(camelCase,
                    BindingFlags.Public | BindingFlags.Instance);
            }
            if (field == null) return false;

            var param = field.GetValue(component);
            if (param == null) return false;

            // Set overrideState = true
            var overrideProp = param.GetType().GetProperty("overrideState");
            if (overrideProp != null)
                overrideProp.SetValue(param, true);

            // Set value with type conversion
            var valueProp = param.GetType().GetProperty("value");
            if (valueProp == null) return false;

            try
            {
                object converted = ConvertToParameterType(value, valueProp.PropertyType);
                valueProp.SetValue(param, converted);
                return true;
            }
            catch (Exception ex)
            {
                McpLog.Warn($"[VolumeOps] Failed to set '{fieldName}': {ex.Message}");
                return false;
            }
        }

        // --- Helper: Convert JToken to target parameter type ---
        private static object ConvertToParameterType(JToken value, Type targetType)
        {
            if (value == null || value.Type == JTokenType.Null) return null;

            // Handle Color
            if (targetType == typeof(Color))
            {
                if (value is JArray arr && arr.Count >= 3)
                {
                    float r = arr[0].Value<float>();
                    float g = arr[1].Value<float>();
                    float b = arr[2].Value<float>();
                    float a = arr.Count >= 4 ? arr[3].Value<float>() : 1f;
                    return new Color(r, g, b, a);
                }
                // Try hex string
                if (value.Type == JTokenType.String)
                {
                    if (ColorUtility.TryParseHtmlString(value.ToString(), out Color c))
                        return c;
                }
            }

            // Handle Vector2
            if (targetType == typeof(Vector2))
            {
                if (value is JArray arr && arr.Count >= 2)
                    return new Vector2(arr[0].Value<float>(), arr[1].Value<float>());
            }

            // Handle Vector3
            if (targetType == typeof(Vector3))
            {
                if (value is JArray arr && arr.Count >= 3)
                    return new Vector3(arr[0].Value<float>(), arr[1].Value<float>(), arr[2].Value<float>());
            }

            // Handle Vector4
            if (targetType == typeof(Vector4))
            {
                if (value is JArray arr && arr.Count >= 4)
                    return new Vector4(arr[0].Value<float>(), arr[1].Value<float>(),
                                      arr[2].Value<float>(), arr[3].Value<float>());
            }

            // Handle enums
            if (targetType.IsEnum)
            {
                string str = value.ToString();
                if (Enum.TryParse(targetType, str, true, out object enumVal))
                    return enumVal;
                // Try as int
                if (int.TryParse(str, out int intVal))
                    return Enum.ToObject(targetType, intVal);
            }

            // Handle bool
            if (targetType == typeof(bool))
                return ParamCoercion.CoerceBool(value, false);

            // Handle float
            if (targetType == typeof(float))
                return ParamCoercion.CoerceFloat(value, 0f);

            // Handle int
            if (targetType == typeof(int))
                return ParamCoercion.CoerceInt(value, 0);

            // Handle Texture2D (by asset path)
            if (targetType == typeof(Texture2D) || targetType == typeof(Texture))
            {
                string path = value.ToString();
                return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }

            // Fallback: try Convert
            try
            {
                return Convert.ChangeType(value.ToObject<object>(), targetType);
            }
            catch
            {
                return value.ToObject<object>();
            }
        }

        private static string ConvertToCamelCase(string snakeCase)
        {
            if (string.IsNullOrEmpty(snakeCase) || !snakeCase.Contains('_'))
                return snakeCase;
            var parts = snakeCase.Split('_');
            return parts[0] + string.Concat(parts.Skip(1).Select(
                p => char.ToUpper(p[0]) + p.Substring(1)));
        }

        // --- Reflection helpers ---
        private static object GetProperty(object obj, string name)
        {
            if (obj == null) return null;
            var prop = obj.GetType().GetProperty(name,
                BindingFlags.Public | BindingFlags.Instance);
            return prop?.GetValue(obj);
        }

        private static T GetPropertyValue<T>(object obj, string name, T defaultValue)
        {
            var val = GetProperty(obj, name);
            if (val is T typed) return typed;
            return defaultValue;
        }

        private static void SetProperty(object obj, string name, object value)
        {
            if (obj == null) return;
            var prop = obj.GetType().GetProperty(name,
                BindingFlags.Public | BindingFlags.Instance);
            if (prop != null && prop.CanWrite)
                prop.SetValue(obj, value);
        }
    }
}
