using MCPForUnity.Editor.ActionTrace.Core.Models;

namespace MCPForUnity.Editor.ActionTrace.Capture
{
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
