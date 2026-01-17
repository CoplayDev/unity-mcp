using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.ActionTrace.Core;
using MCPForUnity.Editor.ActionTrace.Semantics;

namespace MCPForUnity.Editor.ActionTrace.Query
{
    /// <summary>
    /// Statistical analysis results for event data.
    /// Provides AI-friendly insights about captured events.
    /// </summary>
    public sealed class EventStatistics
    {
        // Basic counts
        public int TotalEvents;
        public int CriticalEvents;
        public int HighImportanceEvents;
        public int MediumImportanceEvents;
        public int LowImportanceEvents;

        // Time range
        public long TimeRangeStartMs;
        public long TimeRangeEndMs;
        public long TimeSpanMs;
        public double EventsPerMinute;

        // Type distribution
        public Dictionary<string, int> EventTypeCounts;
        public Dictionary<EventCategory, int> CategoryCounts;

        // Activity patterns
        public List<ActivityPeriod> ActivePeriods;
        public List<ActivityPeriod> IdlePeriods;

        // Top targets
        public List<TargetStats> TopTargets;

        // Errors and issues
        public List<string> ErrorMessages;
        public int ErrorCount;

        // Recent trends (last N minutes)
        public TrendInfo RecentTrend;

        // Memory usage
        public long EstimatedMemoryBytes;
        public string EstimatedMemoryFormatted;

        public override string ToString()
        {
            string trendStr = RecentTrend?.Direction.ToString() ?? "Unknown";
            return $"Events: {TotalEvents} | " +
                   $"Critical: {CriticalEvents} | " +
                   $"Time: {FormatTimeRange()} | " +
                   $"Trend: {trendStr}";
        }

        private string FormatTimeRange()
        {
            if (TimeSpanMs < 60000)
                return $"{TimeSpanMs / 1000}s";
            if (TimeSpanMs < 3600000)
                return $"{TimeSpanMs / 60000}m";
            return $"{TimeSpanMs / 3600000}h";
        }
    }

    /// <summary>
    /// Represents a period of heightened or reduced activity.
    /// </summary>
    public sealed class ActivityPeriod
    {
        public long StartMs;
        public long EndMs;
        public int EventCount;
        public double EventsPerMinute;
        public bool IsHighActivity;

        public TimeSpan Duration => TimeSpan.FromMilliseconds(EndMs - StartMs);

        public override string ToString()
        {
            string activity = IsHighActivity ? "High" : "Low";
            return $"{activity} activity: {EventCount} events over {Duration.TotalMinutes:F1}m ({EventsPerMinute:F1}/min)";
        }
    }

    /// <summary>
    /// Statistics for a specific target (GameObject, asset, etc.).
    /// </summary>
    public sealed class TargetStats
    {
        public string TargetId;
        public string DisplayName;
        public int EventCount;
        public List<string> EventTypes;
        public long LastActivityMs;
        public double ActivityScore; // Composite score of frequency + recency

        public override string ToString()
        {
            return $"{DisplayName}: {EventCount} events (score: {ActivityScore:F1})";
        }
    }

    /// <summary>
    /// Trend information for recent events.
    /// </summary>
    public sealed class TrendInfo
    {
        public TrendDirection Direction;
        public double ChangePercentage;
        public int PreviousCount;
        public int CurrentCount;

        public override string ToString()
        {
            return $"{Direction} ({ChangePercentage:+0;-0}%): {PreviousCount} → {CurrentCount} events";
        }
    }

    /// <summary>
    /// Direction of activity trend.
    /// </summary>
    public enum TrendDirection
    {
        Increasing,
        Decreasing,
        Stable
    }

    /// <summary>
    /// Provides statistical analysis of event data.
    /// Designed to give AI agents basic data insights.
    /// </summary>
    public sealed class EventStatisticsAnalyzer
    {
        private readonly IEventScorer _scorer;
        private readonly IEventCategorizer _categorizer;

