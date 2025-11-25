namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Service for resolving paths to required tools and supporting user overrides
    /// </summary>
    public interface IPathResolverService
    {
        /// <summary>
        /// Gets the uvx package manager path (respects override if set)
        /// </summary>
        /// <returns>Path to the uvx executable, or null if not found</returns>
        string GetUvxPath();

        /// <summary>
        /// Gets the Claude CLI path (respects override if set)
        /// </summary>
        /// <returns>Path to the claude executable, or null if not found</returns>
        string GetClaudeCliPath();

        /// <summary>
        /// Checks if Python is detected on the system
        /// </summary>
        /// <returns>True if Python is found</returns>
        bool IsPythonDetected();

        /// <summary>
        /// Checks if Claude CLI is detected on the system
        /// </summary>
        /// <returns>True if Claude CLI is found</returns>
        bool IsClaudeCliDetected();

        /// <summary>
        /// Sets an override for the uvx path
        /// </summary>
        /// <param name="path">Path to override with</param>
        void SetUvxPathOverride(string path);

        /// <summary>
        /// Sets an override for the Claude CLI path
        /// </summary>
        /// <param name="path">Path to override with</param>
        void SetClaudeCliPathOverride(string path);

        /// <summary>
        /// Clears the uvx path override
        /// </summary>
        void ClearUvxPathOverride();

        /// <summary>
        /// Clears the Claude CLI path override
        /// </summary>
        void ClearClaudeCliPathOverride();

        /// <summary>
        /// Gets whether a uvx path override is active
        /// </summary>
        bool HasUvxPathOverride { get; }

        /// <summary>
        /// Gets whether a Claude CLI path override is active
        /// </summary>
        bool HasClaudeCliPathOverride { get; }
    }
}
