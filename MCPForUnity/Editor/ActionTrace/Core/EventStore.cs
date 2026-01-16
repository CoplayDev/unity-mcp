using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.ActionTrace.Context;
using MCPForUnity.Editor.ActionTrace.Semantics;
using UnityEditor;

namespace MCPForUnity.Editor.ActionTrace.Core
{
    /// <summary>
    /// Thread-safe event store for editor events.
    ///
    /// Threading model:
    /// - Writes: Main thread only (enforced by Debug.Assert in DEBUG builds)
    /// - Reads: Any thread, uses lock for snapshot pattern
    /// - Sequence generation: Uses Interlocked.Increment for atomicity
    ///
    /// Persistence: Uses McpJobStateStore for domain reload survival.
    /// Save strategy: Deferred persistence with dirty flag + delayCall coalescing.
    ///
    /// Memory optimization (Pruning):
    /// - Hot events (latest 100): Full payload retained
    /// - Cold events (older than 100): Automatically dehydrated (payload = null)
    /// - This reduces memory from ~10MB to <1MB for 1000 events
    ///
    /// Event merging (Deduplication):
    /// - High-frequency events (PropertyModified, SelectionPropertyModified, HierarchyChanged)
    ///   are merged within a short time window to reduce noise
    /// - Same event type + same target + same property path within merge window = single event
    ///
    /// Settings: Controlled by ActionTraceSettings asset.
    /// </summary>
    public static class EventStore
    {
        private const int MaxContextMappings = 2000;
        private const string StateKey = "timeline_events";

        // Event types that are eligible for merging (high-frequency, noisy events)
        private static readonly HashSet<string> MergeableEventTypes = new()
        {
            EventTypes.PropertyModified,
            EventTypes.SelectionPropertyModified,
            EventTypes.HierarchyChanged,
            EventTypes.SelectionChanged
        };

        // Schema version for migration support
        // Increment when breaking changes are made to the storage format
        private const int CurrentSchemaVersion = 4;  // Incremented for settings integration

        private static readonly List<EditorEvent> _events = new();
        private static readonly List<ContextMapping> _contextMappings = new();
        private static readonly object _queryLock = new();
        private static long _sequenceCounter;
        private static bool _isLoaded;
        private static bool _isDirty;  // Dirty flag for deferred persistence
        private static bool _saveScheduled;  // Prevents duplicate delayCall registrations

        // Batch notification: accumulate pending events and notify in single delayCall
        private static readonly List<EditorEvent> _pendingNotifications = new();
        private static bool _notifyScheduled;

        // Event merging state (tracks last event for merge detection)
        private static EditorEvent _lastRecordedEvent;
        private static long _lastRecordedTime;
        
        // Main thread detection: Use Unity's EditorApplication.isUpdating for runtime checks.
        // This is more robust than capturing a thread ID at static initialization time,
        // as it works correctly after domain reload and doesn't rely on managed thread IDs.
        // The MainThreadId field is kept for legacy/debugging purposes only.
        private static readonly int MainThreadId = Thread.CurrentThread.ManagedThreadId;

        /// <summary>
        /// Event raised when a new event is recorded.
        /// Used by ContextTrace to create associations.
        /// </summary>
        public static event Action<EditorEvent> EventRecorded;

        static EventStore()
        {
            LoadFromStorage();
        }



