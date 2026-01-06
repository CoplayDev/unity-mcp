using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Tool for managing components on GameObjects.
    /// Actions: add, remove, set_property
    /// 
    /// This is a focused tool for component lifecycle operations.
    /// For reading component data, use the unity://scene/gameobject/{id}/components resource.
    /// </summary>
    [McpForUnityTool("manage_components")]
    public static class ManageComponents
    {
        /// <summary>
        /// Handles the manage_components command.
        /// </summary>
        /// <param name="params">Command parameters</param>
        /// <returns>Result of the component operation</returns>
        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
            {
                return new ErrorResponse("Parameters cannot be null.");
            }

            string action = ParamCoercion.CoerceString(@params["action"], null)?.ToLowerInvariant();
            if (string.IsNullOrEmpty(action))
            {
                return new ErrorResponse("'action' parameter is required (add, remove, set_property).");
            }

            // Target resolution
            JToken targetToken = @params["target"];
            string searchMethod = ParamCoercion.CoerceString(@params["searchMethod"] ?? @params["search_method"], null);

            if (targetToken == null)
            {
                return new ErrorResponse("'target' parameter is required.");
            }

            try
            {
                return action switch
                {
                    "add" => AddComponent(@params, targetToken, searchMethod),
                    "remove" => RemoveComponent(@params, targetToken, searchMethod),
                    "set_property" => SetProperty(@params, targetToken, searchMethod),
                    _ => new ErrorResponse($"Unknown action: '{action}'. Supported actions: add, remove, set_property")
                };
            }
            catch (Exception e)
            {
                Debug.LogError($"[ManageComponents] Action '{action}' failed: {e}");
                return new ErrorResponse($"Internal error processing action '{action}': {e.Message}");
            }
        }

        #region Action Implementations

        private static object AddComponent(JObject @params, JToken targetToken, string searchMethod)
        {
            GameObject targetGo = FindTarget(targetToken, searchMethod);
            if (targetGo == null)
            {
                return new ErrorResponse($"Target GameObject ('{targetToken}') not found using method '{searchMethod ?? "default"}'.");
            }

            string componentType = ParamCoercion.CoerceString(@params["componentType"] ?? @params["component_type"], null);
            if (string.IsNullOrEmpty(componentType))
            {
                return new ErrorResponse("'componentType' parameter is required for 'add' action.");
            }

            // Resolve component type
            Type type = FindComponentType(componentType);
            if (type == null)
            {
                return new ErrorResponse($"Component type '{componentType}' not found. Use a fully-qualified name if needed.");
            }

            // Optional properties to set on the new component
            JObject properties = @params["properties"] as JObject ?? @params["componentProperties"] as JObject;

            try
            {
                // Undo.AddComponent creates its own undo record, no need for RecordObject
                Component newComponent = Undo.AddComponent(targetGo, type);

                if (newComponent == null)
                {
                    return new ErrorResponse($"Failed to add component '{componentType}' to '{targetGo.name}'.");
                }

                // Set properties if provided
                if (properties != null && properties.HasValues)
                {
                    SetPropertiesOnComponent(newComponent, properties);
                }

                EditorUtility.SetDirty(targetGo);

                return new
                {
                    success = true,
                    message = $"Component '{componentType}' added to '{targetGo.name}'.",
                    data = new
                    {
                        instanceID = targetGo.GetInstanceID(),
                        componentType = type.FullName,
                        componentInstanceID = newComponent.GetInstanceID()
                    }
                };
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error adding component '{componentType}': {e.Message}");
            }
        }

        private static object RemoveComponent(JObject @params, JToken targetToken, string searchMethod)
        {
            GameObject targetGo = FindTarget(targetToken, searchMethod);
            if (targetGo == null)
            {
                return new ErrorResponse($"Target GameObject ('{targetToken}') not found using method '{searchMethod ?? "default"}'.");
            }

            string componentType = ParamCoercion.CoerceString(@params["componentType"] ?? @params["component_type"], null);
            if (string.IsNullOrEmpty(componentType))
            {
                return new ErrorResponse("'componentType' parameter is required for 'remove' action.");
            }

            // Resolve component type
            Type type = FindComponentType(componentType);
            if (type == null)
            {
                return new ErrorResponse($"Component type '{componentType}' not found.");
            }

            // Prevent removal of Transform (check early before GetComponent)
            if (type == typeof(Transform))
            {
                return new ErrorResponse("Cannot remove the Transform component.");
            }

            Component component = targetGo.GetComponent(type);
            if (component == null)
            {
                return new ErrorResponse($"Component '{componentType}' not found on '{targetGo.name}'.");
            }

            try
            {
                Undo.DestroyObjectImmediate(component);
                EditorUtility.SetDirty(targetGo);

                return new
                {
                    success = true,
                    message = $"Component '{componentType}' removed from '{targetGo.name}'.",
                    data = new
                    {
                        instanceID = targetGo.GetInstanceID()
                    }
                };
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error removing component '{componentType}': {e.Message}");
            }
        }

        private static object SetProperty(JObject @params, JToken targetToken, string searchMethod)
        {
            GameObject targetGo = FindTarget(targetToken, searchMethod);
            if (targetGo == null)
            {
                return new ErrorResponse($"Target GameObject ('{targetToken}') not found using method '{searchMethod ?? "default"}'.");
            }

            string componentType = ParamCoercion.CoerceString(@params["componentType"] ?? @params["component_type"], null);
            if (string.IsNullOrEmpty(componentType))
            {
                return new ErrorResponse("'componentType' parameter is required for 'set_property' action.");
            }

            // Resolve component type
            Type type = FindComponentType(componentType);
            if (type == null)
            {
                return new ErrorResponse($"Component type '{componentType}' not found.");
            }

            Component component = targetGo.GetComponent(type);
            if (component == null)
            {
                return new ErrorResponse($"Component '{componentType}' not found on '{targetGo.name}'.");
            }

            // Get property and value
            string propertyName = ParamCoercion.CoerceString(@params["property"], null);
            JToken valueToken = @params["value"];

            // Support both single property or properties object
            JObject properties = @params["properties"] as JObject;

            if (string.IsNullOrEmpty(propertyName) && (properties == null || !properties.HasValues))
            {
                return new ErrorResponse("Either 'property'+'value' or 'properties' object is required for 'set_property' action.");
            }

            var errors = new List<string>();

            try
            {
                Undo.RecordObject(component, $"Set property on {componentType}");

                if (!string.IsNullOrEmpty(propertyName) && valueToken != null)
                {
                    // Single property mode
                    var error = TrySetProperty(component, propertyName, valueToken);
                    if (error != null)
                    {
                        errors.Add(error);
                    }
                }

                if (properties != null && properties.HasValues)
                {
                    // Multiple properties mode
                    foreach (var prop in properties.Properties())
                    {
                        var error = TrySetProperty(component, prop.Name, prop.Value);
                        if (error != null)
                        {
                            errors.Add(error);
                        }
                    }
                }

                EditorUtility.SetDirty(component);

                if (errors.Count > 0)
                {
                    return new
                    {
                        success = false,
                        message = $"Some properties failed to set on '{componentType}'.",
                        data = new
                        {
                            instanceID = targetGo.GetInstanceID(),
                            errors = errors
                        }
                    };
                }

                return new
                {
                    success = true,
                    message = $"Properties set on component '{componentType}' on '{targetGo.name}'.",
                    data = new
                    {
                        instanceID = targetGo.GetInstanceID()
                    }
                };
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error setting properties on component '{componentType}': {e.Message}");
            }
        }

        #endregion

        #region Helpers

        private static GameObject FindTarget(JToken targetToken, string searchMethod)
        {
            if (targetToken == null)
                return null;

            // Try instance ID first
            if (targetToken.Type == JTokenType.Integer)
            {
                int instanceId = targetToken.Value<int>();
                return GameObjectLookup.FindById(instanceId);
            }

            string targetStr = targetToken.ToString();

            // Try parsing as instance ID
            if (int.TryParse(targetStr, out int parsedId))
            {
                var byId = GameObjectLookup.FindById(parsedId);
                if (byId != null)
                    return byId;
            }

            // Use GameObjectLookup for search
            return GameObjectLookup.FindByTarget(targetToken, searchMethod ?? "by_name", true);
        }

        /// <summary>
        /// Finds a component type by name. Delegates to GameObjectLookup.FindComponentType.
        /// </summary>
        private static Type FindComponentType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return null;
            return GameObjectLookup.FindComponentType(typeName);
        }

        private static void SetPropertiesOnComponent(Component component, JObject properties)
        {
            if (component == null || properties == null)
                return;

            var errors = new List<string>();
            foreach (var prop in properties.Properties())
            {
                var error = TrySetProperty(component, prop.Name, prop.Value);
                if (error != null)
                    errors.Add(error);
            }
            
            if (errors.Count > 0)
            {
                Debug.LogWarning($"[ManageComponents] Some properties failed to set on {component.GetType().Name}: {string.Join(", ", errors)}");
            }
        }

        /// <summary>
        /// Attempts to set a property or field on a component.
        /// Note: Property/field lookup is case-insensitive for better usability with external callers.
        /// </summary>
        private static string TrySetProperty(Component component, string propertyName, JToken value)
        {
            if (component == null || string.IsNullOrEmpty(propertyName))
                return $"Invalid component or property name";

            var type = component.GetType();

            // Try property first
            var propInfo = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (propInfo != null && propInfo.CanWrite)
            {
                try
                {
                    var convertedValue = ConvertValue(value, propInfo.PropertyType);
                    propInfo.SetValue(component, convertedValue);
                    return null; // Success
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[ManageComponents] Failed to set property '{propertyName}': {e.Message}");
                    return $"Failed to set property '{propertyName}': {e.Message}";
                }
            }

            // Try field
            var fieldInfo = type.GetField(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (fieldInfo != null)
            {
                try
                {
                    var convertedValue = ConvertValue(value, fieldInfo.FieldType);
                    fieldInfo.SetValue(component, convertedValue);
                    return null; // Success
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[ManageComponents] Failed to set field '{propertyName}': {e.Message}");
                    return $"Failed to set field '{propertyName}': {e.Message}";
                }
            }

            Debug.LogWarning($"[ManageComponents] Property or field '{propertyName}' not found on {type.Name}");
            return $"Property '{propertyName}' not found on {type.Name}";
        }

        private static object ConvertValue(JToken token, Type targetType)
        {
            if (token == null || token.Type == JTokenType.Null)
                return null;

            // Handle Unity types
            if (targetType == typeof(Vector3))
            {
                return VectorParsing.ParseVector3OrDefault(token);
            }
            if (targetType == typeof(Vector2))
            {
                return VectorParsing.ParseVector2(token) ?? Vector2.zero;
            }
            if (targetType == typeof(Quaternion))
            {
                return VectorParsing.ParseQuaternion(token) ?? Quaternion.identity;
            }
            if (targetType == typeof(Color))
            {
                return VectorParsing.ParseColor(token) ?? Color.white;
            }

            // Use Newtonsoft for other types
            return token.ToObject(targetType);
        }

        #endregion
    }
}

