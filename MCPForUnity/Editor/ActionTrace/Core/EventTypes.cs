using System;
using System.Collections.Generic;

// ========== Add New Event Checklist ==========
//
// 1. Add event constant above:
//    public const string YourNewEvent = "YourNewEvent";
//
// 2. Add configuration in Metadata._metadata:
//    [YourNewEvent] = new EventMetadata { ... }
//
// 3. If special scoring logic is needed, add to DefaultEventScorer.GetPayloadAdjustment()
//
// 4. If special summary format is needed, use conditional template or handle separately
//
// Done! No need to modify other files.

namespace MCPForUnity.Editor.ActionTrace.Core
{
    /// <summary>
    /// Centralized constant definitions for ActionTrace event types.
    /// Provides type-safe event type names and reduces string literal usage.
    ///
    /// Usage:
    ///   EventTypes.ComponentAdded  // instead of "ComponentAdded"
    ///   EventTypes.Metadata.Get(ComponentAdded)  // get event metadata
    /// </summary>
    public static class EventTypes
    {
        // Component events
        public const string ComponentAdded = "ComponentAdded";
        public const string ComponentRemoved = "ComponentRemoved";

        // Property events (P0: Property-Level Tracking)
        public const string PropertyModified = "PropertyModified";
        public const string SelectionPropertyModified = "SelectionPropertyModified";

        // GameObject events
        public const string GameObjectCreated = "GameObjectCreated";
        public const string GameObjectDestroyed = "GameObjectDestroyed";

        // Hierarchy events
        public const string HierarchyChanged = "HierarchyChanged";

        // Selection events (P2.3: Selection Tracking)
        public const string SelectionChanged = "SelectionChanged";

        // Play mode events
        public const string PlayModeChanged = "PlayModeChanged";

        // Scene events
        public const string SceneSaving = "SceneSaving";
        public const string SceneSaved = "SceneSaved";
        public const string SceneOpened = "SceneOpened";
        public const string NewSceneCreated = "NewSceneCreated";

        // Asset events
        public const string AssetImported = "AssetImported";
        public const string AssetCreated = "AssetCreated";
        public const string AssetDeleted = "AssetDeleted";
        public const string AssetMoved = "AssetMoved";
        public const string AssetModified = "AssetModified";

        // Script events
        public const string ScriptCompiled = "ScriptCompiled";
        public const string ScriptCompilationFailed = "ScriptCompilationFailed";

        // Build events
        public const string BuildStarted = "BuildStarted";
        public const string BuildCompleted = "BuildCompleted";
        public const string BuildFailed = "BuildFailed";

        // ========== Event Metadata Configuration ==========

