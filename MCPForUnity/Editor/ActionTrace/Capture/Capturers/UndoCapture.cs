using System;
using UnityEditor;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.ActionTrace.Helpers;

namespace MCPForUnity.Editor.ActionTrace.Capture
{
    /// <summary>
    /// Manages Unity Undo grouping for AI tool calls.
    ///
    /// Purpose:
    /// - Groups multiple Undo operations into a single logical transaction
    /// - Enables one Ctrl+Z to undo an entire AI tool call
    /// - Works with TransactionAggregator to provide atomic operation semantics
    ///
    /// Usage (from ActionTrace-enhancements.md line 320-336):
    ///   UndoGroupManager.BeginToolCall("manage_gameobject", "abc123");
    ///   // ... perform operations ...
    ///   UndoGroupManager.EndToolCall();
    ///
    /// Integration with add_ActionTrace_note:
    /// - When AI adds a note with is_transaction_end=true,
    ///   automatically collapses Undo operations since BeginToolCall
    ///
    /// Architecture notes:
    /// - This is an optional enhancement for better UX
    /// - Does not affect ActionTrace event recording
    /// - Independent of OperationContext (tracks Undo state, not tool context)
    /// </summary>
    public static class UndoGroupManager
    {
        // State tracking
        private static string _currentToolName;
        private static string _currentToolCallId;
        private static int _currentUndoGroupStart = -1;
        private static bool _isInToolCall = false;

        /// <summary>
        /// Starts a new Undo group for a tool call.
        ///
        /// Call this at the beginning of an AI tool operation.
        /// All subsequent Undo operations will be grouped under this name.
        ///
        /// Parameters:
        ///   toolName: Name of the tool (e.g., "manage_gameobject")
        ///   toolCallId: Unique identifier for this tool call (UUID)
        ///
        /// Example:
        ///   UndoGroupManager.BeginToolCall("manage_gameobject", "abc-123-def");
        /// </summary>
        public static void BeginToolCall(string toolName, string toolCallId)
        {
            // Guard against nested BeginToolCall invocations
            if (_isInToolCall)
            {
                McpLog.Warn($"[UndoGroupManager] BeginToolCall called while already in tool call '{_currentToolName}' (id: {_currentToolCallId}). Aborting previous state.");
                AbortToolCall();
            }

            if (string.IsNullOrEmpty(toolName))
            {
                McpLog.Warn("[UndoGroupManager] BeginToolCall called with null toolName");
                toolName = "AI Operation";
            }

            // Set the current Undo group name
            // This name will appear in the Undo history (e.g., "Ctrl+Z AI: Create Player")
            Undo.SetCurrentGroupName($"AI: {ActionTraceHelper.FormatToolName(toolName)}");

            // Record the group start position for later collapsing
            _currentUndoGroupStart = Undo.GetCurrentGroup();
            _currentToolName = toolName;
            _currentToolCallId = toolCallId;
            _isInToolCall = true;

            McpLog.Info($"[UndoGroupManager] BeginToolCall: {toolName} (group {_currentUndoGroupStart})");
        }

        /// <summary>
        /// Ends the current Undo group and collapses all operations.
        ///
        /// Call this at the end of an AI tool operation.
        /// All Undo operations since BeginToolCall will be merged into one.
        ///
        /// Example:
        ///   UndoGroupManager.EndToolCall();
        ///
        /// After this, user can press Ctrl+Z once to undo the entire tool call.
        /// </summary>
        public static void EndToolCall()
        {
            if (!_isInToolCall)
            {
                McpLog.Warn("[UndoGroupManager] EndToolCall called without matching BeginToolCall");
                return;
            }

            if (_currentUndoGroupStart >= 0)
            {
                // Collapse all Undo operations since BeginToolCall into one group
                Undo.CollapseUndoOperations(_currentUndoGroupStart);

                McpLog.Info($"[UndoGroupManager] EndToolCall: {_currentToolName} (collapsed from group {_currentUndoGroupStart})");
            }

            // Reset state
            _currentToolName = null;
            _currentToolCallId = null;
            _currentUndoGroupStart = -1;
            _isInToolCall = false;
        }

        /// <summary>
        /// Checks if currently in a tool call.
        /// </summary>
        public static bool IsInToolCall => _isInToolCall;

        /// <summary>
        /// Gets the current tool name (if in a tool call).
        /// Returns null if not in a tool call.
        /// </summary>
        public static string CurrentToolName => _currentToolName;

        /// <summary>
        /// Gets the current tool call ID (if in a tool call).
        /// Returns null if not in a tool call.
        /// </summary>
        public static string CurrentToolCallId => _currentToolCallId;

        /// <summary>
        /// Gets the current Undo group start position.
        /// Returns -1 if not in a tool call.
        /// </summary>
        public static int CurrentUndoGroupStart => _currentUndoGroupStart;

        /// <summary>
        /// Clears the current tool call state without collapsing.
        ///
        /// Use this for error recovery when a tool call fails partway through.
        /// Does NOT collapse Undo operations (unlike EndToolCall).
        /// </summary>
        public static void AbortToolCall()
        {
            if (!_isInToolCall)
                return;

            McpLog.Warn($"[UndoGroupManager] AbortToolCall: {_currentToolName} (group {_currentUndoGroupStart})");

            // Reset state without collapsing
            _currentToolName = null;
            _currentToolCallId = null;
            _currentUndoGroupStart = -1;
            _isInToolCall = false;
        }

        /// <summary>
        /// Integration with add_ActionTrace_note.
        ///
        /// When AI adds a note with is_transaction_end=true,
        /// automatically end the current Undo group.
        ///
        /// This allows the AI to mark completion of a logical transaction.
        ///
        /// Parameters:
        ///   note: The note text (will be used as Undo group name if in tool call)
        ///   isTransactionEnd: If true, calls EndToolCall()
        ///
        /// Returns:
        ///   The Undo group name that was set (or current group name if not ending)
        /// </summary>
        public static string HandleActionTraceNote(string note, bool isTransactionEnd)
        {
            string groupName;

            if (_isInToolCall)
            {
                // Use the AI note as the final Undo group name
                groupName = $"AI: {note}";
                Undo.SetCurrentGroupName(groupName);

                if (isTransactionEnd)
                {
                    EndToolCall();
                }

                return groupName;
            }

            // Not in a tool call - just set the Undo name
            groupName = $"AI: {note}";
            Undo.SetCurrentGroupName(groupName);
            return groupName;
        }
    }
}
