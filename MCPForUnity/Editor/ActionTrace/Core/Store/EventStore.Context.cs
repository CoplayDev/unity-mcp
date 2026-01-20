using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.ActionTrace.Context;
using MCPForUnity.Editor.ActionTrace.Core.Models;
using MCPForUnity.Editor.ActionTrace.Core.Settings;

namespace MCPForUnity.Editor.ActionTrace.Core.Store
{
    /// <summary>
    /// Context mapping functionality for EventStore.
    /// Manages associations between events and their operation contexts.
    /// </summary>
    public static partial class EventStore
    {
        /// <summary>
        /// Calculates the maximum number of context mappings to store.
        /// Dynamic: 2x the maxEvents setting (supports multi-agent collaboration).
        /// When maxEvents is 5000 (max), this yields 10000 context mappings.
        /// </summary>
        private static int GetMaxContextMappings()
        {
            var settings = ActionTraceSettings.Instance;
            int maxEvents = settings?.Storage.MaxEvents ?? 800;
            return maxEvents * 2;  // 2x ratio
        }

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

                // Trim oldest mappings if over limit (dynamic based on maxEvents setting)
                int maxContextMappings = GetMaxContextMappings();
                if (_contextMappings.Count > maxContextMappings)
                {
                    int removeCount = _contextMappings.Count - maxContextMappings;
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
            // Store all mappings per eventSequence (not just FirstOrDefault) to preserve
            // multiple contexts for the same event (multi-agent collaboration scenario)
            var mappingBySequence = mappingsSnapshot
                .GroupBy(m => m.EventSequence)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Query and join outside lock
            var query = eventsSnapshot.AsEnumerable();

            if (sinceSequence.HasValue)
            {
                query = query.Where(e => e.Sequence > sinceSequence.Value);
            }

            // SelectMany ensures each event-context pair becomes a separate tuple.
            // An event with 3 contexts yields 3 tuples, preserving all context data.
            var results = query
                .OrderByDescending(e => e.Sequence)
                .Take(limit)
                .SelectMany(e =>
                {
                    if (mappingBySequence.TryGetValue(e.Sequence, out var mappings) && mappings.Count > 0)
                        return mappings.Select(m => (Event: e, Context: m));
                    return new[] { (Event: e, Context: (ContextMapping)null) };
                })
                .ToList();

            return results;
        }
    }
}
