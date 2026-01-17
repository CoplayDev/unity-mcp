using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace MCPForUnity.Editor.ActionTrace.Helpers
{
    /// <summary>
    /// Helper for tracking GameObject creation and destruction.
    /// Monitors instance IDs to detect changes in the scene hierarchy.
    /// </summary>
    internal static class GameObjectTrackingHelper
    {
        private static readonly List<int> _previousInstanceIds = new();
        private static bool _hasInitialized;

        /// <summary>
        /// Initializes tracking by capturing all existing GameObject instance IDs.
        /// Should be called once after domain reload or scene load.
        /// </summary>
        public static void InitializeTracking()
        {
            if (_hasInitialized) return;

            _previousInstanceIds.Clear();
            foreach (var go in GameObject.FindObjectsOfType<GameObject>(true))
            {
                if (go != null)
                    _previousInstanceIds.Add(go.GetInstanceID());
            }
            _hasInitialized = true;
        }

        /// <summary>
        /// Detects newly created GameObjects by comparing with previous instance IDs.
        /// Returns a list of (GameObject, wasNewlyCreated) tuples.
        /// </summary>
        public static List<(GameObject obj, bool isNew)> DetectChanges()
        {
            if (!_hasInitialized)
            {
                InitializeTracking();
                return new List<(GameObject, bool)>();
            }

            var results = new List<(GameObject, bool)>();
            var currentIds = new HashSet<int>();

            try
            {
                var currentObjects = GameObject.FindObjectsOfType<GameObject>(true);

                foreach (var go in currentObjects)
                {
                    if (go != null)
                    {
                        int id = go.GetInstanceID();
                        currentIds.Add(id);

                        // Check if this is a newly created GameObject
                        bool isNew = !_previousInstanceIds.Contains(id);
                        results.Add((go, isNew));
                    }
                }

                // Update tracking list
                _previousInstanceIds.Clear();
                _previousInstanceIds.AddRange(currentIds);
            }
            catch (Exception)
            {
                // Silently ignore errors
            }

            return results;
        }

        /// <summary>
        /// Gets instance IDs of GameObjects that were destroyed since last check.
        /// </summary>
        public static List<int> GetDestroyedInstanceIds()
        {
            if (!_hasInitialized)
                return new List<int>();

            var destroyed = new List<int>();
            var currentIds = new HashSet<int>();

            try
            {
                foreach (var go in GameObject.FindObjectsOfType<GameObject>(true))
                {
                    if (go != null)
                        currentIds.Add(go.GetInstanceID());
                }

                foreach (int id in _previousInstanceIds)
                {
                    if (!currentIds.Contains(id))
                        destroyed.Add(id);
                }
            }
            catch
            {
                // Ignore errors
            }

            return destroyed;
        }

        /// <summary>
        /// Resets tracking state.
        /// Call this when loading a new scene or entering play mode.
        /// </summary>
        public static void Reset()
        {
            _previousInstanceIds.Clear();
            _hasInitialized = false;
        }
    }
}
