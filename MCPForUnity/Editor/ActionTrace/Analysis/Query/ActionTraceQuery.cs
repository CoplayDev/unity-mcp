using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.ActionTrace.Context;
using MCPForUnity.Editor.ActionTrace.Core;
using MCPForUnity.Editor.ActionTrace.Core.Models;
using MCPForUnity.Editor.ActionTrace.Semantics;
using MCPForUnity.Editor.Helpers;
using UnityEngine;

namespace MCPForUnity.Editor.ActionTrace.Analysis.Query
{
    /// <summary>
    /// Query engine that projects events with semantic information.
    /// All semantic data (importance, category, intent) is computed at query time
    /// and does not modify the original events.
    /// </summary>
    public sealed class ActionTraceQuery
    {
        // Static color caches to avoid repeated Color allocations during UI rendering
        private static readonly Dictionary<string, Color> EventTypeColors = new()
        {
            ["ComponentAdded"] = new Color(0.3f, 0.8f, 0.3f),
            ["PropertyModified"] = new Color(0.3f, 0.6f, 0.8f),
            ["SelectionPropertyModified"] = new Color(0.5f, 0.8f, 0.9f),
            ["GameObjectCreated"] = new Color(0.8f, 0.3f, 0.8f),
            ["HierarchyChanged"] = new Color(0.8f, 0.8f, 0.3f),
            ["AINote"] = new Color(0.3f, 0.8f, 0.8f),
        };

        private static readonly Dictionary<string, Color?> ImportanceColors = new()
        {
            ["critical"] = new Color(1f, 0.3f, 0.3f, 0.1f),
            ["high"] = new Color(1f, 0.6f, 0f, 0.08f),
            ["medium"] = new Color(1f, 1f, 0.3f, 0.06f),
            ["low"] = null,
        };

        private static readonly Dictionary<string, Color> ImportanceBadgeColors = new()
        {
            ["critical"] = new Color(0.8f, 0.2f, 0.2f),
            ["high"] = new Color(1f, 0.5f, 0f),
            ["medium"] = new Color(1f, 0.8f, 0.2f),
            ["low"] = new Color(0.5f, 0.5f, 0.5f),
        };

        private readonly IEventScorer _scorer;
        private readonly IEventCategorizer _categorizer;
        private readonly IIntentInferrer _inferrer;

        /// <summary>
        /// Create a new ActionTraceQuery with optional custom semantic components.
        /// If null, default implementations are used.
        /// </summary>
        public ActionTraceQuery(
            IEventScorer scorer = null,
            IEventCategorizer categorizer = null,
            IIntentInferrer inferrer = null)
        {
            _scorer = scorer ?? new Semantics.DefaultEventScorer();
            _categorizer = categorizer ?? new Semantics.DefaultCategorizer();
            _inferrer = inferrer ?? new Semantics.DefaultIntentInferrer();
        }

