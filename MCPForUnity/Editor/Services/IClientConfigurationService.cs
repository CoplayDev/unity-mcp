using MCPForUnity.Editor.Models;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Service for configuring MCP clients
    /// </summary>
    public interface IClientConfigurationService
    {
        /// <summary>
        /// Configures a specific MCP client
        /// </summary>
        /// <param name="client">The client to configure</param>
        void ConfigureClient(McpClient client);
        
        /// <summary>
        /// Configures all detected/installed MCP clients (skips clients where CLI/tools not found)
        /// </summary>
        /// <returns>Summary of configuration results</returns>
        ClientConfigurationSummary ConfigureAllDetectedClients();
        
        /// <summary>
        /// Checks the configuration status of a client
        /// </summary>
        /// <param name="client">The client to check</param>
        /// <param name="attemptAutoRewrite">If true, attempts to auto-fix mismatched paths</param>
        /// <returns>True if status changed, false otherwise</returns>
        bool CheckClientStatus(McpClient client, bool attemptAutoRewrite = true);
        
        /// <summary>
        /// Registers MCP for Unity with Claude Code CLI
        /// </summary>
        void RegisterClaudeCode();
        
        /// <summary>
        /// Unregisters MCP for Unity from Claude Code CLI
        /// </summary>
        void UnregisterClaudeCode();
        
        /// <summary>
        /// Gets the configuration file path for a client
        /// </summary>
        /// <param name="client">The client</param>
        /// <returns>Platform-specific config path</returns>
        string GetConfigPath(McpClient client);
        
        /// <summary>
        /// Generates the configuration JSON for a client
        /// </summary>
        /// <param name="client">The client</param>
        /// <returns>JSON configuration string</returns>
        string GenerateConfigJson(McpClient client);
        
        /// <summary>
        /// Gets human-readable installation steps for a client
        /// </summary>
        /// <param name="client">The client</param>
        /// <returns>Installation instructions</returns>
        string GetInstallationSteps(McpClient client);
    }
    
    /// <summary>
    /// Summary of configuration results for multiple clients
    /// </summary>
    public class ClientConfigurationSummary
    {
        /// <summary>
        /// Number of clients successfully configured
        /// </summary>
        public int SuccessCount { get; set; }
        
        /// <summary>
        /// Number of clients that failed to configure
        /// </summary>
        public int FailureCount { get; set; }
        
        /// <summary>
        /// Number of clients skipped (already configured or tool not found)
        /// </summary>
        public int SkippedCount { get; set; }
        
        /// <summary>
        /// Detailed messages for each client
        /// </summary>
        public System.Collections.Generic.List<string> Messages { get; set; } = new();
        
        /// <summary>
        /// Gets a human-readable summary message
        /// </summary>
        public string GetSummaryMessage()
        {
            return $"✓ {SuccessCount} configured, ⚠ {FailureCount} failed, ➜ {SkippedCount} skipped";
        }
    }
}
