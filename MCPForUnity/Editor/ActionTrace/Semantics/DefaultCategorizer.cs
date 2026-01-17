namespace MCPForUnity.Editor.ActionTrace.Semantics
{
    /// <summary>
    /// Default implementation of event categorization.
    /// Maps importance scores to category labels.
    /// </summary>
    public sealed class DefaultCategorizer : IEventCategorizer
    {
        /// <summary>
        /// Categorize an importance score into a label.
        /// </summary>
        public string Categorize(float score)
        {
            // Ensure score is in valid range
            if (score < 0f) score = 0f;
            if (score > 1f) score = 1f;

            return score switch
            {
                >= 0.9f => "critical",
                >= 0.7f => "high",
                >= 0.4f => "medium",
                _ => "low"
            };
        }
    }
}
