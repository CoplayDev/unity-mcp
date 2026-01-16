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
    /// </summary>
    [McpForUnityTool("get_action_trace")]
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
            [ToolParameter("Only return events after this sequence number", Required = false)]
            public long? SinceSequence { get; set; }

            /// <summary>
            /// Filter by event types (e.g., ["GameObjectCreated", "ComponentAdded"])
            /// </summary>
            [ToolParameter("Filter by event types", Required = false)]
            public string[] EventTypes { get; set; }

            /// <summary>
            /// Whether to include full event payload (default: true)
            /// </summary>
            [ToolParameter("Whether to include full event payload", Required = false, DefaultValue = "true")]
            public bool IncludePayload { get; set; } = true;

            /// <summary>
            /// Whether to include context associations (default: false)
            /// </summary>
            [ToolParameter("Whether to include context associations", Required = false, DefaultValue = "false")]
            public bool IncludeContext { get; set; } = false;

            /// <summary>
            /// Whether to include semantic analysis results (importance, category, intent)
            /// </summary>
            [ToolParameter("Whether to include semantic analysis results", Required = false, DefaultValue = "false")]
            public bool IncludeSemantics { get; set; } = false;

            /// <summary>
            /// Minimum importance level (low/medium/high/critical)
            /// </summary>
            [ToolParameter("Minimum importance level", Required = false, DefaultValue = "medium")]
            public string MinImportance { get; set; } = "medium";

            /// <summary>
            /// Filter by task ID
            /// </summary>
            [ToolParameter("Filter by task ID", Required = false)]
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
