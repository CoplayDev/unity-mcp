using System.Collections.Concurrent;
using System.Collections.Generic;
using MCPForUnity.Editor.ActionTrace.Core;

namespace MCPForUnity.Editor.ActionTrace.Capture
{
    /// <summary>
    /// Static configuration for sampling strategies.
    /// Event types can be registered with their desired sampling behavior.
    ///
    /// Sampling intervals are configured via hardcoded constants.
    /// To customize sampling behavior, modify InitializeStrategies() directly.
    /// </summary>
    public static class SamplingConfig
    {
        // Hardcoded sampling intervals (milliseconds)
        private const int HierarchySamplingMs = 1000;
        private const int SelectionSamplingMs = 500;
        private const int PropertySamplingMs = 200;

        /// <summary>
        /// Default sampling strategies for common event types.
        /// Configured to prevent event floods while preserving important data.
        /// Thread-safe: uses ConcurrentDictionary to prevent race conditions
        /// when accessed from EditorApplication.update and event emitters simultaneously.
        /// </summary>
        public static readonly ConcurrentDictionary<string, SamplingStrategy> Strategies = new(
            InitializeStrategies()
        );

        /// <summary>
        /// Initializes sampling strategies with hardcoded values.
        /// </summary>
        private static Dictionary<string, SamplingStrategy> InitializeStrategies()
        {
            return new Dictionary<string, SamplingStrategy>
            {
                // Hierarchy changes: Throttle to 1 event per second
                {
                    EventTypes.HierarchyChanged,
                    new SamplingStrategy(SamplingMode.Throttle, HierarchySamplingMs)
                },

                // Selection changes: Throttle to 1 event per 500ms
                {
                    EventTypes.SelectionChanged,
                    new SamplingStrategy(SamplingMode.Throttle, SelectionSamplingMs)
                },

                // Property modifications: Debounce by key (200ms window)
                // Note: PropertyChangeTracker also implements its own debounce window,
                // so this provides additional protection against event storms.
                {
                    EventTypes.PropertyModified,
                    new SamplingStrategy(SamplingMode.DebounceByKey, PropertySamplingMs)
                },

                // Component/GameObject events: No sampling (always record)
                // ComponentAdded, ComponentRemoved, GameObjectCreated, GameObjectDestroyed
                // are intentionally not in this dictionary, so they default to None

                // Play mode changes: No sampling (record all)
                // PlayModeChanged is not in this dictionary

                // Scene events: No sampling (record all)
                // SceneSaving, SceneSaved, SceneOpened, NewSceneCreated are not in this dictionary

                // Build events: No sampling (record all)
                // BuildStarted, BuildCompleted, BuildFailed are not in this dictionary
            };
        }

        /// <summary>
        /// Adds or updates a sampling strategy for an event type.
        /// </summary>
        public static void SetStrategy(string eventType, SamplingMode mode, long windowMs = 1000)
        {
            Strategies[eventType] = new SamplingStrategy(mode, windowMs);
        }

        /// <summary>
        /// Removes the sampling strategy for an event type (reverts to None).
        /// </summary>
        public static void RemoveStrategy(string eventType)
        {
            Strategies.TryRemove(eventType, out _);
        }

        /// <summary>
        /// Gets the sampling strategy for an event type, or null if not configured.
        /// </summary>
        public static SamplingStrategy GetStrategy(string eventType)
        {
            return Strategies.TryGetValue(eventType, out var strategy) ? strategy : null;
        }

        /// <summary>
        /// Checks if an event type has a sampling strategy configured.
        /// </summary>
        public static bool HasStrategy(string eventType)
        {
            return Strategies.ContainsKey(eventType);
        }
    }
}
