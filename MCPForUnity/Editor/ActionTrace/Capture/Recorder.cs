using System;
using System.Collections.Generic;
using MCPForUnity.Editor.ActionTrace.Core;
using MCPForUnity.Editor.ActionTrace.Core.Models;
using MCPForUnity.Editor.ActionTrace.Core.Store;
using MCPForUnity.Editor.ActionTrace.Integration.VCS;
using MCPForUnity.Editor.ActionTrace.Sources.Helpers;
using MCPForUnity.Editor.Hooks.EventArgs;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Hooks;
using UnityEditor;
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
    /// The GameObject tracking capability is injected via IGameObjectCacheProvider interface.
    /// </summary>
    [InitializeOnLoad]
    internal static class ActionTraceRecorder
    {
        private static GameObjectTrackingHelper _trackingHelper;

        static ActionTraceRecorder()
        {
            // Initialize GameObject tracking helper
            _trackingHelper = new GameObjectTrackingHelper();

            // Inject cache provider into UnityEventHooks
            var cacheProvider = new GameObjectTrackingCacheProvider(_trackingHelper);
            Hooks.UnityEventHooks.SetGameObjectCacheProvider(cacheProvider);

            // Subscribe to cleanup events
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;

            // Subscribe to HookRegistry events
            HookRegistry.OnComponentAdded += OnComponentAdded;
            HookRegistry.OnComponentRemoved += OnComponentRemoved;
            HookRegistry.OnComponentRemovedDetailed += OnComponentRemovedDetailed;
            HookRegistry.OnGameObjectCreated += OnGameObjectCreated;
            // Note: We only use OnGameObjectDestroyedDetailed since it has complete cached data
            // OnGameObjectDestroyed is called first with null, so we skip it to avoid duplicates
            HookRegistry.OnGameObjectDestroyedDetailed += OnGameObjectDestroyedDetailed;
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

        private static void OnBeforeAssemblyReload()
        {
            // Clear GameObject tracking helper state before reload
            _trackingHelper?.Reset();

            // Clear cache provider from UnityEventHooks to release references
            Hooks.UnityEventHooks.SetGameObjectCacheProvider(null);

            // Unsubscribe from HookRegistry events before domain reload
            HookRegistry.OnComponentAdded -= OnComponentAdded;
            HookRegistry.OnComponentRemoved -= OnComponentRemoved;
            HookRegistry.OnComponentRemovedDetailed -= OnComponentRemovedDetailed;
            HookRegistry.OnGameObjectCreated -= OnGameObjectCreated;
            HookRegistry.OnGameObjectDestroyedDetailed -= OnGameObjectDestroyedDetailed;
            HookRegistry.OnSelectionChanged -= OnSelectionChanged;
            HookRegistry.OnHierarchyChanged -= OnHierarchyChanged;
            HookRegistry.OnPlayModeChanged -= OnPlayModeChanged;
            HookRegistry.OnSceneSaved -= OnSceneSaved;
            HookRegistry.OnSceneOpenedDetailed -= OnSceneOpenedDetailed;
            HookRegistry.OnNewSceneCreatedDetailed -= OnNewSceneCreatedDetailed;
            HookRegistry.OnScriptCompiledDetailed -= OnScriptCompiledDetailed;
            HookRegistry.OnScriptCompilationFailedDetailed -= OnScriptCompilationFailedDetailed;
            HookRegistry.OnBuildCompletedDetailed -= OnBuildCompletedDetailed;

            // Unsubscribe from cleanup event
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
        }

        #region Hook Handlers

        private static void OnComponentAdded(Component component)
        {
            if (component == null) return;

            var goName = component.gameObject != null ? component.gameObject.name : "Unknown";
            var payload = new Dictionary<string, object>
            {
                ["component_type"] = component.GetType().Name,
                ["name"] = goName
            };

            string globalId = GlobalIdHelper.ToGlobalIdString(component);
            RecordEvent("ComponentAdded", globalId, payload);
        }

        private static void OnComponentRemoved(Component component)
        {
            if (component == null) return;

            var goName = component.gameObject != null ? component.gameObject.name : "Unknown";
            var payload = new Dictionary<string, object>
            {
                ["component_type"] = component.GetType().Name,
                ["name"] = goName
            };

            string globalId = GlobalIdHelper.ToGlobalIdString(component);
            RecordEvent("ComponentRemoved", globalId, payload);
        }

        private static void OnComponentRemovedDetailed(ComponentRemovedArgs args)
        {
            if (args == null) return;

            var goName = args.Owner != null ? args.Owner.name : "Unknown";
            var payload = new Dictionary<string, object>
            {
                ["component_type"] = args.ComponentType ?? "Unknown",
                ["name"] = goName,
                ["component_instance_id"] = args.ComponentInstanceId
            };

            string targetId = args.Owner != null
                ? GlobalIdHelper.ToGlobalIdString(args.Owner)
                : args.ComponentInstanceId.ToString();

            RecordEvent("ComponentRemoved", targetId, payload);
        }

        private static void OnGameObjectCreated(GameObject go)
        {
            if (go == null) return;

            var payload = new Dictionary<string, object>
            {
                ["name"] = go.name,
                ["tag"] = go.tag,
                ["layer"] = go.layer,
                ["scene"] = go.scene.name,
                ["is_prefab"] = PrefabUtility.IsPartOfAnyPrefab(go)
            };

            string globalId = GlobalIdHelper.ToGlobalIdString(go);
            RecordEvent("GameObjectCreated", globalId, payload);
        }

        private static void OnGameObjectDestroyedDetailed(GameObjectDestroyedArgs args)
        {
            if (args == null) return;

            var payload = new Dictionary<string, object>
            {
                ["name"] = args.Name ?? "Unknown",
                ["instance_id"] = args.InstanceId,
                ["destroyed"] = true
            };

            string targetId = args.GlobalId ?? $"Instance:{args.InstanceId}";
            RecordEvent("GameObjectDestroyed", targetId, payload);
        }

        private static void OnSelectionChanged(GameObject selectedGo)
        {
            // Use the parameter instead of Selection.activeObject (more efficient and reliable)
            if (selectedGo == null) return;

            var payload = new Dictionary<string, object>
            {
                ["name"] = selectedGo.name,
                ["type"] = selectedGo.GetType().Name,
                ["instance_id"] = selectedGo.GetInstanceID(),
                ["path"] = GetGameObjectPath(selectedGo)
            };

            string globalId = GlobalIdHelper.ToGlobalIdString(selectedGo);
            RecordEvent("SelectionChanged", globalId, payload);
        }

        private static void OnHierarchyChanged()
        {
            RecordEvent("HierarchyChanged", "Scene", new Dictionary<string, object>());
        }

        private static void OnPlayModeChanged(bool isPlaying)
        {
            var state = isPlaying ? PlayModeStateChange.EnteredPlayMode : PlayModeStateChange.ExitingPlayMode;
            var payload = new Dictionary<string, object>
            {
                ["state"] = state.ToString()
            };

            RecordEvent("PlayModeChanged", "Editor", payload);
        }

        private static void OnSceneSaved(Scene scene)
        {
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

        private static void OnSceneOpenedDetailed(Scene scene, SceneOpenArgs args)
        {
            var mode = args.Mode.GetValueOrDefault(global::UnityEditor.SceneManagement.OpenSceneMode.Single);
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

        private static void OnNewSceneCreatedDetailed(Scene scene, NewSceneArgs args)
        {
            var setup = args.Setup.GetValueOrDefault(global::UnityEditor.SceneManagement.NewSceneSetup.DefaultGameObjects);
            var mode = args.Mode.GetValueOrDefault(global::UnityEditor.SceneManagement.NewSceneMode.Single);
            var payload = new Dictionary<string, object>
            {
                ["scene_name"] = scene.name,
                ["setup"] = setup.ToString(),
                ["mode"] = mode.ToString()
            };

            RecordEvent("NewSceneCreated", $"Scene:{scene.name}", payload);
        }

        private static void OnScriptCompiledDetailed(ScriptCompilationArgs args)
        {
            var payload = new Dictionary<string, object>
            {
                ["script_count"] = args.ScriptCount ?? 0,
                ["duration_ms"] = args.DurationMs ?? 0
            };

            RecordEvent("ScriptCompiled", "Editor", payload);
        }

        private static void OnScriptCompilationFailedDetailed(ScriptCompilationFailedArgs args)
        {
            var payload = new Dictionary<string, object>
            {
                ["script_count"] = args.ScriptCount ?? 0,
                ["duration_ms"] = args.DurationMs ?? 0,
                ["error_count"] = args.ErrorCount
            };

            RecordEvent("ScriptCompilationFailed", "Editor", payload);
        }

        private static void OnBuildCompletedDetailed(BuildArgs args)
        {
            if (args.Success)
            {
                var payload = new Dictionary<string, object>
                {
                    ["platform"] = args.Platform,
                    ["location"] = args.Location,
                    ["duration_ms"] = args.DurationMs ?? 0,
                    ["size_bytes"] = args.SizeBytes ?? 0,
                    ["size_mb"] = (args.SizeBytes ?? 0) / (1024.0 * 1024.0)
                };

                RecordEvent("BuildCompleted", "Build", payload);
            }
            else
            {
                var payload = new Dictionary<string, object>
                {
                    ["platform"] = args.Platform,
                    ["location"] = args.Location,
                    ["duration_ms"] = args.DurationMs ?? 0,
                    ["error"] = args.Summary ?? "Build failed"
                };

                RecordEvent("BuildFailed", "Build", payload);
            }
        }

        #endregion

        #region Event Recording

        private static void RecordEvent(string type, string targetId, Dictionary<string, object> payload)
        {
            try
            {
                // Inject VCS context if available
                var vcsContext = VcsContextProvider.GetCurrentContext();
                if (vcsContext != null)
                {
                    payload["vcs_context"] = vcsContext.ToDictionary();
                }

                // Create event
                var evt = new EditorEvent(
                    0, // sequence (assigned by EventStore)
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    type,
                    targetId,
                    payload
                );

                // Apply sampling middleware
                if (!SamplingMiddleware.ShouldRecord(evt))
                {
                    return;
                }

                // Record to EventStore
                EventStore.Record(evt);
            }
            catch (Exception ex)
            {
                McpLog.Warn($"[ActionTraceRecorder] Recording failed: {ex.Message}");
            }
        }

        private static string GetGameObjectPath(GameObject obj)
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

        #endregion
    }
}