        /// <summary>
        /// Record a new event. Must be called from main thread.
        ///
        /// Returns:
        /// - New sequence number for newly recorded events
        /// - Existing sequence number when events are merged (same type/target/property within merge window)
        /// - -1 when event is rejected by importance filter
        ///
        /// To detect merging, check if the returned sequence matches a previously recorded event,
        /// or inspect the event's payload for the "merge_count" field (present when merge_count > 0).
        ///
        /// Thread safety: Write operations are protected by _queryLock for defensive
        /// programming, even though Record is expected to be called from main thread only.
        ///
        /// Event merging: High-frequency events within MergeWindowMs that have the same
        /// type, target, and property path will be merged into the previous event instead
        /// of creating a new entry. This reduces noise from actions like dragging sliders.
        ///
        /// Importance filtering: Events below ActionTraceSettings.MinImportanceForRecording
        /// are silently rejected at the store level (not just UI filtering).
        /// </summary>
        public static long Record(EditorEvent @event)
        {
            // Apply importance filter at store level
            // This prevents low-importance events from being recorded at all
            var settings = ActionTraceSettings.Instance;
            if (settings != null)
            {
                float importance = DefaultEventScorer.Instance.Score(@event);
                if (importance < settings.MinImportanceForRecording)
                {
                    // Event rejected due to low importance
                    return -1;
                }
            }

            // Note: EditorApplication.isUpdating check removed because it was too strict.
            // Many valid callbacks (delayCall, AssetPostprocessor, hierarchyChanged) run
            // on the main thread but outside the update loop. The lock(_queryLock) provides
            // actual thread safety for the critical section.

            // Use Interlocked.Increment for thread-safe sequence generation
            // Even though we assert main thread, this provides defense-in-depth
            long newSequence = Interlocked.Increment(ref _sequenceCounter);

            var evtWithSequence = new EditorEvent(
                sequence: newSequence,
                timestampUnixMs: @event.TimestampUnixMs,
                type: @event.Type,
                targetId: @event.TargetId,
                payload: @event.Payload
            );

            // Store reference for merge detection
            _lastRecordedEvent = evtWithSequence;
            _lastRecordedTime = @event.TimestampUnixMs;

            // Capture settings limits before lock
            int hotEventCount = settings?.HotEventCount ?? 100;
            int maxEvents = settings?.MaxEvents ?? 1000;

            // Write lock: protect _events and _contextMappings mutations
            lock (_queryLock)
            {
                // Check if event merging is enabled and this event should be merged
                // Must check inside lock since _lastRecordedEvent may be modified by other threads
                if (settings?.EnableEventMerging != false && ShouldMergeWithLast(@event))
                {
                    // Merge with last event: update timestamp and end_value
                    // Return the existing sequence instead of creating a new event
                    MergeWithLastEventLocked(@event);
                    return _lastRecordedEvent.Sequence;
                }

                _events.Add(evtWithSequence);

                // Auto-dehydrate old events to save memory
                // Keep latest HotEventCount events with full payload, dehydrate the rest
                if (_events.Count > hotEventCount)
                {
                    DehydrateOldEvents(hotEventCount);
                }

                // Trim oldest events if over limit (batch remove is more efficient than loop RemoveAt)
                if (_events.Count > maxEvents)
                {
                    int removeCount = _events.Count - maxEvents;
                    // Capture sequences to cascade delete mappings
                    var removedSequences = new HashSet<long>();
                    for (int i = 0; i < removeCount; i++)
                    {
                        removedSequences.Add(_events[i].Sequence);
                    }
                    _events.RemoveRange(0, removeCount);

                    // Cascade delete: remove mappings for deleted events
                    _contextMappings.RemoveAll(m => removedSequences.Contains(m.EventSequence));
                }
            }

            // Mark dirty and schedule deferred save (reduces disk I/O from O(N) to O(1))
            _isDirty = true;
            ScheduleSave();

            // Batch notification: accumulate event and schedule single delayCall
            // This prevents callback queue bloat in high-frequency scenarios
            lock (_pendingNotifications)
            {
                _pendingNotifications.Add(evtWithSequence);
            }

            ScheduleNotify();

            return evtWithSequence.Sequence;
        }

