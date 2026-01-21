using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.ActionTrace.Core.Settings;
using MCPForUnity.Editor.ActionTrace.Core.Store;
using MCPForUnity.Editor.ActionTrace.Core.Models;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// MCP Tool for adding AI comments/notes to the ActionTrace.
    ///
    /// Usage: AI agents call this tool to record summaries, decisions, or task completion notes.
    ///
    /// Multi-Agent Collaboration:
    /// - task_id: Groups all notes from a single task (e.g., "refactor-player-movement")
    /// - conversation_id: Tracks continuity across sessions
    /// - agent_id: Identifies which AI wrote the note
    ///
    /// Example payload:
    /// {
    ///   "note": "Completed player movement system refactor, speed increased from 5 to 8",
    ///   "agent_id": "ChatGLM 1337",
    ///   "intent": "refactoring",
    ///   "task_id": "task-abc123",
    ///   "conversation_id": "conv-xyz789",
    ///   "related_sequences": [100, 101, 102]
    /// }
    /// </summary>
    [McpForUnityTool("add_action_trace_note", Description = "Adds AI notes/annotations to the ActionTrace for task tracking")]
    public static class AddActionTraceNoteTool
    {
        /// <summary>
        /// Helper to normalize parameter names from snake_case to camelCase.
        /// Supports both legacy snake_case (from direct tool calls) and camelCase (from batch_execute normalization).
        /// </summary>
        private static string GetParamValue(JToken @params, string camelCaseName, string defaultValue = null)
        {
            // Try camelCase first (normalized by batch_execute)
            var value = @params[camelCaseName];
            if (value != null) return value.ToString();

            // Fallback to snake_case (legacy format)
            string snakeCase = string.Concat(camelCaseName.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + c.ToString().ToLower() : c.ToString().ToLower()));
            value = @params[snakeCase];
            return value?.ToString() ?? defaultValue;
        }

        /// <summary>
        /// Parameters for add_action_trace_note tool.
        /// </summary>
        public class Parameters
        {
            /// <summary>
            /// The note text to record
            /// </summary>
            [ToolParameter("The note text to record", Required = true)]
            public string Note { get; set; }

            /// <summary>
            /// Identifies which AI wrote the note (default: "unknown")
            /// </summary>
            [ToolParameter("Identifies which AI wrote the note", Required = false, DefaultValue = "unknown")]
            public string AgentId { get; set; } = "unknown";

            /// <summary>
            /// Groups all notes from a single task
            /// </summary>
            [ToolParameter("Groups all notes from a single task (e.g., 'refactor-player-movement')", Required = false)]
            public string TaskId { get; set; }

            /// <summary>
            /// Tracks continuity across sessions
            /// </summary>
            [ToolParameter("Tracks continuity across sessions", Required = false)]
            public string ConversationId { get; set; }

            /// <summary>
            /// Intent or purpose of the note
            /// </summary>
            [ToolParameter("Intent or purpose of the note", Required = false)]
            public string Intent { get; set; }

            /// <summary>
            /// Model identifier of the AI agent
            /// </summary>
            [ToolParameter("Model identifier of the AI agent", Required = false)]
            public string AgentModel { get; set; }

            /// <summary>
            /// Related event sequences to link with this note
            /// </summary>
            [ToolParameter("Related event sequences to link with this note", Required = false)]
            public long[] RelatedSequences { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            try
            {
                // Required parameters
                string note = @params["note"]?.ToString();
                if (string.IsNullOrEmpty(note))
                {
                    return new ErrorResponse("Note text is required.");
                }

                // Use helper to normalize parameter names (supports both snake_case and camelCase)
                string agentId = GetParamValue(@params, "agentId", "unknown");
                string taskId = GetParamValue(@params, "taskId");
                string conversationId = GetParamValue(@params, "conversationId");
                string intent = GetParamValue(@params, "intent");
                string agentModel = GetParamValue(@params, "agentModel");

                // Build payload with all fields
                var payload = new Dictionary<string, object>
                {
                    ["note"] = note,
                    ["agent_id"] = agentId
                };

                // Task-level tracking (P1.2 multi-agent collaboration)
                if (!string.IsNullOrEmpty(taskId))
                {
                    payload["task_id"] = taskId;
                }

                // Conversation-level tracking (cross-session continuity)
                if (!string.IsNullOrEmpty(conversationId))
                {
                    payload["conversation_id"] = conversationId;
                }

                // Optional fields
                if (!string.IsNullOrEmpty(intent))
                {
                    payload["intent"] = intent;
                }

                if (!string.IsNullOrEmpty(agentModel))
                {
                    payload["agent_model"] = agentModel;
                }

                // Related event sequences (if explicitly linking to specific events)
                var relatedSeqToken = @params["related_sequences"] ?? @params["relatedSequences"];
                if (relatedSeqToken != null)
                {
                    try
                    {
                        var relatedSeqs = relatedSeqToken.ToObject<long[]>();
                        if (relatedSeqs != null && relatedSeqs.Length > 0)
                        {
                            payload["related_sequences"] = relatedSeqs;
                        }
                    }
                    catch (Exception ex)
                    {
                        McpLog.Warn($"[AddActionTraceNoteTool] Failed to parse related_sequences: {ex.Message}");
                    }
                }

                // Record the AINote event
                var evt = new EditorEvent(
                    sequence: 0,  // Assigned by EventStore.Record()
                    timestampUnixMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    type: "AINote",  // P1.2: AI notes are always critical importance
                    targetId: $"agent:{agentId}",
                    payload: payload
                );

                long recordedSequence = EventStore.Record(evt);

                return new SuccessResponse($"AI note added to action trace (sequence {recordedSequence})", new
                {
                    sequence = recordedSequence,
                    timestamp_unix_ms = evt.TimestampUnixMs,
                    task_id = taskId,
                    conversation_id = conversationId
                });
            }
            catch (Exception ex)
            {
                McpLog.Error($"[AddActionTraceNoteTool] Error: {ex.Message}");
                return new ErrorResponse($"Failed to add action trace note: {ex.Message}");
            }
        }
    }
}
