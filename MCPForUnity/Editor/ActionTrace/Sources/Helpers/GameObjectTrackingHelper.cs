using System;
using System.Collections.Generic;
using MCPForUnity.Editor.ActionTrace.Core;
using MCPForUnity.Editor.ActionTrace.Core.Models;
using MCPForUnity.Editor.Helpers;
using UnityEngine;

namespace MCPForUnity.Editor.ActionTrace.Sources.Helpers
{
    /// <summary>
    /// Result of GameObject change detection.
    /// </summary>
    internal readonly struct GameObjectChangeResult
    {
        public List<(GameObject obj, bool isNew)> Changes { get; }
        public List<int> DestroyedIds { get; }
        /// <summary>
        /// InstanceID -> Name mapping for destroyed GameObjects.
        /// Used to preserve names after objects are destroyed.
        /// </summary>
        public Dictionary<int, string> DestroyedNames { get; }
        /// <summary>
        /// InstanceID -> GlobalID mapping for destroyed GameObjects.
        /// Used to preserve cross-session stable IDs after objects are destroyed.
        /// </summary>
        public Dictionary<int, string> DestroyedGlobalIds { get; }

        public GameObjectChangeResult(List<(GameObject, bool)> changes, List<int> destroyedIds, Dictionary<int, string> destroyedNames, Dictionary<int, string> destroyedGlobalIds)
        {
            Changes = changes;
            DestroyedIds = destroyedIds;
            DestroyedNames = destroyedNames ?? new Dictionary<int, string>();
            DestroyedGlobalIds = destroyedGlobalIds ?? new Dictionary<int, string>();
        }
    }

    /// <summary>
    /// Helper for tracking GameObject creation and destruction.
    /// Uses HashSet for O(1) lookup instead of List.Contains O(n).
    /// Also caches GameObject names and GlobalIDs for destroyed objects to preserve context.
    /// </summary>
    internal sealed class GameObjectTrackingHelper
    {
        private HashSet<int> _previousInstanceIds = new(256);
        /// <summary>
        /// InstanceID -> Name cache for GameObject name preservation after destruction.
        /// </summary>
        private Dictionary<int, string> _nameCache = new(256);
        /// <summary>
        /// InstanceID -> GlobalID cache for cross-session stable ID preservation.
        /// Cached during the object's lifetime to enable retrieval after destruction.
        /// </summary>
        private Dictionary<int, string> _globalIdCache = new(256);
        private bool _hasInitialized;

        public void InitializeTracking()
        {
            if (_hasInitialized) return;

            _previousInstanceIds.Clear();
            _previousInstanceIds.EnsureCapacity(256);
            _nameCache.Clear();
            _nameCache = new Dictionary<int, string>(256);
            _globalIdCache.Clear();
            _globalIdCache = new Dictionary<int, string>(256);

            try
            {
                GameObject[] allObjects = GameObject.FindObjectsOfType<GameObject>(true);
                foreach (var go in allObjects)
                {
                    if (go != null)
                    {
                        int id = go.GetInstanceID();
                        _previousInstanceIds.Add(id);
                        _nameCache[id] = go.name;
                        // Cache GlobalID for cross-session stable reference
                        _globalIdCache[id] = GlobalIdHelper.ToGlobalIdString(go);
                    }
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"[GameObjectTrackingHelper] Failed to initialize GameObject tracking: {ex.Message}");
            }

            _hasInitialized = true;
        }

        public void Reset()
        {
            _previousInstanceIds.Clear();
            _nameCache.Clear();
            _globalIdCache.Clear();
            _hasInitialized = false;
        }

        /// <summary>
        /// Get cached name for a GameObject by InstanceID.
        /// Used by IGameObjectCacheProvider implementation.
        /// </summary>
        public string GetCachedName(int instanceId)
        {
            return _nameCache.TryGetValue(instanceId, out string name) ? name : null;
        }

        /// <summary>
        /// Get cached GlobalID for a GameObject by InstanceID.
        /// Used by IGameObjectCacheProvider implementation.
        /// </summary>
        public string GetCachedGlobalId(int instanceId)
        {
            return _globalIdCache.TryGetValue(instanceId, out string globalId) ? globalId : null;
        }

        public GameObjectChangeResult DetectChanges()
        {
            if (!_hasInitialized)
            {
                InitializeTracking();
                return new GameObjectChangeResult(new List<(GameObject, bool)>(0), new List<int>(0),
                 new Dictionary<int, string>(0), new Dictionary<int, string>(0));
            }

            var changes = new List<(GameObject, bool)>(64);
            var destroyedIds = new List<int>(8);
            var destroyedNames = new Dictionary<int, string>(8);
            var destroyedGlobalIds = new Dictionary<int, string>(8);
            var currentIds = new HashSet<int>(256);

            try
            {
                GameObject[] currentObjects = GameObject.FindObjectsOfType<GameObject>(true);

                // First pass: detect new objects and build current IDs set
                foreach (var go in currentObjects)
                {
                    if (go == null) continue;

                    int id = go.GetInstanceID();
                    currentIds.Add(id);

                    // Update name cache
                    _nameCache[id] = go.name;
                    // Update GlobalID cache (pre-death "will")
                    _globalIdCache[id] = GlobalIdHelper.ToGlobalIdString(go);

                    bool isNew = !_previousInstanceIds.Contains(id);
                    changes.Add((go, isNew));
                }

                // Second pass: find destroyed objects (in previous but not in current)
                foreach (int id in _previousInstanceIds)
                {
                    if (!currentIds.Contains(id))
                    {
                        destroyedIds.Add(id);
                        // Preserve name from cache before removal
                        if (_nameCache.TryGetValue(id, out string name))
                        {
                            destroyedNames[id] = name;
                        }
                        else
                        {
                            destroyedNames[id] = "Unknown";
                        }
                        // Preserve GlobalID from cache (pre-death "will")
                        if (_globalIdCache.TryGetValue(id, out string globalId))
                        {
                            destroyedGlobalIds[id] = globalId;
                        }
                        else
                        {
                            destroyedGlobalIds[id] = $"Instance:{id}";
                        }
                    }
                }

                // Clean up cache: remove destroyed entries
                foreach (int id in destroyedIds)
                {
                    _nameCache.Remove(id);
                    _globalIdCache.Remove(id);
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
                McpLog.Warn($"[GameObjectTrackingHelper] Failed to detect GameObject changes: {ex.Message}");
            }

            return new GameObjectChangeResult(changes, destroyedIds, destroyedNames, destroyedGlobalIds);
        }
    }
}
