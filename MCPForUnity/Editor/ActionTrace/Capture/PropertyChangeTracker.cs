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
    /// High-performance property change tracker with debouncing.
    ///
    /// Captures Unity property modifications via Undo.postprocessModifications,
    /// applies debouncing to merge rapid changes (e.g., Slider drag), and records
    /// PropertyModified events to the ActionTrace EventStore.
    ///
    /// Key features:
    /// - Uses EditorApplication.update for consistent periodic flushing
    /// - Object pooling to reduce GC pressure
    /// - Cache size limits to prevent unbounded memory growth
    /// - Cross-session stable IDs via GlobalIdHelper
    ///
    /// Reuses existing Helpers:
    /// - GlobalIdHelper.ToGlobalIdString() for stable object IDs
    /// - UnityJsonSerializer.Instance for Unity type serialization
    /// </summary>
    [InitializeOnLoad]
    public static class PropertyChangeTracker
    {
        // Configuration
        private const long DebounceWindowMs = 500;      // Debounce window in milliseconds
        private const int MaxPendingEntries = 256;       // Max pending changes before forced flush

        // State
        private static readonly Dictionary<string, PendingPropertyChange> _pendingChanges = new();
        private static readonly Stack<PendingPropertyChange> _objectPool = new();
        private static readonly HashSet<string> _removedKeys = new();
        private static double _lastFlushTime;

        /// <summary>
        /// Initializes the property tracker and subscribes to Unity callbacks.
        /// </summary>
        static PropertyChangeTracker()
        {
            Undo.postprocessModifications += mods => ProcessModifications(mods);
            ScheduleNextFlush();
        }

        /// <summary>
        /// Schedules a delayed flush check using delayCall.
        /// This is more efficient than per-frame update checks, as it only runs when needed.
        /// </summary>
        private static void ScheduleNextFlush()
        {
            EditorApplication.delayCall += () =>
            {
                var currentTime = EditorApplication.timeSinceStartup * 1000;

                if (currentTime - _lastFlushTime >= DebounceWindowMs)
                {
                    FlushPendingChanges();
                    _lastFlushTime = currentTime;
                }

                // Reschedule for next check
                ScheduleNextFlush();
            };
        }

        /// <summary>
        /// Called by Unity when properties are modified via Undo system.
        /// This includes Inspector changes, Scene view manipulations, etc.
        /// Returns the modifications unchanged to allow Undo system to continue.
        /// </summary>
        private static UndoPropertyModification[] ProcessModifications(UndoPropertyModification[] modifications)
        {
            if (modifications == null || modifications.Length == 0)
                return modifications;

            foreach (var undoMod in modifications)
            {
                // UndoPropertyModification contains the PropertyModification and value changes
                // Try to extract target and property path
                var target = GetTargetFromUndoMod(undoMod);
                if (target == null)
                    continue;

                var propertyPath = GetPropertyPathFromUndoMod(undoMod);
                if (string.IsNullOrEmpty(propertyPath))
                    continue;

                // Filter out Unity internal properties
                if (propertyPath.StartsWith("m_Script") ||
                    propertyPath.StartsWith("m_EditorClassIdentifier") ||
                    propertyPath.StartsWith("m_ObjectHideFlags"))
                {
                    continue;
                }

                // Generate stable unique key
                string globalId = GlobalIdHelper.ToGlobalIdString(target);
                if (string.IsNullOrEmpty(globalId))
                    continue;

                string uniqueKey = $"{globalId}:{propertyPath}";

                // Get the current value (not the path)
                var currentValue = GetCurrentValueFromUndoMod(undoMod);

                // Check if we already have a pending change for this property
                if (_pendingChanges.TryGetValue(uniqueKey, out var pending))
                {
                    // Update existing pending change
                    // Note: Must reassign to dictionary since PendingPropertyChange is a struct
                    pending.EndValue = FormatPropertyValue(currentValue);
                    pending.ChangeCount++;
                    pending.LastUpdateMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    _pendingChanges[uniqueKey] = pending;
                }
                else
                {
                    // Enforce cache limit to prevent unbounded growth
                    if (_pendingChanges.Count >= MaxPendingEntries)
                    {
                        // Force flush before adding new entry
                        FlushPendingChanges();
                    }

                    // Create new pending change (use object pool if available)
                    var change = AcquirePendingChange();
                    change.GlobalId = globalId;
                    change.TargetName = target.name;
                    change.ComponentType = target.GetType().Name;
                    change.PropertyPath = propertyPath;
                    // Record the start value from the previous value reported by Undo system
                    var prev = GetPreviousValueFromUndoMod(undoMod);
                    change.StartValue = FormatPropertyValue(prev);
                    change.EndValue = FormatPropertyValue(currentValue);
                    change.PropertyType = GetPropertyTypeName(currentValue);
                    change.ChangeCount = 1;
                    change.LastUpdateMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                    _pendingChanges[uniqueKey] = change;
                }
            }

            return modifications;
        }

        #region UndoPropertyModification Helpers (via UndoReflectionHelper)

        /// <summary>
        /// Extracts the previous value from an UndoPropertyModification.
        /// Uses shared UndoReflectionHelper for reflection logic.
        /// </summary>
        private static object GetPreviousValueFromUndoMod(UndoPropertyModification undoMod)
        {
            return UndoReflectionHelper.GetPreviousValue(undoMod);
        }

        /// <summary>
        /// Extracts the target object from an UndoPropertyModification.
        /// Uses shared UndoReflectionHelper for reflection logic.
        /// </summary>
        private static UnityEngine.Object GetTargetFromUndoMod(UndoPropertyModification undoMod)
        {
            return UndoReflectionHelper.GetTarget(undoMod);
        }

        /// <summary>
        /// Extracts the property path from an UndoPropertyModification.
        /// Uses shared UndoReflectionHelper for reflection logic.
        /// </summary>
        private static string GetPropertyPathFromUndoMod(UndoPropertyModification undoMod)
        {
            return UndoReflectionHelper.GetPropertyPath(undoMod);
        }

        /// <summary>
        /// Extracts the current value from an UndoPropertyModification.
        /// Uses shared UndoReflectionHelper for reflection logic.
        /// </summary>
        private static object GetCurrentValueFromUndoMod(UndoPropertyModification undoMod)
        {
            return UndoReflectionHelper.GetCurrentValue(undoMod);
        }

        #endregion

        /// <summary>
        /// Formats a property value for JSON storage.
        ///
        /// Reuses existing Helpers:
        /// - UnityJsonSerializer.Instance for Vector3, Color, Quaternion, etc.
        /// </summary>
        private static string FormatPropertyValue(object value)
        {
            if (value == null)
                return "null";

            try
            {
                // Use UnityJsonSerializer for proper Unity type serialization
                using (var writer = new System.IO.StringWriter())
                {
                    UnityJsonSerializer.Instance.Serialize(writer, value);
                    return writer.ToString();
                }
            }
            catch (Exception)
            {
                // Fallback to ToString() if serialization fails
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

            // Use friendly names for common Unity types
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
        /// Flushes all pending property changes that have exceeded the debounce window.
        /// Called periodically via EditorApplication.update.
        /// </summary>
        private static void FlushPendingChanges()
        {
            if (_pendingChanges.Count == 0)
                return;

            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            foreach (var kvp in _pendingChanges)
            {
                // Check if entry has expired (no updates for DebounceWindowMs)
                if (nowMs - kvp.Value.LastUpdateMs >= DebounceWindowMs)
                {
                    // Record the PropertyModified event
                    RecordPropertyModifiedEvent(kvp.Value);

                    // Return to object pool
                    ReturnPendingChange(kvp.Value);

                    // Mark for removal
                    _removedKeys.Add(kvp.Key);
                }
            }

            // Batch remove expired entries
            foreach (var key in _removedKeys)
            {
                _pendingChanges.Remove(key);
            }
            _removedKeys.Clear();
        }

        /// <summary>
        /// Records a PropertyModified event to the ActionTrace EventStore.
        /// </summary>
        private static void RecordPropertyModifiedEvent(in PendingPropertyChange change)
        {
            var payload = new Dictionary<string, object>
            {
                ["target_name"] = change.TargetName,
                ["component_type"] = change.ComponentType,
                ["property_path"] = change.PropertyPath,
                ["start_value"] = change.StartValue,
                ["end_value"] = change.EndValue,
                ["value_type"] = change.PropertyType,
                ["change_count"] = change.ChangeCount
            };

            var evt = new EditorEvent(
                sequence: 0, // Will be assigned by EventStore.Record()
                timestampUnixMs: change.LastUpdateMs,
                type: EventTypes.PropertyModified,
                targetId: change.GlobalId,
                payload: payload
            );

            EventStore.Record(evt);
        }

        /// <summary>
        /// Acquires a PendingPropertyChange from the object pool.
        /// Creates a new instance if pool is empty.
        /// </summary>
        private static PendingPropertyChange AcquirePendingChange()
        {
            if (_objectPool.Count > 0)
            {
                var change = _objectPool.Pop();
                // Reset is handled by ReturnPendingChange before pushing back
                return change;
            }
            return new PendingPropertyChange();
        }

        /// <summary>
        /// Returns a PendingPropertyChange to the object pool after clearing its data.
        /// </summary>
        private static void ReturnPendingChange(in PendingPropertyChange change)
        {
            // Create a copy to clear (structs are value types)
            var cleared = change;
            cleared.Reset();
            _objectPool.Push(cleared);
        }

        /// <summary>
        /// Forces an immediate flush of all pending changes.
        /// Useful for shutdown or before critical operations.
        /// </summary>
        public static void ForceFlush()
        {
            FlushPendingChanges();
        }

        /// <summary>
        /// Gets the current count of pending changes.
        /// Useful for debugging and monitoring.
        /// </summary>
        public static int PendingCount => _pendingChanges.Count;

        /// <summary>
        /// Clears all pending changes without recording them.
        /// Useful for testing or error recovery.
        /// </summary>
        public static void ClearPending()
        {
            foreach (var kvp in _pendingChanges)
            {
                ReturnPendingChange(kvp.Value);
            }
            _pendingChanges.Clear();
        }
    }

    /// <summary>
    /// Represents a property change that is pending debounce.
    /// Uses a struct to reduce GC pressure (stored on stack when possible).
    /// </summary>
    public struct PendingPropertyChange
    {
        public string GlobalId;          // Cross-session stable object ID
        public string TargetName;        // Object name (e.g., "Main Camera")
        public string ComponentType;     // Component type (e.g., "Light")
        public string PropertyPath;      // Serialized property path (e.g., "m_Intensity")
        public string StartValue;        // JSON formatted start value
        public string EndValue;          // JSON formatted end value
        public string PropertyType;      // Type name of the property value
        public int ChangeCount;          // Number of changes merged (for Slider drag)
        public long LastUpdateMs;        // Last update timestamp for debouncing

        /// <summary>
        /// Resets all fields to default values.
        /// Called before returning the struct to the object pool.
        /// </summary>
        public void Reset()
        {
            GlobalId = null;
            TargetName = null;
            ComponentType = null;
            PropertyPath = null;
            StartValue = null;
            EndValue = null;
            PropertyType = null;
            ChangeCount = 0;
            LastUpdateMs = 0;
        }
    }
}
