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
}