        public EventStatisticsAnalyzer(IEventScorer scorer = null, IEventCategorizer categorizer = null)
        {
            _scorer = scorer ?? new DefaultEventScorer();
            _categorizer = categorizer ?? new DefaultCategorizer();
        }

        /// <summary>
        /// Analyze a collection of events and return statistics.
        /// </summary>
        public EventStatistics Analyze(IReadOnlyList<EditorEvent> events)
        {
            if (events == null || events.Count == 0)
            {
                return new EventStatistics();
            }

            var stats = new EventStatistics
            {
                TotalEvents = events.Count
            };

            // Time range
            var orderedEvents = events.OrderBy(e => e.TimestampUnixMs).ToList();
            stats.TimeRangeStartMs = orderedEvents[0].TimestampUnixMs;
            stats.TimeRangeEndMs = orderedEvents[^1].TimestampUnixMs;
            stats.TimeSpanMs = stats.TimeRangeEndMs - stats.TimeRangeStartMs;

            double minutes = stats.TimeSpanMs / 60000.0;
            stats.EventsPerMinute = minutes > 0 ? stats.TotalEvents / minutes : 0;

            // Importance distribution
            foreach (var evt in events)
            {
                float score = _scorer.Score(evt);
                string category = _categorizer.Categorize(score);

                switch (category)
                {
                    case "critical":
                        stats.CriticalEvents++;
                        break;
                    case "high":
                        stats.HighImportanceEvents++;
                        break;
                    case "medium":
                        stats.MediumImportanceEvents++;
                        break;
                    case "low":
                        stats.LowImportanceEvents++;
                        break;
                }
            }

            // Type distribution
            stats.EventTypeCounts = new Dictionary<string, int>();
            foreach (var evt in events)
            {
                string type = evt.Type ?? "Unknown";
                stats.EventTypeCounts.TryGetValue(type, out int count);
                stats.EventTypeCounts[type] = count + 1;
            }

            // Category distribution
            stats.CategoryCounts = new Dictionary<EventCategory, int>();
            foreach (var evt in events)
            {
                var meta = EventTypes.Metadata.Get(evt.Type);
                EventCategory category = meta?.Category ?? EventCategory.Unknown;
                stats.CategoryCounts.TryGetValue(category, out int count);
                stats.CategoryCounts[category] = count + 1;
            }

            // Activity periods
            stats.ActivePeriods = FindActivePeriods(orderedEvents);
            stats.IdlePeriods = FindIdlePeriods(orderedEvents);

            // Top targets
            stats.TopTargets = FindTopTargets(events);

            // Errors
            stats.ErrorMessages = FindErrors(events);
            stats.ErrorCount = stats.ErrorMessages.Count;

            // Recent trend
            stats.RecentTrend = CalculateTrend(orderedEvents);

            // Memory estimate
            stats.EstimatedMemoryBytes = EstimateMemory(events);
            stats.EstimatedMemoryFormatted = FormatMemory(stats.EstimatedMemoryBytes);

            return stats;
        }

