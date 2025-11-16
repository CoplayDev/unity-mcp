namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Interface for cache management operations
    /// </summary>
    public interface ICacheManagementService
    {
        /// <summary>
        /// Clear the local uvx cache for the MCP server package
        /// </summary>
        /// <returns>True if successful, false otherwise</returns>
        bool ClearUvxCache();
    }
}
