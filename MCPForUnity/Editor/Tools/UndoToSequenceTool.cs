using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.ActionTrace.Core.Settings;
using MCPForUnity.Editor.ActionTrace.Core.Store;
using UnityEditor;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// MCP Tool for reverting the editor state to a specific ActionTrace sequence.
    ///
    /// Purpose (from ActionTrace-enhancements.md P2.4):
    /// - Allows AI to "undo" to a previous state identified by sequence number
    /// - Provides "regret medicine" for AI operations
    ///
    /// Implementation Notes:
    /// - Unity Undo API does NOT support "revert to specific group" directly
    /// - This tool calculates how many Undo steps are needed to reach target sequence
    /// - Performs multiple Undo.PerformUndo() operations
    ///
    /// Limitations:
    /// - Only works if Undo history is intact (no domain reload)
    /// - Cannot redo after this operation (standard Ctrl+Y won't work)
    /// - Best used immediately after realizing a mistake
    /// </summary>
    [McpForUnityTool("undo_to_sequence")]
    public static class UndoToSequenceTool
    {
        /// <summary>
        /// Parameters for undo_to_sequence tool.
        /// </summary>
        public class Parameters
        {
            /// <summary>
            /// Target sequence number to revert to
            /// </summary>
            [ToolParameter("Target sequence number to revert to", Required = true)]
            public long SequenceId { get; set; }

            /// <summary>
            /// If true, only calculate steps without executing
            /// </summary>
            [ToolParameter("If true, only calculate steps without executing", Required = false, DefaultValue = "false")]
            public bool DryRun { get; set; } = false;
        }

        /// <summary>
        /// Handles the undo_to_sequence command.
        ///
        /// Parameters:
        ///   sequence_id (long): Target sequence number to revert to
        ///   dry_run (bool, optional): If true, only calculate steps without executing
        ///
        /// Returns:
        ///   Success response with steps performed and information
        /// </summary>
        public static object HandleCommand(JObject @params)
        {
            try
            {
                // Parse required parameter
                long targetSequence = 0;
                var sequenceToken = @params["sequence_id"] ?? @params["sequenceId"];
                if (sequenceToken == null || !long.TryParse(sequenceToken.ToString(), out targetSequence))
                {
                    return new ErrorResponse("sequence_id parameter is required and must be a number.");
                }

                // Optional dry_run parameter
                bool dryRun = false;
                var dryRunToken = @params["dry_run"] ?? @params["dryRun"];
                if (dryRunToken != null)
                {
                    bool.TryParse(dryRunToken.ToString(), out dryRun);
                }

                // Get current sequence
                long currentSequence = EventStore.CurrentSequence;
                if (targetSequence >= currentSequence)
                {
                    return new ErrorResponse($"Target sequence ({targetSequence}) is not in the past. Current sequence: {currentSequence}");
                }

                // Query all events to validate target range and compute steps
                // Using QueryAll() instead of Query() to ensure we have complete history for validation
                var allEvents = EventStore.QueryAll().ToList();
                if (allEvents.Count == 0)
                {
                    return new ErrorResponse("No events recorded yet.");
                }

                // Check if target sequence is older than the oldest stored event
                // QueryAll returns events in descending sequence order (newest first), so last element is oldest
                var oldestSequence = allEvents[allEvents.Count - 1].Sequence;
                if (targetSequence < oldestSequence)
                {
                    return new ErrorResponse(
                        $"Target sequence ({targetSequence}) is older than the earliest stored event ({oldestSequence}). " +
                        $"Event history has been trimmed due to max events limit ({ActionTraceSettings.Instance?.Storage.MaxEvents ?? 800}).");
                }

                // Filter events after target sequence
                var eventsAfterTarget = allEvents.Where(e => e.Sequence > targetSequence).ToList();

                if (eventsAfterTarget.Count == 0)
                {
                    return new ErrorResponse($"No events found after sequence {targetSequence}.");
                }

                // Count Undo groups after target sequence
                var undoGroupsAfterTarget = new HashSet<int>();
                foreach (var evt in eventsAfterTarget)
                {
                    if (evt.Payload != null && evt.Payload.TryGetValue("undo_group", out var groupObj))
                    {
                        if (int.TryParse(groupObj?.ToString(), out int groupId))
                        {
                            undoGroupsAfterTarget.Add(groupId);
                        }
                    }
                }

                int stepsToUndo = undoGroupsAfterTarget.Count;

                if (stepsToUndo == 0)
                {
                    return new SuccessResponse($"No Undo operations to perform. The target sequence {targetSequence} may have been reached without Undo-recorded operations.");
                }

                // Dry run - only return information
                if (dryRun)
                {
                    return new SuccessResponse($"Dry run: {stepsToUndo} Undo steps needed to reach sequence {targetSequence}", new
                    {
                        target_sequence = targetSequence,
                        current_sequence = currentSequence,
                        undo_steps_needed = stepsToUndo,
                        events_after_target = eventsAfterTarget.Count,
                        affected_undo_groups = undoGroupsAfterTarget.ToArray()
                    });
                }

                // Perform the undo operations
                McpLog.Info($"[UndoToSequenceTool] Reverting {stepsToUndo} Undo steps to reach sequence {targetSequence}");

                for (int i = 0; i < stepsToUndo; i++)
                {
                    Undo.PerformUndo();
                }

                // Force GUI refresh to update the scene (with validity check)
                EditorApplication.delayCall += () =>
                {
                    var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
                    if (scene.IsValid() && scene.isLoaded)
                    {
                        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
                    }
                };

                return new SuccessResponse($"Successfully reverted {stepsToUndo} Undo steps to reach sequence {targetSequence}", new
                {
                    target_sequence = targetSequence,
                    undo_steps_performed = stepsToUndo,
                    events_after_target = eventsAfterTarget.Count,
                    note = "Warning: Redo (Ctrl+Y) may not work correctly after this operation. Consider this a one-way revert."
                });
            }
            catch (Exception ex)
            {
                McpLog.Error($"[UndoToSequenceTool] Error: {ex.Message}");
                return new ErrorResponse($"Failed to undo to sequence: {ex.Message}");
            }
        }
    }
}