        /// <summary>
        /// Get a quick summary suitable for AI consumption.
        /// </summary>
        public string GetSummary(EventStatistics stats)
        {
            if (stats == null) return "No statistics available";

            var summary = new System.Text.StringBuilder();

            summary.AppendLine("=== Event Statistics ===");
            summary.AppendLine($"Total Events: {stats.TotalEvents}");
            summary.AppendLine($"Time Range: {FormatTimestamp(stats.TimeRangeStartMs)} - {FormatTimestamp(stats.TimeRangeEndMs)}");
            summary.AppendLine($"Duration: {TimeSpan.FromMilliseconds(stats.TimeSpanMs).TotalMinutes:F1} minutes");
            summary.AppendLine($"Event Rate: {stats.EventsPerMinute:F1} events/minute");
            summary.AppendLine();

            summary.AppendLine("Importance Distribution:");
            summary.AppendLine($"  Critical: {stats.CriticalEvents}");
            summary.AppendLine($"  High: {stats.HighImportanceEvents}");
            summary.AppendLine($"  Medium: {stats.MediumImportanceEvents}");
            summary.AppendLine($"  Low: {stats.LowImportanceEvents}");
            summary.AppendLine();

            if (stats.TopTargets.Count > 0)
            {
                summary.AppendLine("Top Targets:");
                foreach (var target in stats.TopTargets.Take(5))
                {
                    summary.AppendLine($"  - {target.DisplayName}: {target.EventCount} events");
                }
                summary.AppendLine();
            }

            if (stats.ErrorCount > 0)
            {
                summary.AppendLine($"Errors: {stats.ErrorCount}");
                foreach (var error in stats.ErrorMessages.Take(3))
                {
                    summary.AppendLine($"  - {error}");
                }
                summary.AppendLine();
            }

            if (stats.RecentTrend != null)
            {
                summary.AppendLine($"Trend: {stats.RecentTrend.Direction} ({stats.RecentTrend.ChangePercentage:+0;-0}%)");
            }

            return summary.ToString();
        }

        /// <summary>
        /// Get event distribution by type as a formatted string.
        /// </summary>
        public string GetEventTypeDistribution(EventStatistics stats)
        {
            if (stats?.EventTypeCounts == null || stats.EventTypeCounts.Count == 0)
                return "No event types recorded";

            var sorted = stats.EventTypeCounts.OrderByDescending(x => x.Value);
            var lines = new List<string>();

            foreach (var kvp in sorted)
            {
                double percentage = (kvp.Value * 100.0) / stats.TotalEvents;
                lines.Add($"  {kvp.Key}: {kvp.Value} ({percentage:F1}%)");
            }

            return string.Join("\n", lines);
        }

        /// <summary>
        /// Get activity insights for the current session.
        /// </summary>
        public string GetActivityInsights(EventStatistics stats)
        {
            if (stats == null) return "No insights available";

            var insights = new System.Text.StringBuilder();

            // Overall activity level
            string activityLevel = stats.EventsPerMinute switch
            {
                > 10 => "Very High",
                > 5 => "High",
                > 2 => "Moderate",
                > 0.5 => "Low",
                _ => "Very Low"
            };
            insights.AppendLine($"Activity Level: {activityLevel} ({stats.EventsPerMinute:F1} events/min)");

            // Pattern detection
            if (stats.ActivePeriods.Count > 0)
            {
                var peak = stats.ActivePeriods.OrderByDescending(p => p.EventsPerMinute).FirstOrDefault();
                if (peak != null)
                {
                    insights.AppendLine($"Peak Activity: {peak.EventsPerMinute:F1} events/min");
                }
            }

            // Error rate
            if (stats.TotalEvents > 0)
            {
                double errorRate = (stats.ErrorCount * 100.0) / stats.TotalEvents;
                insights.AppendLine($"Error Rate: {errorRate:F1}%");
            }

            // Critical events
            if (stats.CriticalEvents > 0)
            {
                insights.AppendLine($"Critical Events: {stats.CriticalEvents} (requires attention)");
            }

            return insights.ToString();
        }

        // Private helper methods

        private List<ActivityPeriod> FindActivePeriods(List<EditorEvent> orderedEvents)
        {
            if (orderedEvents.Count < 10)
                return new List<ActivityPeriod>();

            var periods = new List<ActivityPeriod>();
            const int windowSize = 10;
            const double activityThreshold = 5.0; // events per minute

            for (int i = 0; i <= orderedEvents.Count - windowSize; i += windowSize)
            {
                long windowStart = orderedEvents[i].TimestampUnixMs;
                long windowEnd = orderedEvents[Math.Min(i + windowSize - 1, orderedEvents.Count - 1)].TimestampUnixMs;

                double eventsPerMin = (windowSize * 60000.0) / Math.Max(1, windowEnd - windowStart);

                if (eventsPerMin >= activityThreshold)
                {
                    periods.Add(new ActivityPeriod
                    {
                        StartMs = windowStart,
                        EndMs = windowEnd,
                        EventCount = windowSize,
                        EventsPerMinute = eventsPerMin,
                        IsHighActivity = true
                    });
                }
            }

            return periods;
        }

