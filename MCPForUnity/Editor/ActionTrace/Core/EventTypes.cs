namespace MCPForUnity.Editor.ActionTrace.Core
{
    /// <summary>
    /// Centralized constant definitions for ActionTrace event types.
    /// Provides type-safe event type names and reduces string literal usage.
    ///
    /// Usage:
    ///   EventTypes.ComponentAdded  // instead of "ComponentAdded"
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
    }
}
