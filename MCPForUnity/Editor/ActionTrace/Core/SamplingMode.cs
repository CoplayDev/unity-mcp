namespace MCPForUnity.Editor.ActionTrace.Core
{
    /// <summary>
    /// Sampling mode.
    /// </summary>
    public enum SamplingMode
    {
        /// <summary>No sampling, record all events</summary>
        None,

        /// <summary>Throttle - only record first event within window</summary>
        Throttle,

        /// <summary>Debounce - only record last event within window</summary>
        Debounce,

        /// <summary>DebounceByKey - debounce per unique key</summary>
        DebounceByKey
    }
}
