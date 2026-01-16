using System;
using System.IO;
using UnityEngine;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.ActionTrace.Helpers
{
    /// <summary>
    /// Unified property formatting utilities for ActionTrace events.
    /// Eliminates code duplication between PropertyChangeTracker and SelectionPropertyTracker.
    /// </summary>
    public static class PropertyFormatter
    {
        /// <summary>
        /// Checks if a property is a Unity internal property that should be ignored.
        /// </summary>
        public static bool IsInternalProperty(string propertyPath)
        {
            if (string.IsNullOrEmpty(propertyPath))
                return false;

            return propertyPath.StartsWith("m_Script") ||
                   propertyPath.StartsWith("m_EditorClassIdentifier") ||
                   propertyPath.StartsWith("m_ObjectHideFlags");
        }

        /// <summary>
        /// Formats a property value for JSON storage.
        /// Uses UnityJsonSerializer.Instance for proper Unity type serialization.
        /// </summary>
        public static string FormatPropertyValue(object value)
        {
            if (value == null)
                return "null";

            try
            {
                using (var writer = new StringWriter())
                {
                    UnityJsonSerializer.Instance.Serialize(writer, value);
                    return writer.ToString();
                }
            }
            catch (Exception)
            {
                return value.ToString();
            }
        }

        /// <summary>
        /// Gets the type name of a property value for the event payload.
        /// Uses friendly names for common Unity types.
        /// </summary>
        public static string GetPropertyTypeName(object value)
        {
            if (value == null)
                return "null";

            Type type = value.GetType();

            // Number types
            if (type == typeof(float) || type == typeof(int) || type == typeof(double))
                return "Number";
            if (type == typeof(bool))
                return "Boolean";
            if (type == typeof(string))
                return "String";

            // Unity types
            if (type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Vector4))
                return type.Name;
            if (type == typeof(Quaternion))
                return "Quaternion";
            if (type == typeof(Color))
                return "Color";
            if (type == typeof(Rect))
                return "Rect";
            if (type == typeof(Bounds))
                return "Bounds";
            if (type == typeof(Vector2Int))
                return "Vector2Int";
            if (type == typeof(Vector3Int))
                return "Vector3Int";

            return type.Name;
        }
    }
}
