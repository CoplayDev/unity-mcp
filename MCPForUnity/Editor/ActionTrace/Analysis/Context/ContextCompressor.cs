using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.ActionTrace.Core;
using MCPForUnity.Editor.ActionTrace.Core.Models;
using MCPForUnity.Editor.ActionTrace.Semantics;

namespace MCPForUnity.Editor.ActionTrace.Analysis.Context
{
    /// <summary>
    /// Configuration for context compression behavior.
    /// </summary>
    [Serializable]
    public sealed class ContextCompressionConfig
    {
        /// <summary>
        /// Minimum importance threshold for keeping full event payload.
        /// Events below this will be dehydrated (payload = null).
        /// </summary>
        public float MinImportanceForFullPayload = 0.5f;

        /// <summary>
        /// Always keep full payload for critical events (score >= this value).
        /// </summary>
        public float CriticalEventThreshold = 0.9f;

        /// <summary>
        /// Always keep events with these types (regardless of importance).
        /// </summary>
        public string[] AlwaysKeepEventTypes = new[]
        {
            EventTypes.BuildFailed,
            EventTypes.ScriptCompilationFailed,
            EventTypes.SceneSaved,
            "AINote"
        };

        /// <summary>
        /// Time window for "recent events" summary (minutes).
        /// Recent events are always kept with full payload.
        /// </summary>
        public int RecentEventsWindowMinutes = 10;

        /// <summary>
        /// Maximum number of events to keep in compressed context.
        /// </summary>
        public int MaxCompressedEvents = 200;

        /// <summary>
        /// Target compression ratio (0.0 - 1.0).
        /// 1.0 = no compression, 0.1 = aggressive compression.
        /// </summary>
        public float TargetCompressionRatio = 0.3f;

        /// <summary>
        /// Enable smart preservation of asset-related events.
        /// </summary>
        public bool PreserveAssetEvents = true;

        /// <summary>
        /// Enable smart preservation of error/failure events.
        /// </summary>
        public bool PreserveErrorEvents = true;
    }

    /// <summary>
    /// Result of context compression.
    /// </summary>
    public sealed class CompressedContext
    {
        public List<EditorEvent> PreservedEvents;
        public List<EditorEvent> DehydratedEvents;
        public List<EditorEvent> SummaryEvents;

        // Statistics
        public int OriginalCount;
        public int PreservedCount;
        public int DehydratedCount;
        public int SummaryCount;
        public float CompressionRatio;

        public int TotalEvents => PreservedCount + DehydratedCount + SummaryCount;
    }

    /// <summary>
    /// Compresses event context to reduce memory while preserving important information.
    ///
    /// Strategy:
    /// 1. Always keep critical events (high importance, errors, builds)
    /// 2. Keep recent events with full payload
    /// 3. Dehydrate older events (payload = null)
    /// 4. Generate summary for long-running operations
    /// </summary>
    public sealed class ContextCompressor
    {
        private readonly ContextCompressionConfig _config;
        private readonly IEventScorer _scorer;

        public ContextCompressor(ContextCompressionConfig config = null, IEventScorer scorer = null)
        {
            _config = config ?? new ContextCompressionConfig();
            _scorer = scorer ?? new Semantics.DefaultEventScorer();
        }

        /// <summary>
        /// Compress a list of events, preserving important information.
        /// Returns a new list with compressed events (original list is not modified).
        /// </summary>
        public List<EditorEvent> Compress(IReadOnlyList<EditorEvent> events)
        {
            if (events == null || events.Count == 0)
                return new List<EditorEvent>();

            var result = new CompressedContext
            {
                OriginalCount = events.Count,
                PreservedEvents = new List<EditorEvent>(),
                DehydratedEvents = new List<EditorEvent>(),
                SummaryEvents = new List<EditorEvent>()
            };

            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long recentThresholdMs = nowMs - (_config.RecentEventsWindowMinutes * 60 * 1000);

            // Separate events into categories
            foreach (var evt in events)
            {
                if (ShouldPreserveFull(evt, nowMs, recentThresholdMs))
                {
                    result.PreservedEvents.Add(evt);
                }
                else
                {
                    // Create dehydrated copy
                    var dehydrated = evt.Dehydrate();
                    result.DehydratedEvents.Add(dehydrated);
                }
            }

            // Sort preserved events by timestamp
            result.PreservedEvents.Sort((a, b) => a.TimestampUnixMs.CompareTo(b.TimestampUnixMs));

            // Limit total count if needed
            int totalAfterPreserve = result.PreservedEvents.Count + result.DehydratedEvents.Count;
            if (totalAfterPreserve > _config.MaxCompressedEvents)
            {
                // Keep all preserved, trim dehydrated
                int maxDehydrated = _config.MaxCompressedEvents - result.PreservedEvents.Count;
                if (maxDehydrated < 0) maxDehydrated = 0;

                // Keep most recent dehydrated events
                result.DehydratedEvents = result.DehydratedEvents
                    .OrderByDescending(e => e.TimestampUnixMs)
                    .Take(maxDehydrated)
                    .ToList();
            }

            // Update statistics
            result.PreservedCount = result.PreservedEvents.Count;
            result.DehydratedCount = result.DehydratedEvents.Count;
            result.CompressionRatio = result.OriginalCount > 0
                ? (float)result.TotalEvents / result.OriginalCount
                : 1f;

            // Combine results
            var compressed = new List<EditorEvent>(result.TotalEvents);
            compressed.AddRange(result.PreservedEvents);
            compressed.AddRange(result.DehydratedEvents);

            // Sort by timestamp
            compressed.Sort((a, b) => a.TimestampUnixMs.CompareTo(b.TimestampUnixMs));

            return compressed;
        }

