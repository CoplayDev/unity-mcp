using System.Threading.Tasks;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Service for registering custom tools with MCP server via HTTP
    /// </summary>
    public interface ICustomToolRegistrationService
    {
        /// <summary>
        /// Registers all discovered tools with the MCP server
        /// </summary>
        /// <param name="projectId">Optional pre-captured project ID (for thread safety)</param>
        Task<bool> RegisterAllToolsAsync(string projectId = null);

    }
}
