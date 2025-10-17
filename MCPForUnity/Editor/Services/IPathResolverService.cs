namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Service for resolving paths to required tools and supporting user overrides
    /// </summary>
    public interface IPathResolverService
    {
        /// <summary>
        /// Gets the MCP server path (respects override if set)
        /// </summary>
        /// <returns>Path to the MCP server directory containing server.py, or null if not found</returns>
        string GetMcpServerPath();
        
        /// <summary>
        /// Gets the UV package manager path (respects override if set)
        /// </summary>
        /// <returns>Path to the uv executable, or null if not found</returns>
        string GetUvPath();
        
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
        /// Checks if UV is detected on the system
        /// </summary>
        /// <returns>True if UV is found</returns>
        bool IsUvDetected();
        
        /// <summary>
        /// Checks if Claude CLI is detected on the system
        /// </summary>
        /// <returns>True if Claude CLI is found</returns>
        bool IsClaudeCliDetected();
        
        /// <summary>
        /// Sets an override for the MCP server path
        /// </summary>
        /// <param name="path">Path to override with</param>
        void SetMcpServerOverride(string path);
        
        /// <summary>
        /// Sets an override for the UV path
        /// </summary>
        /// <param name="path">Path to override with</param>
        void SetUvPathOverride(string path);
        
        /// <summary>
        /// Sets an override for the Claude CLI path
        /// </summary>
        /// <param name="path">Path to override with</param>
        void SetClaudeCliPathOverride(string path);
        
        /// <summary>
        /// Clears the MCP server path override
        /// </summary>
        void ClearMcpServerOverride();
        
        /// <summary>
        /// Clears the UV path override
        /// </summary>
        void ClearUvPathOverride();
        
        /// <summary>
        /// Clears the Claude CLI path override
        /// </summary>
        void ClearClaudeCliPathOverride();
        
        /// <summary>
        /// Gets whether a MCP server path override is active
        /// </summary>
        bool HasMcpServerOverride { get; }
        
        /// <summary>
        /// Gets whether a UV path override is active
        /// </summary>
        bool HasUvPathOverride { get; }
        
        /// <summary>
        /// Gets whether a Claude CLI path override is active
        /// </summary>
        bool HasClaudeCliPathOverride { get; }
    }
}
