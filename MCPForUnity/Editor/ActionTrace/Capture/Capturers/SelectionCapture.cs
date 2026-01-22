using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MCPForUnity.Editor.ActionTrace.Core;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.ActionTrace.Helpers;
using MCPForUnity.Editor.ActionTrace.Core.Models;
using MCPForUnity.Editor.ActionTrace.Core.Store;

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
                var target = PropertyModificationHelper.GetTarget(undoMod);
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

                var propertyPath = PropertyModificationHelper.GetPropertyPath(undoMod);
                if (string.IsNullOrEmpty(propertyPath))
                    continue;

                // Filter out Unity internal properties
                if (PropertyFormatter.IsInternalProperty(propertyPath))
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
            var currentValue = PropertyModificationHelper.GetCurrentValue(undoMod);
            var prevValue = PropertyModificationHelper.GetPreviousValue(undoMod);

            var payload = new Dictionary<string, object>
            {
                ["target_name"] = target.name,
                ["component_type"] = target.GetType().Name,
                ["property_path"] = propertyPath,
                ["start_value"] = PropertyFormatter.FormatPropertyValue(prevValue),
                ["end_value"] = PropertyFormatter.FormatPropertyValue(currentValue),
                ["value_type"] = PropertyFormatter.GetPropertyTypeName(currentValue),
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
    }
}
