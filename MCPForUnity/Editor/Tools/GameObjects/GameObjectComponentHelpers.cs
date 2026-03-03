#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using MCPForUnity.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.GameObjects
{
    internal static class GameObjectComponentHelpers
    {
        internal static object AddComponentInternal(GameObject targetGo, string typeName, JObject properties)
        {
            Type componentType = FindType(typeName);
            if (componentType == null)
            {
                return new ErrorResponse($"Component type '{typeName}' not found or is not a valid Component.");
            }
            if (!typeof(Component).IsAssignableFrom(componentType))
            {
                return new ErrorResponse($"Type '{typeName}' is not a Component.");
            }

            if (componentType == typeof(Transform))
            {
                return new ErrorResponse("Cannot add another Transform component.");
            }

            bool isAdding2DPhysics = typeof(Rigidbody2D).IsAssignableFrom(componentType) || typeof(Collider2D).IsAssignableFrom(componentType);
            bool isAdding3DPhysics = typeof(Rigidbody).IsAssignableFrom(componentType) || typeof(Collider).IsAssignableFrom(componentType);

            if (isAdding2DPhysics)
            {
                if (targetGo.GetComponent<Rigidbody>() != null || targetGo.GetComponent<Collider>() != null)
                {
                    return new ErrorResponse($"Cannot add 2D physics component '{typeName}' because the GameObject '{targetGo.name}' already has a 3D Rigidbody or Collider.");
                }
            }
            else if (isAdding3DPhysics)
            {
                if (targetGo.GetComponent<Rigidbody2D>() != null || targetGo.GetComponent<Collider2D>() != null)
                {
                    return new ErrorResponse($"Cannot add 3D physics component '{typeName}' because the GameObject '{targetGo.name}' already has a 2D Rigidbody or Collider.");
                }
            }

            Component existingComponent = targetGo.GetComponent(componentType);
            if (existingComponent != null && !AllowsMultiple(componentType))
            {
                return new ErrorResponse(
                    $"Component '{typeName}' already exists on '{targetGo.name}' and this type does not allow multiple instances."
                );
            }

            try
            {
                Component newComponent = Undo.AddComponent(targetGo, componentType);
                if (newComponent == null)
                {
                    if (targetGo.GetComponent(componentType) != null && !AllowsMultiple(componentType))
                    {
                        return new ErrorResponse(
                            $"Component '{typeName}' already exists on '{targetGo.name}' and this type does not allow multiple instances."
                        );
                    }

                    return new ErrorResponse(
                        $"Failed to add component '{typeName}' to '{targetGo.name}'. Unity may restrict this component on the current target."
                    );
                }

                if (newComponent is Light light)
                {
                    light.type = LightType.Directional;
                }

                if (properties != null)
                {
                    var setResult = SetComponentPropertiesInternal(targetGo, typeName, properties, newComponent);
                    if (setResult != null)
                    {
                        Undo.DestroyObjectImmediate(newComponent);
                        return setResult;
                    }
                }

                return null;
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error adding component '{typeName}' to '{targetGo.name}': {e.Message}");
            }
        }

        private static bool AllowsMultiple(Type componentType)
        {
            if (componentType == null)
            {
                return false;
            }

            return !Attribute.IsDefined(componentType, typeof(DisallowMultipleComponent), inherit: true);
        }

        internal static object RemoveComponentInternal(GameObject targetGo, string typeName)
        {
            if (targetGo == null)
            {
                return new ErrorResponse("Target GameObject is null.");
            }

            Type componentType = FindType(typeName);
            if (componentType == null)
            {
                return new ErrorResponse($"Component type '{typeName}' not found for removal.");
            }

            if (componentType == typeof(Transform))
            {
                return new ErrorResponse("Cannot remove the Transform component.");
            }

            Component componentToRemove = targetGo.GetComponent(componentType);
            if (componentToRemove == null)
            {
                return new ErrorResponse($"Component '{typeName}' not found on '{targetGo.name}' to remove.");
            }

            try
            {
                Undo.DestroyObjectImmediate(componentToRemove);
                return null;
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error removing component '{typeName}' from '{targetGo.name}': {e.Message}");
            }
        }

        internal static object SetComponentPropertiesInternal(GameObject targetGo, string componentTypeName, JObject properties, Component targetComponentInstance = null)
        {
            Component targetComponent = targetComponentInstance;
            if (targetComponent == null)
            {
                if (ComponentResolver.TryResolve(componentTypeName, out var compType, out var compError))
                {
                    targetComponent = targetGo.GetComponent(compType);
                }
                else
                {
                    targetComponent = targetGo.GetComponent(componentTypeName);
                }
            }
            if (targetComponent == null)
            {
                return new ErrorResponse($"Component '{componentTypeName}' not found on '{targetGo.name}' to set properties.");
            }

            Undo.RecordObject(targetComponent, "Set Component Properties");

            var failures = new List<string>();
            foreach (var prop in properties.Properties())
            {
                string propName = prop.Name;
                JToken propValue = prop.Value;

                try
                {
                    bool setResult;
                    string setError;

                    // Nested paths (e.g. "transform.position") need local handling
                    // since ComponentOps doesn't support dot/bracket notation.
                    if (propName.Contains('.') || propName.Contains('['))
                    {
                        setResult = SetNestedProperty(targetComponent, propName, propValue, InputSerializer, out setError);
                    }
                    else
                    {
                        // ComponentOps handles reflection + SerializedProperty fallback
                        setResult = ComponentOps.SetProperty(targetComponent, propName, propValue, out setError);
                    }

                    if (!setResult)
                    {
                        string msg = setError;
                        if (msg == null || msg.Contains("not found"))
                        {
                            var availableProperties = ComponentResolver.GetAllComponentProperties(targetComponent.GetType());
                            var suggestions = ComponentResolver.GetFuzzyPropertySuggestions(propName, availableProperties);
                            msg = suggestions.Any()
                                ? $"Property '{propName}' not found. Did you mean: {string.Join(", ", suggestions)}? Available: [{string.Join(", ", availableProperties)}]"
                                : $"Property '{propName}' not found. Available: [{string.Join(", ", availableProperties)}]";
                        }
                        McpLog.Warn($"[ManageGameObject] {msg}");
                        failures.Add(msg);
                    }
                }
                catch (Exception e)
                {
                    McpLog.Error($"[ManageGameObject] Error setting property '{propName}' on '{componentTypeName}': {e.Message}");
                    failures.Add($"Error setting '{propName}': {e.Message}");
                }
            }

            EditorUtility.SetDirty(targetComponent);
            return failures.Count == 0
                ? null
                : new ErrorResponse($"One or more properties failed on '{componentTypeName}'.", new { errors = failures });
        }

        private static JsonSerializer InputSerializer => UnityJsonSerializer.Instance;

        private static bool SetNestedProperty(object target, string path, JToken value, JsonSerializer inputSerializer, out string error)
        {
            error = null;
            try
            {
                string[] pathParts = SplitPropertyPath(path);
                if (pathParts.Length == 0)
                {
                    error = $"Invalid nested property path '{path}'.";
                    return false;
                }

                object currentObject = target;
                Type currentType = currentObject.GetType();
                BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

                for (int i = 0; i < pathParts.Length - 1; i++)
                {
                    string part = pathParts[i];
                    bool isArray = false;
                    int arrayIndex = -1;

                    if (part.Contains("["))
                    {
                        int startBracket = part.IndexOf('[');
                        int endBracket = part.IndexOf(']');
                        if (startBracket > 0 && endBracket > startBracket)
                        {
                            string indexStr = part.Substring(startBracket + 1, endBracket - startBracket - 1);
                            if (int.TryParse(indexStr, out arrayIndex))
                            {
                                isArray = true;
                                part = part.Substring(0, startBracket);
                            }
                        }
                    }

                    PropertyInfo propInfo = currentType.GetProperty(part, flags);
                    FieldInfo fieldInfo = null;
                    if (propInfo == null)
                    {
                        fieldInfo = currentType.GetField(part, flags);
                        if (fieldInfo == null)
                        {
                            error = $"Could not find property or field '{part}' on type '{currentType.Name}' in path '{path}'.";
                            return false;
                        }
                    }

                    currentObject = propInfo != null ? propInfo.GetValue(currentObject) : fieldInfo.GetValue(currentObject);
                    if (currentObject == null)
                    {
                        error = $"Property '{part}' is null in path '{path}', cannot access nested properties.";
                        return false;
                    }

                    if (isArray)
                    {
                        if (currentObject is Material[])
                        {
                            var materials = currentObject as Material[];
                            if (materials.Length == 0)
                            {
                                error = $"Material array is empty in path '{path}', cannot access index {arrayIndex}.";
                                return false;
                            }
                            if (arrayIndex < 0 || arrayIndex >= materials.Length)
                            {
                                error = $"Material index {arrayIndex} out of range (0-{materials.Length - 1}) in path '{path}'.";
                                return false;
                            }
                            currentObject = materials[arrayIndex];
                        }
                        else if (currentObject is System.Collections.IList)
                        {
                            var list = currentObject as System.Collections.IList;
                            if (list.Count == 0)
                            {
                                error = $"List is empty in path '{path}', cannot access index {arrayIndex}.";
                                return false;
                            }
                            if (arrayIndex < 0 || arrayIndex >= list.Count)
                            {
                                error = $"Index {arrayIndex} out of range (0-{list.Count - 1}) in path '{path}'.";
                                return false;
                            }
                            currentObject = list[arrayIndex];
                        }
                        else
                        {
                            error = $"Property '{part}' is not an array or list in path '{path}', cannot access by index.";
                            return false;
                        }
                    }

                    currentType = currentObject.GetType();
                }

                string finalPart = pathParts[pathParts.Length - 1];

                if (currentObject is Material material && finalPart.StartsWith("_"))
                {
                    if (!MaterialOps.TrySetShaderProperty(material, finalPart, value, inputSerializer))
                    {
                        error = $"Failed to set shader property '{finalPart}' on material '{material.name}' in path '{path}'.";
                        return false;
                    }
                    return true;
                }

                PropertyInfo finalPropInfo = currentType.GetProperty(finalPart, flags);
                if (finalPropInfo != null && finalPropInfo.CanWrite)
                {
                    object convertedValue = ConvertJTokenToType(value, finalPropInfo.PropertyType, inputSerializer);
                    if (convertedValue != null || value.Type == JTokenType.Null)
                    {
                        finalPropInfo.SetValue(currentObject, convertedValue);
                        return true;
                    }
                    error = $"Failed to convert value for '{finalPart}' to type '{finalPropInfo.PropertyType.Name}' in path '{path}'.";
                    return false;
                }

                FieldInfo finalFieldInfo = currentType.GetField(finalPart, flags);
                if (finalFieldInfo != null)
                {
                    object convertedValue = ConvertJTokenToType(value, finalFieldInfo.FieldType, inputSerializer);
                    if (convertedValue != null || value.Type == JTokenType.Null)
                    {
                        finalFieldInfo.SetValue(currentObject, convertedValue);
                        return true;
                    }
                    error = $"Failed to convert value for '{finalPart}' to type '{finalFieldInfo.FieldType.Name}' in path '{path}'.";
                    return false;
                }

                // Try non-public [SerializeField] fields (nested paths need this too)
                FieldInfo serializedField = ComponentOps.FindSerializedFieldInHierarchy(currentType, finalPart);
                if (serializedField != null)
                {
                    object convertedValue = ConvertJTokenToType(value, serializedField.FieldType, inputSerializer);
                    if (convertedValue != null || value.Type == JTokenType.Null)
                    {
                        serializedField.SetValue(currentObject, convertedValue);
                        return true;
                    }
                    error = $"Failed to convert value for '{finalPart}' to type '{serializedField.FieldType.Name}' in path '{path}'.";
                    return false;
                }

                error = $"Property or field '{finalPart}' not found on type '{currentType.Name}' in path '{path}'.";
            }
            catch (Exception ex)
            {
                error = $"Error setting nested property '{path}': {ex.Message}";
            }

            return false;
        }

        private static string[] SplitPropertyPath(string path)
        {
            List<string> parts = new List<string>();
            int startIndex = 0;
            bool inBrackets = false;

            for (int i = 0; i < path.Length; i++)
            {
                char c = path[i];

                if (c == '[')
                {
                    inBrackets = true;
                }
                else if (c == ']')
                {
                    inBrackets = false;
                }
                else if (c == '.' && !inBrackets)
                {
                    parts.Add(path.Substring(startIndex, i - startIndex));
                    startIndex = i + 1;
                }
            }
            if (startIndex < path.Length)
            {
                parts.Add(path.Substring(startIndex));
            }
            return parts.ToArray();
        }

        private static object ConvertJTokenToType(JToken token, Type targetType, JsonSerializer inputSerializer)
        {
            return PropertyConversion.ConvertToType(token, targetType);
        }

        private static Type FindType(string typeName)
        {
            if (ComponentResolver.TryResolve(typeName, out Type resolvedType, out string error))
            {
                return resolvedType;
            }

            if (!string.IsNullOrEmpty(error))
            {
                McpLog.Warn($"[FindType] {error}");
            }

            return null;
        }
    }
}
