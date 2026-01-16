using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.ActionTrace.Context;

namespace MCPForUnity.Editor.ActionTrace.Core
{
    /// <summary>
    /// Context mapping functionality for EventStore.
    /// Manages associations between events and their operation contexts.
    /// </summary>
    public static partial class EventStore
    {
        private const int MaxContextMappings = 2000;

        /// <summary>
        /// Add a context mapping for an event.
        /// Strategy: Multiple mappings allowed for same eventSequence (different contexts).
        /// Duplicate detection: Same (eventSequence, contextId) pair will be skipped.
        /// Thread-safe - can be called from EventRecorded subscribers.
        /// </summary>
        public static void AddContextMapping(ContextMapping mapping)
        {
            lock (_queryLock)
            {
                // Skip duplicate mappings (same eventSequence and contextId)
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

        /// <summary>
        /// Query events with their context associations.
        /// Returns a tuple of (Event, Context) where Context may be null.
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
                    return (Event: e, Context: mapping);
                })
                .ToList();

            return results;
        }
    }
}