        private List<ActivityPeriod> FindIdlePeriods(List<EditorEvent> orderedEvents)
        {
            if (orderedEvents.Count < 2)
                return new List<ActivityPeriod>();

            var periods = new List<ActivityPeriod>();
            const long idleThresholdMs = 30000; // 30 seconds with no events

            for (int i = 1; i < orderedEvents.Count; i++)
            {
                long gap = orderedEvents[i].TimestampUnixMs - orderedEvents[i - 1].TimestampUnixMs;

                if (gap >= idleThresholdMs)
                {
                    periods.Add(new ActivityPeriod
                    {
                        StartMs = orderedEvents[i - 1].TimestampUnixMs,
                        EndMs = orderedEvents[i].TimestampUnixMs,
                        EventCount = 0,
                        EventsPerMinute = 0,
                        IsHighActivity = false
                    });
                }
            }

            return periods;
        }

        private List<TargetStats> FindTopTargets(IReadOnlyList<EditorEvent> events)
        {
            var targetEvents = new Dictionary<string, List<EditorEvent>>();

            foreach (var evt in events)
            {
                if (string.IsNullOrEmpty(evt.TargetId)) continue;

                if (!targetEvents.TryGetValue(evt.TargetId, out var list))
                {
                    list = new List<EditorEvent>();
                    targetEvents[evt.TargetId] = list;
                }
                list.Add(evt);
            }

            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var stats = new List<TargetStats>();

            foreach (var kvp in targetEvents)
            {
                var targetEventsList = kvp.Value;
                double recencyFactor = CalculateRecencyFactor(targetEventsList, nowMs);
                double activityScore = targetEventsList.Count * recencyFactor;

                stats.Add(new TargetStats
                {
                    TargetId = kvp.Key,
                    DisplayName = GetDisplayName(targetEventsList[^1]),
                    EventCount = targetEventsList.Count,
                    EventTypes = targetEventsList.Select(e => e.Type).Distinct().ToList(),
                    LastActivityMs = targetEventsList[^1].TimestampUnixMs,
                    ActivityScore = activityScore
                });
            }

            return stats.OrderByDescending(s => s.ActivityScore).Take(10).ToList();
        }

        private double CalculateRecencyFactor(List<EditorEvent> events, long nowMs)
        {
            if (events.Count == 0) return 0;

            long lastActivity = events[^1].TimestampUnixMs;
            long ageMs = nowMs - lastActivity;

            // Decay factor: 1.0 for recent, 0.1 for old (> 1 hour)
            if (ageMs < 300000) return 1.0; // < 5 min
            if (ageMs < 900000) return 0.7;  // < 15 min
            if (ageMs < 1800000) return 0.5; // < 30 min
            if (ageMs < 3600000) return 0.3; // < 1 hour
            return 0.1;
        }

        private string GetDisplayName(EditorEvent evt)
        {
            if (evt.Payload != null)
            {
                if (evt.Payload.TryGetValue("name", out var name))
                    return name.ToString();
                if (evt.Payload.TryGetValue("game_object", out var go))
                    return go.ToString();
                if (evt.Payload.TryGetValue("scene_name", out var scene))
                    return scene.ToString();
            }
            return evt.TargetId ?? "Unknown";
        }

