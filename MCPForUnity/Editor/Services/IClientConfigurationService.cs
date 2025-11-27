using System.Collections.Generic;
using MCPForUnity.Editor.Clients;
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
        void ConfigureClient(IMcpClientConfigurator configurator);

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
        bool CheckClientStatus(IMcpClientConfigurator configurator, bool attemptAutoRewrite = true);

        /// <summary>Gets the registry of discovered configurators.</summary>
        IReadOnlyList<IMcpClientConfigurator> GetAllClients();
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