        /// <summary>
        /// Event metadata configuration.
        /// Centrally manages default importance, summary templates, sampling config, etc. for each event type.
        ///
        /// When adding new events, simply add configuration here. No need to modify other files.
        /// </summary>
        public static class Metadata
        {
            private static readonly Dictionary<string, EventMetadata> _metadata = new(StringComparer.Ordinal)
            {
                // ========== Critical (1.0) ==========
                [BuildFailed] = new EventMetadata
                {
                    Category = EventCategory.Build,
                    DefaultImportance = 1.0f,
                    SummaryTemplate = "Build failed: {platform}",
                },
                [ScriptCompilationFailed] = new EventMetadata
                {
                    Category = EventCategory.Script,
                    DefaultImportance = 1.0f,
                    SummaryTemplate = "Script compilation failed: {error_count} errors",
                },
                ["AINote"] = new EventMetadata
                {
                    Category = EventCategory.System,
                    DefaultImportance = 1.0f,
                    SummaryTemplate = "AI Note{if:agent_id, ({agent_id})}: {note}",
                },

                // ========== High (0.7-0.9) ==========
                [BuildStarted] = new EventMetadata
                {
                    Category = EventCategory.Build,
                    DefaultImportance = 0.9f,
                    SummaryTemplate = "Build started: {platform}",
                },
                [BuildCompleted] = new EventMetadata
                {
                    Category = EventCategory.Build,
                    DefaultImportance = 1.0f,
                    SummaryTemplate = "Build completed: {platform}",
                },
                [SceneSaved] = new EventMetadata
                {
                    Category = EventCategory.Scene,
                    DefaultImportance = 0.8f,
                    SummaryTemplate = "Scene saved: {scene_name} ({target_id})",
                },
                [AssetDeleted] = new EventMetadata
                {
                    Category = EventCategory.Asset,
                    DefaultImportance = 0.8f,
                    SummaryTemplate = "Deleted asset: {path} ({target_id})",
                },
                [SceneOpened] = new EventMetadata
                {
                    Category = EventCategory.Scene,
                    DefaultImportance = 0.7f,
                    SummaryTemplate = "Opened scene: {scene_name} ({target_id})",
                },
                [ComponentRemoved] = new EventMetadata
                {
                    Category = EventCategory.Component,
                    DefaultImportance = 0.7f,
                    SummaryTemplate = "Removed Component: {component_type} from {name} (GameObject:{target_id})",
                },
                [SelectionPropertyModified] = new EventMetadata
                {
                    Category = EventCategory.Property,
                    DefaultImportance = 0.7f,
                    SummaryTemplate = "Changed {component_type}.{property_path}: {start_value} → {end_value} (selected, GameObject:{target_id})",
                },

                // ========== Medium (0.4-0.6) ==========
                [ComponentAdded] = new EventMetadata
                {
                    Category = EventCategory.Component,
                    DefaultImportance = 0.6f,
                    SummaryTemplate = "Added Component: {component_type} to {name} (GameObject:{target_id})",
                },
                [PropertyModified] = new EventMetadata
                {
                    Category = EventCategory.Property,
                    DefaultImportance = 0.6f,
                    SummaryTemplate = "Changed {component_type}.{property_path}: {start_value} → {end_value} (GameObject:{target_id})",
                },
                [NewSceneCreated] = new EventMetadata
                {
                    Category = EventCategory.Scene,
                    DefaultImportance = 0.6f,
                    SummaryTemplate = "New scene created ({target_id})",
                },
                [GameObjectDestroyed] = new EventMetadata
                {
                    Category = EventCategory.GameObject,
                    DefaultImportance = 0.6f,
                    SummaryTemplate = "Destroyed: {name} (GameObject:{target_id})",
                },
                [SceneSaving] = new EventMetadata
                {
                    Category = EventCategory.Scene,
                    DefaultImportance = 0.5f,
                    SummaryTemplate = "Saving scene: {scene_name} ({target_id})",
                },
                [GameObjectCreated] = new EventMetadata
                {
                    Category = EventCategory.GameObject,
                    DefaultImportance = 0.5f,
                    SummaryTemplate = "Created: {name} (GameObject:{target_id})",
                },
                [AssetImported] = new EventMetadata
                {
                    Category = EventCategory.Asset,
                    DefaultImportance = 0.5f,
                    SummaryTemplate = "Imported {asset_type}: {path} ({target_id})",
                },
                [AssetCreated] = new EventMetadata
                {
                    Category = EventCategory.Asset,
                    DefaultImportance = 0.5f,
                    SummaryTemplate = "Created {asset_type}: {path} ({target_id})",
                },
                [AssetModified] = new EventMetadata
                {
                    Category = EventCategory.Asset,
                    DefaultImportance = 0.4f,
                    SummaryTemplate = "Modified {asset_type}: {path} ({target_id})",
                },
                [ScriptCompiled] = new EventMetadata
                {
                    Category = EventCategory.Script,
                    DefaultImportance = 0.4f,
                    SummaryTemplate = "Scripts compiled: {script_count} files ({duration_ms}ms)",
                },

                // ========== Low (0.1-0.3) ==========
                [AssetMoved] = new EventMetadata
                {
                    Category = EventCategory.Asset,
                    DefaultImportance = 0.3f,
                    SummaryTemplate = "Moved {from_path} → {to_path} ({target_id})",
                },
                [PlayModeChanged] = new EventMetadata
                {
                    Category = EventCategory.Editor,
                    DefaultImportance = 0.3f,
                    SummaryTemplate = "Play mode: {state}",
                },
                [HierarchyChanged] = new EventMetadata
                {
                    Category = EventCategory.Hierarchy,
                    DefaultImportance = 0.2f,
                    SummaryTemplate = "Hierarchy changed",
                    EnableSampling = true,
                    SamplingMode = SamplingMode.Throttle,
                    SamplingWindow = 1000,
                },
                [SelectionChanged] = new EventMetadata
                {
                    Category = EventCategory.Selection,
                    DefaultImportance = 0.1f,
                    SummaryTemplate = "Selection changed ({target_id})",
                },
            };

            /// <summary>
            /// Get metadata for an event type.
            /// Returns default metadata if not found.
            /// </summary>
            public static EventMetadata Get(string eventType)
            {
                return _metadata.TryGetValue(eventType, out var meta) ? meta : Default;
            }

            /// <summary>
            /// Set or update metadata for an event type.
            /// Use for runtime dynamic configuration.
            /// </summary>
            public static void Set(string eventType, EventMetadata metadata)
            {
                _metadata[eventType] = metadata;
            }

            /// <summary>
            /// Default metadata for unconfigured event types.
            /// </summary>
            public static EventMetadata Default { get; } = new EventMetadata
            {
                Category = EventCategory.Unknown,
                DefaultImportance = 0.1f,
                SummaryTemplate = "{type} on {target}",
            };
        }


    }
}

