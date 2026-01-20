using System;
using MCPForUnity.Editor.ActionTrace.Core;
using MCPForUnity.Editor.ActionTrace.Core.Models;
using UnityEngine;

namespace MCPForUnity.Editor.ActionTrace.Semantics
{
    /// <summary>
    /// Default implementation of event importance scoring.
    /// Scores are based on event type metadata, with special handling for payload-based adjustments.
    ///
    /// Scoring priority:
    /// 1. Metadata.DefaultImportance (configured in EventTypes.Metadata)
    /// 2. Payload-based adjustments (Script, Scene, Prefab detection)
    /// 3. Dehydrated events (Payload is null) → 0.1f
    /// </summary>
    public sealed class DefaultEventScorer : IEventScorer
    {
        private static readonly Lazy<DefaultEventScorer> _instance = new(() => new DefaultEventScorer());

        /// <summary>
        /// Singleton instance for use in EventStore importance filtering.
        /// </summary>
        public static DefaultEventScorer Instance => _instance.Value;

        /// <summary>
        /// Calculate importance score for an event.
        /// Higher scores indicate more significant events.
        ///
        /// Scoring strategy:
        /// - Uses EventTypes.Metadata.Get() for base score
        /// - Applies payload-based adjustments for assets (Script=+0.4, Scene=+0.2, Prefab=+0.3)
        /// - Dehydrated events return 0.1f
        /// </summary>
        public float Score(EditorEvent evt)
        {
            // Dehydrated events (Payload is null) use low default score
            if (evt.Payload == null)
                return 0.1f;

            // Get base score from metadata
            var meta = EventTypes.Metadata.Get(evt.Type);
            float baseScore = meta.DefaultImportance;

            // Special case: AINote is always critical
            if (evt.Type == "AINote")
                return 1.0f;

            // Apply payload-based adjustments for asset events
            float adjustment = GetPayloadAdjustment(evt);
            return Mathf.Clamp01(baseScore + adjustment);
        }

        /// <summary>
        /// Calculate score adjustment based on payload content.
        /// Used to boost/reduce scores for specific asset types.
        /// </summary>
        private static float GetPayloadAdjustment(EditorEvent evt)
        {
            if (evt.Payload == null)
                return 0f;

            // Asset type adjustments (only for AssetCreated/AssetImported)
            bool isAssetEvent = evt.Type == EventTypes.AssetCreated ||
                               evt.Type == EventTypes.AssetImported;

            if (!isAssetEvent)
                return 0f;

            if (IsScript(evt))
                return 0.4f;  // Scripts are high priority
            if (IsScene(evt))
                return 0.2f;
            if (IsPrefab(evt))
                return 0.3f;

            return 0f;
        }

        private static bool IsScript(EditorEvent e)
        {
            if (e.Payload.TryGetValue("extension", out var ext))
                return ext.ToString() == ".cs";
            if (e.Payload.TryGetValue("asset_type", out var type))
                return type.ToString()?.Contains("Script") == true ||
                       type.ToString()?.Contains("MonoScript") == true;
            return false;
        }

        private static bool IsScene(EditorEvent e)
        {
            if (e.Payload.TryGetValue("extension", out var ext))
                return ext.ToString() == ".unity";
            if (e.Payload.TryGetValue("asset_type", out var type))
                return type.ToString()?.Contains("Scene") == true;
            return false;
        }

        private static bool IsPrefab(EditorEvent e)
        {
            if (e.Payload.TryGetValue("extension", out var ext))
                return ext.ToString() == ".prefab";
            if (e.Payload.TryGetValue("asset_type", out var type))
                return type.ToString()?.Contains("Prefab") == true;
            return false;
        }
    }
}
