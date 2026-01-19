using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Hooks;
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
    /// You should use HookRegistry to subscribe to events, not UnityEventHooks directly.
    ///
    /// This keeps UnityEventHooks as a pure detector without dependencies on ActionTrace
    /// or other recording systems.
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
    public static class UnityEventHooks
    {
        private static DateTime _lastHierarchyChange;
        private static readonly object _lock = new();

        // Track compilation state
        private static DateTime _compileStartTime;
        private static bool _isCompiling;

        // Track build state (moved to Build Events region)

        static UnityEventHooks()
        {
            // ========== GameObject/Component Events ==========
            ObjectFactory.componentWasAdded += OnComponentAdded;

            // Monitor hierarchy changes (with debouncing)
            EditorApplication.hierarchyChanged += OnHierarchyChanged;

            // ========== Selection Events ==========
            Selection.selectionChanged += OnSelectionChanged;

            // ========== Play Mode Events ==========
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

            // ========== Scene Events ==========
            EditorSceneManager.sceneSaved += OnSceneSaved;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorSceneManager.sceneLoaded += OnSceneLoaded;
            EditorSceneManager.sceneUnloaded += OnSceneUnloaded;
            EditorSceneManager.newSceneCreated += OnNewSceneCreated;

            // ========== Build Events ==========
            BuildPlayerWindow.RegisterBuildPlayerHandler(BuildPlayerHandler);

            // Track changes in edit mode to detect GameObject creation/destruction
            EditorApplication.update += OnUpdate;

            // Initialize GameObject tracking after domain reload
            EditorApplication.delayCall += () => InitializeTracking();
        }

        #region GameObject/Component Events

        private static void OnComponentAdded(Component component)
        {
            if (component == null) return;
            HookRegistry.NotifyComponentAdded(component);
        }

        private static void OnUpdate()
        {
            // Only track in edit mode, not during play mode
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            // Notify update event
            HookRegistry.NotifyEditorUpdate();

            // Track script compilation
            TrackScriptCompilation();

            // Detect GameObject changes
            TrackGameObjectChanges();
        }

        private static void TrackGameObjectChanges()
        {
            var changes = DetectGameObjectChanges();

            foreach (var change in changes)
            {
                if (change.isNew)
                {
                    HookRegistry.NotifyGameObjectCreated(change.obj);
                }
            }

            // Check for destroyed GameObjects
            var destroyedIds = GetDestroyedInstanceIds();
            foreach (int _ in destroyedIds)
            {
                // GameObject already destroyed, pass null
                HookRegistry.NotifyGameObjectDestroyed(null);
            }
        }

        #endregion

        #region Selection Events

        private static void OnSelectionChanged()
        {
            GameObject selectedGo = Selection.activeObject as GameObject;
            HookRegistry.NotifySelectionChanged(selectedGo);
        }

        #endregion

        #region Hierarchy Events

        private static void OnHierarchyChanged()
        {
            var now = DateTime.Now;
            lock (_lock)
            {
                // Debounce: ignore changes within 200ms of the last one
                if ((now - _lastHierarchyChange).TotalMilliseconds < 200)
                {
                    return;
                }
                _lastHierarchyChange = now;
            }

            HookRegistry.NotifyHierarchyChanged();
        }

        #endregion

        #region Play Mode Events

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            bool isPlaying = state == PlayModeStateChange.EnteredPlayMode;
            HookRegistry.NotifyPlayModeChanged(isPlaying);
        }

        #endregion

        #region Scene Events

        private static void OnSceneSaved(Scene scene)
        {
            HookRegistry.NotifySceneSaved(scene);
        }

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

            // Reset tracking to clear stale data from previous scenes
            ResetTracking();
            InitializeTracking();
        }

        private static void OnSceneUnloaded(Scene scene)
        {
            HookRegistry.NotifySceneUnloaded(scene);

            // Reset tracking before clearing the scene
            ResetTracking();
        }

        #endregion

        #region Script Compilation Events

        private static void TrackScriptCompilation()
        {
            bool isNowCompiling = EditorApplication.isCompiling;

            if (isNowCompiling && !_isCompiling)
            {
                // Compilation just started
                _compileStartTime = DateTime.UtcNow;
                _isCompiling = true;
            }
            else if (!isNowCompiling && _isCompiling)
            {
                // Compilation just finished
                _isCompiling = false;

                var duration = DateTime.UtcNow - _compileStartTime;
                int scriptCount = CountScripts();
                int errorCount = GetCompilationErrorCount();

                if (errorCount > 0)
                {
                    HookRegistry.NotifyScriptCompilationFailed(errorCount);
                    HookRegistry.NotifyScriptCompilationFailedDetailed(new ScriptCompilationFailedArgs
                    {
                        ScriptCount = scriptCount,
                        DurationMs = (long)duration.TotalMilliseconds,
                        ErrorCount = errorCount
                    });
                }
                else
                {
                    HookRegistry.NotifyScriptCompiled();
                    HookRegistry.NotifyScriptCompiledDetailed(new ScriptCompilationArgs
                    {
                        ScriptCount = scriptCount,
                        DurationMs = (long)duration.TotalMilliseconds
                    });
                }
            }
        }

        private static int CountScripts()
        {
            try
            {
                return AssetDatabase.FindAssets("t:Script").Length;
            }
            catch
            {
                return 0;
            }
        }

        private static int GetCompilationErrorCount()
        {
            try
            {
                var assembly = typeof(UnityEditor.EditorUtility).Assembly;
                var type = assembly.GetType("UnityEditor.Scripting.ScriptCompilationErrorCount");
                if (type != null)
                {
                    var property = type.GetProperty("errorCount", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                    if (property != null)
                    {
                        var value = property.GetValue(null);
                        if (value is int count)
                            return count;
                    }
                }
                return 0;
            }
            catch
            {
                return 0;
            }
        }

        #endregion

        #region Build Events

        private static DateTime _buildStartTime;
        private static string _currentBuildPlatform;

        private static void BuildPlayerHandler(BuildPlayerOptions options)
        {
            _buildStartTime = DateTime.UtcNow;
            _currentBuildPlatform = GetBuildTargetName(options.target);

            // Execute the build
            var result = BuildPipeline.BuildPlayer(options);

            // Notify build result
            var duration = DateTime.UtcNow - _buildStartTime;
            bool success = result.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded;

            HookRegistry.NotifyBuildCompleted(success);
            HookRegistry.NotifyBuildCompletedDetailed(new BuildArgs
            {
                Platform = _currentBuildPlatform,
                Location = options.locationPathName,
                DurationMs = (long)duration.TotalMilliseconds,
                SizeBytes = success ? result.summary.totalSize : null,
                Success = success,
                Summary = success ? null : result.summary.ToString()
            });

            _currentBuildPlatform = null;
        }

        private static string GetBuildTargetName(BuildTarget target)
        {
            try
            {
                var assembly = typeof(HookRegistry).Assembly;
                var type = assembly.GetType("MCPForUnity.Editor.ActionTrace.Helpers.BuildTargetUtility");
                if (type != null)
                {
                    var method = type.GetMethod("GetBuildTargetName", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                    if (method != null)
                    {
                        var result = method.Invoke(null, new object[] { target });
                        if (result is string name)
                            return name;
                    }
                }
            }
            catch { }

            return target.ToString();
        }

        #endregion

        #region GameObject Tracking

        private static GameObjectTrackingHelper _trackingHelper;

        private static void InitializeTracking()
        {
            if (_trackingHelper == null)
            {
                _trackingHelper = new GameObjectTrackingHelper();
            }
            _trackingHelper.InitializeTracking();
        }

        private static void ResetTracking()
        {
            _trackingHelper?.Reset();
        }

        private static List<(GameObject obj, bool isNew)> DetectGameObjectChanges()
        {
            if (_trackingHelper == null)
                return new List<(GameObject, bool)>();

            return _trackingHelper.DetectChanges();
        }

        private static List<int> GetDestroyedInstanceIds()
        {
            return _trackingHelper?.GetDestroyedInstanceIds() ?? new List<int>();
        }

        /// <summary>
        /// Helper for tracking GameObject creation and destruction.
        /// Uses HashSet for O(1) lookup instead of List.Contains O(n).
        /// </summary>
        private class GameObjectTrackingHelper
        {
            private HashSet<int> _previousInstanceIds = new(256);
            private bool _hasInitialized;

            public void InitializeTracking()
            {
                if (_hasInitialized) return;

                _previousInstanceIds.Clear();
                _previousInstanceIds.EnsureCapacity(256);

                try
                {
                    GameObject[] allObjects = GameObject.FindObjectsOfType<GameObject>(true);
                    foreach (var go in allObjects)
                    {
                        if (go != null)
                            _previousInstanceIds.Add(go.GetInstanceID());
                    }
                }
                catch (Exception ex)
                {
                    McpLog.Warn($"[UnityEventHooks] Failed to initialize GameObject tracking: {ex.Message}");
                }

                _hasInitialized = true;
            }

            public void Reset()
            {
                _previousInstanceIds.Clear();
                _hasInitialized = false;
            }

            public List<(GameObject obj, bool isNew)> DetectChanges()
            {
                if (!_hasInitialized)
                {
                    InitializeTracking();
                    return new List<(GameObject, bool)>(0);
                }

                var results = new List<(GameObject, bool)>(64);
                var currentIds = new HashSet<int>(256);

                try
                {
                    GameObject[] currentObjects = GameObject.FindObjectsOfType<GameObject>(true);

                    foreach (var go in currentObjects)
                    {
                        if (go == null) continue;

                        int id = go.GetInstanceID();
                        currentIds.Add(id);

                        bool isNew = !_previousInstanceIds.Contains(id);
                        results.Add((go, isNew));
                    }

                    // Update tracking
                    _previousInstanceIds.Clear();
                    foreach (int id in currentIds)
                    {
                        _previousInstanceIds.Add(id);
                    }
                }
                catch (Exception ex)
                {
                    McpLog.Warn($"[UnityEventHooks] Failed to detect GameObject changes: {ex.Message}");
                }

                return results;
            }

            public List<int> GetDestroyedInstanceIds()
            {
                if (!_hasInitialized)
                    return new List<int>(0);

                var destroyed = new List<int>(8);
                var currentIds = new HashSet<int>(256);

                try
                {
                    GameObject[] currentObjects = GameObject.FindObjectsOfType<GameObject>(true);
                    foreach (var go in currentObjects)
                    {
                        if (go != null)
                            currentIds.Add(go.GetInstanceID());
                    }

                    // Find IDs that were in previous but not in current
                    foreach (int id in _previousInstanceIds)
                    {
                        if (!currentIds.Contains(id))
                            destroyed.Add(id);
                    }

                    // Update tracking for next call
                    _previousInstanceIds.Clear();
                    foreach (int id in currentIds)
                    {
                        _previousInstanceIds.Add(id);
                    }
                }
                catch (Exception ex)
                {
                    McpLog.Warn($"[UnityEventHooks] Failed to get destroyed instance IDs: {ex.Message}");
                }

                return destroyed;
            }
        }

        #endregion
    }
}
