using System;
using MCPForUnity.Editor.ActionTrace.Core;

namespace MCPForUnity.Editor.ActionTrace.Semantics
{
    /// <summary>
    /// Default implementation of event importance scoring.
    /// Scores are based on event type and payload characteristics.
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
        /// Scoring strategy (L3 Semantic Whitelist):
        /// - Critical (1.0): Build failures, AI notes, critical errors
        /// - High (0.7-0.9): Scripts, Scenes, Component operations, Property changes
        /// - Medium (0.4-0.6): GameObject operations, Asset imports
        /// - Low (0.1-0.3): Hierarchy changes, Play mode toggles
        ///
        /// Default behavior: get_action_Trace only returns events with score >= 0.4 (medium+)
        /// unless include_low_importance=true is specified.
        /// </summary>
        public float Score(EditorEvent evt)
        {
            // For dehydrated events (Payload is null), use a low default score
            if (evt.Payload == null)
                return 0.1f;

            return evt.Type switch
            {
                // ========== Critical (1.0) ==========
                // Build failures and AI annotations are top priority
                EventTypes.BuildFailed => 1.0f,
                EventTypes.ScriptCompilationFailed => 1.0f,
                "AINote" => 1.0f,  // AI-written notes are always critical

                // ========== High (0.7-0.9) ==========
                // Scripts and Scenes are project structure changes
                EventTypes.AssetCreated or EventTypes.AssetImported when IsScript(evt) => 0.9f,
                EventTypes.AssetCreated or EventTypes.AssetImported when IsScene(evt) => 0.7f,
                EventTypes.AssetCreated or EventTypes.AssetImported when IsPrefab(evt) => 0.8f,

                // Component and Property modifications (direct user actions)
                EventTypes.ComponentRemoved => 0.7f,
                EventTypes.SelectionPropertyModified => 0.7f,  // Selected object property changes are high priority
                EventTypes.PropertyModified => 0.6f,  // P0 property-level tracking
                EventTypes.ComponentAdded => 0.6f,

                // Scene operations
                EventTypes.SceneSaved => 0.8f,
                EventTypes.SceneSaving => 0.5f,  // Scene saving in progress
                EventTypes.SceneOpened => 0.7f,
                EventTypes.NewSceneCreated => 0.6f,  // New scene creation

                // Build operations (success is important but less than failure)
                EventTypes.BuildStarted => 0.9f,
                EventTypes.BuildCompleted => 1.0f,

                // Destructive asset operations (high importance)
                EventTypes.AssetDeleted => 0.8f,  // Permanent deletion is critical

                // ========== Medium (0.4-0.6) ==========
                // GameObject operations (structure changes)
                EventTypes.GameObjectDestroyed => 0.6f,
                EventTypes.GameObjectCreated => 0.5f,
                EventTypes.AssetCreated or EventTypes.AssetImported => 0.5f,
                EventTypes.AssetModified => 0.4f,  // Asset content changes
                EventTypes.ScriptCompiled => 0.4f,

                // ========== Low (0.1-0.3) ==========
                // Hierarchy changes are noise (happen very frequently)
                EventTypes.HierarchyChanged => 0.2f,
                EventTypes.SelectionChanged => 0.1f,  // Selection changes happen very frequently
                EventTypes.AssetMoved => 0.3f,  // Moving assets within project
                EventTypes.PlayModeChanged => 0.3f,

                // Default low importance for unknown types
                _ => 0.1f
            };
        }

        private static bool IsScript(EditorEvent e)
        {
            if (e.Payload == null) return false;
            if (e.Payload.TryGetValue("extension", out var ext))
                return ext.ToString() == ".cs";
            if (e.Payload.TryGetValue("type", out var type))
                return type.ToString()?.Contains("Script") == true ||
                       type.ToString()?.Contains("MonoScript") == true;
            return false;
        }

        private static bool IsScene(EditorEvent e)
        {
            if (e.Payload == null) return false;
            if (e.Payload.TryGetValue("extension", out var ext))
                return ext.ToString() == ".unity";
            if (e.Payload.TryGetValue("type", out var type))
                return type.ToString()?.Contains("Scene") == true;
            return false;
        }

        private static bool IsPrefab(EditorEvent e)
        {
            if (e.Payload == null) return false;
            if (e.Payload.TryGetValue("extension", out var ext))
                return ext.ToString() == ".prefab";
            if (e.Payload.TryGetValue("type", out var type))
                return type.ToString()?.Contains("Prefab") == true;
            return false;
        }
    }
}
