using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.ActionTrace.Context;
using MCPForUnity.Editor.ActionTrace.Core.Models;
using MCPForUnity.Editor.ActionTrace.Core.Settings;
using MCPForUnity.Editor.Helpers;
using UnityEditor;

namespace MCPForUnity.Editor.ActionTrace.Core.Store
{
    /// <summary>
    /// Persistence functionality for EventStore.
    /// Handles domain reload survival and deferred save scheduling.
    /// </summary>
    public static partial class EventStore
    {
        private const string StateKey = "timeline_events";
        private const int CurrentSchemaVersion = 4;

        // P1 Fix: Save throttling to prevent excessive disk writes
        private const long MinSaveIntervalMs = 1000;  // Minimum 1 second between saves

        private static bool _isLoaded;
        private static bool _saveScheduled;     // Prevents duplicate delayCall registrations
        private static long _lastSaveTime;     // Track last save time for throttling

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

        /// <summary>
        /// Schedule a deferred save via delayCall.
        /// Multiple rapid calls result in a single save (coalesced).
        /// Thread-safe: uses lock to protect _saveScheduled flag.
        /// P1 Fix: Added throttling to prevent excessive disk writes.
        /// P0 Fix: When throttled, schedule retry instead of returning to prevent data loss.
        /// </summary>
        private static void ScheduleSave()
        {
            // P1 Fix: Check throttling
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long timeSinceLastSave = now - _lastSaveTime;

            if (timeSinceLastSave < MinSaveIntervalMs)
            {
                // P0 Fix: Schedule retry instead of just returning
                // Calculate remaining throttle time with small cap to avoid excessive retries
                long remainingMs = MinSaveIntervalMs - timeSinceLastSave;
                int retryDelayMs = (int)Math.Min(remainingMs, 500); // Cap at 500ms

                lock (_queryLock)
                {
                    // Only schedule retry if not already scheduled
                    if (!_saveScheduled)
                    {
                        _saveScheduled = true;
                        EditorApplication.delayCall += () =>
                        {
                            lock (_queryLock)
                            {
                                _saveScheduled = false;
                            }
                            // Retry scheduling save
                            ScheduleSave();
                        };
                    }
                }
                return;
            }

            // Use lock to prevent race conditions with _saveScheduled
            lock (_queryLock)
            {
                // Only schedule if not already scheduled (prevents callback queue bloat)
                if (_saveScheduled)
                    return;

                _saveScheduled = true;
            }

            // Use delayCall to coalesce multiple saves into one
            EditorApplication.delayCall += () =>
            {
                bool wasDirty;
                lock (_queryLock)
                {
                    _saveScheduled = false;
                    wasDirty = _isDirty;
                    if (_isDirty)
                    {
                        _isDirty = false;
                        // P1 Fix: Update last save time when actually saving
                        _lastSaveTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    }
                }

                // Perform save outside lock to avoid holding lock during I/O
                if (wasDirty)
                {
                    SaveToStorage();
                }
            };
        }

        /// <summary>
        /// Clears all pending notifications and scheduled saves.
        /// Call this when shutting down or reloading domains.
        /// </summary>
        public static void ClearPendingOperations()
        {
            lock (_pendingNotifications)
            {
                _pendingNotifications.Clear();
                _notifyScheduled = false;
            }
            _saveScheduled = false;
            _lastDehydratedCount = -1;  // Reset dehydration optimization marker
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
                    // Note: We assume forward compatibility - newer data can be loaded by older code
                    if (state.SchemaVersion > CurrentSchemaVersion)
                    {
                        McpLog.Warn(
                            $"[EventStore] Loading data from newer schema version {state.SchemaVersion} " +
                            $"(current is {CurrentSchemaVersion}). Assuming forward compatibility.");
                    }
                    else if (state.SchemaVersion < CurrentSchemaVersion)
                    {
                        McpLog.Info(
                            $"[EventStore] Data from schema version {state.SchemaVersion} will be " +
                            $"resaved with current version {CurrentSchemaVersion}.");
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
                McpLog.Error($"[EventStore] Failed to load from storage: {ex.Message}\n{ex.StackTrace}");
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
            int maxEvents = settings?.Storage.MaxEvents ?? 800;
            int hardLimit = maxEvents * 2;  // 2x buffer
            int maxContextMappings = GetMaxContextMappings();

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

                    McpLog.Info($"[EventStore] Trimmed {removeCount} old events " +
                        $"(was {_events.Count + removeCount}, now {maxEvents}, hard limit was {hardLimit})");
                }

                // Trim context mappings if over limit (dynamic based on maxEvents setting)
                if (_contextMappings.Count > maxContextMappings)
                {
                    int removeCount = _contextMappings.Count - maxContextMappings;
                    _contextMappings.RemoveRange(0, removeCount);
                }
            }
        }

        /// <summary>
        /// Save events to persistent storage.
        /// P0 Fix: Acquire snapshot under lock to avoid concurrent mutation, serialize outside lock.
        /// </summary>
        private static void SaveToStorage()
        {
            try
            {
                // P0 Fix: Take snapshot of mutable state under lock
                EventStoreState state;
                lock (_queryLock)
                {
                    state = new EventStoreState
                    {
                        SchemaVersion = CurrentSchemaVersion,
                        SequenceCounter = _sequenceCounter,
                        Events = _events.ToList(),
                        ContextMappings = _contextMappings.ToList()
                    };
                }

                // Serialize and save outside lock to avoid holding lock during I/O
                McpJobStateStore.SaveState(StateKey, state);
            }
            catch (Exception ex)
            {
                McpLog.Error($"[EventStore] Failed to save to storage: {ex.Message}\n{ex.StackTrace}");
            }
        }

    }
}