        /// <summary>
        /// Project events with computed semantic information.
        /// Returns ActionTraceViewItem objects containing the original event plus
        /// dynamically calculated importance, category, and intent.
        /// Also converts GlobalID to InstanceID and Name for unified display.
        /// </summary>
        public IReadOnlyList<ActionTraceViewItem> Project(IReadOnlyList<EditorEvent> events)
        {
            if (events == null || events.Count == 0)
                return Array.Empty<ActionTraceViewItem>();

            var result = new ActionTraceViewItem[events.Count];

            // Build instance info cache for performance (many events may reference the same object)
            var instanceInfoCache = new Dictionary<string, (int? instanceId, string displayName)>();

            for (int i = 0; i < events.Count; i++)
            {
                var evt = events[i];

                // Compute importance score
                var score = _scorer.Score(evt);

                // Categorize the score
                var category = _categorizer.Categorize(score);

                // Compute context window (5 events before and after current event) for intent inference
                int contextWindow = 5;
                int contextStart = Math.Max(0, i - contextWindow);
                int contextEnd = Math.Min(events.Count, i + contextWindow + 1);
                int contextLength = contextEnd - contextStart;

                EditorEvent[] surrounding = null;
                if (contextLength > 0)
                {
                    surrounding = new EditorEvent[contextLength];

                    // Performance: EventStore queries are usually in chronological order (but Query returns may be descending).
                    // Detect order in O(1) (compare first/last sequence) and fill surrounding in chronological order if needed
                    bool isDescending = events.Count > 1 && events[0].Sequence > events[events.Count - 1].Sequence;

                    if (!isDescending)
                    {
                        for (int j = 0; j < contextLength; j++)
                        {
                            surrounding[j] = events[contextStart + j];
                        }
                    }
                    else
                    {
                        // events are descending (newest first), need to build surrounding in ascending order (oldest->newest)
                        // Fill from contextEnd-1 down to contextStart to produce ascending window
                        for (int j = 0; j < contextLength; j++)
                        {
                            surrounding[j] = events[contextEnd - 1 - j];
                        }
                    }
                }

                // Use surrounding parameter for intent inference (in chronological order)
                var intent = _inferrer.Infer(evt, surrounding);

                // Use EditorEvent's GetSummary() method, which automatically handles dehydrated events
                var displaySummary = evt.GetSummary();
                var displaySummaryLower = (displaySummary ?? string.Empty).ToLowerInvariant();
                var displayTargetIdLower = (evt.TargetId ?? string.Empty).ToLowerInvariant();

                // Format as local time including date: MM-dd HH:mm
                var localTime = DateTimeOffset.FromUnixTimeMilliseconds(evt.TimestampUnixMs).ToLocalTime();
                var displayTime = localTime.ToString("MM-dd HH:mm");
                var displaySequence = evt.Sequence.ToString();

                // Precompute colors
                var typeColor = GetEventTypeColor(evt.Type);
                var importanceColor = GetImportanceColor(category);
                var importanceBadgeColor = GetImportanceBadgeColor(category);

                // Convert GlobalID to InstanceID and Name (with caching)
                (int? instanceId, string displayName) targetInfo = (null, null);
                if (!string.IsNullOrEmpty(evt.TargetId))
                {
                    if (!instanceInfoCache.TryGetValue(evt.TargetId, out targetInfo))
                    {
                        targetInfo = GlobalIdHelper.GetInstanceInfo(evt.TargetId);
                        instanceInfoCache[evt.TargetId] = targetInfo;
                    }
                }

                result[i] = new ActionTraceViewItem
                {
                    Event = evt,
                    ImportanceScore = score,
                    ImportanceCategory = category,
                    InferredIntent = intent,
                    // Set display cache
                    DisplaySummary = displaySummary,
                    DisplaySummaryLower = displaySummaryLower,
                    DisplayTargetIdLower = displayTargetIdLower,
                    DisplayTime = displayTime,
                    DisplaySequence = displaySequence,
                    TypeColor = typeColor,
                    ImportanceColor = importanceColor,
                    ImportanceBadgeColor = importanceBadgeColor,
                    // Target info (converted from GlobalID)
                    TargetInstanceId = targetInfo.instanceId,
                    TargetName = targetInfo.displayName
                };
            }

            return result;
        }

        /// <summary>
        /// Project events with context associations.
        /// Overload for QueryWithContext results.
        /// Also converts GlobalID to InstanceID and Name for unified display.
        /// </summary>
        public IReadOnlyList<ActionTraceViewItem> ProjectWithContext(
            IReadOnlyList<(EditorEvent Event, ContextMapping Context)> eventsWithContext)
        {
            if (eventsWithContext == null || eventsWithContext.Count == 0)
                return Array.Empty<ActionTraceViewItem>();

            var result = new ActionTraceViewItem[eventsWithContext.Count];

            // Build instance info cache for performance (many events may reference the same object)
            var instanceInfoCache = new Dictionary<string, (int? instanceId, string displayName)>();

            for (int i = 0; i < eventsWithContext.Count; i++)
            {
                var (evt, ctx) = eventsWithContext[i];

                var score = _scorer.Score(evt);
                var category = _categorizer.Categorize(score);

                // Use simple inference to avoid List allocation
                var intent = _inferrer.Infer(evt, surrounding: null);

                // Use EditorEvent's GetSummary() method, which automatically handles dehydrated events
                var displaySummary = evt.GetSummary();
                var displaySummaryLower = (displaySummary ?? string.Empty).ToLowerInvariant();
                var displayTargetIdLower = (evt.TargetId ?? string.Empty).ToLowerInvariant();

                // Format as local time including date: MM-dd HH:mm
                var localTime = DateTimeOffset.FromUnixTimeMilliseconds(evt.TimestampUnixMs).ToLocalTime();
                var displayTime = localTime.ToString("MM-dd HH:mm");
                var displaySequence = evt.Sequence.ToString();

                // Precompute colors
                var typeColor = GetEventTypeColor(evt.Type);
                var importanceColor = GetImportanceColor(category);
                var importanceBadgeColor = GetImportanceBadgeColor(category);

                // Convert GlobalID to InstanceID and Name (with caching)
                (int? instanceId, string displayName) targetInfo = (null, null);
                if (!string.IsNullOrEmpty(evt.TargetId) &&
                !instanceInfoCache.TryGetValue(evt.TargetId, out targetInfo))
                {
                    targetInfo = GlobalIdHelper.GetInstanceInfo(evt.TargetId);
                    instanceInfoCache[evt.TargetId] = targetInfo;
                }

                result[i] = new ActionTraceViewItem
                {
                    Event = evt,
                    Context = ctx,
                    ImportanceScore = score,
                    ImportanceCategory = category,
                    InferredIntent = intent,
                    // Set display cache
                    DisplaySummary = displaySummary,
                    DisplaySummaryLower = displaySummaryLower,
                    DisplayTargetIdLower = displayTargetIdLower,
                    DisplayTime = displayTime,
                    DisplaySequence = displaySequence,
                    TypeColor = typeColor,
                    ImportanceColor = importanceColor,
                    ImportanceBadgeColor = importanceBadgeColor,
                    // Target info (converted from GlobalID)
                    TargetInstanceId = targetInfo.instanceId,
                    TargetName = targetInfo.displayName
                };
            }

            return result;
        }

