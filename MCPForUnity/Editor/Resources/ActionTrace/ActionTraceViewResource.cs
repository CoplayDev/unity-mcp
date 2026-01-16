using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Contexts;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.ActionTrace.Context;
using MCPForUnity.Editor.ActionTrace.Core;
using MCPForUnity.Editor.ActionTrace.Query;
using MCPForUnity.Editor.ActionTrace.Semantics;
using Newtonsoft.Json.Linq;
using static MCPForUnity.Editor.ActionTrace.Query.ActionTraceQuery;

namespace MCPForUnity.Editor.Resources.ActionTrace
{
    /// <summary>
    /// MCP resource for querying the action trace of editor events.
    ///
    /// URI: mcpforunity://action_trace_view
    ///
    /// Parameters:
    ///   - limit: Maximum number of events to return (default: 50)
    ///   - since_sequence: Only return events after this sequence number
    ///   - include_context: If true, include context associations (default: false)
    ///   - include_semantics: If true, include importance, category, intent (default: false)
    ///   - min_importance: Minimum importance score to include (default: "medium")
    ///                       Options: "low" (0.0+), "medium" (0.4+), "high" (0.7+), "critical" (0.9+)
    ///   - source: Filter by operation source: "ai", "human", "system" (optional)
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
                bool includeContext = GetIncludeContext(@params);
                bool includeSemantics = GetIncludeSemantics(@params);
                string sourceFilter = GetSourceFilter(@params);

                // L3 Semantic Whitelist: Parse minimum importance threshold
                float minImportance = GetMinImportance(@params);

                // P1.2 Task-Level Filtering: Parse task and conversation IDs
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
                bool useContextQuery = includeContext || !string.IsNullOrEmpty(sourceFilter);

                if (useContextQuery)
                {
                    return QueryWithContext(limit, sinceSequence, sourceFilter, includeSemantics, minImportance, taskId, conversationId);
                }

                if (includeSemantics)
                {
                    return QueryWithSemanticsOnly(limit, sinceSequence, minImportance, taskId, conversationId);
                }

