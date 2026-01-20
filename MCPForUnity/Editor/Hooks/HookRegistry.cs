using System;
using MCPForUnity.Editor.ActionTrace.Sources.EventArgs;
using MCPForUnity.Editor.Helpers;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MCPForUnity.Editor.Hooks
{
    /// <summary>
    /// Built-in hook system providing subscription points for all common Unity editor events.
    /// Other systems can subscribe to these events without directly monitoring Unity callbacks.
    ///
    /// Event Design:
    /// - Simple events: Use for basic notifications (backward compatible)
    /// - Detailed events: Include additional context via Args classes (defined in HookEventArgs.cs)
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

        public static event Action OnScriptCompiled;
        public static event Action<ScriptCompilationArgs> OnScriptCompiledDetailed;
        public static event Action<int> OnScriptCompilationFailed;
        public static event Action<ScriptCompilationFailedArgs> OnScriptCompilationFailedDetailed;

        #endregion

        #region Scene Events

        public static event Action<Scene> OnSceneSaved;
        public static event Action<Scene> OnSceneOpened;
        public static event Action<Scene, SceneOpenArgs> OnSceneOpenedDetailed;
        public static event Action<Scene> OnNewSceneCreated;
        public static event Action<Scene, NewSceneArgs> OnNewSceneCreatedDetailed;
        public static event Action<Scene> OnSceneLoaded;
        public static event Action<Scene> OnSceneUnloaded;

        #endregion

        #region Play Mode Events

        public static event Action<bool> OnPlayModeChanged;

        #endregion

        #region Hierarchy Events

        public static event Action OnHierarchyChanged;
        public static event Action<GameObject> OnGameObjectCreated;
        public static event Action<GameObject> OnGameObjectDestroyed;
        public static event Action<GameObjectDestroyedArgs> OnGameObjectDestroyedDetailed;

        #endregion

        #region Selection Events

        public static event Action<GameObject> OnSelectionChanged;

        #endregion

        #region Project Events

        public static event Action OnProjectChanged;
        public static event Action OnAssetImported;
        public static event Action OnAssetDeleted;

        #endregion

        #region Build Events

        public static event Action<bool> OnBuildCompleted;
        public static event Action<BuildArgs> OnBuildCompletedDetailed;

        #endregion

        #region Editor State Events

        public static event Action OnEditorUpdate;
        public static event Action OnEditorIdle;

        #endregion

        #region Component Events

        public static event Action<Component> OnComponentAdded;
        public static event Action<Component> OnComponentRemoved;
        public static event Action<ComponentRemovedArgs> OnComponentRemovedDetailed;

        #endregion

        #region Internal Notification API

        // P1 Fix: Exception handling - prevent subscriber errors from breaking the invocation chain
        // This ensures that a misbehaving subscriber doesn't prevent other subscribers from receiving notifications
        internal static void NotifyScriptCompiled()
        {
            var handler = OnScriptCompiled;
            if (handler == null) return;

            foreach (var subscriber in handler.GetInvocationList())
            {
                try
                {
                    ((Action)subscriber)();
                }
                catch (Exception ex)
                {
                    McpLog.Warn($"[HookRegistry] OnScriptCompiled subscriber threw exception: {ex.Message}");
                }
            }
        }

        internal static void NotifyScriptCompiledDetailed(ScriptCompilationArgs args)
        {
            var handler = OnScriptCompiledDetailed;
            if (handler == null) return;

            foreach (var subscriber in handler.GetInvocationList())
            {
                try
                {
                    ((Action<ScriptCompilationArgs>)subscriber)(args);
                }
                catch (Exception ex)
                {
                    McpLog.Warn($"[HookRegistry] OnScriptCompiledDetailed subscriber threw exception: {ex.Message}");
                }
            }
        }

        internal static void NotifyScriptCompilationFailed(int errorCount)
        {
            var handler = OnScriptCompilationFailed;
            if (handler == null) return;

            foreach (var subscriber in handler.GetInvocationList())
            {
                try
                {
                    ((Action<int>)subscriber)(errorCount);
                }
                catch (Exception ex)
                {
                    McpLog.Warn($"[HookRegistry] OnScriptCompilationFailed subscriber threw exception: {ex.Message}");
                }
            }
        }

        internal static void NotifyScriptCompilationFailedDetailed(ScriptCompilationFailedArgs args)
        {
            var handler = OnScriptCompilationFailedDetailed;
            if (handler == null) return;

            foreach (var subscriber in handler.GetInvocationList())
            {
                try
                {
                    ((Action<ScriptCompilationFailedArgs>)subscriber)(args);
                }
                catch (Exception ex)
                {
                    McpLog.Warn($"[HookRegistry] OnScriptCompilationFailedDetailed subscriber threw exception: {ex.Message}");
                }
            }
        }

        // Apply same exception handling pattern to other notification methods
        internal static void NotifySceneSaved(Scene scene)
        {
            var handler = OnSceneSaved;
            if (handler == null) return;

            foreach (var subscriber in handler.GetInvocationList())
            {
                try { ((Action<Scene>)subscriber)(scene); }
                catch (Exception ex) { McpLog.Warn($"[HookRegistry] OnSceneSaved subscriber threw exception: {ex.Message}"); }
            }
        }

        internal static void NotifySceneOpened(Scene scene)
        {
            var handler = OnSceneOpened;
            if (handler == null) return;

            foreach (var subscriber in handler.GetInvocationList())
            {
                try { ((Action<Scene>)subscriber)(scene); }
                catch (Exception ex) { McpLog.Warn($"[HookRegistry] OnSceneOpened subscriber threw exception: {ex.Message}"); }
            }
        }

        internal static void NotifySceneOpenedDetailed(Scene scene, SceneOpenArgs args)
        {
            var handler = OnSceneOpenedDetailed;
            if (handler == null) return;

            foreach (var subscriber in handler.GetInvocationList())
            {
                try { ((Action<Scene, SceneOpenArgs>)subscriber)(scene, args); }
                catch (Exception ex) { McpLog.Warn($"[HookRegistry] OnSceneOpenedDetailed subscriber threw exception: {ex.Message}"); }
            }
        }

        internal static void NotifyNewSceneCreated(Scene scene)
        {
            var handler = OnNewSceneCreated;
            if (handler == null) return;

            foreach (var subscriber in handler.GetInvocationList())
            {
                try { ((Action<Scene>)subscriber)(scene); }
                catch (Exception ex) { McpLog.Warn($"[HookRegistry] OnNewSceneCreated subscriber threw exception: {ex.Message}"); }
            }
        }

        internal static void NotifyNewSceneCreatedDetailed(Scene scene, NewSceneArgs args)
        {
            var handler = OnNewSceneCreatedDetailed;
            if (handler == null) return;

            foreach (var subscriber in handler.GetInvocationList())
            {
                try { ((Action<Scene, NewSceneArgs>)subscriber)(scene, args); }
                catch (Exception ex) { McpLog.Warn($"[HookRegistry] OnNewSceneCreatedDetailed subscriber threw exception: {ex.Message}"); }
            }
        }

        internal static void NotifySceneLoaded(Scene scene)
        {
            var handler = OnSceneLoaded;
            if (handler == null) return;

            foreach (var subscriber in handler.GetInvocationList())
            {
                try { ((Action<Scene>)subscriber)(scene); }
                catch (Exception ex) { McpLog.Warn($"[HookRegistry] OnSceneLoaded subscriber threw exception: {ex.Message}"); }
            }
        }

        internal static void NotifySceneUnloaded(Scene scene)
        {
            var handler = OnSceneUnloaded;
            if (handler == null) return;

            foreach (var subscriber in handler.GetInvocationList())
            {
                try { ((Action<Scene>)subscriber)(scene); }
                catch (Exception ex) { McpLog.Warn($"[HookRegistry] OnSceneUnloaded subscriber threw exception: {ex.Message}"); }
            }
        }

        internal static void NotifyPlayModeChanged(bool isPlaying)
        {
            var handler = OnPlayModeChanged;
            if (handler == null) return;

            foreach (var subscriber in handler.GetInvocationList())
            {
                try { ((Action<bool>)subscriber)(isPlaying); }
                catch (Exception ex) { McpLog.Warn($"[HookRegistry] OnPlayModeChanged subscriber threw exception: {ex.Message}"); }
            }
        }

        internal static void NotifyHierarchyChanged()
        {
            var handler = OnHierarchyChanged;
            if (handler == null) return;

            foreach (var subscriber in handler.GetInvocationList())
            {
                try { ((Action)subscriber)(); }
                catch (Exception ex) { McpLog.Warn($"[HookRegistry] OnHierarchyChanged subscriber threw exception: {ex.Message}"); }
            }
        }

        internal static void NotifyGameObjectCreated(GameObject gameObject)
        {
            var handler = OnGameObjectCreated;
            if (handler == null) return;

            foreach (var subscriber in handler.GetInvocationList())
            {
                try { ((Action<GameObject>)subscriber)(gameObject); }
                catch (Exception ex) { McpLog.Warn($"[HookRegistry] OnGameObjectCreated subscriber threw exception: {ex.Message}"); }
            }
        }

        internal static void NotifyGameObjectDestroyed(GameObject gameObject)
        {
            var handler = OnGameObjectDestroyed;
            if (handler == null) return;

            foreach (var subscriber in handler.GetInvocationList())
            {
                try { ((Action<GameObject>)subscriber)(gameObject); }
                catch (Exception ex) { McpLog.Warn($"[HookRegistry] OnGameObjectDestroyed subscriber threw exception: {ex.Message}"); }
            }
        }

        internal static void NotifyGameObjectDestroyedDetailed(GameObjectDestroyedArgs args)
        {
            var handler = OnGameObjectDestroyedDetailed;
            if (handler == null) return;

            foreach (var subscriber in handler.GetInvocationList())
            {
                try { ((Action<GameObjectDestroyedArgs>)subscriber)(args); }
                catch (Exception ex) { McpLog.Warn($"[HookRegistry] OnGameObjectDestroyedDetailed subscriber threw exception: {ex.Message}"); }
            }
        }

        internal static void NotifySelectionChanged(GameObject gameObject)
        {
            var handler = OnSelectionChanged;
            if (handler == null) return;

            foreach (var subscriber in handler.GetInvocationList())
            {
                try { ((Action<GameObject>)subscriber)(gameObject); }
                catch (Exception ex) { McpLog.Warn($"[HookRegistry] OnSelectionChanged subscriber threw exception: {ex.Message}"); }
            }
        }

        internal static void NotifyProjectChanged()
        {
            var handler = OnProjectChanged;
            if (handler == null) return;

            foreach (var subscriber in handler.GetInvocationList())
            {
                try { ((Action)subscriber)(); }
                catch (Exception ex) { McpLog.Warn($"[HookRegistry] OnProjectChanged subscriber threw exception: {ex.Message}"); }
            }
        }

        internal static void NotifyAssetImported()
        {
            var handler = OnAssetImported;
            if (handler == null) return;

            foreach (var subscriber in handler.GetInvocationList())
            {
                try { ((Action)subscriber)(); }
                catch (Exception ex) { McpLog.Warn($"[HookRegistry] OnAssetImported subscriber threw exception: {ex.Message}"); }
            }
        }

        internal static void NotifyAssetDeleted()
        {
            var handler = OnAssetDeleted;
            if (handler == null) return;

            foreach (var subscriber in handler.GetInvocationList())
            {
                try { ((Action)subscriber)(); }
                catch (Exception ex) { McpLog.Warn($"[HookRegistry] OnAssetDeleted subscriber threw exception: {ex.Message}"); }
            }
        }

        internal static void NotifyBuildCompleted(bool success)
        {
            var handler = OnBuildCompleted;
            if (handler == null) return;

            foreach (var subscriber in handler.GetInvocationList())
            {
                try { ((Action<bool>)subscriber)(success); }
                catch (Exception ex) { McpLog.Warn($"[HookRegistry] OnBuildCompleted subscriber threw exception: {ex.Message}"); }
            }
        }

        internal static void NotifyBuildCompletedDetailed(BuildArgs args)
        {
            var handler = OnBuildCompletedDetailed;
            if (handler == null) return;

            foreach (var subscriber in handler.GetInvocationList())
            {
                try { ((Action<BuildArgs>)subscriber)(args); }
                catch (Exception ex) { McpLog.Warn($"[HookRegistry] OnBuildCompletedDetailed subscriber threw exception: {ex.Message}"); }
            }
        }

        internal static void NotifyEditorUpdate()
        {
            var handler = OnEditorUpdate;
            if (handler == null) return;

            foreach (var subscriber in handler.GetInvocationList())
            {
                try { ((Action)subscriber)(); }
                catch (Exception ex) { McpLog.Warn($"[HookRegistry] OnEditorUpdate subscriber threw exception: {ex.Message}"); }
            }
        }

        internal static void NotifyEditorIdle()
        {
            var handler = OnEditorIdle;
            if (handler == null) return;

            foreach (var subscriber in handler.GetInvocationList())
            {
                try { ((Action)subscriber)(); }
                catch (Exception ex) { McpLog.Warn($"[HookRegistry] OnEditorIdle subscriber threw exception: {ex.Message}"); }
            }
        }

        internal static void NotifyComponentAdded(Component component)
        {
            var handler = OnComponentAdded;
            if (handler == null) return;

            foreach (var subscriber in handler.GetInvocationList())
            {
                try { ((Action<Component>)subscriber)(component); }
                catch (Exception ex) { McpLog.Warn($"[HookRegistry] OnComponentAdded subscriber threw exception: {ex.Message}"); }
            }
        }

        internal static void NotifyComponentRemoved(Component component)
        {
            var handler = OnComponentRemoved;
            if (handler == null) return;

            foreach (var subscriber in handler.GetInvocationList())
            {
                try { ((Action<Component>)subscriber)(component); }
                catch (Exception ex) { McpLog.Warn($"[HookRegistry] OnComponentRemoved subscriber threw exception: {ex.Message}"); }
            }
        }

        internal static void NotifyComponentRemovedDetailed(ComponentRemovedArgs args)
        {
            var handler = OnComponentRemovedDetailed;
            if (handler == null) return;

            foreach (var subscriber in handler.GetInvocationList())
            {
                try { ((Action<ComponentRemovedArgs>)subscriber)(args); }
                catch (Exception ex) { McpLog.Warn($"[HookRegistry] OnComponentRemovedDetailed subscriber threw exception: {ex.Message}"); }
            }
        }

        #endregion
    }
}
