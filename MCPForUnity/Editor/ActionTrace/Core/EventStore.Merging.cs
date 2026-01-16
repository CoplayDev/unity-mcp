using System;
using System.Collections.Generic;
using MCPForUnity.Editor.ActionTrace.Semantics;
using UnityEditor;

namespace MCPForUnity.Editor.ActionTrace.Core
{
    /// <summary>
    /// Event merging (deduplication) functionality for EventStore.
    /// High-frequency events within a short time window are merged to reduce noise.
    /// </summary>
    public static partial class EventStore
    {
        // Event types that are eligible for merging (high-frequency, noisy events)
        private static readonly HashSet<string> MergeableEventTypes = new()
        {
            EventTypes.PropertyModified,
            EventTypes.SelectionPropertyModified,
            EventTypes.HierarchyChanged,
            EventTypes.SelectionChanged
        };

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
    }
}
