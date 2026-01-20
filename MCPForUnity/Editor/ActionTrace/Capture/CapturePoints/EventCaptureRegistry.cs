using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace MCPForUnity.Editor.ActionTrace.Capture
{
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
            var sb = new StringBuilder();
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
}