        /// <summary>
        /// Query events with optional filtering.
        /// Thread-safe - can be called from any thread.
        /// Uses snapshot pattern: capture only needed range inside lock, query outside.
        ///
        /// Performance optimization: Only copies the tail portion of _events
        /// (limit + buffer) unless sinceSequence requires earlier events.
        ///
        /// Correctness guarantee: If sinceSequence is provided, ALL events with
        /// Sequence > sinceSequence are returned, even if they fall outside the
        /// normal tail window. The copy window is automatically extended to include
        /// all matching events.
        /// </summary>
        public static IReadOnlyList<EditorEvent> Query(int limit = 50, long? sinceSequence = null)
        {
            List<EditorEvent> snapshot;

            lock (_queryLock)
            {
                int count = _events.Count;
                if (count == 0)
                {
                    return Array.Empty<EditorEvent>();
                }

                // Base window: tail portion for recent queries
                int copyCount = Math.Min(count, limit + (limit / 10) + 10);
                int startIndex = count - copyCount;

                // If sinceSequence is specified, ensure we don't miss matching events
                // Find the first event with Sequence > sinceSequence and extend window if needed
                if (sinceSequence.HasValue)
                {
                    int firstMatchIndex = -1;
                    // Linear search from the end (most recent events are likely to match first)
                    for (int i = count - 1; i >= 0; i--)
                    {
                        if (_events[i].Sequence > sinceSequence.Value)
                        {
                            firstMatchIndex = i;
                        }
                        else if (firstMatchIndex >= 0)
                        {
                            // Found a non-matching event after matches, stop searching
                            break;
                        }
                    }

                    // Extend window to include all matching events
                    if (firstMatchIndex >= 0 && firstMatchIndex < startIndex)
                    {
                        startIndex = firstMatchIndex;
                        copyCount = count - startIndex;
                    }
                }

                snapshot = new List<EditorEvent>(copyCount);
                for (int i = startIndex; i < count; i++)
                {
                    snapshot.Add(_events[i]);
                }
            }

            // Query: LINQ runs outside lock, no blocking
            var query = snapshot.AsEnumerable();

            if (sinceSequence.HasValue)
            {
                query = query.Where(e => e.Sequence > sinceSequence.Value);
            }

            return query.OrderByDescending(e => e.Sequence).Take(limit).ToList();
        }

        /// <summary>
        /// Get the current sequence counter value.
        /// </summary>
        public static long CurrentSequence => _sequenceCounter;

        /// <summary>
        /// Get total event count.
        /// </summary>
        public static int Count
        {
            get
            {
                lock (_queryLock)
                {
                    return _events.Count;
                }
            }
        }

        /// <summary>
        /// Dehydrate old events (beyond hotEventCount) to save memory.
        /// This is called automatically by Record().
        /// </summary>
        private static void DehydrateOldEvents(int hotEventCount)
        {
            // Find events that need dehydration (not already dehydrated and beyond hot count)
            for (int i = 0; i < _events.Count - hotEventCount; i++)
            {
                var evt = _events[i];
                if (evt != null && !evt.IsDehydrated && evt.Payload != null)
                {
                    // Dehydrate the event (creates new instance with Payload = null)
                    _events[i] = evt.Dehydrate();
                }
            }
        }

        // ========================================================================
        // Event Merging (Deduplication)
        // ========================================================================

        /// <summary>
        /// Checks if the given event should be merged with the last recorded event.
        /// Merging criteria:
        /// - Same event type (and type is mergeable)
        /// - Same target ID
        /// - Same property path (for property modification events)
        /// - Within merge time window
        /// </summary>
        private static bool ShouldMergeWithLast(EditorEvent evt)
        {
            if (_lastRecordedEvent == null)
                return false;

            var settings = ActionTraceSettings.Instance;
            int mergeWindowMs = settings?.MergeWindowMs ?? 100;

            // Time window check
            long timeDelta = evt.TimestampUnixMs - _lastRecordedTime;
            if (timeDelta > mergeWindowMs || timeDelta < 0)
                return false;

            // Type check: must be the same mergeable type
            if (evt.Type != _lastRecordedEvent.Type)
                return false;

            if (!MergeableEventTypes.Contains(evt.Type))
                return false;

            // Target check: must be the same target
            if (evt.TargetId != _lastRecordedEvent.TargetId)
                return false;

            // Property path check: for property modification events, must be same property
            string currentPropertyPath = GetPropertyPathFromPayload(evt.Payload);
            string lastPropertyPath = GetPropertyPathFromPayload(_lastRecordedEvent.Payload);
            if (!string.Equals(currentPropertyPath, lastPropertyPath, StringComparison.Ordinal))
                return false;

            return true;
        }

