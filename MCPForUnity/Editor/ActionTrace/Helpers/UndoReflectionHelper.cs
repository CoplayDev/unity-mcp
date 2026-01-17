using System;
using UnityEditor;

namespace MCPForUnity.Editor.ActionTrace.Helpers
{
    /// <summary>
    /// Shared reflection helpers for extracting data from Unity's UndoPropertyModification.
    /// This class centralizes reflection logic that was duplicated across PropertyChangeTracker and SelectionPropertyTracker.
    /// </summary>
    public static class UndoReflectionHelper
    {
        /// <summary>
        /// Generic reflection helper to extract nested values from UndoPropertyModification.
        /// Traverses dot-separated property paths like "propertyModification.target".
        ///
        /// Handles both Property and Field access, providing flexibility for Unity's internal structure variations.
        /// </summary>
        /// <param name="root">The root object to start traversal from (typically UndoPropertyModification)</param>
        /// <param name="path">Dot-separated path to desired value (e.g., "propertyModification.target")</param>
        /// <returns>The extracted value, or null if any part of path cannot be resolved</returns>
        public static object GetNestedValue(object root, string path)
        {
            if (root == null || string.IsNullOrEmpty(path))
                return null;

            var parts = path.Split('.');
            object current = root;

            foreach (var part in parts)
            {
                if (current == null) return null;

                // Try property first (for currentValue, previousValue)
                var prop = current.GetType().GetProperty(part);
                if (prop != null)
                {
                    current = prop.GetValue(current);
                    continue;
                }

                // Try field (for propertyModification, target, value, etc.)
                var field = current.GetType().GetField(part);
                if (field != null)
                {
                    current = field.GetValue(current);
                    continue;
                }

                return null;
            }

            return current;
        }

        /// <summary>
        /// Extracts target object from an UndoPropertyModification.
        /// The target is UnityEngine.Object being modified (e.g., a Component or GameObject).
        /// </summary>
        public static UnityEngine.Object GetTarget(UndoPropertyModification undoMod)
        {
            // Try direct 'currentValue.target' path
            var result = GetNestedValue(undoMod, "currentValue.target");
            if (result is UnityEngine.Object obj) return obj;

            // Fallback to 'previousValue.target'
            result = GetNestedValue(undoMod, "previousValue.target");
            if (result is UnityEngine.Object obj2) return obj2;

            return null;
        }

        /// <summary>
        /// Extracts property path from an UndoPropertyModification.
        /// The property path identifies which property was modified (e.g., "m_Intensity").
        /// </summary>
        public static string GetPropertyPath(UndoPropertyModification undoMod)
        {
            var result = GetNestedValue(undoMod, "currentValue.propertyPath");
            if (result != null) return result as string;

            result = GetNestedValue(undoMod, "previousValue.propertyPath");
            return result as string;
        }

        /// <summary>
        /// Extracts current (new) value from an UndoPropertyModification.
        /// This is value after modification was applied.
        /// </summary>
        public static object GetCurrentValue(UndoPropertyModification undoMod)
        {
            // Try direct 'currentValue.value' path
            var result = GetNestedValue(undoMod, "currentValue.value");
            if (result != null) return result;

            return GetNestedValue(undoMod, "currentValue");
        }

        /// <summary>
        /// Extracts previous (old) value from an UndoPropertyModification.
        /// This is value before modification was applied.
        /// </summary>
        public static object GetPreviousValue(UndoPropertyModification undoMod)
        {
            // Try direct 'previousValue' property first
            var result = GetNestedValue(undoMod, "previousValue");
            if (result != null) return result;

            // Try 'previousValue.value' (nested structure)
            result = GetNestedValue(undoMod, "previousValue.value");
            if (result != null) return result;

            // Some Unity versions use 'propertyModification.value.before'
            return GetNestedValue(undoMod, "propertyModification.value.before");
        }
    }
}
