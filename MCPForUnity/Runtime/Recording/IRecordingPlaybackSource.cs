namespace MCPForUnity.Runtime.Recording
{
    /// <summary>
    /// Opt-in playback contract for project state machines and tween engines.
    /// Implement on a MonoBehaviour; all calls run on Unity's main thread.
    /// Advance is driven by the recorder: do not also auto-update while recording.
    /// </summary>
    public interface IRecordingPlaybackSource
    {
        /// <summary>Stable project identifier; discovery still rejects duplicate matching sources.</summary>
        string RecordingId { get; }
        /// <summary>Declared state names that may be selected by a recording plan.</summary>
        string[] States { get; }
        /// <summary>Source FPS for frame addressing, or zero when the client must supply it.</summary>
        double FrameRate { get; }
        /// <summary>Known source duration in seconds, or zero for an unknown or unbounded source.</summary>
        double DurationSeconds { get; }
        /// <summary>Whether absolute sampling is supported without simulating prior steps.</summary>
        bool CanSeek { get; }
        /// <summary>Capture a non-null opaque snapshot for later restoration before playback changes.</summary>
        object CaptureState();
        /// <summary>Acquire the clock, select the state and reset to zero; suppress normal automatic ticking.</summary>
        void Prepare(string state);
        /// <summary>Evaluate absolute source time when CanSeek is true.</summary>
        void Seek(double timeSeconds);
        /// <summary>Simulate a source-time delta when CanSeek is false, including bounded pre-roll.</summary>
        void Advance(double deltaSeconds);
        /// <summary>Release recorder ownership; null means keep the final visual state.</summary>
        void RestoreState(object snapshot);
    }
}
