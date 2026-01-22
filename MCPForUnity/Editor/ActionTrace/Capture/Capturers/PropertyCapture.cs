using System;
using System.Collections.Generic;
using UnityEditor;
using MCPForUnity.Editor.ActionTrace.Core;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.ActionTrace.Helpers;
using MCPForUnity.Editor.ActionTrace.Core.Models;
using MCPForUnity.Editor.ActionTrace.Core.Store;
using System.Threading;

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
    /// - Uses EditorApplication.update for periodic flushing (safe on domain reload)
    /// - Object pooling to reduce GC pressure
    /// - Cache size limits to prevent unbounded memory growth
    /// - Cross-session stable IDs via GlobalIdHelper
    ///
    /// Reuses existing Helpers:
    /// - GlobalIdHelper.ToGlobalIdString() for stable object IDs
    /// - PropertyFormatter for property value formatting
    /// - PropertyModificationHelper for Undo reflection logic
    /// </summary>
    [InitializeOnLoad]
    public static class PropertyChangeTracker
    {
        // Configuration
        private const long DebounceWindowMs = 500;      // Debounce window in milliseconds
        private const int MaxPendingEntries = 256;       // Max pending changes before forced flush

        // State
        private static readonly object _lock = new();
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
        /// Schedules periodic flush checks using EditorApplication.update.
        /// FlushCheck is called every frame but only processes when debounce window expires.
        /// </summary>
        private static void ScheduleNextFlush()
        {
            // Use EditorApplication.update instead of delayCall to avoid infinite recursion
            // This ensures the callback is properly cleaned up on domain reload
            EditorApplication.update -= FlushCheck;
            EditorApplication.update += FlushCheck;
        }

        /// <summary>
        /// Periodic flush check called by EditorApplication.update.
        /// Only performs flush when the debounce window has expired.
        /// </summary>
        private static void FlushCheck()
        {
            var currentTime = EditorApplication.timeSinceStartup * 1000;

            if (currentTime - _lastFlushTime >= DebounceWindowMs)
            {
                FlushPendingChanges();
                _lastFlushTime = currentTime;
            }
        }

        /// <summary>
        /// Called by Unity when properties are modified via Undo system.
        /// This includes Inspector changes, Scene view manipulations, etc.
        /// Returns the modifications unchanged to allow Undo system to continue.
        ///
        /// Performance: Minimizes lock持有时间 by extracting data before locking.
        /// </summary>
        private static UndoPropertyModification[] ProcessModifications(UndoPropertyModification[] modifications)
        {
            if (modifications == null || modifications.Length == 0)
                return modifications;

            // Phase 1: Extract data from Undo modifications (outside lock)
            // This minimizes time spent inside the critical section
            var extractedData = new List<ModificationData>(modifications.Length);
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            foreach (var undoMod in modifications)
            {
                var target = PropertyModificationHelper.GetTarget(undoMod);
                if (target == null)
                    continue;

                var propertyPath = PropertyModificationHelper.GetPropertyPath(undoMod);
                if (string.IsNullOrEmpty(propertyPath))
                    continue;

                // Filter out Unity internal properties early
                if (PropertyFormatter.IsInternalProperty(propertyPath))
                    continue;

                string globalId = GlobalIdHelper.ToGlobalIdString(target);
                if (string.IsNullOrEmpty(globalId))
                    continue;

                var currentValue = PropertyModificationHelper.GetCurrentValue(undoMod);
                var prevValue = PropertyModificationHelper.GetPreviousValue(undoMod);

                extractedData.Add(new ModificationData
                {
                    GlobalId = globalId,
                    TargetName = target.name,
                    ComponentType = target.GetType().Name,
                    PropertyPath = propertyPath,
                    StartValue = PropertyFormatter.FormatPropertyValue(prevValue),
                    EndValue = PropertyFormatter.FormatPropertyValue(currentValue),
                    PropertyType = PropertyFormatter.GetPropertyTypeName(currentValue),
                    LastUpdateMs = nowMs
                });
            }

            // Phase 2: Update pending changes (inside lock, minimal work)
            lock (_lock)
            {
                foreach (var data in extractedData)
                {
                    string uniqueKey = $"{data.GlobalId}:{data.PropertyPath}";

                    // Check if we already have a pending change for this property
                    if (_pendingChanges.TryGetValue(uniqueKey, out var pending))
                    {
                        // Update existing pending change
                        pending.EndValue = data.EndValue;
                        pending.ChangeCount++;
                        pending.LastUpdateMs = data.LastUpdateMs;
                        _pendingChanges[uniqueKey] = pending;
                    }
                    else
                    {
                        // Enforce cache limit to prevent unbounded growth
                        if (_pendingChanges.Count >= MaxPendingEntries)
                        {
                            // Exit lock before flushing to avoid nested lock
                            Monitor.Exit(_lock);
                            try
                            {
                                FlushPendingChanges(force: true);
                            }
                            finally
                            {
                                Monitor.Enter(_lock);
                            }
                        }

                        // Create new pending change (use object pool if available)
                        var change = AcquirePendingChange();
                        change.GlobalId = data.GlobalId;
                        change.TargetName = data.TargetName;
                        change.ComponentType = data.ComponentType;
                        change.PropertyPath = data.PropertyPath;
                        change.StartValue = data.StartValue;
                        change.EndValue = data.EndValue;
                        change.PropertyType = data.PropertyType;
                        change.ChangeCount = 1;
                        change.LastUpdateMs = data.LastUpdateMs;

                        _pendingChanges[uniqueKey] = change;
                    }
                }
            }

            return modifications;
        }

        /// <summary>
        /// Temporary structure to hold extracted modification data.
        /// Used to minimize time spent inside lock.
        /// </summary>
        private struct ModificationData
        {
            public string GlobalId;
            public string TargetName;
            public string ComponentType;
            public string PropertyPath;
            public string StartValue;
            public string EndValue;
            public string PropertyType;
            public long LastUpdateMs;
        }

        /// <summary>
        /// Flushes all pending property changes that have exceeded the debounce window.
        /// Called periodically via EditorApplication.update.
        ///
        /// When force=true, bypasses the debounce age check and flushes ALL entries.
        /// Used for shutdown or when cache limit is reached.
        /// </summary>
        private static void FlushPendingChanges(bool force = false)
        {
            lock (_lock)
            {
                if (_pendingChanges.Count == 0)
                    return;

                long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                foreach (var kvp in _pendingChanges)
            {
                // When forced, flush all entries. Otherwise, only flush expired entries.
                if (force || nowMs - kvp.Value.LastUpdateMs >= DebounceWindowMs)
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
        /// Forces an immediate flush of ALL pending changes, bypassing debounce window.
        /// Useful for shutdown or before critical operations.
        /// </summary>
        public static void ForceFlush()
        {
            FlushPendingChanges(force: true);
        }

        /// <summary>
        /// Gets the current count of pending changes.
        /// Useful for debugging and monitoring.
        /// </summary>
        public static int PendingCount
        {
            get
            {
                lock (_lock)
                {
                    return _pendingChanges.Count;
                }
            }
        }

        /// <summary>
        /// Clears all pending changes without recording them.
        /// Useful for testing or error recovery.
        /// </summary>
        public static void ClearPending()
        {
            lock (_lock)
            {
                foreach (var kvp in _pendingChanges)
                {
                    ReturnPendingChange(kvp.Value);
                }
                _pendingChanges.Clear();
            }
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
