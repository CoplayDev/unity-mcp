using System;
using System.Collections.Generic;
using MCPForUnity.Editor.ActionTrace.Core;
using MCPForUnity.Editor.ActionTrace.Context;
using UnityEngine;
using UnityEditor;

namespace MCPForUnity.Editor.ActionTrace.Capture
{
    /// <summary>
    /// Defines a point in the editor where events can be captured.
    ///
    /// This interface unifies all event capture sources:
    /// - Unity callbacks (EditorApplication events)
    /// - Asset postprocessors
    /// - Component change tracking
    /// - Custom tool invocations
    ///
    /// Implementations should be lightweight and focus on event capture,
    /// delegating filtering, sampling, and storage to the middleware pipeline.
    /// </summary>
    public interface IEventCapturePoint
    {
        /// <summary>
        /// Unique identifier for this capture point.
        /// Used for diagnostics and configuration.
        /// </summary>
        string CapturePointId { get; }

        /// <summary>
        /// Human-readable description of what this capture point monitors.
        /// </summary>
        string Description { get; }

        /// <summary>
        /// Priority for initialization (higher = earlier).
        /// Useful for dependencies between capture points.
        /// </summary>
        int InitializationPriority { get; }

        /// <summary>
        /// Whether this capture point is currently enabled.
        /// </summary>
        bool IsEnabled { get; set; }

        /// <summary>
        /// Initialize the capture point.
        /// Called when ActionTrace system starts.
        /// </summary>
        void Initialize();

        /// <summary>
        /// Shutdown the capture point.
        /// Called when ActionTrace system stops or domain reloads.
        /// </summary>
        void Shutdown();

        /// <summary>
        /// Get diagnostic information about this capture point.
        /// Useful for debugging and monitoring.
        /// </summary>
        string GetDiagnosticInfo();

        /// <summary>
        /// Get statistics about captured events.
        /// </summary>
        CapturePointStats GetStats();
    }

    /// <summary>
    /// Statistics for a capture point.
    /// </summary>
    [Serializable]
    public sealed class CapturePointStats
    {
        public int TotalEventsCaptured;
        public int EventsFiltered;
        public int EventsSampled;
        public long TotalCaptureTimeMs;
        public double AverageCaptureTimeMs;
        public int ErrorCount;

        private long _startTimeTicks;

        public void StartCapture()
        {
            _startTimeTicks = DateTimeOffset.UtcNow.Ticks;
        }

        public void EndCapture()
        {
            long elapsedTicks = DateTimeOffset.UtcNow.Ticks - _startTimeTicks;
            TotalCaptureTimeMs += elapsedTicks / 10000;
            TotalEventsCaptured++;
            UpdateAverage();
        }

        public void RecordFiltered()
        {
            EventsFiltered++;
        }

        public void RecordSampled()
        {
            EventsSampled++;
        }

        public void RecordError()
        {
            ErrorCount++;
        }

        public void UpdateAverage()
        {
            AverageCaptureTimeMs = TotalEventsCaptured > 0
                ? (double)TotalCaptureTimeMs / TotalEventsCaptured
                : 0;
        }

        public void Reset()
        {
            TotalEventsCaptured = 0;
            EventsFiltered = 0;
            EventsSampled = 0;
            TotalCaptureTimeMs = 0;
            AverageCaptureTimeMs = 0;
            ErrorCount = 0;
        }
    }

    /// <summary>
    /// Base class for capture points with common functionality.
    /// </summary>
    public abstract class EventCapturePointBase : IEventCapturePoint
    {
        private readonly CapturePointStats _stats = new();
        private bool _isEnabled = true;

        public abstract string CapturePointId { get; }
        public abstract string Description { get; }
        public virtual int InitializationPriority => 0;

        public virtual bool IsEnabled
        {
            get => _isEnabled;
            set => _isEnabled = value;
        }

        public virtual void Initialize() { }
        public virtual void Shutdown() { }

        public virtual string GetDiagnosticInfo()
        {
            return $"[{CapturePointId}] {Description}\n" +
                   $"  Enabled: {IsEnabled}\n" +
                   $"  Events: {_stats.TotalEventsCaptured} captured, {_stats.EventsFiltered} filtered, {_stats.EventsSampled} sampled\n" +
                   $"  Avg Capture Time: {_stats.AverageCaptureTimeMs:F3}ms\n" +
                   $"  Errors: {_stats.ErrorCount}";
        }

        public virtual CapturePointStats GetStats() => _stats;

        /// <summary>
        /// Record an event through the capture pipeline.
        /// This method handles filtering, sampling, and storage.
        /// </summary>
        protected void RecordEvent(EditorEvent evt, ContextMapping context = null)
        {
            if (!IsEnabled) return;

            _stats.StartCapture();

            try
            {
                // Create event and record via EventStore
                EventStore.Record(evt);
                _stats.EndCapture();
            }
            catch (Exception ex)
            {
                _stats.RecordError();
                Debug.LogError($"[{CapturePointId}] Error recording event: {ex.Message}");
            }
        }

        /// <summary>
        /// Record a filtered event (doesn't count towards captured stats).
        /// </summary>
        protected void RecordFiltered()
        {
            _stats.RecordFiltered();
        }

        /// <summary>
        /// Record a sampled event (counted as sampled, not captured).
        /// </summary>
        protected void RecordSampled()
        {
            _stats.RecordSampled();
        }

