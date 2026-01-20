using MCPForUnity.Editor.ActionTrace.Core;

namespace MCPForUnity.Editor.ActionTrace.Capture
{
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
}
