using System;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MCPForUnity.Editor.Hooks
{
    #region Event Arguments

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
        public OpenSceneMode? Mode { get; set; }
    }

    /// <summary>
    /// Arguments for new scene creation events.
    /// </summary>
    public class NewSceneArgs : HookEventArgs
    {
        /// <summary>Scene setup configuration (optional)</summary>
        public NewSceneSetup? Setup { get; set; }

        /// <summary>New scene mode (optional)</summary>
        public NewSceneMode? Mode { get; set; }
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

    #endregion

    /// <summary>
    /// Built-in hook system providing subscription points for all common Unity editor events.
    /// Other systems can subscribe to these events without directly monitoring Unity callbacks.
    ///
    /// Event Design:
    /// - Simple events: Use for basic notifications (backward compatible)
    /// - Detailed events: Include additional context via Args classes
    ///
    /// Usage:
    /// <code>
    /// // Simple subscription
    /// HookRegistry.OnSceneOpened += (scene) => Debug.Log(scene.name);
    ///
    /// // Detailed subscription with extra data
    /// HookRegistry.OnSceneOpenedDetailed += (scene, args) => Debug.Log($"{scene.name} - {args.Mode}");
    /// </code>
    /// </summary>
    public static class HookRegistry
    {
        #region Compilation Events

        /// <summary>
        /// Fired when script compilation completes successfully.
        /// </summary>
        public static event Action OnScriptCompiled;

        /// <summary>
        /// Fired when script compilation completes with detailed information.
        /// </summary>
        public static event Action<ScriptCompilationArgs> OnScriptCompiledDetailed;

        /// <summary>
        /// Fired when script compilation fails.
        /// Parameter: error count
        /// </summary>
        public static event Action<int> OnScriptCompilationFailed;

        /// <summary>
        /// Fired when script compilation fails with detailed information.
        /// </summary>
        public static event Action<ScriptCompilationFailedArgs> OnScriptCompilationFailedDetailed;

        #endregion

        #region Scene Events

        /// <summary>
        /// Fired after a scene is saved.
        /// Parameter: the scene that was saved
        /// </summary>
        public static event Action<Scene> OnSceneSaved;

        /// <summary>
        /// Fired when a scene is opened.
        /// Parameter: the scene that was opened
        /// </summary>
        public static event Action<Scene> OnSceneOpened;

        /// <summary>
        /// Fired when a scene is opened with detailed information.
        /// </summary>
        public static event Action<Scene, SceneOpenArgs> OnSceneOpenedDetailed;

        /// <summary>
        /// Fired when a new scene is created.
        /// Parameter: the newly created scene
        /// </summary>
        public static event Action<Scene> OnNewSceneCreated;

        /// <summary>
        /// Fired when a new scene is created with detailed information.
        /// </summary>
        public static event Action<Scene, NewSceneArgs> OnNewSceneCreatedDetailed;

        /// <summary>
        /// Fired after a scene is loaded (after load operation completes).
        /// Parameter: the loaded scene
        /// </summary>
        public static event Action<Scene> OnSceneLoaded;

        /// <summary>
        /// Fired when a scene is unloaded.
        /// Parameter: the scene being unloaded
        /// </summary>
        public static event Action<Scene> OnSceneUnloaded;

        #endregion

        #region Play Mode Events

        /// <summary>
        /// Fired when play mode state changes.
        /// Parameter: true if entering play mode, false if exiting
        /// </summary>
        public static event Action<bool> OnPlayModeChanged;

        #endregion

        #region Hierarchy Events

        /// <summary>
        /// Fired when the hierarchy changes (with debouncing).
        /// </summary>
        public static event Action OnHierarchyChanged;

        /// <summary>
        /// Fired when a GameObject is created.
        /// Parameter: the created GameObject
        /// </summary>
        public static event Action<GameObject> OnGameObjectCreated;

        /// <summary>
        /// Fired when a GameObject is destroyed.
        /// Parameter: the destroyed GameObject (if still available)
        /// </summary>
        public static event Action<GameObject> OnGameObjectDestroyed;

        #endregion

        #region Selection Events

        /// <summary>
        /// Fired when the selection changes.
        /// Parameter: the currently selected GameObject (null if nothing selected)
        /// </summary>
        public static event Action<GameObject> OnSelectionChanged;

        #endregion

        #region Project Events

        /// <summary>
        /// Fired when the project changes (asset import/delete, etc).
        /// </summary>
        public static event Action OnProjectChanged;

        /// <summary>
        /// Fired when assets are imported.
        /// </summary>
        public static event Action OnAssetImported;

        /// <summary>
        /// Fired when assets are deleted.
        /// </summary>
        public static event Action OnAssetDeleted;

        #endregion

        #region Build Events

        /// <summary>
        /// Fired when a build completes.
        /// Parameter: true if build succeeded, false if failed
        /// </summary>
        public static event Action<bool> OnBuildCompleted;

        /// <summary>
        /// Fired when a build completes with detailed information.
        /// </summary>
        public static event Action<BuildArgs> OnBuildCompletedDetailed;

        #endregion

        #region Editor State Events

        /// <summary>
        /// Fired every editor update frame (similar to Update in MonoBehaviour).
        /// </summary>
        public static event Action OnEditorUpdate;

        /// <summary>
        /// Fired when the editor is idle (no ongoing operations).
        /// </summary>
        public static event Action OnEditorIdle;

        #endregion

        #region Component Events

        /// <summary>
        /// Fired when a component is added to a GameObject.
        /// Parameter: the added component
        /// </summary>
        public static event Action<Component> OnComponentAdded;

        #endregion

        #region Internal Notification API (for UnityEventHooks only)

        // ========== Simple Notifications ==========
        internal static void NotifyScriptCompiled()
        {
            OnScriptCompiled?.Invoke();
        }

        internal static void NotifyScriptCompiledDetailed(ScriptCompilationArgs args)
        {
            OnScriptCompiledDetailed?.Invoke(args);
        }

        internal static void NotifyScriptCompilationFailed(int errorCount)
        {
            OnScriptCompilationFailed?.Invoke(errorCount);
        }

        internal static void NotifyScriptCompilationFailedDetailed(ScriptCompilationFailedArgs args)
        {
            OnScriptCompilationFailedDetailed?.Invoke(args);
        }

        internal static void NotifySceneSaved(Scene scene) => OnSceneSaved?.Invoke(scene);

        internal static void NotifySceneOpened(Scene scene) => OnSceneOpened?.Invoke(scene);

        internal static void NotifySceneOpenedDetailed(Scene scene, SceneOpenArgs args)
        {
            OnSceneOpenedDetailed?.Invoke(scene, args);
        }

        internal static void NotifyNewSceneCreated(Scene scene) => OnNewSceneCreated?.Invoke(scene);

        internal static void NotifyNewSceneCreatedDetailed(Scene scene, NewSceneArgs args)
        {
            OnNewSceneCreatedDetailed?.Invoke(scene, args);
        }

        internal static void NotifySceneLoaded(Scene scene) => OnSceneLoaded?.Invoke(scene);
        internal static void NotifySceneUnloaded(Scene scene) => OnSceneUnloaded?.Invoke(scene);
        internal static void NotifyPlayModeChanged(bool isPlaying) => OnPlayModeChanged?.Invoke(isPlaying);
        internal static void NotifyHierarchyChanged() => OnHierarchyChanged?.Invoke();
        internal static void NotifyGameObjectCreated(GameObject gameObject) => OnGameObjectCreated?.Invoke(gameObject);
        internal static void NotifyGameObjectDestroyed(GameObject gameObject) => OnGameObjectDestroyed?.Invoke(gameObject);
        internal static void NotifySelectionChanged(GameObject gameObject) => OnSelectionChanged?.Invoke(gameObject);
        internal static void NotifyProjectChanged() => OnProjectChanged?.Invoke();
        internal static void NotifyAssetImported() => OnAssetImported?.Invoke();
        internal static void NotifyAssetDeleted() => OnAssetDeleted?.Invoke();
        internal static void NotifyBuildCompleted(bool success) => OnBuildCompleted?.Invoke(success);

        internal static void NotifyBuildCompletedDetailed(BuildArgs args)
        {
            OnBuildCompletedDetailed?.Invoke(args);
        }

        internal static void NotifyEditorUpdate() => OnEditorUpdate?.Invoke();
        internal static void NotifyEditorIdle() => OnEditorIdle?.Invoke();
        internal static void NotifyComponentAdded(Component component) => OnComponentAdded?.Invoke(component);

        #endregion
    }
}