        /// <summary>
        /// Merges the new event with the last recorded event.
        /// Updates the last event's timestamp and end_value (if applicable).
        /// IMPORTANT: This method must only be called while holding _queryLock.
        /// </summary>
        private static void MergeWithLastEventLocked(EditorEvent evt)
        {
            if (_lastRecordedEvent == null)
                return;

            // Update timestamp to reflect the most recent activity
            _lastRecordedTime = evt.TimestampUnixMs;

            // For property modification events, update the end_value
            // This allows tracking the start and end of a continuous change (e.g., slider drag)
            if (evt.Payload != null && _lastRecordedEvent.Payload != null)
            {
                var newPayload = new Dictionary<string, object>(_lastRecordedEvent.Payload);

                // Update end_value with the new value
                if (evt.Payload.TryGetValue("end_value", out var newValue))
                {
                    newPayload["end_value"] = newValue;
                }

                // Update timestamp in payload
                newPayload["timestamp"] = evt.TimestampUnixMs;

                // Add merge_count to track how many events were merged
                int mergeCount = 1;
                if (_lastRecordedEvent.Payload.TryGetValue("merge_count", out var existingCount))
                {
                    mergeCount = (int)existingCount + 1;
                }
                newPayload["merge_count"] = mergeCount;

                // Update the last event's payload (requires creating new EditorEvent)
                // Safe to access _events here since caller holds _queryLock
                int lastEventIndex = _events.Count - 1;
                if (lastEventIndex >= 0)
                {
                    _events[lastEventIndex] = new EditorEvent(
                        sequence: _lastRecordedEvent.Sequence,
                        timestampUnixMs: evt.TimestampUnixMs,
                        type: _lastRecordedEvent.Type,
                        targetId: _lastRecordedEvent.TargetId,
                        payload: newPayload
                    );
                    _lastRecordedEvent = _events[lastEventIndex];
                }
            }

            // Schedule save since we modified the last event
            _isDirty = true;
            ScheduleSave();
        }

        /// <summary>
        /// Extracts the property path from an event payload.
        /// Used for merge detection of property modification events.
        /// </summary>
        private static string GetPropertyPathFromPayload(IReadOnlyDictionary<string, object> payload)
        {
            if (payload == null)
                return null;

            if (payload.TryGetValue("property_path", out var propertyPath))
                return propertyPath as string;

            return null;
        }

