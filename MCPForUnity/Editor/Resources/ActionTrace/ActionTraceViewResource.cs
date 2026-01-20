using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.ActionTrace.Context;
using MCPForUnity.Editor.ActionTrace.Core.Settings;
using MCPForUnity.Editor.ActionTrace.Core.Store;
using MCPForUnity.Editor.ActionTrace.Analysis.Query;
using MCPForUnity.Editor.ActionTrace.Analysis.Summarization;
using Newtonsoft.Json.Linq;
using static MCPForUnity.Editor.ActionTrace.Analysis.Query.ActionTraceQuery;
using MCPForUnity.Editor.ActionTrace.Core.Models;
using MCPForUnity.Editor.ActionTrace.Semantics;

namespace MCPForUnity.Editor.Resources.ActionTrace
{
    /// <summary>
    /// Response wrapper constants for ActionTraceView.
    /// Simplified schema: Basic, WithSemantics, Aggregated.
    /// </summary>
    internal static class ResponseSchemas
    {
        public const string Basic = "action_trace_view@1";
        public const string WithSemantics = "action_trace_view@2";
        public const string Aggregated = "action_trace_view@3";
    }

    /// <summary>
    /// Event type constants for filtering.
    /// </summary>
    internal static class EventTypes
    {
        public const string AINote = "AINote";
    }

    /// <summary>
    /// MCP resource for querying the action trace of editor events.
    ///
    /// URI: mcpforunity://action_trace_view
    ///
    /// Parameters:
    ///   - limit: Maximum number of events to return (default: 50)
    ///   - since_sequence: Only return events after this sequence number
    ///   - include_semantics: If true, include importance, category, intent (default: false)
    ///   - min_importance: Minimum importance score to include (default: "medium")
    ///                       Options: "low" (0.0+), "medium" (0.4+), "high" (0.7+), "critical" (0.9+)
    ///   - summary_only: If true, return aggregated transactions instead of raw events (default: false)
    ///   - task_id: Filter events by task ID (for AINote events)
    ///   - conversation_id: Filter events by conversation ID (for AINote events)
    ///
    /// L3 Semantic Whitelist:
    /// By default, only events with importance >= 0.4 (medium+) are returned.
    /// To include low-importance events like HierarchyChanged, specify min_importance="low".
    /// </summary>
    [McpForUnityResource("action_trace_view")]
    public static class ActionTraceViewResource
    {
        public static object HandleCommand(JObject @params)
        {
            try
            {
                int limit = GetLimit(@params);
                long? sinceSequence = GetSinceSequence(@params);
                bool includeSemantics = GetIncludeSemantics(@params);

                // L3 Semantic Whitelist: Parse minimum importance threshold
                float minImportance = GetMinImportance(@params);

                // Task-level filtering: Parse task_id and conversation_id
                string taskId = GetTaskId(@params);
                string conversationId = GetConversationId(@params);

                // P1.1 Transaction Aggregation: Parse summary_only parameter
                bool summaryOnly = GetSummaryOnly(@params);

                // If summary_only is requested, return aggregated transactions
                if (summaryOnly)
                {
                    return QueryAggregated(limit, sinceSequence, minImportance, taskId, conversationId);
                }

                // Decide query mode based on parameters
                if (includeSemantics)
                {
                    return QueryWithSemanticsOnly(limit, sinceSequence, minImportance, taskId, conversationId);
                }

                // Basic query without semantics (apply importance filter anyway)
                return QueryBasic(limit, sinceSequence, minImportance, taskId, conversationId);
            }
            catch (Exception ex)
            {
                McpLog.Error($"[ActionTraceViewResource] Error: {ex.Message}");
                return new ErrorResponse($"Error retrieving ActionTrace: {ex.Message}");
            }
        }

        /// <summary>
        /// Basic query without semantics.
        /// Applies L3 importance filter by default (medium+ importance).
        /// Supports task_id and conversation_id filtering.
        /// </summary>
        private static object QueryBasic(int limit, long? sinceSequence, float minImportance, string taskId, string conversationId)
        {
            var events = EventStore.Query(limit, sinceSequence);

            // Apply disabled event types filter
            events = ApplyDisabledTypesFilter(events);

            // L3 Semantic Whitelist: Filter by importance
            var scorer = new DefaultEventScorer();
            var filteredEvents = events
                .Where(e => scorer.Score(e) >= minImportance)
                .ToList();

            // Apply task-level filtering
            filteredEvents = ApplyTaskFilters(filteredEvents, taskId, conversationId);

            var eventItems = filteredEvents.Select(e => new
            {
                sequence = e.Sequence,
                timestamp_unix_ms = e.TimestampUnixMs,
                type = e.Type,
                target_id = e.TargetId,
                summary = e.GetSummary()
            }).ToArray();

            return new SuccessResponse("Retrieved ActionTrace events.", new
            {
                schema_version = ResponseSchemas.Basic,
                events = eventItems,
                total_count = eventItems.Length,
                current_sequence = EventStore.CurrentSequence
            });
        }

