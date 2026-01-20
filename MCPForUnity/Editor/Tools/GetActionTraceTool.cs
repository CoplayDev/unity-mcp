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
    [McpForUnityTool("get_action_trace", Description = "Query Unity editor action trace (operation history). Returns events with optional semantic analysis or aggregated transactions.")]
    public static class GetActionTraceTool
    {
        /// <summary>
        /// Parameters for get_action_trace tool.
        /// </summary>
        public class Parameters
        {
            /// <summary>
            /// Maximum number of events to return (1-1000, default: 50)
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
        /// </summary>
        public static object HandleCommand(JObject @params)
        {
            // Delegate to the existing ActionTraceViewResource implementation
            return ActionTraceViewResource.HandleCommand(@params);
        }
    }
}
