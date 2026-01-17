using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.Assertions;
using MCPForUnity.Editor.ActionTrace.Core;

namespace MCPForUnity.Editor.ActionTrace.Helpers
{
    /// <summary>
    /// Helper for tracking GameObject creation and destruction.
    /// Monitors instance IDs to detect changes in the scene hierarchy.
    ///
    /// Performance optimizations:
    /// - Uses HashSet for O(1) lookup instead of List.Contains O(n)
    /// - Pre-allocates capacity to reduce resizing
    /// - Manual loops instead of LINQ to avoid GC allocations
    ///
    /// Thread safety: All methods must be called from the main thread.
    /// This is enforced via debug assertions in development builds.
    /// </summary>
    internal static class GameObjectTrackingHelper
    {
        // Use HashSet for O(1) Contains() instead of List's O(n)
        private static readonly HashSet<int> _previousInstanceIds = new(256);
        private static bool _hasInitialized;

        // Cache for the main thread ID to validate thread safety
        private static readonly int MainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;

        /// <summary>
        /// Initializes tracking by capturing all existing GameObject instance IDs.
        /// Should be called once after domain reload or scene load.
        /// Must be called from the main thread.
        /// </summary>
        public static void InitializeTracking()
        {
            AssertMainThread();

            if (_hasInitialized) return;

            _previousInstanceIds.Clear();
            // Reserve capacity for typical scene sizes
            _previousInstanceIds.EnsureCapacity(256);

            try
            {
                // Manual loop instead of LINQ for performance
                GameObject[] allObjects = GameObject.FindObjectsOfType<GameObject>(true);
                foreach (var go in allObjects)
                {
                    if (go != null)
                        _previousInstanceIds.Add(go.GetInstanceID());
                }
            }
            catch (Exception ex)
            {
                // Log error instead of silent swallow for debugging
                Debug.LogError($"[ActionTrace] Failed to initialize GameObject tracking: {ex.Message}");
            }

            _hasInitialized = true;
        }

        /// <summary>
        /// Detects newly created GameObjects by comparing with previous instance IDs.
        /// Returns a list of (GameObject, wasNewlyCreated) tuples.
        /// Must be called from the main thread.
        /// </summary>
        public static List<(GameObject obj, bool isNew)> DetectChanges()
        {
            AssertMainThread();

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

                // First pass: collect current IDs and detect new objects
                foreach (var go in currentObjects)
                {
                    if (go == null) continue;

                    int id = go.GetInstanceID();
                    currentIds.Add(id);

                    // HashSet.Contains() is O(1) vs List.Contains() O(n)
                    bool isNew = !_previousInstanceIds.Contains(id);
                    results.Add((go, isNew));
                }

                // Update tracking: swap hash sets to avoid allocation
                _previousInstanceIds.Clear();
                foreach (int id in currentIds)
                {
                    _previousInstanceIds.Add(id);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ActionTrace] Failed to detect GameObject changes: {ex.Message}");
            }

            return results;
        }

        /// <summary>
        /// Gets instance IDs of GameObjects that were destroyed since last check.
        /// Must be called from the main thread.
        /// </summary>
        public static List<int> GetDestroyedInstanceIds()
        {
            AssertMainThread();

            if (!_hasInitialized)
                return new List<int>(0);

            var destroyed = new List<int>(8);
            var currentIds = new HashSet<int>(256);

            try
            {
                // Collect current IDs
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
                Debug.LogError($"[ActionTrace] Failed to get destroyed instance IDs: {ex.Message}");
            }

            return destroyed;
        }

        /// <summary>
        /// Resets tracking state.
        /// Call this when loading a new scene or entering play mode.
        /// Must be called from the main thread.
        /// </summary>
        public static void Reset()
        {
            AssertMainThread();
            _previousInstanceIds.Clear();
            _hasInitialized = false;
        }

        /// <summary>
        /// Gets the current count of tracked instance IDs.
        /// Useful for debugging and monitoring.
        /// </summary>
        public static int TrackedCount => _previousInstanceIds.Count;

        /// <summary>
        /// Debug assertion to ensure methods are called from main thread.
        /// Only active in development builds.
        /// </summary>
        [System.Diagnostics.Conditional("UNITY_ASSERTIONS")]
        private static void AssertMainThread()
        {
            if (System.Threading.Thread.CurrentThread.ManagedThreadId != MainThreadId)
            {
                throw new InvalidOperationException(
                    $"[ActionTrace] GameObjectTrackingHelper must be called from the main thread. " +
                    $"Current thread: {System.Threading.Thread.CurrentThread.ManagedThreadId}, " +
                    $"Main thread: {MainThreadId}");
            }
        }
    }
}