        /// <summary>
        /// Query with semantics.
        /// Applies L3 importance filter by default.
        /// Supports task_id and conversation_id filtering.
        /// </summary>
        private static object QueryWithSemanticsOnly(int limit, long? sinceSequence, float minImportance, string taskId, string conversationId)
        {
            var rawEvents = EventStore.Query(limit, sinceSequence);

            // Apply disabled event types filter
            rawEvents = ApplyDisabledTypesFilter(rawEvents);

            var query = new ActionTraceQuery();
            var projected = query.Project(rawEvents);

            // L3 Semantic Whitelist: Filter by importance
            var filtered = projected
                .Where(p => p.ImportanceScore >= minImportance)
                .ToList();

            // Apply task-level filtering
            filtered = ApplyTaskFiltersToProjected(filtered, taskId, conversationId);

            var eventItems = filtered.Select(p => new
            {
                sequence = p.Event.Sequence,
                timestamp_unix_ms = p.Event.TimestampUnixMs,
                type = p.Event.Type,
                target_id = p.Event.TargetId,
                summary = p.Event.GetSummary(),
                importance_score = p.ImportanceScore,
                importance_category = p.ImportanceCategory,
                inferred_intent = p.InferredIntent
            }).ToArray();

            return new SuccessResponse("Retrieved ActionTrace events with semantics.", new
            {
                schema_version = ResponseSchemas.WithSemantics,
                events = eventItems,
                total_count = eventItems.Length,
                current_sequence = EventStore.CurrentSequence
            });
        }

        /// <summary>
        /// Query with transaction aggregation.
        ///
        /// Returns AtomicOperation list instead of raw events.
        /// Reduces token consumption by grouping related events.
        /// Supports task_id and conversation_id filtering.
        /// </summary>
        private static object QueryAggregated(int limit, long? sinceSequence, float minImportance, string taskId, string conversationId)
        {
            // Step 1: Query raw events
            var events = EventStore.Query(limit, sinceSequence);

            // Step 2: Apply disabled event types filter
            events = ApplyDisabledTypesFilter(events);

            // Step 3: Apply importance filter (L3 Semantic Whitelist)
            var scorer = new DefaultEventScorer();
            var filteredEvents = events
                .Where(e => scorer.Score(e) >= minImportance)
                .ToList();

            // Step 4: Apply task-level filtering
            filteredEvents = ApplyTaskFilters(filteredEvents, taskId, conversationId);

            // Step 5: Aggregate into transactions
            var operations = TransactionAggregator.Aggregate(filteredEvents);

            // Step 6: Project to response format
            var eventItems = operations.Select(op => new
            {
                start_sequence = op.StartSequence,
                end_sequence = op.EndSequence,
                summary = op.Summary,
                event_count = op.EventCount,
                duration_ms = op.DurationMs,
                tool_call_id = op.ToolCallId,
                triggered_by_tool = op.TriggeredByTool
            }).ToArray();

            return new SuccessResponse($"Retrieved {eventItems.Length} aggregated operations.", new
            {
                schema_version = ResponseSchemas.Aggregated,
                events = eventItems,
                total_count = eventItems.Length,
                current_sequence = EventStore.CurrentSequence
            });
        }

        private static int GetLimit(JObject @params)
        {
            var limitToken = @params["limit"] ?? @params["count"];
            if (limitToken != null && int.TryParse(limitToken.ToString(), out int limit))
            {
                return Math.Clamp(limit, 1, 1000);
            }
            return 50; // Default
        }

        private static long? GetSinceSequence(JObject @params)
        {
            var sinceToken = @params["since_sequence"] ?? @params["sinceSequence"] ?? @params["since"];
            if (sinceToken != null && long.TryParse(sinceToken.ToString(), out long since))
            {
                return since;
            }
            return null;
        }

        private static bool GetIncludeSemantics(JObject @params)
        {
            var includeToken = @params["include_semantics"] ?? @params["includeSemantics"];
            if (includeToken != null)
            {
                if (bool.TryParse(includeToken.ToString(), out bool include))
                {
                    return include;
                }
            }
            return false;
        }

        /// <summary>
        /// L3 Semantic Whitelist: Parse minimum importance threshold.
        ///
        /// Default: "medium" (0.4) - filters out low-importance noise like HierarchyChanged
        ///
        /// Options:
        ///   - "low" or 0.0: Include all events
        ///   - "medium" or 0.4: Include meaningful operations (default)
        ///   - "high" or 0.7: Include only significant changes
        ///   - "critical" or 0.9: Include only critical events (build failures, AI notes)
        ///
        /// Returns: float threshold for importance filtering
        /// </summary>
        private static float GetMinImportance(JObject @params)
        {
            var importanceToken = @params["min_importance"] ?? @params["minImportance"];
            if (importanceToken != null)
            {
                string importanceStr = importanceToken.ToString()?.ToLower()?.Trim();

                // Parse string values
                if (!string.IsNullOrEmpty(importanceStr))
                {
                    return importanceStr switch
                    {
                        "low" => 0.0f,
                        "medium" => 0.4f,
                        "high" => 0.7f,
                        "critical" => 0.9f,
                        _ => float.TryParse(importanceStr, out float val) ? val : 0.4f
                    };
                }
            }

            // Default to medium importance (L3 Semantic Whitelist active by default)
            return 0.4f;
        }