        /// <summary>
        /// Reset statistics.
        /// </summary>
        public void ResetStats()
        {
            _stats.Reset();
        }
    }

    /// <summary>
    /// Registry for all event capture points.
    /// Manages lifecycle and provides access for diagnostics.
    /// </summary>
    public sealed class EventCaptureRegistry
    {
        private static readonly Lazy<EventCaptureRegistry> _instance =
            new(() => new EventCaptureRegistry());

        private readonly List<IEventCapturePoint> _capturePoints = new();
        private bool _isInitialized;

        public static EventCaptureRegistry Instance => _instance.Value;

        private EventCaptureRegistry() { }

        /// <summary>
        /// Register a capture point.
        /// Should be called during initialization, before Start().
        /// </summary>
        public void Register(IEventCapturePoint capturePoint)
        {
            if (capturePoint == null) return;

            _capturePoints.Add(capturePoint);

            // Sort by priority
            _capturePoints.Sort((a, b) => b.InitializationPriority.CompareTo(a.InitializationPriority));
        }

        /// <summary>
        /// Unregister a capture point.
        /// </summary>
        public bool Unregister(string capturePointId)
        {
            var point = _capturePoints.Find(p => p.CapturePointId == capturePointId);
            if (point != null)
            {
                if (_isInitialized)
                    point.Shutdown();
                _capturePoints.Remove(point);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Initialize all registered capture points.
        /// </summary>
        public void InitializeAll()
        {
            if (_isInitialized) return;

            foreach (var point in _capturePoints)
            {
                try
                {
                    point.Initialize();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[EventCaptureRegistry] Failed to initialize {point.CapturePointId}: {ex.Message}");
                }
            }

            _isInitialized = true;
            Debug.Log($"[EventCaptureRegistry] Initialized {_capturePoints.Count} capture points");
        }

        /// <summary>
        /// Shutdown all registered capture points.
        /// </summary>
        public void ShutdownAll()
        {
            if (!_isInitialized) return;

            // Shutdown in reverse order
            for (int i = _capturePoints.Count - 1; i >= 0; i--)
            {
                try
                {
                    _capturePoints[i].Shutdown();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[EventCaptureRegistry] Failed to shutdown {_capturePoints[i].CapturePointId}: {ex.Message}");
                }
            }

            _isInitialized = false;
        }

        /// <summary>
        /// Get a capture point by ID.
        /// </summary>
        public IEventCapturePoint GetCapturePoint(string id)
        {
            return _capturePoints.Find(p => p.CapturePointId == id);
        }

        /// <summary>
        /// Get all registered capture points.
        /// </summary>
        public IReadOnlyList<IEventCapturePoint> GetAllCapturePoints()
        {
            return _capturePoints.AsReadOnly();
        }

        /// <summary>
        /// Get enabled capture points.
        /// </summary>
        public IReadOnlyList<IEventCapturePoint> GetEnabledCapturePoints()
        {
            return _capturePoints.FindAll(p => p.IsEnabled).AsReadOnly();
        }

        /// <summary>
        /// Get diagnostic information for all capture points.
        /// </summary>
        public string GetDiagnosticInfo()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Event Capture Registry - {_capturePoints.Count} points registered:");
            sb.AppendLine($"Initialized: {_isInitialized}");

            foreach (var point in _capturePoints)
            {
                sb.AppendLine();
                sb.AppendLine(point.GetDiagnosticInfo());
            }

            return sb.ToString();
        }

        /// <summary>
        /// Get aggregated statistics from all capture points.
        /// </summary>
        public CapturePointStats GetAggregatedStats()
        {
            var aggregated = new CapturePointStats();

            foreach (var point in _capturePoints)
            {
                var stats = point.GetStats();
                aggregated.TotalEventsCaptured += stats.TotalEventsCaptured;
                aggregated.EventsFiltered += stats.EventsFiltered;
                aggregated.EventsSampled += stats.EventsSampled;
                aggregated.TotalCaptureTimeMs += stats.TotalCaptureTimeMs;
                aggregated.ErrorCount += stats.ErrorCount;
            }

            aggregated.UpdateAverage();
            return aggregated;
        }

        /// <summary>
        /// Enable or disable a capture point by ID.
        /// </summary>
        public bool SetEnabled(string id, bool enabled)
        {
            var point = GetCapturePoint(id);
            if (point != null)
            {
                point.IsEnabled = enabled;
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Attribute to mark a class as an event capture point.
    /// Used for auto-discovery during initialization.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class EventCapturePointAttribute : Attribute
    {
        public string Id { get; }
        public string Description { get; }
        public int Priority { get; }

        public EventCapturePointAttribute(string id, string description = null, int priority = 0)
        {
            Id = id;
            Description = description ?? id;
            Priority = priority;
        }
    }

    /// <summary>
    /// Built-in capture point identifiers.
    /// </summary>
    public static class BuiltInCapturePoints
    {
        public const string UnityCallbacks = "UnityCallbacks";
        public const string AssetPostprocessor = "AssetPostprocessor";
        public const string PropertyTracking = "PropertyTracking";
        public const string SelectionTracking = "SelectionTracking";
        public const string HierarchyTracking = "HierarchyTracking";
        public const string BuildTracking = "BuildTracking";
        public const string CompilationTracking = "CompilationTracking";
        public const string ToolInvocation = "ToolInvocation";
    }
}
