using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.ActionTrace.Core;
using UnityEditor;

namespace MCPForUnity.Editor.ActionTrace.Capture
{
    /// <summary>
    /// Smart sampling middleware to prevent event floods in high-frequency scenarios.
    ///
    /// Protects the ActionTrace from event storms (e.g., rapid Slider dragging,
    /// continuous Hierarchy changes) by applying configurable sampling strategies.
    ///
    /// Sampling modes:
    /// - None: No filtering, record all events
    /// - Throttle: Only record the first event within the window
    /// - Debounce: Only record the last event within the window
    /// - DebounceByKey: Only record the last event per unique key within the window
    ///
    /// Reuses existing infrastructure:
    /// - GlobalIdHelper.ToGlobalIdString() for stable keys
    /// - EditorEvent payload for event metadata
    /// </summary>
    [InitializeOnLoad]
    public static class SamplingMiddleware
    {
        // Configuration
        private const int MaxSampleCache = 128;          // Max pending samples before forced cleanup
        private const long CleanupAgeMs = 2000;          // Cleanup samples older than 2 seconds
        private const long FlushCheckIntervalMs = 200;   // Check for expired debounce samples every 200ms

        // State
        // Thread-safe dictionary to prevent race conditions in multi-threaded scenarios
        private static readonly ConcurrentDictionary<string, PendingSample> _pendingSamples = new();
        private static long _lastCleanupTime;
        private static long _lastFlushCheckTime;

        /// <summary>
        /// Initializes the sampling middleware and schedules periodic flush checks.
        /// </summary>
        static SamplingMiddleware()
        {
            ScheduleFlushCheck();
        }

        /// <summary>
        /// Schedules a periodic flush check using EditorApplication.update.
        /// This ensures Debounce modes emit trailing events after their windows expire.
        /// Using update instead of delayCall to avoid infinite recursion.
        /// </summary>
        private static void ScheduleFlushCheck()
        {
            // Use EditorApplication.update instead of delayCall to avoid infinite recursion
            // This ensures the callback is properly cleaned up on domain reload
            EditorApplication.update -= FlushExpiredDebounceSamples;
            EditorApplication.update += FlushExpiredDebounceSamples;
        }

        /// <summary>
        /// Flushes debounce samples whose windows have expired.
        /// This ensures Debounce/DebounceByKey modes emit the trailing event.
        /// </summary>
        private static void FlushExpiredDebounceSamples()
        {
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // Only check periodically to avoid performance impact
            if (nowMs - _lastFlushCheckTime < FlushCheckIntervalMs)
                return;

            _lastFlushCheckTime = nowMs;

            var toRecord = new List<PendingSample>();

            // Directly remove expired entries without intermediate list
            foreach (var kvp in _pendingSamples)
            {
                // Check if this key has a debounce strategy configured
                if (SamplingConfig.Strategies.TryGetValue(kvp.Value.Event.Type, out var strategy))
                {
                    // Only process Debounce/DebounceByKey modes
                    if (strategy.Mode == SamplingMode.Debounce || strategy.Mode == SamplingMode.DebounceByKey)
                    {
                        // If window has expired, this sample should be recorded
                        if (nowMs - kvp.Value.TimestampMs > strategy.WindowMs)
                        {
                            toRecord.Add(kvp.Value);
                            // Remove immediately while iterating (TryRemove is safe)
                            _pendingSamples.TryRemove(kvp.Key, out _);
                        }
                    }
                }
            }

            // Record the trailing events
            foreach (var sample in toRecord)
            {
                // Record directly to EventStore without going through ShouldRecord again
                EventStore.Record(sample.Event);
            }
        }

