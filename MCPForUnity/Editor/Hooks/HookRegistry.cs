using System;
using MCPForUnity.Editor.Hooks.EventArgs;
using MCPForUnity.Editor.Helpers;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;

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

        #region Property Events

        /// <summary>
        /// Fired when properties are modified via the Undo system.
        /// Passes the UndoPropertyModification array from Unity.
        /// Subscribers should return the array unchanged to allow Undo system to continue.
        /// </summary>
        public static event Func<UndoPropertyModification[], UndoPropertyModification[]> OnPropertiesModified;

        #endregion

        #region Internal Notification API

        /// <summary>
        /// Generic helper to safely invoke event handlers with exception handling.
        /// Prevents subscriber errors from breaking the invocation chain.
        /// Uses dynamic dispatch to handle different delegate signatures.
        /// </summary>
        private static void InvokeSafely<TDelegate>(TDelegate handler, string eventName, Action<dynamic> invoke)
            where TDelegate : class
        {
            if (handler == null) return;

            // Cast to Delegate base type to access GetInvocationList()
            var multicastDelegate = handler as Delegate;
            if (multicastDelegate == null) return;

            foreach (Delegate subscriber in multicastDelegate.GetInvocationList())
            {
                try
                {
                    invoke(subscriber);
                }
                catch (Exception ex)
                {
                    McpLog.Warn($"[HookRegistry] {eventName} subscriber threw exception: {ex.Message}");
                }
            }
        }

        // P1 Fix: Exception handling - prevent subscriber errors from breaking the invocation chain
        // This ensures that a misbehaving subscriber doesn't prevent other subscribers from receiving notifications
        internal static void NotifyScriptCompiled()
        {
            InvokeSafely(OnScriptCompiled, "OnScriptCompiled", h => h());
        }

        internal static void NotifyScriptCompiledDetailed(ScriptCompilationArgs args)
        {
            InvokeSafely(OnScriptCompiledDetailed, "OnScriptCompiledDetailed", h => h(args));
        }

        internal static void NotifyScriptCompilationFailed(int errorCount)
        {
            InvokeSafely(OnScriptCompilationFailed, "OnScriptCompilationFailed", h => h(errorCount));
        }

        internal static void NotifyScriptCompilationFailedDetailed(ScriptCompilationFailedArgs args)
        {
            InvokeSafely(OnScriptCompilationFailedDetailed, "OnScriptCompilationFailedDetailed", h => h(args));
        }

        // Apply same exception handling pattern to other notification methods
        internal static void NotifySceneSaved(Scene scene)
        {
            InvokeSafely(OnSceneSaved, "OnSceneSaved", h => h(scene));
        }

        internal static void NotifySceneOpened(Scene scene)
        {
            InvokeSafely(OnSceneOpened, "OnSceneOpened", h => h(scene));
        }

        internal static void NotifySceneOpenedDetailed(Scene scene, SceneOpenArgs args)
        {
            InvokeSafely(OnSceneOpenedDetailed, "OnSceneOpenedDetailed", h => h(scene, args));
        }

        internal static void NotifyNewSceneCreated(Scene scene)
        {
            InvokeSafely(OnNewSceneCreated, "OnNewSceneCreated", h => h(scene));
        }

        internal static void NotifyNewSceneCreatedDetailed(Scene scene, NewSceneArgs args)
        {
            InvokeSafely(OnNewSceneCreatedDetailed, "OnNewSceneCreatedDetailed", h => h(scene, args));
        }

        internal static void NotifySceneLoaded(Scene scene)
        {
            InvokeSafely(OnSceneLoaded, "OnSceneLoaded", h => h(scene));
        }

        internal static void NotifySceneUnloaded(Scene scene)
        {
            InvokeSafely(OnSceneUnloaded, "OnSceneUnloaded", h => h(scene));
        }

        internal static void NotifyPlayModeChanged(bool isPlaying)
        {
            InvokeSafely(OnPlayModeChanged, "OnPlayModeChanged", h => h(isPlaying));
        }

        internal static void NotifyHierarchyChanged()
        {
            InvokeSafely(OnHierarchyChanged, "OnHierarchyChanged", h => h());
        }

        internal static void NotifyGameObjectCreated(GameObject gameObject)
        {
            InvokeSafely(OnGameObjectCreated, "OnGameObjectCreated", h => h(gameObject));
        }

        internal static void NotifyGameObjectDestroyed(GameObject gameObject)
        {
            InvokeSafely(OnGameObjectDestroyed, "OnGameObjectDestroyed", h => h(gameObject));
        }

        internal static void NotifyGameObjectDestroyedDetailed(GameObjectDestroyedArgs args)
        {
            InvokeSafely(OnGameObjectDestroyedDetailed, "OnGameObjectDestroyedDetailed", h => h(args));
        }

        internal static void NotifySelectionChanged(GameObject gameObject)
        {
            InvokeSafely(OnSelectionChanged, "OnSelectionChanged", h => h(gameObject));
        }

        internal static void NotifyProjectChanged()
        {
            InvokeSafely(OnProjectChanged, "OnProjectChanged", h => h());
        }

        internal static void NotifyAssetImported()
        {
            InvokeSafely(OnAssetImported, "OnAssetImported", h => h());
        }

        internal static void NotifyAssetDeleted()
        {
            InvokeSafely(OnAssetDeleted, "OnAssetDeleted", h => h());
        }

        internal static void NotifyBuildCompleted(bool success)
        {
            InvokeSafely(OnBuildCompleted, "OnBuildCompleted", h => h(success));
        }

        internal static void NotifyBuildCompletedDetailed(BuildArgs args)
        {
            InvokeSafely(OnBuildCompletedDetailed, "OnBuildCompletedDetailed", h => h(args));
        }

        internal static void NotifyEditorUpdate()
        {
            InvokeSafely(OnEditorUpdate, "OnEditorUpdate", h => h());
        }

        internal static void NotifyEditorIdle()
        {
            InvokeSafely(OnEditorIdle, "OnEditorIdle", h => h());
        }

        internal static void NotifyComponentAdded(Component component)
        {
            InvokeSafely(OnComponentAdded, "OnComponentAdded", h => h(component));
        }

        internal static void NotifyComponentRemoved(Component component)
        {
            InvokeSafely(OnComponentRemoved, "OnComponentRemoved", h => h(component));
        }

        internal static void NotifyComponentRemovedDetailed(ComponentRemovedArgs args)
        {
            InvokeSafely(OnComponentRemovedDetailed, "OnComponentRemovedDetailed", h => h(args));
        }

        internal static UndoPropertyModification[] NotifyPropertiesModified(UndoPropertyModification[] modifications)
        {
            if (modifications == null) return modifications;

            var handler = OnPropertiesModified;
            if (handler == null) return modifications;

            // Cast to Delegate base type to access GetInvocationList()
            var multicastDelegate = handler as Delegate;
            if (multicastDelegate == null) return modifications;

            UndoPropertyModification[] result = modifications;

            // Chain all subscribers: each gets the result of the previous
            foreach (Func<UndoPropertyModification[], UndoPropertyModification[]> subscriber in multicastDelegate.GetInvocationList())
            {
                try
                {
                    var nextResult = subscriber(result);
                    if (nextResult != null)
                    {
                        result = nextResult;
                    }
                }
                catch (Exception ex)
                {
                    McpLog.Warn($"[HookRegistry] OnPropertiesModified subscriber threw exception: {ex.Message}");
                }
            }

            return result;
        }

        #endregion
    }
}
