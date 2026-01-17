using System;

namespace MCPForUnity.Editor.ActionTrace.Core
{
    /// <summary>
    /// Event metadata.
    /// Defines category, importance, summary template, and sampling config for event types.
    /// </summary>
    [Serializable]
    public class EventMetadata
    {
        /// <summary>
        /// Event category.
        /// </summary>
        public EventCategory Category { get; set; } = EventCategory.Unknown;

        /// <summary>
        /// Default importance score (0.0 ~ 1.0).
        /// </summary>
        public float DefaultImportance { get; set; } = 0.5f;

        /// <summary>
        /// Summary template.
        /// Supports placeholders: {payload_key}, {type}, {target}, {time}
        /// Supports conditionals: {if:key, then}
        /// </summary>
        public string SummaryTemplate { get; set; }

        /// <summary>
        /// Whether sampling is enabled.
        /// </summary>
        public bool EnableSampling { get; set; }

        /// <summary>
        /// Sampling mode.
        /// </summary>
        public SamplingMode SamplingMode { get; set; }

        /// <summary>
        /// Sampling window (milliseconds).
        /// </summary>
        public int SamplingWindow { get; set; }
    }
}
