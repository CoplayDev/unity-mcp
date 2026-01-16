namespace MCPForUnity.Editor.ActionTrace.Semantics
{
    /// <summary>
    /// Event categorizer interface.
    /// Converts importance scores into categorical labels.
    /// Categories are computed at query time, not stored with events.
    /// </summary>
    public interface IEventCategorizer
    {
        /// <summary>
        /// Categorize an importance score into a label.
        /// </summary>
        /// <param name="score">Importance score from 0.0 to 1.0</param>
        /// <returns>Category label (e.g., "critical", "high", "medium", "low")</returns>
        string Categorize(float score);
    }
}