        /// <summary>
        /// Determines whether an event should be recorded based on configured sampling strategies.
        /// Returns true if the event should be recorded, false if it should be filtered out.
        ///
        /// This method is called by event emitters before recording to EventStore.
        /// Implements a three-stage filtering pipeline:
        /// 1. Blacklist (EventFilter) - filters system junk
        /// 2. Sampling strategy - merges duplicate events
        /// 3. Cache management - prevents unbounded growth
        /// </summary>
        public static bool ShouldRecord(EditorEvent evt)
        {
            if (evt == null)
                return false;

            // ========== Stage 1: Blacklist Filtering (L1) ==========
            // Check if this event's target is known junk before any other processing
            if (evt.Type == EventTypes.AssetImported ||
                evt.Type == EventTypes.AssetMoved ||
                evt.Type == EventTypes.AssetDeleted)
            {
                // For asset events, check the path (stored in TargetId or payload)
                string assetPath = evt.TargetId;
                if (string.IsNullOrEmpty(assetPath) && evt.Payload.TryGetValue("path", out var pathVal))
                {
                    assetPath = pathVal?.ToString();
                }

                if (!string.IsNullOrEmpty(assetPath) && !EventFilter.ShouldTrackAsset(assetPath))
                {
                    return false; // Filtered by blacklist
                }
            }

            // ========== Stage 2: Sampling Strategy Check (L2) ==========
            // No sampling strategy configured - record all events
            if (!SamplingConfig.Strategies.TryGetValue(evt.Type, out var strategy))
                return true;

            // Strategy is None - record all events of this type
            if (strategy.Mode == SamplingMode.None)
                return true;

            // Generate the sampling key based on mode
            string key = GenerateSamplingKey(evt, strategy.Mode);

            if (string.IsNullOrEmpty(key))
                return true;

            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // Periodic cleanup of expired samples (runs every ~1 second)
            if (nowMs - _lastCleanupTime > 1000)
            {
                CleanupExpiredSamples(nowMs);
                _lastCleanupTime = nowMs;
            }

            // Check if we have a pending sample for this key
            if (_pendingSamples.TryGetValue(key, out var pending))
            {
                // Sample is still within the window
                if (nowMs - pending.TimestampMs <= strategy.WindowMs)
                {
                    switch (strategy.Mode)
                    {
                        case SamplingMode.Throttle:
                            // Throttle: Drop all events after the first in the window
                            return false;

                        case SamplingMode.Debounce:
                        case SamplingMode.DebounceByKey:
                            // Debounce: Keep only the last event in the window
                            // Note: Must update the dictionary entry since PendingSample is a struct
                            _pendingSamples[key] = new PendingSample
                            {
                                Event = evt,
                                TimestampMs = nowMs
                            };
                            return false;
                    }
                }

                // Window expired - remove old entry
                _pendingSamples.TryRemove(key, out _);
            }

            // Enforce cache limit to prevent unbounded growth
            if (_pendingSamples.Count >= MaxSampleCache)
            {
                CleanupExpiredSamples(nowMs);

                // If still over limit after cleanup, force remove oldest entry
                if (_pendingSamples.Count >= MaxSampleCache)
                {
                    // Manual loop to find oldest entry (avoid LINQ allocation in hot path)
                    string oldestKey = null;
                    long oldestTimestamp = long.MaxValue;
                    foreach (var kvp in _pendingSamples)
                    {
                        if (kvp.Value.TimestampMs < oldestTimestamp)
                        {
                            oldestTimestamp = kvp.Value.TimestampMs;
                            oldestKey = kvp.Key;
                        }
                    }
                    if (!string.IsNullOrEmpty(oldestKey))
                    {
                        _pendingSamples.TryRemove(oldestKey, out _);
                    }
                }
            }

            // Add new pending sample
            _pendingSamples[key] = new PendingSample
            {
                Event = evt,
                TimestampMs = nowMs
            };

            // For Debounce modes, don't record immediately - wait for window to expire
            // This prevents duplicate recording: first event here, trailing event in FlushExpiredDebounceSamples
            if (strategy.Mode == SamplingMode.Debounce || strategy.Mode == SamplingMode.DebounceByKey)
                return false;

            // For Throttle mode, record the first event immediately
            return true;
        }

        /// <summary>
        /// Generates the sampling key based on the sampling mode.
        /// - Throttle/Debounce: Key by event type only
        /// - DebounceByKey: Key by event type + target (GlobalId)
        /// </summary>
        private static string GenerateSamplingKey(EditorEvent evt, SamplingMode mode)
        {
            // For DebounceByKey, include TargetId to distinguish different objects
            if (mode == SamplingMode.DebounceByKey)
            {
                return $"{evt.Type}:{evt.TargetId}";
            }

            // For Throttle and Debounce, key by type only
            return evt.Type;
        }

