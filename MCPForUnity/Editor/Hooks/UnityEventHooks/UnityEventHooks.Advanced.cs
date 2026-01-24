using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Hooks.EventArgs;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MCPForUnity.Editor.Hooks
{
    /// <summary>
    /// Advanced tracking features for UnityEventHooks.
    /// Implements script compilation tracking, GameObject change detection,
    /// and component removal tracking using the IGameObjectCacheProvider interface.
    ///
    /// This file uses dependency injection via IGameObjectCacheProvider to decouple
    /// from ActionTrace-specific implementations, allowing UnityEventHooks to remain
    /// general infrastructure in the Hooks/ folder.
    /// </summary>
    public static partial class UnityEventHooks
    {
        #region Cache Provider

        private static IGameObjectCacheProvider _cacheProvider;

        /// <summary>
        /// Set the GameObject cache provider.
        /// Called by ActionTrace during initialization to inject tracking capability.
        /// </summary>
        public static void SetGameObjectCacheProvider(IGameObjectCacheProvider provider)
        {
            _cacheProvider = provider;
        }

        #endregion

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

        #region Partial Method Implementations

        static partial void InitializeTracking()
        {
            _cacheProvider?.InitializeTracking();
        }

        static partial void ResetTracking()
        {
            _cacheProvider?.Reset();
            _gameObjectComponentCache.Clear();
        }

        static partial void RegisterGameObjectForTracking(GameObject gameObject)
        {
            if (gameObject == null) return;

            // Register with cache provider for GameObject tracking
            _cacheProvider?.RegisterGameObject(gameObject);

            // Register locally for component removal tracking
            int goId = gameObject.GetInstanceID();
            var componentMap = new Dictionary<int, string>();

            foreach (var comp in gameObject.GetComponents<Component>())
            {
                if (comp != null) componentMap[comp.GetInstanceID()] = comp.GetType().Name;
            }

            _gameObjectComponentCache[goId] = componentMap;
        }

        static partial void TrackScriptCompilation()
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

        static partial void TrackGameObjectChanges()
        {
            _cacheProvider?.DetectChanges(
                onCreated: (go) =>
                {
                    HookRegistry.NotifyGameObjectCreated(go);
                },
                onDestroyed: (instanceId) =>
                {
                    // Get cached data for detailed event
                    string name = _cacheProvider?.GetCachedName(instanceId) ?? "Unknown";
                    string globalId = _cacheProvider?.GetCachedGlobalId(instanceId) ?? $"Instance:{instanceId}";

                    HookRegistry.NotifyGameObjectDestroyedDetailed(new GameObjectDestroyedArgs
                    {
                        InstanceId = instanceId,
                        Name = name,
                        GlobalId = globalId
                    });
                }
            );
        }

        static partial void TrackComponentRemoval()
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
                }

                if (removedIds.Count > 0 || currentIds.Count != cachedMap.Count)
                {
                    RegisterGameObjectForTracking(go);
                }
            }

            foreach (int id in toRemove)
            {
                _gameObjectComponentCache.Remove(id);
            }
        }

        static partial void BuildPlayerHandler(BuildPlayerOptions options)
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

        #endregion

        #region Helper Methods

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
    }
}
