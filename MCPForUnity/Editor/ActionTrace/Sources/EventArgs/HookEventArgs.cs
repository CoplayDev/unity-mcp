using System;

namespace MCPForUnity.Editor.ActionTrace.Sources.EventArgs
{
    /// <summary>
    /// Base class for all hook event arguments.
    /// Follows .NET conventions (similar to EventArgs).
    /// </summary>
    public abstract class HookEventArgs
    {
        /// <summary>
        /// Timestamp when the event occurred.
        /// </summary>
        public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
    }

    #region Compilation Args

    /// <summary>
    /// Arguments for script compilation events.
    /// </summary>
    public class ScriptCompilationArgs : HookEventArgs
    {
        /// <summary>Number of scripts compiled (optional)</summary>
        public int? ScriptCount { get; set; }

        /// <summary>Compilation duration in milliseconds (optional)</summary>
        public long? DurationMs { get; set; }
    }

    /// <summary>
    /// Arguments for script compilation failure events.
    /// </summary>
    public class ScriptCompilationFailedArgs : ScriptCompilationArgs
    {
        /// <summary>Number of compilation errors</summary>
        public int ErrorCount { get; set; }
    }

    #endregion

    #region Scene Args

    /// <summary>
    /// Arguments for scene open events.
    /// </summary>
    public class SceneOpenArgs : HookEventArgs
    {
        /// <summary>Mode used to open the scene (optional)</summary>
        public UnityEditor.SceneManagement.OpenSceneMode? Mode { get; set; }
    }

    /// <summary>
    /// Arguments for new scene creation events.
    /// </summary>
    public class NewSceneArgs : HookEventArgs
    {
        /// <summary>Scene setup configuration (optional)</summary>
        public UnityEditor.SceneManagement.NewSceneSetup? Setup { get; set; }

        /// <summary>New scene mode (optional)</summary>
        public UnityEditor.SceneManagement.NewSceneMode? Mode { get; set; }
    }

    #endregion

    #region Build Args

    /// <summary>
    /// Arguments for build completion events.
    /// </summary>
    public class BuildArgs : HookEventArgs
    {
        /// <summary>Build platform name (optional)</summary>
        public string Platform { get; set; }

        /// <summary>Build output location (optional)</summary>
        public string Location { get; set; }

        /// <summary>Build duration in milliseconds (optional)</summary>
        public long? DurationMs { get; set; }

        /// <summary>Output size in bytes (optional, only on success)</summary>
        public ulong? SizeBytes { get; set; }

        /// <summary>Whether the build succeeded</summary>
        public bool Success { get; set; }

        /// <summary>Build summary/error message (optional)</summary>
        public string Summary { get; set; }
    }

    #endregion

    #region GameObject Args

    /// <summary>
    /// Arguments for GameObject destruction events.
    /// Used when the GameObject has already been destroyed and is no longer accessible.
    /// </summary>
    public class GameObjectDestroyedArgs : HookEventArgs
    {
        /// <summary>The InstanceID of the destroyed GameObject (for reference)</summary>
        public int InstanceId { get; set; }

        /// <summary>The name of the destroyed GameObject (cached before destruction)</summary>
        public string Name { get; set; }

        /// <summary>The GlobalID of the destroyed GameObject (cached before destruction for cross-session stable reference)</summary>
        public string GlobalId { get; set; }
    }

    #endregion

    #region Component Args

    /// <summary>
    /// Arguments for component removal events.
    /// Used when the component has already been destroyed and is no longer accessible.
    /// </summary>
    public class ComponentRemovedArgs : HookEventArgs
    {
        /// <summary>The GameObject that owned the removed component</summary>
        public UnityEngine.GameObject Owner { get; set; }

        /// <summary>The InstanceID of the removed component (for reference)</summary>
        public int ComponentInstanceId { get; set; }

        /// <summary>The type name of the removed component (cached before destruction)</summary>
        public string ComponentType { get; set; }
    }

    #endregion
}
