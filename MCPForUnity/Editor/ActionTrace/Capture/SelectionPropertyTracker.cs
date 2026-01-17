using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json;
using MCPForUnity.Editor.ActionTrace.Core;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.ActionTrace.Helpers;

namespace MCPForUnity.Editor.ActionTrace.Capture
{
    /// <summary>
    /// Tracks property modifications made to the currently selected object.
    ///
    /// Combines Selection.selectionChanged with Undo.postprocessModifications
    /// to provide rich context about which object's properties are being modified.
    ///
    /// Key features:
    /// - Detects if property modification targets the currently selected object
    /// - Records SelectionPropertyModified events with selection context
    /// - Reuses existing helpers (GlobalIdHelper, UnityJsonSerializer)
    /// - Lightweight event-based design (no polling)
    /// </summary>
    [InitializeOnLoad]
    public static class SelectionPropertyTracker
    {
        // Current selection state
        private static string _currentSelectionGlobalId;
        private static string _currentSelectionName;
        private static string _currentSelectionType;
        private static string _currentSelectionPath;

        static SelectionPropertyTracker()
        {
            // Initialize with current selection
            UpdateSelectionState();

            // Monitor selection changes
            Selection.selectionChanged += OnSelectionChanged;

            // Monitor property modifications
            Undo.postprocessModifications += OnPropertyModified;

            McpLog.Debug("[SelectionPropertyTracker] Initialized");
        }

        /// <summary>
        /// Updates the cached selection state when selection changes.
        /// </summary>
        private static void OnSelectionChanged()
        {
            UpdateSelectionState();
        }

        /// <summary>
        /// Updates the cached selection state from current Selection.activeObject.
        /// </summary>
        private static void UpdateSelectionState()
        {
            var activeObject = Selection.activeObject;
            if (activeObject == null)
            {
                _currentSelectionGlobalId = null;
                _currentSelectionName = null;
                _currentSelectionType = null;
                _currentSelectionPath = null;
                return;
            }

            _currentSelectionGlobalId = GlobalIdHelper.ToGlobalIdString(activeObject);
            _currentSelectionName = activeObject.name;
            _currentSelectionType = activeObject.GetType().Name;

            // Get path for GameObject/Component selections
            if (activeObject is GameObject go)
            {
                _currentSelectionPath = GetGameObjectPath(go);
            }
            else if (activeObject is Component comp)
            {
                _currentSelectionPath = GetGameObjectPath(comp.gameObject);
            }
            else
            {
                _currentSelectionPath = AssetDatabase.GetAssetPath(activeObject);
            }
        }

        /// <summary>
        /// Called by Unity when properties are modified via Undo system.
        /// Checks if the modification targets the currently selected object.
        /// </summary>
        private static UndoPropertyModification[] OnPropertyModified(UndoPropertyModification[] modifications)
        {
            if (modifications == null || modifications.Length == 0)
                return modifications;

            McpLog.Debug($"[SelectionPropertyTracker] OnPropertyModified: {modifications.Length} mods, selectionId={_currentSelectionGlobalId}");

            // Skip if no valid selection
            if (string.IsNullOrEmpty(_currentSelectionGlobalId))
                return modifications;

            foreach (var undoMod in modifications)
            {
                var target = GetTargetFromUndoMod(undoMod);
                if (target == null)
                {
                    continue;
                }

                // Check if this modification targets the currently selected object or its components
                string targetGlobalId = GlobalIdHelper.ToGlobalIdString(target);
                bool isMatch = IsTargetMatchSelection(target, targetGlobalId);
                // McpLog.Debug($"[SelectionPropertyTracker] targetId={targetGlobalId}, selectionId={_currentSelectionGlobalId}, match={isMatch}");
                if (!isMatch)
                    continue;

                var propertyPath = GetPropertyPathFromUndoMod(undoMod);
                if (string.IsNullOrEmpty(propertyPath))
                    continue;

                // Filter out Unity internal properties
                if (IsInternalProperty(propertyPath))
                    continue;

                // Record the SelectionPropertyModified event
                // McpLog.Debug($"[SelectionPropertyTracker] MATCH! Recording event for {target.name}.{propertyPath}");
                RecordSelectionPropertyModified(undoMod, target, targetGlobalId, propertyPath);
            }

            return modifications;
        }

