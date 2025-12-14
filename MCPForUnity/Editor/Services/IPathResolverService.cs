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
        /// Gets the Python path (respects override if set)
        /// </summary>
        /// <returns>Path to the python executable</returns>
        string GetPythonPath();

        /// <summary>
        /// Gets the Node.js path (respects override if set)
        /// </summary>
        /// <returns>Path to the node executable</returns>
        string GetNodePath();

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
        /// Sets an override for the Python path
        /// </summary>
        /// <param name="path">Path to override with</param>
        void SetPythonPathOverride(string path);

        /// <summary>
        /// Clears the Python path override
        /// </summary>
        void ClearPythonPathOverride();

        /// <summary>
        /// Sets an override for the Node.js path
        /// </summary>
        /// <param name="path">Path to override with</param>
        void SetNodePathOverride(string path);

        /// <summary>
        /// Clears the Node.js path override
        /// </summary>
        void ClearNodePathOverride();

        /// <summary>
        /// Gets whether a uvx path override is active
        /// </summary>
        bool HasUvxPathOverride { get; }

        /// <summary>
        /// Gets whether a Claude CLI path override is active
        /// </summary>
        bool HasClaudeCliPathOverride { get; }

        /// <summary>
        /// Gets whether a Python path override is active
        /// </summary>
        bool HasPythonPathOverride { get; }

        /// <summary>
        /// Gets whether a Node.js path override is active
        /// </summary>
        bool HasNodePathOverride { get; }
    }
}
