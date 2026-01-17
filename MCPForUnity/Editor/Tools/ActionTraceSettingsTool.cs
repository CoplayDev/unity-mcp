using System;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.ActionTrace.Core;
using Newtonsoft.Json.Linq;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// MCP tool for querying ActionTrace system settings.
    ///
    /// Returns the current configuration of the ActionTrace system,
    /// allowing Python端 to access live settings instead of hardcoded defaults.
    /// </summary>
    [McpForUnityTool("get_action_trace_settings")]
    public static class ActionTraceSettingsTool
    {
        /// <summary>
        /// Parameters for get_action_trace_settings tool.
        /// This tool takes no parameters.
        /// </summary>
        public class Parameters
        {
            // No parameters required
        }

        public static object HandleCommand(JObject @params)
        {
            try
            {
                var settings = ActionTraceSettings.Instance;

                return new SuccessResponse("Retrieved ActionTrace settings.", new
                {
                    schema_version = "action_trace_settings@1",

                    // Event filtering
                    min_importance_for_recording = settings.Filtering.MinImportanceForRecording,
                    disabled_event_types = settings.Filtering.DisabledEventTypes,

                    // Event merging
                    enable_event_merging = settings.Merging.EnableEventMerging,
                    merge_window_ms = settings.Merging.MergeWindowMs,

                    // Storage limits
                    max_events = settings.Storage.MaxEvents,
                    hot_event_count = settings.Storage.HotEventCount,

                    // Transaction aggregation
                    transaction_window_ms = settings.Merging.TransactionWindowMs,

                    // Current store state
                    current_sequence = EventStore.CurrentSequence,
                    total_events_stored = EventStore.Count,
                    context_mapping_count = EventStore.ContextMappingCount
                });
            }
            catch (Exception ex)
            {
                McpLog.Error($"[ActionTraceSettingsTool] Error: {ex.Message}");
                return new ErrorResponse($"Failed to get ActionTrace settings: {ex.Message}");
            }
        }
    }
}