        /// <summary>
        /// Records a SelectionPropertyModified event to the ActionTrace EventStore.
        /// </summary>
        private static void RecordSelectionPropertyModified(UndoPropertyModification undoMod, UnityEngine.Object target, string targetGlobalId, string propertyPath)
        {
            var currentValue = GetCurrentValueFromUndoMod(undoMod);
            var prevValue = GetPreviousValueFromUndoMod(undoMod);

            var payload = new Dictionary<string, object>
            {
                ["target_name"] = target.name,
                ["component_type"] = target.GetType().Name,
                ["property_path"] = propertyPath,
                ["start_value"] = FormatPropertyValue(prevValue),
                ["end_value"] = FormatPropertyValue(currentValue),
                ["value_type"] = GetPropertyTypeName(currentValue),
                ["selection_context"] = new Dictionary<string, object>
                {
                    ["selection_id"] = _currentSelectionGlobalId,
                    ["selection_name"] = _currentSelectionName,
                    ["selection_type"] = _currentSelectionType,
                    ["selection_path"] = _currentSelectionPath ?? string.Empty
                }
            };

            var evt = new EditorEvent(
                sequence: 0,
                timestampUnixMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                type: EventTypes.SelectionPropertyModified,
                targetId: targetGlobalId,
                payload: payload
            );

            EventStore.Record(evt);
        }

        #region UndoPropertyModification Helpers (via UndoReflectionHelper)

        /// <summary>
        /// Extracts the target object from UndoPropertyModification.
        /// Uses shared UndoReflectionHelper for reflection logic.
        /// </summary>
        private static UnityEngine.Object GetTargetFromUndoMod(UndoPropertyModification undoMod)
        {
            return UndoReflectionHelper.GetTarget(undoMod);
        }

        /// <summary>
        /// Extracts the property path from UndoPropertyModification.
        /// Uses shared UndoReflectionHelper for reflection logic.
        /// </summary>
        private static string GetPropertyPathFromUndoMod(UndoPropertyModification undoMod)
        {
            return UndoReflectionHelper.GetPropertyPath(undoMod);
        }

        /// <summary>
        /// Extracts the current value from UndoPropertyModification.
        /// Uses shared UndoReflectionHelper for reflection logic.
        /// </summary>
        private static object GetCurrentValueFromUndoMod(UndoPropertyModification undoMod)
        {
            return UndoReflectionHelper.GetCurrentValue(undoMod);
        }

        /// <summary>
        /// Extracts the previous value from UndoPropertyModification.
        /// Uses shared UndoReflectionHelper for reflection logic.
        /// </summary>
        private static object GetPreviousValueFromUndoMod(UndoPropertyModification undoMod)
        {
            return UndoReflectionHelper.GetPreviousValue(undoMod);
        }

        #endregion

        /// <summary>
        /// Checks if the modified target matches the current selection.
        /// Handles both direct GameObject matches and Component-on-selected-GameObject matches.
        /// </summary>
        private static bool IsTargetMatchSelection(UnityEngine.Object target, string targetGlobalId)
        {
            // Direct match
            if (targetGlobalId == _currentSelectionGlobalId)
                return true;

            // If target is a Component, check if its owner GameObject matches the selection
            if (target is Component comp)
            {
                string gameObjectId = GlobalIdHelper.ToGlobalIdString(comp.gameObject);
                if (gameObjectId == _currentSelectionGlobalId)
                    return true;
            }

            return false;
        }

        #region Property Formatting Helpers

        /// <summary>
        /// Checks if a property is a Unity internal property that should be ignored.
        /// </summary>
        private static bool IsInternalProperty(string propertyPath)
        {
            if (string.IsNullOrEmpty(propertyPath))
                return false;

            return propertyPath.StartsWith("m_Script") ||
                   propertyPath.StartsWith("m_EditorClassIdentifier") ||
                   propertyPath.StartsWith("m_ObjectHideFlags");
        }

        /// <summary>
        /// Formats a property value for JSON storage.
        /// Reuses UnityJsonSerializer.Instance for proper Unity type serialization.
        /// </summary>
        private static string FormatPropertyValue(object value)
        {
            if (value == null)
                return "null";

            try
            {
                using (var writer = new System.IO.StringWriter())
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
        /// </summary>
        private static string GetPropertyTypeName(object value)
        {
            if (value == null)
                return "null";

            Type type = value.GetType();

            if (type == typeof(float) || type == typeof(int) || type == typeof(double))
                return "Number";
            if (type == typeof(bool))
                return "Boolean";
            if (type == typeof(string))
                return "String";
            if (type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Vector4))
                return type.Name;
            if (type == typeof(Quaternion))
                return "Quaternion";
            if (type == typeof(Color))
                return "Color";
            if (type == typeof(Rect))
                return "Rect";

            return type.Name;
        }

        /// <summary>
        /// Gets the full Hierarchy path for a GameObject.
        /// Example: "Level1/Player/Arm/Hand"
        /// </summary>
        private static string GetGameObjectPath(GameObject obj)
        {
            if (obj == null)
                return "Unknown";

            var path = obj.name;
            var parent = obj.transform.parent;

            while (parent != null)
            {
                path = $"{parent.name}/{path}";
                parent = parent.parent;
            }

            return path;
        }

        #endregion
    }
}
