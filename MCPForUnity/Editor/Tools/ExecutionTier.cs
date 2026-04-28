namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Classifies a tool's execution characteristics for queue dispatch.
    /// </summary>
    public enum ExecutionTier
    {
        /// <summary>
        /// Read-only, microsecond-scale. Executes synchronously, returns inline.
        /// Examples: find_gameobjects, read_console, scene hierarchy queries.
        /// </summary>
        Instant = 0,

        /// <summary>
        /// Main-thread writes that don't trigger domain reload. Non-blocking.
        /// Multiple smooth operations can coexist.
        /// Examples: set_property, modify transform, material changes.
        /// </summary>
        Smooth = 1,

        /// <summary>
        /// Triggers compilation, domain reload, or long-running processes.
        /// Requires exclusive access — blocks all other operations.
        /// Examples: script create/delete, compile, test runs, scene load.
        /// </summary>
        Heavy = 2
    }
}
