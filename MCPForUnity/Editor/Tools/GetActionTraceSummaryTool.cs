using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Resources.ActionTrace;
using MCPForUnity.Editor.ActionTrace.Core.Store;
using MCPForUnity.Editor.ActionTrace.Core.Models;
using MCPForUnity.Editor.ActionTrace.Semantics;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.ActionTrace.Core;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// MCP tool for generating AI-friendly summaries of ActionTrace events.
    ///
    /// This is a "compressed view" tool designed for Agentic Workflow.
    /// Instead of returning hundreds of individual events, it returns:
    /// - Structured aggregates (counts by type, target, category)
    /// - Textual summary (human-readable description)
    /// - Warnings (detected anomalies like excessive modifications)
    /// - Suggested actions (AI can use these to decide next steps)
    ///
    /// Python wrapper: Server/src/services/tools/get_action_trace_summary.py
    /// </summary>
    [McpForUnityTool("get_action_trace_summary",
        Description = "Get AI-friendly summary of recent ActionTrace events. Returns categorized changes, warnings, and suggested actions to reduce token usage and improve context understanding.")]
    public static class GetActionTraceSummaryTool
    {
        /// <summary>
        /// Parameters for get_action_trace_summary tool.
        /// </summary>
        public class Parameters
        {
            /// <summary>
            /// Time window for the summary: '5m', '15m', '1h', 'today'
            /// </summary>
            [ToolParameter("Time window: '5m', '15m', '1h', 'today' (default: '1h')", Required = false, DefaultValue = "1h")]
            public string TimeRange { get; set; } = "1h";

            /// <summary>
            /// Maximum number of events to analyze for the summary (default: 200)
            /// </summary>
            [ToolParameter("Maximum events to analyze (1-500, default: 200)", Required = false, DefaultValue = "200")]
            public int Limit { get; set; } = 200;

            /// <summary>
            /// Filter by task ID (only show events associated with this task)
            /// </summary>
            [ToolParameter("Filter by task ID (for multi-agent scenarios)", Required = false)]
            public string TaskId { get; set; }

            /// <summary>
            /// Filter by conversation ID
            /// </summary>
            [ToolParameter("Filter by conversation ID", Required = false)]
            public string ConversationId { get; set; }

            /// <summary>
            /// Minimum importance level (low/medium/high/critical)
            /// </summary>
            [ToolParameter("Minimum importance level (low/medium/high/critical)", Required = false, DefaultValue = "low")]
            public string MinImportance { get; set; } = "low";
        }

        /// <summary>
        /// Main handler for generating action trace summaries.
        /// </summary>
        public static object HandleCommand(JObject @params)
        {
            try
            {
                // Parse parameters
                string timeRange = GetTimeRange(@params);
                int limit = GetLimit(@params);
                string taskId = GetTaskId(@params);
                string conversationId = GetConversationId(@params);
                float minImportance = GetMinImportance(@params);

                // Calculate time threshold
                long? sinceSequence = CalculateSinceSequence(timeRange);

                // Query events
                var events = EventStore.Query(limit, sinceSequence);

                // Apply disabled types filter
                events = ApplyDisabledTypesFilter(events);

                // Apply importance filter
                var scorer = new DefaultEventScorer();
                var filteredEvents = events
                    .Where(e => scorer.Score(e) >= minImportance)
                    .ToList();

                // Apply task-level filtering
                filteredEvents = ApplyTaskFilters(filteredEvents, taskId, conversationId);

                if (filteredEvents.Count == 0)
                {
                    return new SuccessResponse("No events found for the specified criteria.", new
                    {
                        time_range = timeRange,
                        summary = "No significant activity detected in the specified time range.",
                        categories = new
                        {
                            total_count = 0,
                            by_type = new Dictionary<string, int>(),
                            by_importance = new Dictionary<string, int>()
                        },
                        warnings = Array.Empty<object>(),
                        suggested_actions = Array.Empty<object>()
                    });
                }

                // Generate summary
                var summary = GenerateSummary(filteredEvents, timeRange);

                return new SuccessResponse($"Generated summary for {filteredEvents.Count} events.", summary);
            }
            catch (Exception ex)
            {
                McpLog.Error($"[GetActionTraceSummaryTool] Error: {ex.Message}");
                return new ErrorResponse($"Error generating ActionTrace summary: {ex.Message}");
            }
        }

        /// <summary>
        /// Generate the structured summary from filtered events.
        /// </summary>
        private static object GenerateSummary(List<EditorEvent> events, string timeRange)
        {
            // Aggregates
            var byType = new Dictionary<string, int>();
            var byImportance = new Dictionary<string, int>();
            var byTarget = new Dictionary<string, TargetStats>();
            var errorEvents = new List<object>();
            var warnings = new List<string>();
            var suggestions = new List<string>();

            // Track event types for categorization
            int createdCount = 0;
            int deletedCount = 0;
            int modifiedCount = 0;
            int errorCount = 0;

            // Calculate time range from actual events
            long startTimeMs = events[0].TimestampUnixMs;
            long endTimeMs = events[events.Count - 1].TimestampUnixMs;
            long durationMs = endTimeMs - startTimeMs;

            foreach (var evt in events)
            {
                // Count by type
                if (!byType.ContainsKey(evt.Type))
                    byType[evt.Type] = 0;
                byType[evt.Type]++;

                // Count by importance (using scorer)
                var scorer = new DefaultEventScorer();
                float importance = scorer.Score(evt);
                string importanceCategory = GetImportanceCategory(importance);
                if (!byImportance.ContainsKey(importanceCategory))
                    byImportance[importanceCategory] = 0;
                byImportance[importanceCategory]++;

                // Get target name
                var targetInfo = GlobalIdHelper.GetInstanceInfo(evt.TargetId);
                string targetName = targetInfo.displayName ?? evt.TargetId;

                // Track by target
                if (!byTarget.ContainsKey(targetName))
                    byTarget[targetName] = new TargetStats { Name = targetName };
                byTarget[targetName].Count++;
                byTarget[targetName].Types.Add(evt.Type);

                // Categorize event
                string evtTypeLower = evt.Type.ToLower();
                if (evtTypeLower.Contains("create") || evtTypeLower.Contains("add"))
                    createdCount++;
                else if (evtTypeLower.Contains("delete") || evtTypeLower.Contains("destroy") || evtTypeLower.Contains("remove"))
                    deletedCount++;
                else if (evtTypeLower.Contains("modify") || evtTypeLower.Contains("change") || evtTypeLower.Contains("set"))
                    modifiedCount++;

                // Check for errors
                // P0 Fix: Exclude AINote events from error counting (they are intentionally critical but not errors)
                if (!string.Equals(evt.Type, "AINote", StringComparison.OrdinalIgnoreCase) &&
                    (importanceCategory == "critical" || evtTypeLower.Contains("error") || evtTypeLower.Contains("exception")))
                {
                    errorCount++;
                    errorEvents.Add(new
                    {
                        sequence = evt.Sequence,
                        type = evt.Type,
                        target = targetName,
                        summary = evt.GetSummary()
                    });
                }
            }

            // Detect anomalies and generate warnings
            var topTargets = byTarget.OrderByDescending(kv => kv.Value.Count).Take(5).ToList();
            foreach (var targetStat in topTargets)
            {
                // Flag excessive modifications (potential loop or thrashing)
                if (targetStat.Value.Count > 20)
                {
                    warnings.Add($"Object '{targetStat.Key}' was modified {targetStat.Value.Count} times " +
                        $"(potential infinite loop or rapid-fire operations)");
                }
            }

            // Check for high error rate
            if (errorCount > 0)
            {
                double errorRate = (double)errorCount / events.Count;
                if (errorRate > 0.1) // More than 10% errors
                {
                    warnings.Add($"High error rate detected: {errorCount}/{events.Count} operations failed");
                }
            }

            // Generate suggested actions based on patterns
            if (errorCount > 0)
                suggestions.Add("Investigate compilation or runtime errors before proceeding");

            if (createdCount > 0 && !events.Any(e => e.Type.ToLower().Contains("save")))
            {
                suggestions.Add("Consider saving the scene (unsaved changes detected)");
            }

            if (deletedCount > 5)
            {
                suggestions.Add("Review bulk deletions (potential accidental mass delete)");
            }

            if (modifiedCount > 50)
            {
                suggestions.Add("Consider creating a prefab for frequently modified objects");
            }

            // Build textual summary
            var summaryParts = new List<string>();
            if (createdCount > 0)
                summaryParts.Add($"created {createdCount} objects");
            if (modifiedCount > 0)
                summaryParts.Add($"modified {modifiedCount} properties/objects");
            if (deletedCount > 0)
                summaryParts.Add($"deleted {deletedCount} objects");
            if (errorCount > 0)
                summaryParts.Add($"encountered {errorCount} error(s)");

            string summaryText = summaryParts.Count > 0
                ? $"In the last {timeRange}, {string.Join(", ", summaryParts)}."
                : $"No significant activity detected in the last {timeRange}.";

            // Build top targets summary
            var topTargetsSummary = topTargets.Take(3).Select(t => new
            {
                name = t.Key,
                count = t.Value.Count,
                types = t.Value.Types.Take(5).ToList()
            }).ToList();

            return new
            {
                time_range = timeRange,
                duration_analyzed_ms = durationMs,
                summary = summaryText,
                categories = new
                {
                    total_count = events.Count,
                    created_count = createdCount,
                    modified_count = modifiedCount,
                    deleted_count = deletedCount,
                    error_count = errorCount,
                    by_type = byType.OrderByDescending(kv => kv.Value).Take(10).ToDictionary(kv => kv.Key, kv => kv.Value),
                    by_importance = byImportance
                },
                top_targets = topTargetsSummary,
                errors = errorEvents.Take(5).ToList(),
                warnings = warnings.Take(3).ToList(),
                suggested_actions = suggestions,
                current_sequence = EventStore.CurrentSequence
            };
        }

        /// <summary>
        /// Convert importance score to category string.
        /// </summary>
        private static string GetImportanceCategory(float score)
        {
            if (score >= 0.9f) return "critical";
            if (score >= 0.7f) return "high";
            if (score >= 0.4f) return "medium";
            return "low";
        }

        /// <summary>
        /// Parse time_range parameter and convert to since_sequence threshold.
        /// P0 Fix: Actually implement time-based filtering by querying events and finding sequence threshold.
        /// </summary>
        private static long? CalculateSinceSequence(string timeRange)
        {
            if (string.IsNullOrEmpty(timeRange))
                return null;

            // Get current timestamp
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // Calculate threshold based on time range
            long thresholdMs = timeRange.ToLower() switch
            {
                "5m" => nowMs - (5 * 60 * 1000),
                "15m" => nowMs - (15 * 60 * 1000),
                "1h" => nowMs - (60 * 60 * 1000),
                "today" => nowMs - (24 * 60 * 60 * 1000),
                _ => nowMs - (60 * 60 * 1000) // Default to 1h
            };

            // P0 Fix: Find the sequence number closest to the time threshold
            // Query recent events to find the sequence number at the threshold time
            try
            {
                // Query a larger sample to find events around the threshold
                var sampleEvents = EventStore.Query(500, null);

                // Find first event after threshold
                var thresholdEvent = sampleEvents.FirstOrDefault(e => e.TimestampUnixMs >= thresholdMs);

                if (thresholdEvent != null)
                {
                    // Return sequence number to filter from this point
                    return thresholdEvent.Sequence;
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"[GetActionTraceSummaryTool] Failed to calculate since_sequence for time_range '{timeRange}': {ex.Message}");
            }

            // Fallback: return null (query from beginning within limit)
            return null;
        }

        #region Parameter Parsers (reuse from ActionTraceViewResource)

        private static string GetTimeRange(JObject @params)
        {
            var token = @params["time_range"] ?? @params["timeRange"];
            string value = token?.ToString()?.ToLower()?.Trim();

            // Validate against allowed values
            if (value == "5m" || value == "15m" || value == "1h" || value == "today")
                return value;

            return "1h"; // Default
        }

        private static int GetLimit(JObject @params)
        {
            var token = @params["limit"];
            if (token != null && int.TryParse(token.ToString(), out int limit))
            {
                return Math.Clamp(limit, 1, 500);
            }
            return 200; // Default for summary
        }

        private static string GetTaskId(JObject @params)
        {
            var token = @params["task_id"] ?? @params["taskId"];
            return token?.ToString();
        }

        private static string GetConversationId(JObject @params)
        {
            var token = @params["conversation_id"] ?? @params["conversationId"];
            return token?.ToString();
        }

        private static float GetMinImportance(JObject @params)
        {
            var token = @params["min_importance"] ?? @params["minImportance"];
            if (token != null)
            {
                string value = token?.ToString()?.ToLower()?.Trim();
                if (!string.IsNullOrEmpty(value))
                {
                    return value switch
                    {
                        "low" => 0.0f,
                        "medium" => 0.4f,
                        "high" => 0.7f,
                        "critical" => 0.9f,
                        _ => float.TryParse(value, out float val) ? val : 0.0f
                    };
                }
            }
            return 0.0f; // Default to low for summary (include everything)
        }

        private static IReadOnlyList<EditorEvent> ApplyDisabledTypesFilter(IReadOnlyList<EditorEvent> events)
        {
            var settings = ActionTrace.Core.Settings.ActionTraceSettings.Instance;
            if (settings == null)
                return events;

            var disabledTypes = settings.Filtering.DisabledEventTypes;
            if (disabledTypes == null || disabledTypes.Length == 0)
                return events;

            return events.Where(e => !IsEventTypeDisabled(e.Type, disabledTypes)).ToList();
        }

        private static bool IsEventTypeDisabled(string eventType, string[] disabledTypes)
        {
            foreach (string disabled in disabledTypes)
            {
                if (string.Equals(eventType, disabled, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static List<EditorEvent> ApplyTaskFilters(List<EditorEvent> events, string taskId, string conversationId)
        {
            if (string.IsNullOrEmpty(taskId) && string.IsNullOrEmpty(conversationId))
                return events;

            return events.Where(e =>
            {
                if (e.Type != "AINote")
                    return true;

                if (e.Payload == null)
                    return false;

                if (!string.IsNullOrEmpty(taskId))
                {
                    if (e.Payload.TryGetValue("task_id", out var taskVal))
                    {
                        if (taskVal?.ToString() != taskId)
                            return false;
                    }
                    else
                    {
                        return false;
                    }
                }

                // P0 Fix: Require conversation_id to be present when filter is specified
                if (!string.IsNullOrEmpty(conversationId))
                {
                    if (e.Payload.TryGetValue("conversation_id", out var convVal))
                    {
                        if (convVal?.ToString() != conversationId)
                            return false;
                    }
                    else
                    {
                        // P0 Fix: Reject events without conversation_id when filter is specified
                        return false;
                    }
                }

                return true;
            }).ToList();
        }

        #endregion

        /// <summary>
        /// Statistics for a single target.
        /// </summary>
        private class TargetStats
        {
            public string Name { get; set; }
            public int Count { get; set; }
            public HashSet<string> Types { get; set; } = new HashSet<string>();
        }
    }
}
