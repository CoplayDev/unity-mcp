using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.ActionTrace.Sources.EventArgs;
using MCPForUnity.Editor.ActionTrace.Sources.Helpers;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Hooks;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MCPForUnity.Editor.ActionTrace.Sources.Unity
{
    public static partial class UnityEventHooks
    {
        #region Script Compilation State

        private static DateTime _compileStartTime;
        private static bool _isCompiling;

        #endregion

        #region Build State

        private static DateTime _buildStartTime;
        private static string _currentBuildPlatform;

        #endregion

        #region Component Removal Tracking State

        // GameObject InstanceID -> Dictionary<Component InstanceID, Component TypeName>
        private static readonly Dictionary<int, Dictionary<int, string>> _gameObjectComponentCache = new();

        #endregion

        #region GameObject Tracking State

        private static GameObjectTrackingHelper _trackingHelper;

        #endregion

        #region Script Compilation Tracking

        private static void TrackScriptCompilation()
        {
            bool isNowCompiling = EditorApplication.isCompiling;

            if (isNowCompiling && !_isCompiling)
            {
                _compileStartTime = DateTime.UtcNow;
                _isCompiling = true;
            }
            else if (!isNowCompiling && _isCompiling)
            {
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
            try { return AssetDatabase.FindAssets("t:Script").Length; }
            catch { return 0; }
        }

        private static int GetCompilationErrorCount()
        {
            try
            {
                var assembly = typeof(EditorUtility).Assembly;
                var type = assembly.GetType("UnityEditor.Scripting.ScriptCompilationErrorCount");
                if (type != null)
                {
                    var property = type.GetProperty("errorCount", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                    if (property != null)
                    {
                        var value = property.GetValue(null);
                        if (value is int count) return count;
                    }
                }
                return 0;
            }
            catch { return 0; }
        }

        #endregion

        #region Build Handling

        private static void BuildPlayerHandler(BuildPlayerOptions options)
        {
            _buildStartTime = DateTime.UtcNow;
            _currentBuildPlatform = GetBuildTargetName(options.target);

            BuildReport result = BuildPipeline.BuildPlayer(options);

            var duration = DateTime.UtcNow - _buildStartTime;
            bool success = result.summary.result == BuildResult.Succeeded;

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
                var type = assembly.GetType("MCPForUnity.Editor.Helpers.BuildTargetUtility");
                if (type != null)
                {
                    var method = type.GetMethod("GetBuildTargetName", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                    if (method != null)
                    {
                        var result = method.Invoke(null, new object[] { target });
                        if (result is string name) return name;
                    }
                }
            }
            catch { }

            return target.ToString();
        }

        #endregion

        #region Component Removal Tracking

        private static void TrackComponentRemoval()
        {
            if (_gameObjectComponentCache.Count == 0) return;

            var trackedIds = _gameObjectComponentCache.Keys.ToList();
            var toRemove = new List<int>();

            foreach (int goId in trackedIds)
            {
                var go = EditorUtility.InstanceIDToObject(goId) as GameObject;

                if (go == null)
                {
                    toRemove.Add(goId);
                    continue;
                }

                var currentComponents = go.GetComponents<Component>();
                var currentIds = new HashSet<int>();

                foreach (var comp in currentComponents)
                {
                    if (comp != null) currentIds.Add(comp.GetInstanceID());
                }

                var cachedMap = _gameObjectComponentCache[goId];
                var removedIds = cachedMap.Keys.Except(currentIds).ToList();

                foreach (int removedId in removedIds)
                {
                    string componentType = cachedMap[removedId];
                    HookRegistry.NotifyComponentRemovedDetailed(new ComponentRemovedArgs
                    {
                        Owner = go,
                        ComponentInstanceId = removedId,
                        ComponentType = componentType
                    });
                    McpLog.Info($"[UnityEventHooks] Component removed: {componentType} (ID: {removedId}) from {go.name}");
                }

                if (removedIds.Count > 0 || currentIds.Count != cachedMap.Count)
                {
                    RegisterGameObject(go);
                }
            }

            foreach (int id in toRemove)
            {
                _gameObjectComponentCache.Remove(id);
            }
        }

        private static void RegisterGameObject(GameObject go)
        {
            if (go == null) return;

            int goId = go.GetInstanceID();
            var componentMap = new Dictionary<int, string>();

            foreach (var comp in go.GetComponents<Component>())
            {
                if (comp != null) componentMap[comp.GetInstanceID()] = comp.GetType().Name;
            }

            _gameObjectComponentCache[goId] = componentMap;
        }

        #endregion

        #region GameObject Tracking

        private static void InitializeTracking()
        {
            if (_trackingHelper == null) _trackingHelper = new GameObjectTrackingHelper();
            _trackingHelper.InitializeTracking();
        }

        private static void ResetTracking()
        {
            _trackingHelper?.Reset();
            _gameObjectComponentCache.Clear();
        }

        private static void TrackGameObjectChanges()
        {
            if (_trackingHelper == null) return;

            var result = _trackingHelper.DetectChanges();

            foreach (var change in result.Changes)
            {
                if (change.isNew) HookRegistry.NotifyGameObjectCreated(change.obj);
            }

            // Notify for each destroyed GameObject with cached name and GlobalID
            foreach (int id in result.DestroyedIds)
            {
                HookRegistry.NotifyGameObjectDestroyed(null);

                // Get the cached name for the detailed event
                string name = result.DestroyedNames.TryGetValue(id, out string cachedName)
                    ? cachedName
                    : "Unknown";

                // Get the cached GlobalID (pre-death "will") for cross-session reference
                string globalId = result.DestroyedGlobalIds.TryGetValue(id, out string cachedGlobalId)
                    ? cachedGlobalId
                    : $"Instance:{id}";

                HookRegistry.NotifyGameObjectDestroyedDetailed(new GameObjectDestroyedArgs
                {
                    InstanceId = id,
                    Name = name,
                    GlobalId = globalId
                });
            }
        }

        #endregion
    }
}