        /// <summary>
        /// P1.2: Parse task_id parameter.
        /// </summary>
        private static string GetTaskId(JObject @params)
        {
            var token = @params["task_id"] ?? @params["taskId"];
            return token?.ToString();
        }

        /// <summary>
        /// P1.2: Parse conversation_id parameter.
        /// </summary>
        private static string GetConversationId(JObject @params)
        {
            var token = @params["conversation_id"] ?? @params["conversationId"];
            return token?.ToString();
        }

        /// <summary>
        /// P1.2: Apply task_id and conversation_id filters to raw event list.
        /// Filters AINote events by matching task_id and conversation_id in payload.
        /// </summary>
        private static List<EditorEvent> ApplyTaskFilters(List<EditorEvent> events, string taskId, string conversationId)
        {
            // If no filters specified, return original list
            if (string.IsNullOrEmpty(taskId) && string.IsNullOrEmpty(conversationId))
                return events;

            return events.Where(e =>
            {
                // Only AINote events have task_id and conversation_id
                if (e.Type != EventTypes.AINote)
                    return true;

                // Guard against dehydrated events (null payload)
                if (e.Payload == null)
                    return false; // Can't match filters without payload

                // Check task_id filter
                if (!string.IsNullOrEmpty(taskId))
                {
                    if (e.Payload.TryGetValue("task_id", out var taskVal))
                    {
                        string eventTaskId = taskVal?.ToString();
                        if (eventTaskId != taskId)
                            return false;
                    }
                    else
                    {
                        return false;
                    }
                }

                // Check conversation_id filter
                if (!string.IsNullOrEmpty(conversationId))
                {
                    if (e.Payload.TryGetValue("conversation_id", out var convVal))
                    {
                        string eventConvId = convVal?.ToString();
                        if (eventConvId != conversationId)
                            return false;
                    }
                }

                return true;
            }).ToList();
        }

        /// <summary>
        /// P1.2: Apply task filters to projected events (with semantics).
        /// </summary>
        private static List<ActionTraceViewItem> ApplyTaskFiltersToProjected(List<ActionTraceViewItem> projected, string taskId, string conversationId)
        {
            if (string.IsNullOrEmpty(taskId) && string.IsNullOrEmpty(conversationId))
                return projected;

            return projected.Where(p =>
            {
                if (p.Event.Type != EventTypes.AINote)
                    return true;

                // Guard against dehydrated events (null payload)
                if (p.Event.Payload == null)
                    return false; // Can't match filters without payload

                if (!string.IsNullOrEmpty(taskId))
                {
                    if (p.Event.Payload.TryGetValue("task_id", out var taskVal))
                    {
                        if (taskVal?.ToString() != taskId)
                            return false;
                    }
                    else
                    {
                        return false;
                    }
                }

                if (!string.IsNullOrEmpty(conversationId))
                {
                    if (p.Event.Payload.TryGetValue("conversation_id", out var convVal))
                    {
                        if (convVal?.ToString() != conversationId)
                            return false;
                    }
                }

                return true;
            }).ToList();
        }

        /// <summary>
        /// P1.1: Parse summary_only parameter.
        /// </summary>
        private static bool GetSummaryOnly(JObject @params)
        {
            var token = @params["summary_only"] ?? @params["summaryOnly"];
            if (token != null)
            {
                if (bool.TryParse(token.ToString(), out bool summaryOnly))
                {
                    return summaryOnly;
                }
            }
            return false; // Default to false
        }

        /// <summary>
        /// Filter out disabled event types from the event list.
        /// This ensures that events recorded before a type was disabled are also filtered out.
        /// </summary>
        private static IReadOnlyList<EditorEvent> ApplyDisabledTypesFilter(IReadOnlyList<EditorEvent> events)
        {
            var settings = ActionTraceSettings.Instance;
            if (settings == null)
                return events;

            var disabledTypes = settings.Filtering.DisabledEventTypes;
            if (disabledTypes == null || disabledTypes.Length == 0)
                return events;

            return events.Where(e => !IsEventTypeDisabled(e.Type, disabledTypes)).ToList();
        }

        /// <summary>
        /// Check if an event type is in the disabled types list.
        /// </summary>
        private static bool IsEventTypeDisabled(string eventType, string[] disabledTypes)
        {
            foreach (string disabled in disabledTypes)
            {
                if (string.Equals(eventType, disabled, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }
    }
}
