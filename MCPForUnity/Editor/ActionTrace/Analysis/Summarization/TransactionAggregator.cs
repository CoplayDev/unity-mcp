using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.ActionTrace.Core.Models;
using MCPForUnity.Editor.ActionTrace.Core.Settings;
using MCPForUnity.Editor.ActionTrace.Helpers;

namespace MCPForUnity.Editor.ActionTrace.Analysis.Summarization
{
    /// <summary>
    /// Logical transaction aggregator for ActionTrace events.
    ///
    /// Groups continuous events into "atomic operations" (logical transactions)
    /// to reduce token consumption and improve AI efficiency.
    ///
    /// Aggregation priority (from document ActionTrace-enhancements.md P1.1):
    /// 1. ToolCallId boundary (strongest) - Different tool calls split
    /// 2. TriggeredByTool boundary - Different tools split
    /// 3. Time window boundary (from ActionTraceSettings.TransactionWindowMs) - User operations backup
    ///
    /// Design principles:
    /// - Query-time computation (does not modify stored events)
    /// - Preserves EventStore immutability
    /// - Compatible with semantic projection layer
    ///
    /// Usage:
    ///   var operations = TransactionAggregator.Aggregate(events);
    ///   // Returns: 50 events → 3 AtomicOperation objects
    /// </summary>
    public static class TransactionAggregator
    {
        /// <summary>
        /// Default time window for user operation aggregation (fallback if settings unavailable).
        /// Events within 2 seconds are grouped if no ToolId information exists.
        /// </summary>
        private const long DefaultTransactionWindowMs = 2000;

        /// <summary>
        /// Aggregates a flat list of events into logical transactions.
        ///
        /// Algorithm (from document decision tree):
        /// 1. Check ToolCallId boundary (if exists)
        /// 2. Check TriggeredByTool boundary (if exists)
        /// 3. Fallback to 2-second time window
        ///
        /// Returns a list of AtomicOperation objects, each representing
        /// a logical group of events (e.g., one tool call).
        /// </summary>
        public static List<AtomicOperation> Aggregate(IReadOnlyList<EditorEvent> events)
        {
            if (events == null || events.Count == 0)
                return new List<AtomicOperation>();

            var result = new List<AtomicOperation>();
            var currentBatch = new List<EditorEvent>(events.Count / 2); // Preallocate half capacity

            for (int i = 0; i < events.Count; i++)
            {
                var evt = events[i];

                if (currentBatch.Count == 0)
                {
                    // First event starts a new batch
                    currentBatch.Add(evt);
                    continue;
                }

                var first = currentBatch[0];
                if (ShouldSplit(first, evt))
                {
                    // Boundary reached - finalize current batch
                    if (currentBatch.Count > 0)
                        result.Add(CreateAtomicOperation(currentBatch));

                    // Start new batch with current event - clear and reuse list
                    currentBatch.Clear();
                    currentBatch.Add(evt);
                }
                else
                {
                    // Same transaction - add to current batch
                    currentBatch.Add(evt);
                }
            }

            // Don't forget the last batch
            if (currentBatch.Count > 0)
                result.Add(CreateAtomicOperation(currentBatch));

            return result;
        }

        /// <summary>
        /// Determines if two events should be in different transactions.
        ///
        /// Decision tree (from ActionTrace-enhancements.md line 274-290):
        /// - Priority 1: ToolCallId boundary (mandatory split if different)
        /// - Priority 2: TriggeredByTool boundary (mandatory split if different)
        /// - Priority 3: Time window (from ActionTraceSettings.TransactionWindowMs, default 2000ms)
        /// </summary>
        private static bool ShouldSplit(EditorEvent first, EditorEvent current)
        {
            // Get transaction window from settings, with fallback to default
            var settings = ActionTraceSettings.Instance;
            long transactionWindowMs = settings?.Merging.TransactionWindowMs ?? DefaultTransactionWindowMs;

            // Extract ToolCallId from Payload (if exists)
            string firstToolCallId = GetToolCallId(first);
            string currentToolCallId = GetToolCallId(current);

            // ========== Priority 1: ToolCallId boundary ==========
            // Split if tool call IDs differ (including null vs non-null for symmetry)
            if (currentToolCallId != firstToolCallId)
                return true; // Different tool call → split

            // ========== Priority 2: TriggeredByTool boundary ==========
            string firstTool = GetTriggeredByTool(first);
            string currentTool = GetTriggeredByTool(current);

            // Split if tools differ (including null vs non-null for symmetry)
            if (currentTool != firstTool)
                return true; // Different tool → split

            // ========== Priority 3: Time window (user operations) ==========
            // If no ToolId information, use configured time window
            long timeDelta = current.TimestampUnixMs - first.TimestampUnixMs;
            return timeDelta > transactionWindowMs;
        }

