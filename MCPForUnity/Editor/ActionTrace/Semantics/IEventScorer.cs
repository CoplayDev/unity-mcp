using MCPForUnity.Editor.ActionTrace.Core;

namespace MCPForUnity.Editor.ActionTrace.Semantics
{
    /// <summary>
    /// Event importance scorer interface.
    /// Returns a float score (0.0 to 1.0) representing event importance.
    /// Scores are computed at query time, not stored with events.
    /// </summary>
    public interface IEventScorer
    {
        /// <summary>
        /// Calculate importance score for an event.
        /// Higher values indicate more important events.
        /// </summary>
        /// <param name="evt">The event to score</param>
        /// <returns>Score from 0.0 (least important) to 1.0 (most important)</returns>
        float Score(EditorEvent evt);
    }
}
