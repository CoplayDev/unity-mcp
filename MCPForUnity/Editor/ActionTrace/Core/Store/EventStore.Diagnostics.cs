using System;
using System.Collections.Generic;
using MCPForUnity.Editor.ActionTrace.Core.Settings;
using MCPForUnity.Editor.ActionTrace.Semantics;

namespace MCPForUnity.Editor.ActionTrace.Core.Store
{
    /// <summary>
    /// Diagnostic and memory management functionality for EventStore.
    /// </summary>
    public static partial class EventStore
    {
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

        /// <summary>
        /// Get diagnostic information about memory usage.
        /// Useful for monitoring and debugging memory issues.
        /// </summary>
        public static string GetMemoryDiagnostics()
        {
            lock (_queryLock)
            {
                var settings = ActionTraceSettings.Instance;
                int hotEventCount = settings?.Storage.HotEventCount ?? 100;
                int maxEvents = settings?.Storage.MaxEvents ?? 800;

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
                        estimatedPayloadBytes += EstimatePayloadSize(evt.Payload);
                    }
                }

                // Estimate dehydrated events size (~100 bytes each)
                long dehydratedBytes = dehydratedCount * 100;
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
                    size += dict.Count * 100;
                else if (kvp.Value is System.Collections.ICollection list)
                    size += list.Count * 50;
                else
                    size += 50;
            }

            return size;
        }
    }
}