                // Basic query without context or semantics (apply importance filter anyway)
                return QueryBasic(limit, sinceSequence, minImportance, taskId, conversationId);
            }
            catch (Exception ex)
            {
                McpLog.Error($"[ActionTraceViewResource] Error: {ex.Message}");
                return new ErrorResponse($"Error retrieving ActionTrace: {ex.Message}");
            }
        }

        /// <summary>
        /// Basic query without context or semantics
        /// Applies L3 importance filter by default (medium+ importance).
        /// P1.2: Supports task_id and conversation_id filtering.
        /// </summary>
        private static object QueryBasic(int limit, long? sinceSequence, float minImportance, string taskId, string conversationId)
        {
            var events = EventStore.Query(limit, sinceSequence);

            // L3 Semantic Whitelist: Filter by importance
            var scorer = new DefaultEventScorer();
            var filteredEvents = events
                .Where(e => scorer.Score(e) >= minImportance)
                .ToList();

            // P1.2 Task-Level Filtering: Filter by task_id and conversation_id
            filteredEvents = ApplyTaskFilters(filteredEvents, taskId, conversationId);

            var eventItems = filteredEvents.Select(e => new
            {
                sequence = e.Sequence,
                timestamp_unix_ms = e.TimestampUnixMs,
                type = e.Type,
                target_id = e.TargetId,
                summary = EventSummarizer.Summarize(e)
            }).ToArray();

            return new SuccessResponse("Retrieved ActionTrace events.", new
            {
                schema_version = "action_trace_view@1",
                events = eventItems,
                total_count = eventItems.Length,
                current_sequence = EventStore.CurrentSequence
            });
        }

        /// <summary>
        /// Query with semantics but without context
        /// Applies L3 importance filter by default.
        /// P1.2: Supports task_id and conversation_id filtering.
        /// </summary>
        private static object QueryWithSemanticsOnly(int limit, long? sinceSequence, float minImportance, string taskId, string conversationId)
        {
            var rawEvents = EventStore.Query(limit, sinceSequence);
            var query = new ActionTraceQuery();
            var projected = query.Project(rawEvents);

            // L3 Semantic Whitelist: Filter by importance
            var filtered = projected
                .Where(p => p.ImportanceScore >= minImportance)
                .ToList();

            // P1.2 Task-Level Filtering: Filter by task_id and conversation_id
            filtered = ApplyTaskFiltersToProjected(filtered, taskId, conversationId);

            var eventItems = filtered.Select(p => new
            {
                sequence = p.Event.Sequence,
                timestamp_unix_ms = p.Event.TimestampUnixMs,
                type = p.Event.Type,
                target_id = p.Event.TargetId,
                summary = EventSummarizer.Summarize(p.Event),
                importance_score = p.ImportanceScore,
                importance_category = p.ImportanceCategory,
                inferred_intent = p.InferredIntent
            }).ToArray();

            return new SuccessResponse("Retrieved ActionTrace events with semantics.", new
            {
                schema_version = "action_trace_view@3",
                events = eventItems,
                total_count = eventItems.Length,
                current_sequence = EventStore.CurrentSequence
            });
        }

        /// <summary>
        /// Query with context and optional semantics
        /// Note: Source filtering requires persistent OperationContext storage.
        /// </summary>
        private static object QueryWithContext(int limit, long? sinceSequence, string sourceFilter, bool includeSemantics, float minImportance, string taskId, string conversationId)
        {
            // Check if source filter is requested (requires persistent context storage)
            if (!string.IsNullOrEmpty(sourceFilter))
            {
                return new ErrorResponse(
                    "Source filtering requires persistent OperationContext storage. " +
                    "The 'source' parameter is not yet supported."
                );
            }

            var eventsWithContext = EventStore.QueryWithContext(limit, sinceSequence);

            // Apply semantics if requested
            if (includeSemantics)
            {
                var query = new ActionTraceQuery();
                var projected = query.ProjectWithContext(eventsWithContext);

                // L3 Semantic Whitelist: Filter by importance
                var filtered = projected
                    .Where(p => p.ImportanceScore >= minImportance)
                    .ToList();

                var events = filtered.Select(p =>
                {
                    var hasContext = p.Context != null;
                    var contextId = hasContext ? p.Context.ContextId.ToString() : null;

                    return new
                    {
                        sequence = p.Event.Sequence,
                        timestamp_unix_ms = p.Event.TimestampUnixMs,
                        type = p.Event.Type,
                        target_id = p.Event.TargetId,
                        summary = EventSummarizer.Summarize(p.Event),
                        has_context = hasContext,
                        context = hasContext ? new { context_id = contextId } : null,
                        importance_score = p.ImportanceScore,
                        importance_category = p.ImportanceCategory,
                        inferred_intent = p.InferredIntent
                    };
                }).ToArray();

                return new SuccessResponse("Retrieved ActionTrace events with context and semantics.", new
                {
                    schema_version = "action_trace_view@3",
                    events = events,
                    total_count = events.Length,
                    current_sequence = EventStore.CurrentSequence,
                    context_mapping_count = EventStore.ContextMappingCount
                });
            }
            else
            {
                // Context only response - apply importance filter manually
                var scorer = new DefaultEventScorer();
                var filtered = eventsWithContext
                    .Where(x => scorer.Score(x.Event) >= minImportance)
                    .ToList();

                var events = filtered.Select(x =>
                {
                    var hasContext = x.Context != null;
                    var contextId = hasContext ? x.Context.ContextId.ToString() : null;

                    return new
                    {
                        sequence = x.Event.Sequence,
                        timestamp_unix_ms = x.Event.TimestampUnixMs,
                        type = x.Event.Type,
                        target_id = x.Event.TargetId,
                        summary = EventSummarizer.Summarize(x.Event),
                        has_context = hasContext,
                        context = hasContext ? new { context_id = contextId } : null
                    };
                }).ToArray();

                return new SuccessResponse("Retrieved ActionTrace events with context.", new
                {
                    schema_version = "action_trace_view@2",
                    events = events,
                    total_count = events.Length,
                    current_sequence = EventStore.CurrentSequence,
                    context_mapping_count = EventStore.ContextMappingCount
                });
            }
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

        private static bool GetIncludeContext(JObject @params)
        {
            var includeToken = @params["include_context"] ?? @params["includeContext"];
            if (includeToken != null)
            {
                if (bool.TryParse(includeToken.ToString(), out bool include))
                {
                    return include;
                }
            }
            return false;
        }

        private static string GetSourceFilter(JObject @params)
        {
            var sourceToken = @params["source"] ?? @params["operation_source"];
            return sourceToken?.ToString();
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
                if (e.Type != "AINote")
                    return true;  // Keep non-AINote events

                // Check task_id filter
                if (!string.IsNullOrEmpty(taskId))
                {
                    if (e.Payload.TryGetValue("task_id", out var taskVal))
                    {
                        string eventTaskId = taskVal?.ToString();
                        if (eventTaskId != taskId)
                            return false;  // Filter out: task_id doesn't match
                    }
                    else
                    {
                        return false;  // Filter out: AINote without task_id
                    }
                }

                // Check conversation_id filter
                if (!string.IsNullOrEmpty(conversationId))
                {
                    if (e.Payload.TryGetValue("conversation_id", out var convVal))
                    {
                        string eventConvId = convVal?.ToString();
                        if (eventConvId != conversationId)
                            return false;  // Filter out: conversation_id doesn't match
                    }
                }

                return true;  // Keep: passed all filters
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
                if (p.Event.Type != "AINote")
                    return true;

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
        /// P1.2: Apply task filters to EventWithContext list.
        /// </summary>
        private static List<(EditorEvent Event, ContextMapping Context)> ApplyTaskFiltersToEventWithContext(List<(EditorEvent Event, ContextMapping Context)> eventsWithContext, string taskId, string conversationId)
        {
            if (string.IsNullOrEmpty(taskId) && string.IsNullOrEmpty(conversationId))
                return eventsWithContext;

            return eventsWithContext.Where(x =>
            {
                if (x.Event.Type != "AINote")
                    return true;

                if (!string.IsNullOrEmpty(taskId))
                {
                    if (x.Event.Payload.TryGetValue("task_id", out var taskVal))
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
                    if (x.Event.Payload.TryGetValue("conversation_id", out var convVal))
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
        /// P1.1: Query with transaction aggregation.
        ///
        /// Returns AtomicOperation list instead of raw events.
        /// Reduces token consumption by grouping related events.
        ///
        /// From ActionTrace-enhancements.md line 294-300:
        /// "summary_only=True 时返回 AtomicOperation 列表而非原始事件"
        /// </summary>
        private static object QueryAggregated(int limit, long? sinceSequence, float minImportance, string taskId, string conversationId)
        {
            // Step 1: Query raw events
            var events = EventStore.Query(limit, sinceSequence);

            // Step 2: Apply importance filter (L3 Semantic Whitelist)
            var scorer = new DefaultEventScorer();
            var filteredEvents = events
                .Where(e => scorer.Score(e) >= minImportance)
                .ToList();

            // Step 3: Apply task-level filtering (P1.2)
            filteredEvents = ApplyTaskFilters(filteredEvents, taskId, conversationId);

            // Step 4: Aggregate into transactions
            var operations = TransactionAggregator.Aggregate(filteredEvents);

            // Step 5: Project to response format
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
                schema_version = "action_trace_view@4",
                events = eventItems,
                total_count = eventItems.Length,
                current_sequence = EventStore.CurrentSequence
            });
        }
    }
}
