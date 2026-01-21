using System;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Resources.ActionTrace;
using Newtonsoft.Json.Linq;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// MCP tool for querying the action trace of editor events.
    ///
    /// This is a convenience wrapper around ActionTraceViewResource that provides
    /// a cleaner "get_action_trace" tool name for AI consumption.
    ///
    /// Aligned with simplified schema (Basic, WithSemantics, Aggregated).
    /// Removed unsupported parameters: event_types, include_payload, include_context
    /// Added summary_only for transaction aggregation mode.
    /// </summary>
    [McpForUnityTool("get_action_trace", Description = "Query Unity editor action trace (operation history). Returns events with optional semantic analysis or aggregated transactions. Supports query_mode preset for common AI use cases.")]
    public static class GetActionTraceTool
    {
        /// <summary>
        /// Parameters for get_action_trace tool.
        /// </summary>
        public class Parameters
        {
            /// <summary>
            /// P0: Preset query mode for common AI scenarios.
            /// When specified, overrides other parameters with sensible defaults:
            /// - 'recent_errors': High importance only, include semantics (limit=20, min_importance=high, include_semantics=true)
            /// - 'recent_changes': Substantive changes only, exclude logs (limit=30, min_importance=medium)
            /// - 'summary': Minimal fields for quick overview (limit=5, min_importance=high)
            /// - 'verbose': Full details with semantics (limit=100, include_semantics=true, min_importance=low)
            /// </summary>
            [ToolParameter("Preset mode: 'recent_errors', 'recent_changes', 'summary', 'verbose'", Required = false)]
            public string QueryMode { get; set; }

            /// <summary>
            /// Maximum number of events to return (1-1000, default: 50)
            /// Note: Overridden by query_mode if specified
            /// </summary>
            [ToolParameter("Maximum number of events to return (1-1000, default: 50)", Required = false, DefaultValue = "50")]
            public int Limit { get; set; } = 50;

            /// <summary>
            /// Only return events after this sequence number (for incremental queries)
            /// </summary>
            [ToolParameter("Only return events after this sequence number (for incremental queries)", Required = false)]
            public long? SinceSequence { get; set; }

            /// <summary>
            /// Whether to include semantic analysis results (importance, category, intent)
            /// </summary>
            [ToolParameter("Whether to include semantic analysis (importance, category, intent)", Required = false, DefaultValue = "false")]
            public bool IncludeSemantics { get; set; } = false;

            /// <summary>
            /// Minimum importance level (low/medium/high/critical)
            /// Default: medium - filters out low-importance noise like HierarchyChanged
            /// </summary>
            [ToolParameter("Minimum importance level (low/medium/high/critical)", Required = false, DefaultValue = "medium")]
            public string MinImportance { get; set; } = "medium";

            /// <summary>
            /// Return aggregated transactions instead of raw events (reduces token usage)
            /// </summary>
            [ToolParameter("Return aggregated transactions instead of raw events (reduces token usage)", Required = false, DefaultValue = "false")]
            public bool SummaryOnly { get; set; } = false;

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
        }

        /// <summary>
        /// Main handler for action trace queries.
        /// Processes query_mode preset first, then delegates to ActionTraceViewResource.
        /// </summary>
        public static object HandleCommand(JObject @params)
        {
            // P0: Apply query_mode preset if specified
            var processedParams = ApplyQueryMode(@params);

            // Delegate to the existing ActionTraceViewResource implementation
            return ActionTraceViewResource.HandleCommand(processedParams);
        }

        /// <summary>
        /// P0: Apply query_mode preset to override default parameters.
        /// This reduces AI's parameter construction burden for common scenarios.
        /// </summary>
        private static JObject ApplyQueryMode(JObject @params)
        {
            var queryModeToken = @params["query_mode"] ?? @params["queryMode"];
            if (queryModeToken == null)
                return @params; // No query_mode, return original params

            string queryMode = queryModeToken.ToString()?.ToLower()?.Trim();
            if (string.IsNullOrEmpty(queryMode))
                return @params;

            // Create a mutable copy of params
            var modifiedParams = new JObject(@params);

            switch (queryMode)
            {
                case "recent_errors":
                    // Focus on errors and critical events
                    modifiedParams["limit"] = 20;
                    modifiedParams["min_importance"] = "high";
                    modifiedParams["include_semantics"] = true;
                    break;

                case "recent_changes":
                    // Substantive changes, exclude noise
                    modifiedParams["limit"] = 30;
                    modifiedParams["min_importance"] = "medium";
                    // Don't include_semantics for faster query
                    break;

                case "summary":
                    // Quick overview, minimal data
                    modifiedParams["limit"] = 5;
                    modifiedParams["min_importance"] = "high";
                    break;

                case "verbose":
                    // Full details with semantics
                    modifiedParams["limit"] = 100;
                    modifiedParams["include_semantics"] = true;
                    modifiedParams["min_importance"] = "low";
                    break;

                default:
                    // Unknown query_mode, log warning but proceed
                    McpLog.Warn($"[GetActionTraceTool] Unknown query_mode: '{queryMode}'. Using manual parameters.");
                    break;
            }

            return modifiedParams;
        }
    }
}
