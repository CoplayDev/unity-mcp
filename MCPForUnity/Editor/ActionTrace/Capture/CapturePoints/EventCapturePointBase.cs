using System;
using MCPForUnity.Editor.ActionTrace.Context;
using MCPForUnity.Editor.ActionTrace.Core.Models;
using MCPForUnity.Editor.ActionTrace.Core.Store;
using UnityEngine;

namespace MCPForUnity.Editor.ActionTrace.Capture
{
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
}
