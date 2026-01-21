using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MCPForUnity.Editor.ActionTrace.Context;
using MCPForUnity.Editor.ActionTrace.Core;
using MCPForUnity.Editor.ActionTrace.Core.Models;
using MCPForUnity.Editor.ActionTrace.Core.Settings;
using MCPForUnity.Editor.ActionTrace.Semantics;
using UnityEditor;

namespace MCPForUnity.Editor.ActionTrace.Core.Store
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
        // P1 Fix: Added max limit to prevent unbounded growth
        private const int MaxPendingNotifications = 256;  // Max pending notifications before forced drain
        private static readonly List<EditorEvent> _pendingNotifications = new();
        private static bool _notifyScheduled;

        // Fields shared with other partial class files
        private static EditorEvent _lastRecordedEvent;
        private static long _lastRecordedTime;
        private static bool _isDirty;
        private static int _lastDehydratedCount = -1;  // Optimizes dehydration trigger

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
        /// - -1 when event is rejected by filters
        ///
        /// Note: Set ActionTraceSettings.BypassImportanceFilter = true to record all events
        /// regardless of importance score (useful for complete timeline view).
        /// </summary>
        public static long Record(EditorEvent @event)
        {
            var settings = ActionTraceSettings.Instance;

            // Apply disabled event types filter (hard filter, cannot be bypassed)
            if (settings != null && IsEventTypeDisabled(@event.Type, settings.Filtering.DisabledEventTypes))
            {
                return -1;
            }

            // Apply importance filter at store level (unless bypassed in Settings)
            if (settings != null && !settings.Filtering.BypassImportanceFilter)
            {
                float importance = DefaultEventScorer.Instance.Score(@event);
                if (importance <= settings.Filtering.MinImportanceForRecording)
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

            int hotEventCount = settings?.Storage.HotEventCount ?? 100;
            int maxEvents = settings?.Storage.MaxEvents ?? 800;

            lock (_queryLock)
            {
                // Check if this event should be merged with the last one
                if (settings?.Merging.EnableEventMerging != false && ShouldMergeWithLast(@event))
                {
                    MergeWithLastEventLocked(@event, evtWithSequence);
                    return _lastRecordedEvent.Sequence;
                }

                _events.Add(evtWithSequence);

                // Update merge tracking AFTER merge check and add to prevent self-merge
                _lastRecordedEvent = evtWithSequence;
                _lastRecordedTime = @event.TimestampUnixMs;

                // Auto-dehydrate old events (optimized: only when count changes)
                if (_events.Count > hotEventCount && _events.Count != _lastDehydratedCount)
                {
                    DehydrateOldEvents(hotEventCount);
                    _lastDehydratedCount = _events.Count;
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

                // Mark dirty inside lock for thread safety
                _isDirty = true;
            }

            ScheduleSave();

            // Batch notification with limit check (P1 Fix)
            lock (_pendingNotifications)
            {
                // P1 Fix: Force drain if over limit to prevent unbounded growth
                if (_pendingNotifications.Count >= MaxPendingNotifications)
                {
                    _notifyScheduled = false;  // Reset flag so we can drain immediately
                }

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
                // Use checked arithmetic to detect overflow, fall back to full list if overflow occurs
                int copyCount;
                try
                {
                    int windowSize = checked(limit + (limit / 10) + 10);
                    copyCount = Math.Min(count, windowSize);
                }
                catch (OverflowException)
                {
                    // If limit is too large (e.g., int.MaxValue), just take all events
                    copyCount = count;
                }
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
        /// Query all events without limit. Returns events in sequence order (newest first).
        /// This is more efficient than Query(int.MaxValue) as it avoids overflow checks.
        /// Thread-safe - can be called from any thread.
        /// </summary>
        public static IReadOnlyList<EditorEvent> QueryAll()
        {
            List<EditorEvent> snapshot;

            lock (_queryLock)
            {
                int count = _events.Count;
                if (count == 0)
                    return Array.Empty<EditorEvent>();

                // Create a snapshot of all events
                snapshot = new List<EditorEvent>(count);
                for (int i = 0; i < count; i++)
                {
                    snapshot.Add(_events[i]);
                }
            }

            // Return in sequence order (newest first)
            // Note: snapshot is already in sequence order (ascending), so we reverse
            snapshot.Reverse();
            return snapshot;
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

            // Reset merge tracking and pending notifications
            _lastRecordedEvent = null;
            _lastRecordedTime = 0;
            _lastDehydratedCount = -1;
            lock (_pendingNotifications)
            {
                _pendingNotifications.Clear();
                _notifyScheduled = false;
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

        /// <summary>
        /// Check if an event type is disabled in settings.
        /// </summary>
        private static bool IsEventTypeDisabled(string eventType, string[] disabledTypes)
        {
            if (disabledTypes == null || disabledTypes.Length == 0)
                return false;

            foreach (string disabled in disabledTypes)
            {
                if (string.Equals(eventType, disabled, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }
    }
}