        private List<string> FindErrors(IReadOnlyList<EditorEvent> events)
        {
            var errors = new List<string>();

            foreach (var evt in events)
            {
                if (evt.Type == EventTypes.BuildFailed)
                {
                    if (evt.Payload != null && evt.Payload.TryGetValue("error", out var error))
                        errors.Add($"Build: {error}");
                    else
                        errors.Add("Build failed");
                }
                else if (evt.Type == EventTypes.ScriptCompilationFailed)
                {
                    if (evt.Payload != null && evt.Payload.TryGetValue("errors", out var errs))
                        errors.Add($"Compilation: {errs}");
                    else
                        errors.Add("Script compilation failed");
                }
                else if (evt.Payload != null && evt.Payload.TryGetValue("error", out var err))
                {
                    errors.Add(err.ToString());
                }
            }

            return errors;
        }

        private TrendInfo CalculateTrend(List<EditorEvent> orderedEvents)
        {
            if (orderedEvents.Count < 10)
                return null;

            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long recentWindowMs = 5 * 60 * 1000; // Last 5 minutes
            long previousWindowMs = 5 * 60 * 1000; // 5-10 minutes ago

            long recentStart = nowMs - recentWindowMs;
            long previousEnd = recentStart;
            long previousStart = previousEnd - previousWindowMs;

            int recentCount = orderedEvents.Count(e => e.TimestampUnixMs >= recentStart);
            int previousCount = orderedEvents.Count(e => e.TimestampUnixMs >= previousStart && e.TimestampUnixMs < previousEnd);

            TrendDirection direction;
            double changePercent = 0;

            if (previousCount == 0)
            {
                direction = recentCount > 0 ? TrendDirection.Increasing : TrendDirection.Stable;
            }
            else
            {
                changePercent = ((recentCount - previousCount) * 100.0) / previousCount;

                if (changePercent > 20)
                    direction = TrendDirection.Increasing;
                else if (changePercent < -20)
                    direction = TrendDirection.Decreasing;
                else
                    direction = TrendDirection.Stable;
            }

            return new TrendInfo
            {
                Direction = direction,
                ChangePercentage = changePercent,
                PreviousCount = previousCount,
                CurrentCount = recentCount
            };
        }

        private long EstimateMemory(IReadOnlyList<EditorEvent> events)
        {
            // Approximate: 300 bytes per hydrated event, 100 bytes per dehydrated
            long total = 0;
            foreach (var evt in events)
            {
                total += evt.Payload == null ? 100 : 300;
            }
            return total;
        }

        private string FormatMemory(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024} KB";
            return $"{bytes / (1024 * 1024)} MB";
        }

        private string FormatTimestamp(long ms)
        {
            var dt = DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime();
            return dt.ToString("HH:mm:ss");
        }
    }

    /// <summary>
    /// Extension methods for quick statistics.
    /// </summary>
    public static class StatisticsExtensions
    {
        /// <summary>
        /// Get quick statistics for events.
        /// </summary>
        public static EventStatistics GetStatistics(this IReadOnlyList<EditorEvent> events)
        {
            var analyzer = new EventStatisticsAnalyzer();
            return analyzer.Analyze(events);
        }

        /// <summary>
        /// Get a quick summary of events.
        /// </summary>
        public static string GetQuickSummary(this IReadOnlyList<EditorEvent> events)
        {
            if (events == null || events.Count == 0)
                return "No events";

            var analyzer = new EventStatisticsAnalyzer();
            var stats = analyzer.Analyze(events);
            return analyzer.GetSummary(stats);
        }

        /// <summary>
        /// Count events by type.
        /// </summary>
        public static Dictionary<string, int> CountByType(this IReadOnlyList<EditorEvent> events)
        {
            var counts = new Dictionary<string, int>();
            if (events == null) return counts;

            foreach (var evt in events)
            {
                string type = evt.Type ?? "Unknown";
                counts.TryGetValue(type, out int count);
                counts[type] = count + 1;
            }
            return counts;
        }

        /// <summary>
        /// Get most recent N events.
        /// </summary>
        public static List<EditorEvent> GetMostRecent(this IReadOnlyList<EditorEvent> events, int count)
        {
            if (events == null) return new List<EditorEvent>();
            return events.OrderByDescending(e => e.TimestampUnixMs).Take(count).ToList();
        }
    }
}
