using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.ActionTrace.Core;
using MCPForUnity.Editor.ActionTrace.Helpers;
using MCPForUnity.Editor.Helpers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MCPForUnity.Editor.ActionTrace.Capture
{
    /// <summary>
    /// Captures Unity editor events and records them to the EventStore.
    /// Uses debouncing to avoid spamming for rapid successive changes.
    ///
    /// Hook Coverage:
    /// - Component events: ComponentAdded
    /// - GameObject events: GameObjectCreated, GameObjectDestroyed
    /// - Hierarchy events: HierarchyChanged
    /// - Selection events: SelectionChanged
    /// - Play mode events: PlayModeChanged
    /// - Scene events: SceneSaving, SceneSaved, SceneOpened, NewSceneCreated
    /// - Script events: ScriptCompiled, ScriptCompilationFailed
    /// - Build events: BuildStarted, BuildCompleted, BuildFailed
    ///
    /// Note: Asset events are handled in AssetChangePostprocessor.cs
    /// </summary>
    [InitializeOnLoad]
    public static class UnityEventHooks
    {
        private static DateTime _lastHierarchyChange;
        private static readonly object _lock = new();

        // Track compilation state
        private static DateTime _compileStartTime;
        private static bool _isCompiling;

        // Track build state
        private static string _currentBuildPlatform;
        private static DateTime _buildStartTime;

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
            EditorSceneManager.sceneSaving += OnSceneSaving;
            EditorSceneManager.sceneSaved += OnSceneSaved;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorSceneManager.newSceneCreated += OnNewSceneCreated;

            // ========== Build Events ==========
            BuildPlayerWindow.RegisterBuildPlayerHandler(BuildPlayerHandler);

            // Track changes in edit mode to detect GameObject creation/destruction
            EditorApplication.update += OnUpdate;

            // Initialize GameObject tracking after domain reload
            EditorApplication.delayCall += () => GameObjectTrackingHelper.InitializeTracking();
        }

        #region GameObject/Component Events

        /// <summary>
        /// Called when a component is added to a GameObject.
        /// </summary>
        private static void OnComponentAdded(Component component)
        {
            if (component == null) return;

            string globalId = GlobalIdHelper.ToGlobalIdString(component);

            var payload = new Dictionary<string, object>
            {
                ["component_type"] = component.GetType().Name,
                ["game_object"] = component.gameObject?.name ?? "Unknown"
            };

            RecordEvent(EventTypes.ComponentAdded, globalId, payload);
        }

        /// <summary>
        /// Update loop for detecting GameObject creation and destruction.
        /// </summary>
        private static void OnUpdate()
        {
            // Only track in edit mode, not during play mode
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            // Track script compilation
            TrackScriptCompilation();

            // Detect GameObject changes
            TrackGameObjectChanges();
        }

        /// <summary>
        /// Track GameObject creation and destruction using GameObjectTrackingHelper.
        /// </summary>
        private static void TrackGameObjectChanges()
        {
            var changes = GameObjectTrackingHelper.DetectChanges();

            foreach (var change in changes)
            {
                if (change.isNew)
                {
                    GameObject go = change.obj;
                    string globalId = GlobalIdHelper.ToGlobalIdString(go);

                    var payload = new Dictionary<string, object>
                    {
                        ["name"] = go.name,
                        ["tag"] = go.tag,
                        ["layer"] = go.layer,
                        ["scene"] = go.scene.name,
                        ["is_prefab"] = PrefabUtility.IsPartOfAnyPrefab(go)
                    };

                    RecordEvent(EventTypes.GameObjectCreated, globalId, payload);
                }
            }

            // Check for destroyed GameObjects
            var destroyedIds = GameObjectTrackingHelper.GetDestroyedInstanceIds();
            foreach (int id in destroyedIds)
            {
                string globalId = $"Instance:{id}";

                var payload = new Dictionary<string, object>
                {
                    ["instance_id"] = id,
                    ["destroyed"] = true
                };

                RecordEvent(EventTypes.GameObjectDestroyed, globalId, payload);
            }
        }

        #endregion

        #region Selection Events

        /// <summary>
        /// Handles Selection changes (P2.3: Selection Tracking).
        /// Records what the user is currently focusing on for AI context awareness.
        /// </summary>
        private static void OnSelectionChanged()
        {
            if (Selection.activeObject == null)
                return;

            string globalId = GlobalIdHelper.ToGlobalIdString(Selection.activeObject);

            var payload = new Dictionary<string, object>
            {
                ["name"] = Selection.activeObject.name,
                ["type"] = Selection.activeObject.GetType().Name,
                ["instance_id"] = Selection.activeObject.GetInstanceID()
            };

            // Add path for GameObject/Component selections
            if (Selection.activeObject is GameObject go)
            {
                payload["path"] = GetGameObjectPath(go);
            }
            else if (Selection.activeObject is Component comp)
            {
                payload["path"] = GetGameObjectPath(comp.gameObject);
                payload["component_type"] = comp.GetType().Name;
            }

            RecordEvent(EventTypes.SelectionChanged, globalId, payload);
        }

        #endregion

        #region Hierarchy Events

        /// <summary>
        /// Handles hierarchy changes with debouncing.
        /// </summary>
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

            RecordEvent(EventTypes.HierarchyChanged, "Scene", new Dictionary<string, object>());
        }

        #endregion

        #region Play Mode Events

        /// <summary>
        /// Handles play mode state changes.
        /// </summary>
        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            var payload = new Dictionary<string, object>
            {
                ["state"] = state.ToString()
            };

            RecordEvent(EventTypes.PlayModeChanged, "Editor", payload);
        }

        #endregion

        #region Scene Events

        /// <summary>
        /// Called when a scene is about to be saved.
        /// </summary>
        private static void OnSceneSaving(Scene scene, string path)
        {
            string targetId = string.IsNullOrEmpty(path) ? scene.name : $"Asset:{path}";

            var payload = new Dictionary<string, object>
            {
                ["scene_name"] = scene.name,
                ["path"] = path,
                ["root_count"] = scene.rootCount
            };

            RecordEvent(EventTypes.SceneSaving, targetId, payload);
        }

        /// <summary>
        /// Called after a scene has been saved.
        /// </summary>
        private static void OnSceneSaved(Scene scene)
        {
            string path = scene.path;
            string targetId = string.IsNullOrEmpty(path) ? scene.name : $"Asset:{path}";

            var payload = new Dictionary<string, object>
            {
                ["scene_name"] = scene.name,
                ["path"] = path,
                ["root_count"] = scene.rootCount
            };

            RecordEvent(EventTypes.SceneSaved, targetId, payload);
        }

        /// <summary>
        /// Called when a scene is opened.
        /// </summary>
        private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            string path = scene.path;
            string targetId = string.IsNullOrEmpty(path) ? scene.name : $"Asset:{path}";

            var payload = new Dictionary<string, object>
            {
                ["scene_name"] = scene.name,
                ["path"] = path,
                ["mode"] = mode.ToString(),
                ["root_count"] = scene.rootCount
            };

            RecordEvent(EventTypes.SceneOpened, targetId, payload);
        }

        /// <summary>
        /// Called when a new scene is created.
        /// </summary>
        private static void OnNewSceneCreated(Scene scene, NewSceneSetup setup, NewSceneMode mode)
        {
            string targetId = $"Scene:{scene.name}";

            var payload = new Dictionary<string, object>
            {
                ["scene_name"] = scene.name,
                ["setup"] = setup.ToString(),
                ["mode"] = mode.ToString()
            };

            RecordEvent(EventTypes.NewSceneCreated, targetId, payload);
        }

        #endregion

        #region Script Compilation Events

        /// <summary>
        /// Track script compilation state changes.
        /// Unity doesn't provide direct events, so we monitor EditorApplication.isCompiling.
        /// </summary>
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

                // Check if compilation was successful
                int errorCount = GetCompilationErrorCount();

                var payload = new Dictionary<string, object>
                {
                    ["script_count"] = scriptCount,
                    ["duration_ms"] = (long)duration.TotalMilliseconds
                };

                if (errorCount > 0)
                {
                    payload["error_count"] = errorCount;
                    RecordEvent(EventTypes.ScriptCompilationFailed, "Editor", payload);
                }
                else
                {
                    RecordEvent(EventTypes.ScriptCompiled, "Editor", payload);
                }
            }
        }

        /// <summary>
        /// Count the number of script files in the project.
        /// </summary>
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

        /// <summary>
        /// Get the current compilation error count.
        /// </summary>
        private static int GetCompilationErrorCount()
        {
            try
            {
                // Try to get error count from console
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

        /// <summary>
        /// Custom build handler for tracking build events.
        /// </summary>
        private static void BuildPlayerHandler(BuildPlayerOptions options)
        {
            _buildStartTime = DateTime.UtcNow;
            _currentBuildPlatform = BuildTargetUtility.GetBuildTargetName(options.target);

            // Record build started
            var startPayload = new Dictionary<string, object>
            {
                ["platform"] = _currentBuildPlatform,
                ["location"] = options.locationPathName,
                ["scene_count"] = options.scenes.Length
            };
            RecordEvent(EventTypes.BuildStarted, "Build", startPayload);

            // Execute the build
            var result = BuildPipeline.BuildPlayer(options);

            // Record build result
            var duration = DateTime.UtcNow - _buildStartTime;

            if (result.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                var successPayload = new Dictionary<string, object>
                {
                    ["platform"] = _currentBuildPlatform,
                    ["location"] = options.locationPathName,
                    ["duration_ms"] = (long)duration.TotalMilliseconds,
                    ["size_bytes"] = result.summary.totalSize,
                    ["size_mb"] = result.summary.totalSize / (1024.0 * 1024.0)
                };
                RecordEvent(EventTypes.BuildCompleted, "Build", successPayload);
            }
            else
            {
                var failPayload = new Dictionary<string, object>
                {
                    ["platform"] = _currentBuildPlatform,
                    ["location"] = options.locationPathName,
                    ["duration_ms"] = (long)duration.TotalMilliseconds,
                    ["error"] = result.summary.ToString()
                };
                RecordEvent(EventTypes.BuildFailed, "Build", failPayload);
            }

            _currentBuildPlatform = null;
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Gets the full Hierarchy path for a GameObject.
        /// Example: "Level1/Player/Arm/Hand"
        /// </summary>
        private static string GetGameObjectPath(GameObject obj)
        {
            if (obj == null)
                return "Unknown";

            var path = obj.name;
            var parent = obj.transform.parent;

            while (parent != null)
            {
                path = $"{parent.name}/{path}";
                parent = parent.parent;
            }

            return path;
        }

        /// <summary>
        /// Records an event to the EventStore with proper context injection.
        /// </summary>
        private static void RecordEvent(string type, string targetId, Dictionary<string, object> payload)
        {
            try
            {
                // Inject VCS context into all recorded events
                var vcsContext = VCS.VcsContextProvider.GetCurrentContext();
                payload["vcs_context"] = vcsContext.ToDictionary();

                // Inject Undo Group ID for undo_to_sequence functionality (P2.4)
                int currentUndoGroup = Undo.GetCurrentGroup();
                payload["undo_group"] = currentUndoGroup;

                var evt = new EditorEvent(
                    sequence: 0,  // Will be assigned by EventStore.Record
                    timestampUnixMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    type: type,
                    targetId: targetId,
                    payload: payload
                );

                // Apply sampling middleware to protect from event floods.
                // If sampling filters this event, do not record it here.
                if (SamplingMiddleware.ShouldRecord(evt))
                {
                    Core.EventStore.Record(evt);
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"[UnityEventHooks] Failed to record event: {ex.Message}");
            }
        }
    }

    #endregion
}