        /// <summary>
        /// Get event type color for display.
        /// Uses cached values to avoid repeated allocations.
        /// </summary>
        private static Color GetEventTypeColor(string eventType)
        {
            return EventTypeColors.TryGetValue(eventType, out var color) ? color : Color.gray;
        }

        /// <summary>
        /// Get importance background color (nullable).
        /// Uses cached values to avoid repeated allocations.
        /// </summary>
        private static Color? GetImportanceColor(string category)
        {
            return ImportanceColors.TryGetValue(category, out var color) ? color : null;
        }

        /// <summary>
        /// Get importance badge color.
        /// Uses cached values to avoid repeated allocations.
        /// </summary>
        private static Color GetImportanceBadgeColor(string category)
        {
            return ImportanceBadgeColors.TryGetValue(category, out var color) ? color : Color.gray;
        }

        /// <summary>
        /// A view of an event with projected semantic information.
        /// This is a computed projection, not stored data.
        ///
        /// Performance optimization: All display strings are precomputed at projection time
        /// to avoid repeated allocations in OnGUI.
        /// </summary>
        public sealed class ActionTraceViewItem
        {
            /// <summary>
            /// The original immutable event.
            /// </summary>
            public EditorEvent Event { get; set; }

            /// <summary>
            /// Optional context association (may be null).
            /// </summary>
            public ContextMapping Context { get; set; }

            /// <summary>
            /// Computed importance score (0.0 to 1.0).
            /// Higher values indicate more important events.
            /// </summary>
            public float ImportanceScore { get; set; }

            /// <summary>
            /// Category label derived from importance score.
            /// Values: "critical", "high", "medium", "low"
            /// </summary>
            public string ImportanceCategory { get; set; }

            /// <summary>
            /// Inferred user intent or purpose.
            /// May be null if intent cannot be determined.
            /// </summary>
            public string InferredIntent { get; set; }

            /// <summary>
            /// Unity InstanceID converted from GlobalID.
            /// Null if object no longer exists or GlobalID is invalid.
            /// Used for runtime object access and tool integration.
            /// </summary>
            public int? TargetInstanceId { get; set; }

            /// <summary>
            /// Human-readable target name converted from GlobalID.
            /// GameObject name if resolvable, or formatted ID/placeholder if not found.
            /// </summary>
            public string TargetName { get; set; }

            // ========== Display cache (avoid repeated allocations in OnGUI) ==========

            /// <summary>
            /// Precomputed event summary for display.
            /// </summary>
            public string DisplaySummary { get; set; }

            /// <summary>
            /// Precomputed summary in lowercase for search filtering.
            /// </summary>
            public string DisplaySummaryLower { get; set; }

            /// <summary>
            /// Precomputed target ID in lowercase for search filtering.
            /// </summary>
            public string DisplayTargetIdLower { get; set; }

            /// <summary>
            /// Precomputed formatted time (HH:mm:ss).
            /// </summary>
            public string DisplayTime { get; set; }

            /// <summary>
            /// Precomputed sequence number as string.
            /// </summary>
            public string DisplaySequence { get; set; }

            /// <summary>
            /// Precomputed event type color (avoid switch during rendering).
            /// </summary>
            public Color TypeColor { get; set; }

            /// <summary>
            /// Precomputed importance background color.
            /// </summary>
            public Color? ImportanceColor { get; set; }

            /// <summary>
            /// Precomputed importance badge color.
            /// </summary>
            public Color ImportanceBadgeColor { get; set; }
        }
    }
}