        /// <summary>
        /// Compress with detailed result information.
        /// </summary>
        public CompressedContext CompressWithDetails(IReadOnlyList<EditorEvent> events)
        {
            if (events == null || events.Count == 0)
                return new CompressedContext { OriginalCount = 0 };

            var result = new CompressedContext
            {
                OriginalCount = events.Count,
                PreservedEvents = new List<EditorEvent>(),
                DehydratedEvents = new List<EditorEvent>(),
                SummaryEvents = new List<EditorEvent>()
            };

            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long recentThresholdMs = nowMs - (_config.RecentEventsWindowMinutes * 60 * 1000);

            foreach (var evt in events)
            {
                if (ShouldPreserveFull(evt, nowMs, recentThresholdMs))
                {
                    result.PreservedEvents.Add(evt);
                }
                else
                {
                    var dehydrated = evt.Dehydrate();
                    result.DehydratedEvents.Add(dehydrated);
                }
            }

            result.PreservedEvents.Sort((a, b) => a.TimestampUnixMs.CompareTo(b.TimestampUnixMs));

            // Apply size limits
            ApplySizeLimits(result);

            result.PreservedCount = result.PreservedEvents.Count;
            result.DehydratedCount = result.DehydratedEvents.Count;
            result.CompressionRatio = result.OriginalCount > 0
                ? (float)result.TotalEvents / result.OriginalCount
                : 1f;

            return result;
        }

        /// <summary>
        /// Generate a summary of events for a time range.
        /// Useful for compressing long event sequences.
        /// </summary>
        public string SummarizeEvents(IReadOnlyList<EditorEvent> events, int startIdx, int count)
        {
            if (events == null || startIdx < 0 || startIdx >= events.Count)
                return string.Empty;

            int endIdx = Math.Min(startIdx + count, events.Count);
            int actualCount = endIdx - startIdx;

            if (actualCount == 0)
                return string.Empty;

            // Count by type
            var typeCounts = new Dictionary<string, int>();
            for (int i = startIdx; i < endIdx; i++)
            {
                string type = events[i].Type ?? "Unknown";
                typeCounts.TryGetValue(type, out int existingCount);
                typeCounts[type] = existingCount + 1;
            }

            // Build summary
            var summary = new System.Text.StringBuilder();
            summary.Append(actualCount).Append(" events: ");

            int shown = 0;
            foreach (var kvp in typeCounts.OrderByDescending(x => x.Value))
            {
                if (shown > 0) summary.Append(", ");
                summary.Append(kvp.Key).Append(" (").Append(kvp.Value).Append(")");
                shown++;
                if (shown >= 5) break;
            }

            if (typeCounts.Count > 5)
            {
                summary.Append(", ...");
            }

            return summary.ToString();
        }

        /// <summary>
        /// Check if an event should be preserved with full payload.
        /// </summary>
        private bool ShouldPreserveFull(EditorEvent evt, long nowMs, long recentThresholdMs)
        {
            // Always preserve if in "always keep" list
            if (_config.AlwaysKeepEventTypes != null)
            {
                foreach (string type in _config.AlwaysKeepEventTypes)
                {
                    if (evt.Type == type)
                        return true;
                }
            }

            // Always preserve critical events
            float importance = _scorer.Score(evt);
            if (importance >= _config.CriticalEventThreshold)
                return true;

            // Preserve recent events
            if (evt.TimestampUnixMs >= recentThresholdMs)
                return true;

            // Preserve high importance events
            if (importance >= _config.MinImportanceForFullPayload)
                return true;

            // Preserve asset events if configured
            if (_config.PreserveAssetEvents && IsAssetEvent(evt))
                return true;

            // Preserve error events if configured
            if (_config.PreserveErrorEvents && IsErrorEvent(evt))
                return true;

            return false;
        }

