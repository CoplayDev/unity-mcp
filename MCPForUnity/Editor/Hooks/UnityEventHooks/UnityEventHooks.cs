using System;
using MCPForUnity.Editor.Hooks.EventArgs;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MCPForUnity.Editor.Hooks
{
    /// <summary>
    /// Pure event detector for Unity editor events.
    /// Detects Unity callbacks and notifies HookRegistry for other systems to subscribe.
    ///
    /// Architecture:
    /// Unity Events → UnityEventHooks (detection) → HookRegistry → Subscribers
    ///
    /// You should use HookRegistry to subscribe to events, not UnityEventHooks directly.
    ///
    /// Hook Coverage:
    /// - Component events: ComponentAdded
    /// - GameObject events: GameObjectCreated, GameObjectDestroyed
    /// - Hierarchy events: HierarchyChanged
    /// - Selection events: SelectionChanged
    /// - Play mode events: PlayModeChanged
    /// - Scene events: SceneSaved, SceneOpened, SceneLoaded, SceneUnloaded, NewSceneCreated
    /// - Script events: ScriptCompiled, ScriptCompilationFailed
    /// - Build events: BuildCompleted
    /// - Editor events: EditorUpdate
    /// </summary>
    [InitializeOnLoad]
    public static partial class UnityEventHooks
    {
        #region Hierarchy State

        private static DateTime _lastHierarchyChange;
        private static readonly object _lock = new();
        private static bool _isInitialized;

        #endregion

        static UnityEventHooks()
        {
            // Subscribe to cleanup events first
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            EditorApplication.quitting -= OnEditorQuitting;
            EditorApplication.quitting += OnEditorQuitting;

            // Only initialize subscriptions once
            if (!_isInitialized)
            {
                SubscribeToUnityEvents();
                _isInitialized = true;
            }
        }

        /// <summary>
        /// Subscribe to all Unity events.
        /// </summary>
        private static void SubscribeToUnityEvents()
        {
            // GameObject/Component Events
            ObjectFactory.componentWasAdded += OnComponentAdded;

            // Hierarchy Events
            EditorApplication.hierarchyChanged += OnHierarchyChanged;

            // Selection Events
            Selection.selectionChanged += OnSelectionChanged;

            // Play Mode Events
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

            // Scene Events
            EditorSceneManager.sceneSaved += OnSceneSaved;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorSceneManager.sceneLoaded += OnSceneLoaded;
            EditorSceneManager.sceneUnloaded += OnSceneUnloaded;
            EditorSceneManager.newSceneCreated += OnNewSceneCreated;

            // Build Events
            BuildPlayerWindow.RegisterBuildPlayerHandler(options => BuildPlayerHandler(options));

            // Editor Update
            EditorApplication.update += OnUpdate;

            // Initialize tracking (one-time delayCall is safe)
            EditorApplication.delayCall += () => InitializeTracking();
        }

        /// <summary>
        /// Unsubscribe from all Unity events.
        /// Called before domain reload and when editor quits.
        /// </summary>
        private static void UnsubscribeFromUnityEvents()
        {
            // GameObject/Component Events
            ObjectFactory.componentWasAdded -= OnComponentAdded;

            // Hierarchy Events
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;

            // Selection Events
            Selection.selectionChanged -= OnSelectionChanged;

            // Play Mode Events
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;

            // Scene Events
            EditorSceneManager.sceneSaved -= OnSceneSaved;
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            EditorSceneManager.sceneLoaded -= OnSceneLoaded;
            EditorSceneManager.sceneUnloaded -= OnSceneUnloaded;
            EditorSceneManager.newSceneCreated -= OnNewSceneCreated;

            // Editor Update
            EditorApplication.update -= OnUpdate;

            // Note: BuildPlayerHandler doesn't have an unregister API
        }

        /// <summary>
        /// Called before assembly reload (domain reload).
        /// Unsubscribes from all Unity events to prevent memory leaks.
        /// </summary>
        private static void OnBeforeAssemblyReload()
        {
            UnsubscribeFromUnityEvents();
            ResetTracking();
            _isInitialized = false;
        }

        /// <summary>
        /// Called when Unity editor is quitting.
        /// Unsubscribes from all Unity events to ensure clean shutdown.
        /// </summary>
        private static void OnEditorQuitting()
        {
            UnsubscribeFromUnityEvents();
            ResetTracking();
            _isInitialized = false;
        }

        #region GameObject/Component Events

        private static void OnComponentAdded(Component component)
        {
            if (component == null) return;
            HookRegistry.NotifyComponentAdded(component);

            var gameObject = component.gameObject;
            if (gameObject != null) RegisterGameObjectForTracking(gameObject);
        }

        #endregion

        #region Hierarchy Events

        private static void OnHierarchyChanged()
        {
            var now = DateTime.Now;
            lock (_lock)
            {
                // Debounce: ignore changes within 200ms of the last one
                if ((now - _lastHierarchyChange).TotalMilliseconds < 200) return;
                _lastHierarchyChange = now;
            }

            HookRegistry.NotifyHierarchyChanged();
            TrackComponentRemoval();
        }

        #endregion

        #region Selection Events

        private static void OnSelectionChanged()
        {
            GameObject selectedGo = Selection.activeObject as GameObject;
            HookRegistry.NotifySelectionChanged(selectedGo);

            if (selectedGo != null) RegisterGameObjectForTracking(selectedGo);
        }

        #endregion

        #region Play Mode Events

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.EnteredPlayMode:
                    HookRegistry.NotifyPlayModeChanged(true);
                    break;
                case PlayModeStateChange.EnteredEditMode:
                    HookRegistry.NotifyPlayModeChanged(false);
                    break;
            }
        }

        #endregion

        #region Scene Events

        private static void OnSceneSaved(Scene scene) => HookRegistry.NotifySceneSaved(scene);

        private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            HookRegistry.NotifySceneOpened(scene);
            HookRegistry.NotifySceneOpenedDetailed(scene, new SceneOpenArgs { Mode = mode });
        }

        private static void OnNewSceneCreated(Scene scene, NewSceneSetup setup, NewSceneMode mode)
        {
            HookRegistry.NotifyNewSceneCreated(scene);
            HookRegistry.NotifyNewSceneCreatedDetailed(scene, new NewSceneArgs { Setup = setup, Mode = mode });
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            HookRegistry.NotifySceneLoaded(scene);
            ResetTracking();
            InitializeTracking();
        }

        private static void OnSceneUnloaded(Scene scene)
        {
            HookRegistry.NotifySceneUnloaded(scene);
            ResetTracking();
        }

        #endregion

        #region Editor Update Events

        private static void OnUpdate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            HookRegistry.NotifyEditorUpdate();
            TrackScriptCompilation();
            TrackGameObjectChanges();
        }

        #endregion

        #region Tracking Extension Points (for Advanced features)

        /// <summary>
        /// Extension point for tracking initialization.
        /// Override in Advanced partial class to provide custom tracking.
        /// </summary>
        static partial void InitializeTracking();

        /// <summary>
        /// Extension point for tracking reset.
        /// Override in Advanced partial class to provide custom tracking.
        /// </summary>
        static partial void ResetTracking();

        /// <summary>
        /// Extension point for GameObject registration.
        /// Called when a GameObject is selected or has a component added.
        /// </summary>
        static partial void RegisterGameObjectForTracking(GameObject gameObject);

        /// <summary>
        /// Extension point for script compilation tracking.
        /// Override in Advanced partial class to detect compilation state changes.
        /// </summary>
        static partial void TrackScriptCompilation();

        /// <summary>
        /// Extension point for GameObject change tracking.
        /// Override in Advanced partial class to detect created/destroyed GameObjects.
        /// </summary>
        static partial void TrackGameObjectChanges();

        /// <summary>
        /// Extension point for component removal tracking.
        /// Override in Advanced partial class to detect removed components.
        /// </summary>
        static partial void TrackComponentRemoval();

        /// <summary>
        /// Extension point for build player handling.
        /// Override in Advanced partial class to handle build completion.
        /// </summary>
        static partial void BuildPlayerHandler(BuildPlayerOptions options);

        #endregion
    }
}
