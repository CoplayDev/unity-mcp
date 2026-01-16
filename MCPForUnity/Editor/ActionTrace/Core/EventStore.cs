using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MCPForUnity.Editor.ActionTrace.Context;
using MCPForUnity.Editor.ActionTrace.Semantics;
using UnityEditor;

namespace MCPForUnity.Editor.ActionTrace.Core
{
    /// <summary>
    /// Thread-safe event store for editor events.
    ///
    /// Threading model:
    /// - Writes: Main thread only
    /// - Reads: Any thread, uses lock for snapshot pattern
    /// - Sequence generation: Uses Interlocked.Increment for atomicity
    ///
    /// Persistence: Uses McpJobStateStore for domain reload survival.
    /// Save strategy: Deferred persistence with dirty flag + delayCall coalescing.
    ///
    /// Memory optimization (Pruning):
    /// - Hot events (latest 100): Full payload retained
    /// - Cold events (older than 100): Automatically dehydrated (payload = null)
    ///
    /// Event merging (Deduplication):
    /// - High-frequency events are merged within a short time window to reduce noise
    ///
    /// Code organization: Split into multiple partial class files:
    /// - EventStore.cs (this file): Core API (Record, Query, Clear, Count)
    /// - EventStore.Merging.cs: Event merging/deduplication logic
    /// - EventStore.Persistence.cs: Save/load, domain reload survival
    /// - EventStore.Context.cs: Context mapping management
    /// - EventStore.Diagnostics.cs: Memory diagnostics and dehydration
    /// </summary>
    public static partial class EventStore
    {
        // Core state
        private static readonly List<EditorEvent> _events = new();
        private static readonly List<ContextMapping> _contextMappings = new();
        private static readonly object _queryLock = new();
        private static long _sequenceCounter;

        // Batch notification: accumulate pending events and notify in single delayCall
        private static readonly List<EditorEvent> _pendingNotifications = new();
        private static bool _notifyScheduled;

        // Main thread detection: Kept for legacy/debugging purposes only
        private static readonly int MainThreadId = Thread.CurrentThread.ManagedThreadId;

        // Fields shared with other partial class files
        private static EditorEvent _lastRecordedEvent;
        private static long _lastRecordedTime;
        private static bool _isDirty;

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
        /// - Existing sequence number when events are merged
        /// - -1 when event is rejected by importance filter
        /// </summary>
        public static long Record(EditorEvent @event)
        {
            // Apply importance filter at store level
            var settings = ActionTraceSettings.Instance;
            if (settings != null)
            {
                float importance = DefaultEventScorer.Instance.Score(@event);
                if (importance < settings.MinImportanceForRecording)
                {
                    return -1;
                }
            }

            long newSequence = Interlocked.Increment(ref _sequenceCounter);

            var evtWithSequence = new EditorEvent(
                sequence: newSequence,
                timestampUnixMs: @event.TimestampUnixMs,
                type: @event.Type,
                targetId: @event.TargetId,
                payload: @event.Payload
            );

            // Store reference for merge detection (used in EventStore.Merging.cs)
            _lastRecordedEvent = evtWithSequence;
            _lastRecordedTime = @event.TimestampUnixMs;

            int hotEventCount = settings?.HotEventCount ?? 100;
            int maxEvents = settings?.MaxEvents ?? 800;

            lock (_queryLock)
            {
                // Check if this event should be merged with the last one
                if (settings?.EnableEventMerging != false && ShouldMergeWithLast(@event))
                {
                    MergeWithLastEventLocked(@event);
                    return _lastRecordedEvent.Sequence;
                }

                _events.Add(evtWithSequence);

                // Auto-dehydrate old events
                if (_events.Count > hotEventCount)
                {
                    DehydrateOldEvents(hotEventCount);
                }

                // Trim oldest events if over limit
                if (_events.Count > maxEvents)
                {
                    int removeCount = _events.Count - maxEvents;
                    var removedSequences = new HashSet<long>();
                    for (int i = 0; i < removeCount; i++)
                    {
                        removedSequences.Add(_events[i].Sequence);
                    }
                    _events.RemoveRange(0, removeCount);
                    _contextMappings.RemoveAll(m => removedSequences.Contains(m.EventSequence));
                }
            }

            _isDirty = true;
            ScheduleSave();

            // Batch notification
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
        /// </summary>
        public static IReadOnlyList<EditorEvent> Query(int limit = 50, long? sinceSequence = null)
        {
            List<EditorEvent> snapshot;

            lock (_queryLock)
            {
                int count = _events.Count;
                if (count == 0)
                    return Array.Empty<EditorEvent>();

                // Base window: tail portion for recent queries
                int copyCount = Math.Min(count, limit + (limit / 10) + 10);
                int startIndex = count - copyCount;

                // If sinceSequence is specified, ensure we don't miss matching events
                if (sinceSequence.HasValue)
                {
                    int firstMatchIndex = -1;
                    for (int i = count - 1; i >= 0; i--)
                    {
                        if (_events[i].Sequence > sinceSequence.Value)
                            firstMatchIndex = i;
                        else if (firstMatchIndex >= 0)
                            break;
                    }

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
        /// Clear all events and context mappings.
        /// WARNING: This is destructive and cannot be undone.
        /// </summary>
        public static void Clear()
        {
            lock (_queryLock)
            {
                _events.Clear();
                _contextMappings.Clear();
                _sequenceCounter = 0;
            }
            SaveToStorage();
        }

        /// <summary>
        /// Schedule batch notification via delayCall.
        /// Multiple rapid events result in a single notification batch.
        /// </summary>
        private static void ScheduleNotify()
        {
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

            foreach (var evt in toNotify)
            {
                EventRecorded?.Invoke(evt);
            }
        }
    }
}
