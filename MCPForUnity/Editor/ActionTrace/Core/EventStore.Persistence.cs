using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.ActionTrace.Context;
using MCPForUnity.Editor.ActionTrace.Semantics;
using MCPForUnity.Editor.Helpers;
using UnityEditor;

namespace MCPForUnity.Editor.ActionTrace.Core
{
    /// <summary>
    /// Persistence functionality for EventStore.
    /// Handles domain reload survival and deferred save scheduling.
    /// </summary>
    public static partial class EventStore
    {
        private const string StateKey = "timeline_events";
        private const int CurrentSchemaVersion = 4;

        private static bool _isLoaded;
        private static bool _saveScheduled;     // Prevents duplicate delayCall registrations

        /// <summary>
        /// Schedule a deferred save via delayCall.
        /// Multiple rapid calls result in a single save (coalesced).
        /// </summary>
        private static void ScheduleSave()
        {
            // Only schedule if not already scheduled (prevents callback queue bloat)
            if (_saveScheduled)
                return;

            _saveScheduled = true;

            // Use delayCall to coalesce multiple saves into one
            EditorApplication.delayCall += () =>
            {
                _saveScheduled = false;
                if (_isDirty)
                {
                    SaveToStorage();
                    _isDirty = false;
                }
            };
        }

        /// <summary>
        /// Clears all pending notifications and scheduled saves.
        /// Call this when shutting down or reloading domains to prevent delayCall leaks.
        /// </summary>
        public static void ClearPendingOperations()
        {
            lock (_pendingNotifications)
            {
                _pendingNotifications.Clear();
                _notifyScheduled = false;
            }
            _saveScheduled = false;
        }

        /// <summary>
        /// Load events from persistent storage.
        /// Called once during static initialization.
        /// </summary>
        private static void LoadFromStorage()
        {
            if (_isLoaded) return;

            try
            {
                var state = McpJobStateStore.LoadState<EventStoreState>(StateKey);
                if (state != null)
                {
                    // Schema version check for migration support
                    if (state.SchemaVersion > CurrentSchemaVersion)
                    {
                        UnityEngine.Debug.LogWarning(
                            $"[EventStore] Stored schema version {state.SchemaVersion} is newer " +
                            $"than current version {CurrentSchemaVersion}. Data may not load correctly.");
                    }
                    else if (state.SchemaVersion < CurrentSchemaVersion)
                    {
                        UnityEngine.Debug.Log(
                            $"[EventStore] Migrating data from schema version {state.SchemaVersion} to {CurrentSchemaVersion}");
                    }

                    _sequenceCounter = state.SequenceCounter;
                    _events.Clear();
                    if (state.Events != null)
                    {
                        _events.AddRange(state.Events);
                    }
                    _contextMappings.Clear();
                    if (state.ContextMappings != null)
                    {
                        _contextMappings.AddRange(state.ContextMappings);
                    }

                    // CRITICAL: Trim to MaxEvents limit after loading
                    TrimToMaxEventsLimit();
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[EventStore] Failed to load from storage: {ex.Message}");
            }
            finally
            {
                _isLoaded = true;
            }
        }

        /// <summary>
        /// Trims events and context mappings if they exceed the hard limit.
        /// Uses a two-tier limit to avoid aggressive trimming.
        /// </summary>
        private static void TrimToMaxEventsLimit()
        {
            var settings = ActionTraceSettings.Instance;
            int maxEvents = settings?.MaxEvents ?? 800;
            int hardLimit = maxEvents + (maxEvents / 2);  // 1.5x buffer
            int maxContextMappings = MaxContextMappings;

            lock (_queryLock)
            {
                // Only trim if exceeding hard limit, not soft limit
                if (_events.Count > hardLimit)
                {
                    int removeCount = _events.Count - maxEvents;
                    var removedSequences = new HashSet<long>();
                    for (int i = 0; i < removeCount; i++)
                    {
                        removedSequences.Add(_events[i].Sequence);
                    }
                    _events.RemoveRange(0, removeCount);

                    // Cascade delete context mappings
                    _contextMappings.RemoveAll(m => removedSequences.Contains(m.EventSequence));

                    UnityEngine.Debug.Log($"[EventStore] Trimmed {removeCount} old events " +
                        $"(was {_events.Count + removeCount}, now {maxEvents}, hard limit was {hardLimit})");
                }

                // Trim context mappings if over limit
                if (_contextMappings.Count > maxContextMappings)
                {
                    int removeCount = _contextMappings.Count - maxContextMappings;
                    _contextMappings.RemoveRange(0, removeCount);
                }
            }
        }

        /// <summary>
        /// Save events to persistent storage.
        /// </summary>
        private static void SaveToStorage()
        {
            try
            {
                var state = new EventStoreState
                {
                    SchemaVersion = CurrentSchemaVersion,
                    SequenceCounter = _sequenceCounter,
                    Events = _events.ToList(),
                    ContextMappings = _contextMappings.ToList()
                };
                McpJobStateStore.SaveState(StateKey, state);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[EventStore] Failed to save to storage: {ex.Message}");
            }
        }

        /// <summary>
        /// Persistent state schema for EventStore.
        /// </summary>
        private class EventStoreState
        {
            public int SchemaVersion { get; set; } = CurrentSchemaVersion;
            public long SequenceCounter { get; set; }
            public List<EditorEvent> Events { get; set; }
            public List<ContextMapping> ContextMappings { get; set; }
        }
    }
}
