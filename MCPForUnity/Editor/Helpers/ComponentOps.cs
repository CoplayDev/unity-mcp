using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Low-level component operations extracted from ManageGameObject and ManageComponents.
    /// Provides pure C# operations without JSON parsing or response formatting.
    /// </summary>
    public static class ComponentOps
    {
        /// <summary>
        /// Adds a component to a GameObject with Undo support.
        /// </summary>
        /// <param name="target">The target GameObject</param>
        /// <param name="componentType">The type of component to add</param>
        /// <param name="error">Error message if operation fails</param>
        /// <returns>The added component, or null if failed</returns>
        public static Component AddComponent(GameObject target, Type componentType, out string error)
        {
            error = null;

            if (target == null)
            {
                error = "Target GameObject is null.";
                return null;
            }

            if (componentType == null || !typeof(Component).IsAssignableFrom(componentType))
            {
                error = $"Type '{componentType?.Name ?? "null"}' is not a valid Component type.";
                return null;
            }

            // Prevent adding duplicate Transform
            if (componentType == typeof(Transform))
            {
                error = "Cannot add another Transform component.";
                return null;
            }

            // Check for 2D/3D physics conflicts
            string conflictError = CheckPhysicsConflict(target, componentType);
            if (conflictError != null)
            {
                error = conflictError;
                return null;
            }

            try
            {
                Component newComponent = Undo.AddComponent(target, componentType);
                if (newComponent == null)
                {
                    error = $"Failed to add component '{componentType.Name}' to '{target.name}'. It might be disallowed.";
                    return null;
                }

                // Apply default values for specific component types
                ApplyDefaultValues(newComponent);

                return newComponent;
            }
            catch (Exception ex)
            {
                error = $"Error adding component '{componentType.Name}': {ex.Message}";
                return null;
            }
        }

        /// <summary>
        /// Removes a component from a GameObject with Undo support.
        /// </summary>
        /// <param name="target">The target GameObject</param>
        /// <param name="componentType">The type of component to remove</param>
        /// <param name="error">Error message if operation fails</param>
        /// <returns>True if component was removed successfully</returns>
        public static bool RemoveComponent(GameObject target, Type componentType, out string error)
        {
            error = null;

            if (target == null)
            {
                error = "Target GameObject is null.";
                return false;
            }

            if (componentType == null)
            {
                error = "Component type is null.";
                return false;
            }

            // Prevent removing Transform
            if (componentType == typeof(Transform))
            {
                error = "Cannot remove Transform component.";
                return false;
            }

            Component component = target.GetComponent(componentType);
            if (component == null)
            {
                error = $"Component '{componentType.Name}' not found on '{target.name}'.";
                return false;
            }

            try
            {
                Undo.DestroyObjectImmediate(component);
                return true;
            }
            catch (Exception ex)
            {
                error = $"Error removing component '{componentType.Name}': {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Sets a property value on a component using reflection.
        /// </summary>
        /// <param name="component">The target component</param>
        /// <param name="propertyName">The property or field name</param>
        /// <param name="value">The value to set (JToken)</param>
        /// <param name="error">Error message if operation fails</param>
        /// <returns>True if property was set successfully</returns>
        public static bool SetProperty(Component component, string propertyName, JToken value, out string error)
        {
            error = null;

            if (component == null)
            {
                error = "Component is null.";
                return false;
            }

            if (string.IsNullOrEmpty(propertyName))
            {
                error = "Property name is null or empty.";
                return false;
            }

            Type type = component.GetType();
            BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;
            string normalizedName = ParamCoercion.NormalizePropertyName(propertyName);

            // Try property first - check both original and normalized names for backwards compatibility
            PropertyInfo propInfo = type.GetProperty(propertyName, flags) 
                                 ?? type.GetProperty(normalizedName, flags);
            if (propInfo != null && propInfo.CanWrite)
            {
                try
                {
                    object convertedValue = PropertyConversion.ConvertToType(value, propInfo.PropertyType);
                    // Detect conversion failure: null result when input wasn't null
                    if (convertedValue == null && value.Type != JTokenType.Null)
                    {
                        error = $"Failed to convert value for property '{propertyName}' to type '{propInfo.PropertyType.Name}'.";
                        return false;
                    }
                    propInfo.SetValue(component, convertedValue);
                    return true;
                }
                catch (Exception ex)
                {
                    error = $"Failed to set property '{propertyName}': {ex.Message}";
                    return false;
                }
            }

            // Try field - check both original and normalized names for backwards compatibility
            FieldInfo fieldInfo = type.GetField(propertyName, flags) 
                               ?? type.GetField(normalizedName, flags);
            if (fieldInfo != null && !fieldInfo.IsInitOnly)
            {
                try
                {
                    object convertedValue = PropertyConversion.ConvertToType(value, fieldInfo.FieldType);
                    // Detect conversion failure: null result when input wasn't null
                    if (convertedValue == null && value.Type != JTokenType.Null)
                    {
                        error = $"Failed to convert value for field '{propertyName}' to type '{fieldInfo.FieldType.Name}'.";
                        return false;
                    }
                    fieldInfo.SetValue(component, convertedValue);
                    return true;
                }
                catch (Exception ex)
                {
                    error = $"Failed to set field '{propertyName}': {ex.Message}";
                    return false;
                }
            }

            // Try non-public serialized fields - check both original and normalized names
            BindingFlags privateFlags = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase;
            fieldInfo = type.GetField(propertyName, privateFlags) 
                     ?? type.GetField(normalizedName, privateFlags);
            if (fieldInfo != null && fieldInfo.GetCustomAttribute<SerializeField>() != null)
            {
                try
                {
                    object convertedValue = PropertyConversion.ConvertToType(value, fieldInfo.FieldType);
                    // Detect conversion failure: null result when input wasn't null
                    if (convertedValue == null && value.Type != JTokenType.Null)
                    {
                        error = $"Failed to convert value for serialized field '{propertyName}' to type '{fieldInfo.FieldType.Name}'.";
                        return false;
                    }
                    fieldInfo.SetValue(component, convertedValue);
                    return true;
                }
                catch (Exception ex)
                {
                    error = $"Failed to set serialized field '{propertyName}': {ex.Message}";
                    return false;
                }
            }

            error = $"Property or field '{propertyName}' not found on component '{type.Name}'.";
            return false;
        }

        /// <summary>
        /// Gets all public properties and fields from a component type.
        /// </summary>
        public static List<string> GetAccessibleMembers(Type componentType)
        {
            var members = new List<string>();
            if (componentType == null) return members;

            BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;

            foreach (var prop in componentType.GetProperties(flags))
            {
                if (prop.CanWrite && prop.GetSetMethod() != null)
                {
                    members.Add(prop.Name);
                }
            }

            foreach (var field in componentType.GetFields(flags))
            {
                if (!field.IsInitOnly)
                {
                    members.Add(field.Name);
                }
            }

            // Include private [SerializeField] fields
            foreach (var field in componentType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (field.GetCustomAttribute<SerializeField>() != null)
                {
                    members.Add(field.Name);
                }
            }

            members.Sort();
            return members;
        }

        // --- Private Helpers ---

        private static string CheckPhysicsConflict(GameObject target, Type componentType)
        {
            bool isAdding2DPhysics =
                typeof(Rigidbody2D).IsAssignableFrom(componentType) ||
                typeof(Collider2D).IsAssignableFrom(componentType);

            bool isAdding3DPhysics =
                typeof(Rigidbody).IsAssignableFrom(componentType) ||
                typeof(Collider).IsAssignableFrom(componentType);

            if (isAdding2DPhysics)
            {
                if (target.GetComponent<Rigidbody>() != null || target.GetComponent<Collider>() != null)
                {
                    return $"Cannot add 2D physics component '{componentType.Name}' because the GameObject '{target.name}' already has a 3D Rigidbody or Collider.";
                }
            }
            else if (isAdding3DPhysics)
            {
                if (target.GetComponent<Rigidbody2D>() != null || target.GetComponent<Collider2D>() != null)
                {
                    return $"Cannot add 3D physics component '{componentType.Name}' because the GameObject '{target.name}' already has a 2D Rigidbody or Collider.";
                }
            }

            return null;
        }

        private static void ApplyDefaultValues(Component component)
        {
            // Default newly added Lights to Directional
            if (component is Light light)
            {
                light.type = LightType.Directional;
            }
        }
    }
}