        /// <summary>
        /// Creates an AtomicOperation from a batch of events.
        ///
        /// Summary generation strategy:
        /// - If tool_call_id exists: "ToolName: N events in X.Xs"
        /// - If time-based: Use first event's summary + " + N-1 related events"
        /// </summary>
        private static AtomicOperation CreateAtomicOperation(List<EditorEvent> batch)
        {
            if (batch == null || batch.Count == 0)
                throw new ArgumentException("Batch cannot be empty", nameof(batch));

            var first = batch[0];
            var last = batch[batch.Count - 1];

            string toolCallId = GetToolCallId(first);
            string toolName = GetTriggeredByTool(first);

            // Generate summary
            string summary = GenerateSummary(batch, toolCallId, toolName);

            // Calculate duration
            long durationMs = last.TimestampUnixMs - first.TimestampUnixMs;

            return new AtomicOperation
            {
                StartSequence = first.Sequence,
                EndSequence = last.Sequence,
                Summary = summary,
                EventCount = batch.Count,
                DurationMs = durationMs,
                ToolCallId = toolCallId,
                TriggeredByTool = toolName
            };
        }

        /// <summary>
        /// Generates a human-readable summary for an atomic operation.
        /// </summary>
        private static string GenerateSummary(
            List<EditorEvent> batch,
            string toolCallId,
            string toolName)
        {
            if (batch.Count == 1)
            {
                // Single event - use its summary
                return EventSummarizer.Summarize(batch[0]);
            }

            // Multiple events
            if (!string.IsNullOrEmpty(toolCallId))
            {
                // Tool call - use tool name + count
                string displayName = string.IsNullOrEmpty(toolName)
                    ? "AI operation"
                    : ActionTraceHelper.FormatToolName(toolName);

                return $"{displayName}: {batch.Count} events in {ActionTraceHelper.FormatDurationFromRange(batch[0].TimestampUnixMs, batch[batch.Count - 1].TimestampUnixMs)}";
            }

            // Time-based aggregation - use first event + count
            string firstSummary = EventSummarizer.Summarize(batch[0]);
            return $"{firstSummary} + {batch.Count - 1} related events";
        }

        /// <summary>
        /// Extracts tool_call_id from event Payload.
        /// Returns null if not present.
        /// </summary>
        private static string GetToolCallId(EditorEvent evt) => evt.GetPayloadString("tool_call_id");

        /// <summary>
        /// Extracts triggered_by_tool from event Payload.
        /// Returns null if not present.
        /// </summary>
        private static string GetTriggeredByTool(EditorEvent evt) => evt.GetPayloadString("triggered_by_tool");
    }

    /// <summary>
    /// Represents a logical transaction (atomic operation) composed of multiple events.
    ///
    /// Use cases:
    /// - AI tool call grouping (e.g., "create_complex_object" → 50 events)
    /// - User rapid operations (e.g., 5 component additions in 1.5s)
    /// - Undo group alignment (one Ctrl+Z = one AtomicOperation)
    ///
    /// From ActionTrace-enhancements.md P1.1, line 189-198.
    /// </summary>
    public sealed class AtomicOperation
    {
        /// <summary>
        /// First event sequence number in this transaction.
        /// </summary>
        public long StartSequence { get; set; }

        /// <summary>
        /// Last event sequence number in this transaction.
        /// </summary>
        public long EndSequence { get; set; }

        /// <summary>
        /// Human-readable summary of the entire transaction.
        /// Examples:
        /// - "Manage GameObject: 50 events in 2.3s"
        /// - "Added Rigidbody to Player + 4 related events"
        /// </summary>
        public string Summary { get; set; }

        /// <summary>
        /// Number of events in this transaction.
        /// </summary>
        public int EventCount { get; set; }

        /// <summary>
        /// Duration of the transaction in milliseconds.
        /// Time from first event to last event.
        /// </summary>
        public long DurationMs { get; set; }

        /// <summary>
        /// Tool call identifier if this transaction represents a single tool call.
        /// Null for time-based user operations.
        /// </summary>
        public string ToolCallId { get; set; }

        /// <summary>
        /// Tool name that triggered this transaction.
        /// Examples: "manage_gameobject", "add_ActionTrace_note"
        /// Null for user manual operations.
        /// </summary>
        public string TriggeredByTool { get; set; }
    }
}