        /// <summary>
        /// Removes expired samples from the cache.
        /// Samples older than CleanupAgeMs are removed.
        /// </summary>
        private static void CleanupExpiredSamples(long nowMs)
        {
            // Directly remove expired samples without intermediate list
            foreach (var kvp in _pendingSamples)
            {
                if (nowMs - kvp.Value.TimestampMs > CleanupAgeMs)
                {
                    _pendingSamples.TryRemove(kvp.Key, out _);
                }
            }
        }

        /// <summary>
        /// Forces an immediate flush of all pending samples.
        /// Returns the events that were pending (useful for shutdown).
        /// </summary>
        public static List<EditorEvent> FlushPending()
        {
            // Manual loop instead of LINQ Select to avoid allocation
            var result = new List<EditorEvent>(_pendingSamples.Count);
            foreach (var kvp in _pendingSamples)
            {
                result.Add(kvp.Value.Event);
            }
            _pendingSamples.Clear();
            return result;
        }

        /// <summary>
        /// Gets the current count of pending samples.
        /// Useful for debugging and monitoring.
        /// </summary>
        public static int PendingCount => _pendingSamples.Count;

        /// <summary>
        /// Diagnostic helper: returns a snapshot of pending sampling keys.
        /// Safe to call from editor threads; best-effort snapshot.
        /// </summary>
        public static IReadOnlyList<string> GetPendingKeysSnapshot()
        {
            return _pendingSamples.Keys.ToList();
        }

        /// <summary>
        /// Clears all pending samples without recording them.
        /// Useful for testing or error recovery.
        /// </summary>
        public static void ClearPending()
        {
            _pendingSamples.Clear();
        }
    }

    /// <summary>
    /// Configurable sampling strategy for a specific event type.
    /// </summary>
    public class SamplingStrategy
    {
        /// <summary>
        /// The sampling mode to apply.
        /// </summary>
        public SamplingMode Mode { get; set; }

        /// <summary>
        /// Time window in milliseconds.
        /// - Throttle: Only first event within this window is recorded
        /// - Debounce/DebounceByKey: Only last event within this window is recorded
        /// </summary>
        public long WindowMs { get; set; }

        public SamplingStrategy(SamplingMode mode = SamplingMode.None, long windowMs = 1000)
        {
            Mode = mode;
            WindowMs = windowMs;
        }
    }

    /// <summary>
    /// Sampling mode determines how events are filtered.
    /// </summary>
    public enum SamplingMode
    {
        /// <summary>No filtering - record all events</summary>
        None,

        /// <summary>Throttle - only record the first event within the window</summary>
        Throttle,

        /// <summary>Debounce - only record the last event within the window (per type)</summary>
        Debounce,

        /// <summary>DebounceByKey - only record the last event per key within the window</summary>
        DebounceByKey
    }

    /// <summary>
    /// Static configuration for sampling strategies.
    /// Event types can be registered with their desired sampling behavior.
    /// </summary>
    public static class SamplingConfig
    {
        /// <summary>
        /// Default sampling strategies for common event types.
        /// Configured to prevent event floods while preserving important data.
        /// </summary>
        public static readonly Dictionary<string, SamplingStrategy> Strategies = new()
        {
            // Hierarchy changes: Throttle to 1 event per second
            {
                EventTypes.HierarchyChanged,
                new SamplingStrategy(SamplingMode.Throttle, 1000)
            },

            // PropertyModified handling removed here to avoid double-debounce when
            // PropertyChangeTracker already implements a dedicated debounce window.
            // If desired, SamplingConfig.SetStrategy(EventTypes.PropertyModified, ...) can
            // be used at runtime to re-enable middleware-level sampling.

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
            Strategies.Remove(eventType);
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

    /// <summary>
    /// Represents a pending sample that is being filtered.
    /// </summary>
    public struct PendingSample
    {
        /// <summary>
        /// The event being held for potential recording.
        /// </summary>
        public EditorEvent Event;

        /// <summary>
        /// Timestamp when this sample was last updated.
        /// </summary>
        public long TimestampMs;
    }
}