        /// <summary>
        /// Clear all events and context mappings (for testing).
        /// WARNING: This is destructive and cannot be undone.
        /// All ActionTrace history and context associations will be lost.
        /// </summary>
        public static void Clear()
        {
            lock (_queryLock)
            {
                _events.Clear();
                _contextMappings.Clear();
                _sequenceCounter = 0;
            }
            // For Clear(), save immediately (not deferred) since it's a destructive operation
            SaveToStorage();
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
        /// Get diagnostic information about memory usage.
        /// Useful for monitoring and debugging memory issues.
        /// </summary>
        public static string GetMemoryDiagnostics()
        {
            lock (_queryLock)
            {
                var settings = ActionTraceSettings.Instance;
                int hotEventCount = settings?.HotEventCount ?? 100;
                int maxEvents = settings?.MaxEvents ?? 1000;

                int totalEvents = _events.Count;
                int hotEvents = Math.Min(totalEvents, hotEventCount);
                int coldEvents = Math.Max(0, totalEvents - hotEventCount);

                int hydratedCount = 0;
                int dehydratedCount = 0;
                long estimatedPayloadBytes = 0;

                foreach (var evt in _events)
                {
                    if (evt.IsDehydrated)
                        dehydratedCount++;
                    else if (evt.Payload != null)
                    {
                        hydratedCount++;
                        // Estimate payload size (rough approximation)
                        estimatedPayloadBytes += EstimatePayloadSize(evt.Payload);
                    }
                }

                // Estimate dehydrated events size (~100 bytes each)
                long dehydratedBytes = dehydratedCount * 100;

                // Total estimated size
                long totalEstimatedBytes = estimatedPayloadBytes + dehydratedBytes;
                double totalEstimatedMB = totalEstimatedBytes / (1024.0 * 1024.0);

                return $"EventStore Memory Diagnostics:\n" +
                       $"  Total Events: {totalEvents}/{maxEvents}\n" +
                       $"  Hot Events (full payload): {hotEvents}\n" +
                       $"  Cold Events (dehydrated): {coldEvents}\n" +
                       $"  Hydrated: {hydratedCount}\n" +
                       $"  Dehydrated: {dehydratedCount}\n" +
                       $"  Estimated Payload Memory: {estimatedPayloadBytes / 1024} KB\n" +
                       $"  Total Estimated Memory: {totalEstimatedMB:F2} MB";
            }
        }

        /// <summary>
        /// Estimate the size of a payload in bytes.
        /// This is a rough approximation for diagnostics.
        /// </summary>
        private static long EstimatePayloadSize(IReadOnlyDictionary<string, object> payload)
        {
            if (payload == null) return 0;

            long size = 0;
            foreach (var kvp in payload)
            {
                // Key string (assume average 20 chars)
                size += kvp.Key.Length * 2;

                // Value
                if (kvp.Value is string str)
                    size += str.Length * 2;
                else if (kvp.Value is int)
                    size += 4;
                else if (kvp.Value is long)
                    size += 8;
                else if (kvp.Value is double)
                    size += 8;
                else if (kvp.Value is bool)
                    size += 1;
                else if (kvp.Value is IDictionary<string, object> dict)
                    size += dict.Count * 100; // Rough estimate
                else if (kvp.Value is System.Collections.ICollection list)
                    size += list.Count * 50; // Rough estimate
                else
                    size += 50; // Unknown type
            }

            return size;
        }

        // ========================================================================
        // Context Mapping API (Phase 1)
        // ========================================================================

        /// <summary>
        /// Add a context mapping for an event.
        ///
        /// Strategy: Multiple mappings allowed for same eventSequence (different contexts).
        /// Duplicate detection: Same (eventSequence, contextId) pair will be skipped.
        /// Query behavior: QueryWithContext returns the first (most recent) mapping per event.
        ///
        /// Thread-safe - can be called from EventRecorded subscribers.
        /// </summary>
        public static void AddContextMapping(ContextMapping mapping)
        {
            lock (_queryLock)
            {
                // Skip duplicate mappings (same eventSequence and contextId)
                // This allows multiple different contexts for the same event,
                // but prevents identical duplicates from bloating the list.
                bool isDuplicate = false;
                for (int i = _contextMappings.Count - 1; i >= 0; i--)
                {
                    var existing = _contextMappings[i];
                    if (existing.EventSequence == mapping.EventSequence &&
                        existing.ContextId == mapping.ContextId)
                    {
                        isDuplicate = true;
                        break;
                    }
                    // Optimization: mappings are ordered by EventSequence
                    if (existing.EventSequence < mapping.EventSequence)
                        break;
                }

                if (isDuplicate)
                    return;

                _contextMappings.Add(mapping);

                // Trim oldest mappings if over limit
                if (_contextMappings.Count > MaxContextMappings)
                {
                    int removeCount = _contextMappings.Count - MaxContextMappings;
                    _contextMappings.RemoveRange(0, removeCount);
                }
            }

            // Mark dirty and schedule deferred save
            _isDirty = true;
            ScheduleSave();
        }

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
        /// Schedule batch notification via delayCall.
        /// Multiple rapid events result in a single notification batch.
        /// Thread-safe: check-and-set is atomic under lock.
        /// </summary>
        private static void ScheduleNotify()
        {
            // Atomic check-and-set under lock to prevent race conditions
            lock (_pendingNotifications)
            {
                if (_notifyScheduled)
                    return;

                _notifyScheduled = true;
            }

            EditorApplication.delayCall += DrainPendingNotifications;
        }

        /// <summary>
        /// Drain all pending notifications and invoke EventRecorded for each.
        /// Called via delayCall, runs on main thread after current Record() completes.
        /// Thread-safe: clears _notifyScheduled under lock.
        /// </summary>
        private static void DrainPendingNotifications()
        {
            List<EditorEvent> toNotify;
            lock (_pendingNotifications)
            {
                _notifyScheduled = false;

                if (_pendingNotifications.Count == 0)
                    return;

                toNotify = new List<EditorEvent>(_pendingNotifications);
                _pendingNotifications.Clear();
            }

            // Notify subscribers outside lock
            foreach (var evt in toNotify)
            {
                EventRecorded?.Invoke(evt);
            }
        }

        /// <summary>
        /// Query events with their context associations.
        /// Returns a tuple of (Event, Context) where Context may be null.
        /// Note: Source filtering is not supported in Phase 1 (ContextMappings only).
        /// To filter by operation source, implement ContextStore (Phase 2).
        ///
        /// Performance optimization: Only copies the tail portion of lists
        /// unless sinceSequence requires earlier events.
        ///
        /// Correctness guarantee: If sinceSequence is provided, ALL events with
        /// Sequence > sinceSequence are returned, even if they fall outside the
        /// normal tail window.
        /// </summary>
        public static IReadOnlyList<(EditorEvent Event, ContextMapping Context)> QueryWithContext(
            int limit = 50,
            long? sinceSequence = null)
        {
            List<EditorEvent> eventsSnapshot;
            List<ContextMapping> mappingsSnapshot;

            lock (_queryLock)
            {
                int eventCount = _events.Count;
                if (eventCount == 0)
                {
                    return Array.Empty<(EditorEvent, ContextMapping)>();
                }

                // Base window: tail portion for recent queries
                int copyCount = Math.Min(eventCount, limit + (limit / 10) + 10);
                int startIndex = eventCount - copyCount;

                // If sinceSequence is specified, ensure we don't miss matching events
                if (sinceSequence.HasValue)
                {
                    int firstMatchIndex = -1;
                    for (int i = eventCount - 1; i >= 0; i--)
                    {
                        if (_events[i].Sequence > sinceSequence.Value)
                        {
                            firstMatchIndex = i;
                        }
                        else if (firstMatchIndex >= 0)
                        {
                            break;
                        }
                    }

                    if (firstMatchIndex >= 0 && firstMatchIndex < startIndex)
                    {
                        startIndex = firstMatchIndex;
                        copyCount = eventCount - startIndex;
                    }
                }

                eventsSnapshot = new List<EditorEvent>(copyCount);
                for (int i = startIndex; i < eventCount; i++)
                {
                    eventsSnapshot.Add(_events[i]);
                }

                // For mappings, copy all (usually much smaller than events)
                mappingsSnapshot = new List<ContextMapping>(_contextMappings);
            }

            // Build lookup dictionary outside lock
            var mappingBySequence = mappingsSnapshot
                .GroupBy(m => m.EventSequence)
                .ToDictionary(g => g.Key, g => g.FirstOrDefault());

            // Query and join outside lock
            var query = eventsSnapshot.AsEnumerable();

            if (sinceSequence.HasValue)
            {
                query = query.Where(e => e.Sequence > sinceSequence.Value);
            }

            var results = query
                .OrderByDescending(e => e.Sequence)
                .Take(limit)
                .Select(e =>
                {
                    mappingBySequence.TryGetValue(e.Sequence, out var mapping);
                    // mapping may be null if no context association exists
                    return (Event: e, Context: mapping);
                })
                .ToList();

            return results;
        }

        /// <summary>
        /// Remove all context mappings for a specific context ID.
        /// </summary>
        public static void RemoveContextMappings(Guid contextId)
        {
            lock (_queryLock)
            {
                _contextMappings.RemoveAll(m => m.ContextId == contextId);
            }
            // Mark dirty and schedule deferred save
            _isDirty = true;
            ScheduleSave();
        }

        /// <summary>
        /// Get the number of stored context mappings.
        /// </summary>
        public static int ContextMappingCount
        {
            get
            {
                lock (_queryLock)
                {
                    return _contextMappings.Count;
                }
            }
        }

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
                            $"than current version {CurrentSchemaVersion}. Data may not load correctly. " +
                            $"Consider updating the ActionTrace system.");
                    }
                    else if (state.SchemaVersion < CurrentSchemaVersion)
                    {
                        // Future: Add migration logic here when schema changes
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
        /// SchemaVersion: Increment when breaking changes are made.
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
