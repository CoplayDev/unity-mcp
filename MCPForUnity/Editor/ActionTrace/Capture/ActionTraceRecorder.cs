using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Hooks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MCPForUnity.Editor.ActionTrace.Capture
{
    /// <summary>
    /// Records Unity editor events to ActionTrace's EventStore.
    /// Subscribes to HookRegistry events for clean separation of concerns.
    ///
    /// Architecture:
    /// Unity Events → UnityEventHooks (detection) → HookRegistry → ActionTraceRecorder (recording)
    ///
    /// This allows UnityEventHooks to remain a pure detector without ActionTrace dependencies.
    /// </summary>
    [InitializeOnLoad]
    internal static class ActionTraceRecorder
    {
        private static bool _actionTraceAvailable;
        private static readonly ActionTraceCompatibility _actionTrace = new();

        static ActionTraceRecorder()
        {
            // Check if ActionTrace is available
            _actionTraceAvailable = _actionTrace.Initialize();

            if (!_actionTraceAvailable)
                return;

            // Subscribe to HookRegistry events
            HookRegistry.OnComponentAdded += OnComponentAdded;
            HookRegistry.OnGameObjectCreated += OnGameObjectCreated;
            HookRegistry.OnGameObjectDestroyed += OnGameObjectDestroyed;
            HookRegistry.OnSelectionChanged += OnSelectionChanged;
            HookRegistry.OnHierarchyChanged += OnHierarchyChanged;
            HookRegistry.OnPlayModeChanged += OnPlayModeChanged;
            HookRegistry.OnSceneSaved += OnSceneSaved;
            HookRegistry.OnSceneOpenedDetailed += OnSceneOpenedDetailed;
            HookRegistry.OnNewSceneCreatedDetailed += OnNewSceneCreatedDetailed;
            HookRegistry.OnScriptCompiledDetailed += OnScriptCompiledDetailed;
            HookRegistry.OnScriptCompilationFailedDetailed += OnScriptCompilationFailedDetailed;
            HookRegistry.OnBuildCompletedDetailed += OnBuildCompletedDetailed;
        }

        #region Hook Handlers

        private static void OnComponentAdded(Component component)
        {
            if (component == null) return;
            _actionTrace.RecordComponentAdded(component);
        }

        private static void OnGameObjectCreated(GameObject go)
        {
            if (go == null) return;
            _actionTrace.RecordGameObjectCreated(go);
        }

        private static void OnGameObjectDestroyed(GameObject go)
        {
            // GameObject might already be destroyed
            int instanceId = go != null ? go.GetInstanceID() : 0;
            _actionTrace.RecordGameObjectDestroyed(instanceId);
        }

        private static void OnSelectionChanged(GameObject selectedGo)
        {
            // Record the actual Selection.activeObject (could be Component, Asset, etc.)
            if (Selection.activeObject != null)
            {
                _actionTrace.RecordSelectionChanged(Selection.activeObject);
            }
        }

        private static void OnHierarchyChanged()
        {
            _actionTrace.RecordHierarchyChanged();
        }

        private static void OnPlayModeChanged(bool isPlaying)
        {
            var state = isPlaying ? PlayModeStateChange.EnteredPlayMode : PlayModeStateChange.ExitingPlayMode;
            _actionTrace.RecordPlayModeStateChanged(state);
        }

        private static void OnSceneSaved(Scene scene)
        {
            _actionTrace.RecordSceneSaved(scene);
        }

        private static void OnSceneOpenedDetailed(Scene scene, SceneOpenArgs args)
        {
            _actionTrace.RecordSceneOpened(scene, args.Mode.GetValueOrDefault(global::UnityEditor.SceneManagement.OpenSceneMode.Single));
        }

        private static void OnNewSceneCreatedDetailed(Scene scene, NewSceneArgs args)
        {
            _actionTrace.RecordNewSceneCreated(scene,
                args.Setup.GetValueOrDefault(global::UnityEditor.SceneManagement.NewSceneSetup.DefaultGameObjects),
                args.Mode.GetValueOrDefault(global::UnityEditor.SceneManagement.NewSceneMode.Single));
        }

        private static void OnScriptCompiledDetailed(ScriptCompilationArgs args)
        {
            _actionTrace.RecordScriptCompiled(args.ScriptCount ?? 0, args.DurationMs ?? 0);
        }

        private static void OnScriptCompilationFailedDetailed(ScriptCompilationFailedArgs args)
        {
            _actionTrace.RecordScriptCompilationFailed(args.ScriptCount ?? 0, args.DurationMs ?? 0, args.ErrorCount);
        }

        private static void OnBuildCompletedDetailed(BuildArgs args)
        {
            if (args.Success)
            {
                _actionTrace.RecordBuildCompleted(args.Platform, args.Location, args.DurationMs ?? 0, args.SizeBytes ?? 0);
            }
            else
            {
                _actionTrace.RecordBuildFailed(args.Platform, args.Location, args.DurationMs ?? 0, args.Summary ?? "Build failed");
            }
        }

        #endregion

        #region ActionTrace Compatibility Layer

        /// <summary>
        /// Compatibility layer for ActionTrace integration.
        /// Uses reflection to avoid hard dependencies when ActionTrace is not available.
        /// </summary>
        private class ActionTraceCompatibility
        {
            private Type _eventStoreType;
            private Type _editorEventType;
            private Type _vcsContextProviderType;
            private Type _samplingMiddlewareType;

            public bool Initialize()
            {
                try
                {
                    var assembly = typeof(HookRegistry).Assembly;

                    _eventStoreType = assembly.GetType("MCPForUnity.Editor.ActionTrace.Core.EventStore");
                    _editorEventType = assembly.GetType("MCPForUnity.Editor.ActionTrace.Core.EditorEvent");
                    _vcsContextProviderType = assembly.GetType("MCPForUnity.Editor.ActionTrace.VCS.VcsContextProvider");
                    _samplingMiddlewareType = assembly.GetType("MCPForUnity.Editor.ActionTrace.Middleware.SamplingMiddleware");

                    bool available = _eventStoreType != null && _editorEventType != null;
                    if (available)
                    {
                        McpLog.Info("[ActionTraceRecorder] ActionTrace compatibility layer enabled.");
                    }
                    return available;
                }
                catch (Exception ex)
                {
                    McpLog.Warn($"[ActionTraceRecorder] Initialization failed: {ex.Message}");
                    return false;
                }
            }

            public void RecordComponentAdded(Component component)
            {
                if (_editorEventType == null) return;

                var payload = new Dictionary<string, object>
                {
                    ["component_type"] = component.GetType().Name,
                    ["game_object"] = component.gameObject?.name ?? "Unknown"
                };

                var globalId = $"Component:{component.GetInstanceID()}";
                RecordEvent("ComponentAdded", globalId, payload);
            }

            public void RecordGameObjectCreated(GameObject go)
            {
                if (_editorEventType == null) return;

                var payload = new Dictionary<string, object>
                {
                    ["name"] = go.name,
                    ["tag"] = go.tag,
                    ["layer"] = go.layer,
                    ["scene"] = go.scene.name,
                    ["is_prefab"] = PrefabUtility.IsPartOfAnyPrefab(go)
                };

                var globalId = $"Instance:{go.GetInstanceID()}";
                RecordEvent("GameObjectCreated", globalId, payload);
            }

            public void RecordGameObjectDestroyed(int instanceId)
            {
                if (_editorEventType == null) return;

                var payload = new Dictionary<string, object>
                {
                    ["instance_id"] = instanceId,
                    ["destroyed"] = true
                };

                var globalId = $"Instance:{instanceId}";
                RecordEvent("GameObjectDestroyed", globalId, payload);
            }

            public void RecordSelectionChanged(UnityEngine.Object selected)
            {
                if (_editorEventType == null) return;

                var payload = new Dictionary<string, object>
                {
                    ["name"] = selected.name,
                    ["type"] = selected.GetType().Name,
                    ["instance_id"] = selected.GetInstanceID()
                };

                if (selected is GameObject go)
                {
                    payload["path"] = GetGameObjectPath(go);
                }
                else if (selected is Component comp)
                {
                    payload["path"] = GetGameObjectPath(comp.gameObject);
                    payload["component_type"] = comp.GetType().Name;
                }

                var globalId = $"Instance:{selected.GetInstanceID()}";
                RecordEvent("SelectionChanged", globalId, payload);
            }

            public void RecordHierarchyChanged()
            {
                if (_editorEventType == null) return;
                RecordEvent("HierarchyChanged", "Scene", new Dictionary<string, object>());
            }

            public void RecordPlayModeStateChanged(PlayModeStateChange state)
            {
                if (_editorEventType == null) return;

                var payload = new Dictionary<string, object>
                {
                    ["state"] = state.ToString()
                };

                RecordEvent("PlayModeChanged", "Editor", payload);
            }

            public void RecordSceneSaved(Scene scene)
            {
                if (_editorEventType == null) return;

                var path = scene.path;
                var targetId = string.IsNullOrEmpty(path) ? scene.name : $"Asset:{path}";
                var payload = new Dictionary<string, object>
                {
                    ["scene_name"] = scene.name,
                    ["path"] = path,
                    ["root_count"] = scene.rootCount
                };

                RecordEvent("SceneSaved", targetId, payload);
            }

            public void RecordSceneOpened(Scene scene, global::UnityEditor.SceneManagement.OpenSceneMode mode)
            {
                if (_editorEventType == null) return;

                var path = scene.path;
                var targetId = string.IsNullOrEmpty(path) ? scene.name : $"Asset:{path}";
                var payload = new Dictionary<string, object>
                {
                    ["scene_name"] = scene.name,
                    ["path"] = path,
                    ["mode"] = mode.ToString(),
                    ["root_count"] = scene.rootCount
                };

                RecordEvent("SceneOpened", targetId, payload);
            }

            public void RecordNewSceneCreated(Scene scene, global::UnityEditor.SceneManagement.NewSceneSetup setup, global::UnityEditor.SceneManagement.NewSceneMode mode)
            {
                if (_editorEventType == null) return;

                var payload = new Dictionary<string, object>
                {
                    ["scene_name"] = scene.name,
                    ["setup"] = setup.ToString(),
                    ["mode"] = mode.ToString()
                };

                RecordEvent("NewSceneCreated", $"Scene:{scene.name}", payload);
            }

            public void RecordScriptCompiled(int scriptCount, long durationMs)
            {
                if (_editorEventType == null) return;

                var payload = new Dictionary<string, object>
                {
                    ["script_count"] = scriptCount,
                    ["duration_ms"] = durationMs
                };

                RecordEvent("ScriptCompiled", "Editor", payload);
            }

            public void RecordScriptCompilationFailed(int scriptCount, long durationMs, int errorCount)
            {
                if (_editorEventType == null) return;

                var payload = new Dictionary<string, object>
                {
                    ["script_count"] = scriptCount,
                    ["duration_ms"] = durationMs,
                    ["error_count"] = errorCount
                };

                RecordEvent("ScriptCompilationFailed", "Editor", payload);
            }

            public void RecordBuildCompleted(string platform, string location, long durationMs, ulong sizeBytes)
            {
                if (_editorEventType == null) return;

                var payload = new Dictionary<string, object>
                {
                    ["platform"] = platform,
                    ["location"] = location,
                    ["duration_ms"] = durationMs,
                    ["size_bytes"] = sizeBytes,
                    ["size_mb"] = sizeBytes / (1024.0 * 1024.0)
                };

                RecordEvent("BuildCompleted", "Build", payload);
            }

            public void RecordBuildFailed(string platform, string location, long durationMs, string error)
            {
                if (_editorEventType == null) return;

                var payload = new Dictionary<string, object>
                {
                    ["platform"] = platform,
                    ["location"] = location,
                    ["duration_ms"] = durationMs,
                    ["error"] = error
                };

                RecordEvent("BuildFailed", "Build", payload);
            }

            private void RecordEvent(string type, string targetId, Dictionary<string, object> payload)
            {
                try
                {
                    // Inject VCS context if available
                    if (_vcsContextProviderType != null)
                    {
                        var getCurrentMethod = _vcsContextProviderType.GetMethod("GetCurrentContext", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                        if (getCurrentMethod != null)
                        {
                            var context = getCurrentMethod.Invoke(null, null);
                            if (context != null)
                            {
                                var toDictMethod = context.GetType().GetMethod("ToDictionary");
                                if (toDictMethod != null)
                                {
                                    payload["vcs_context"] = toDictMethod.Invoke(context, null);
                                }
                            }
                        }
                    }

                    // Inject Undo Group ID
                    payload["undo_group"] = Undo.GetCurrentGroup();

                    // Create event
                    var evt = Activator.CreateInstance(_editorEventType,
                        0, // sequence
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        type,
                        targetId,
                        payload);

                    // Apply sampling middleware
                    if (_samplingMiddlewareType != null)
                    {
                        var shouldRecordMethod = _samplingMiddlewareType.GetMethod("ShouldRecord", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                        if (shouldRecordMethod != null)
                        {
                            var shouldRecord = (bool)shouldRecordMethod.Invoke(null, new object[] { evt });
                            if (!shouldRecord)
                                return;
                        }
                    }

                    // Record to EventStore
                    var recordMethod = _eventStoreType.GetMethod("Record", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                    recordMethod?.Invoke(null, new object[] { evt });
                }
                catch (Exception ex)
                {
                    McpLog.Warn($"[ActionTraceRecorder] Recording failed: {ex.Message}");
                }
            }

            private string GetGameObjectPath(GameObject obj)
            {
                if (obj == null) return "Unknown";

                var path = obj.name;
                var parent = obj.transform.parent;

                while (parent != null)
                {
                    path = $"{parent.name}/{path}";
                    parent = parent.parent;
                }

                return path;
            }
        }

        #endregion
    }
}