        private bool IsAssetEvent(EditorEvent evt)
        {
            return evt.Type == EventTypes.AssetImported ||
                   evt.Type == EventTypes.AssetCreated ||
                   evt.Type == EventTypes.AssetDeleted ||
                   evt.Type == EventTypes.AssetMoved ||
                   evt.Type == EventTypes.AssetModified;
        }

        private bool IsErrorEvent(EditorEvent evt)
        {
            return evt.Type == EventTypes.BuildFailed ||
                   evt.Type == EventTypes.ScriptCompilationFailed ||
                   (evt.Payload != null && evt.Payload.ContainsKey("error"));
        }

        private void ApplySizeLimits(CompressedContext result)
        {
            int totalAfterPreserve = result.PreservedEvents.Count + result.DehydratedEvents.Count;

            if (totalAfterPreserve <= _config.MaxCompressedEvents)
                return;

            int maxDehydrated = _config.MaxCompressedEvents - result.PreservedEvents.Count;
            if (maxDehydrated < 0) maxDehydrated = 0;

            // Keep most recent dehydrated events
            result.DehydratedEvents = result.DehydratedEvents
                .OrderByDescending(e => e.TimestampUnixMs)
                .Take(maxDehydrated)
                .ToList();
        }
    }

    /// <summary>
    /// Extension methods for context compression.
    /// </summary>
    public static class ContextCompressionExtensions
    {
        /// <summary>
        /// Compress events with default configuration.
        /// </summary>
        public static List<EditorEvent> Compress(this IReadOnlyList<EditorEvent> events)
        {
            var compressor = new ContextCompressor();
            return compressor.Compress(events);
        }

        /// <summary>
        /// Compress events with custom configuration.
        /// </summary>
        public static List<EditorEvent> Compress(this IReadOnlyList<EditorEvent> events, ContextCompressionConfig config)
        {
            var compressor = new ContextCompressor(config);
            return compressor.Compress(events);
        }

        /// <summary>
        /// Compress events targeting a specific count.
        /// </summary>
        public static List<EditorEvent> CompressTo(this IReadOnlyList<EditorEvent> events, int targetCount)
        {
            var config = new ContextCompressionConfig { MaxCompressedEvents = targetCount };
            var compressor = new ContextCompressor(config);
            return compressor.Compress(events);
        }

        /// <summary>
        /// Get recent events within a time window.
        /// </summary>
        public static List<EditorEvent> GetRecent(this IReadOnlyList<EditorEvent> events, int minutes)
        {
            if (events == null || events.Count == 0)
                return new List<EditorEvent>();

            long thresholdMs = DateTimeOffset.UtcNow.AddMinutes(-minutes).ToUnixTimeMilliseconds();

            return events
                .Where(e => e.TimestampUnixMs >= thresholdMs)
                .OrderBy(e => e.TimestampUnixMs)
                .ToList();
        }

        /// <summary>
        /// Get high importance events.
        /// </summary>
        public static List<EditorEvent> GetHighImportance(this IReadOnlyList<EditorEvent> events, float threshold = 0.7f)
        {
            if (events == null || events.Count == 0)
                return new List<EditorEvent>();

            var scorer = new Semantics.DefaultEventScorer();

            return events
                .Where(e => scorer.Score(e) >= threshold)
                .OrderByDescending(e => e.TimestampUnixMs)
                .ToList();
        }

        /// <summary>
        /// Get events of specific types.
        /// </summary>
        public static List<EditorEvent> GetByTypes(this IReadOnlyList<EditorEvent> events, params string[] types)
        {
            if (events == null || events.Count == 0 || types == null || types.Length == 0)
                return new List<EditorEvent>();

            var typeSet = new HashSet<string>(types);
            return events.Where(e => typeSet.Contains(e.Type)).ToList();
        }

        /// <summary>
        /// Deduplicate events by target and type within a time window.
        /// </summary>
        public static List<EditorEvent> Deduplicate(this IReadOnlyList<EditorEvent> events, int windowMs = 100)
        {
            if (events == null || events.Count == 0)
                return new List<EditorEvent>();

            var seen = new HashSet<string>();
            var result = new List<EditorEvent>();

            // Process in chronological order
            foreach (var evt in events.OrderBy(e => e.TimestampUnixMs))
            {
                string key = $"{evt.Type}|{evt.TargetId}|{evt.TimestampUnixMs / windowMs}";

                if (seen.Add(key))
                {
                    result.Add(evt);
                }
            }

            return result;
        }
    }
}
