using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Clients;
using MCPForUnity.Editor.Models;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Implementation of client configuration service
    /// </summary>
    public class ClientConfigurationService : IClientConfigurationService
    {
        private readonly List<IMcpClientConfigurator> configurators;

        public ClientConfigurationService()
        {
            configurators = McpClientRegistry.All.ToList();
        }

        public IReadOnlyList<IMcpClientConfigurator> GetAllClients() => configurators;

        public void ConfigureClient(IMcpClientConfigurator configurator)
        {
            configurator.Configure();
        }

        public ClientConfigurationSummary ConfigureAllDetectedClients()
        {
            var summary = new ClientConfigurationSummary();
            foreach (var configurator in configurators)
            {
                try
                {
                    // Always re-run configuration so core fields stay current
                    configurator.CheckStatus(attemptAutoRewrite: false);
                    configurator.Configure();
                    summary.SuccessCount++;
                    summary.Messages.Add($"✓ {configurator.DisplayName}: Configured successfully");
                }
                catch (Exception ex)
                {
                    summary.FailureCount++;
                    summary.Messages.Add($"⚠ {configurator.DisplayName}: {ex.Message}");
                }
            }

            return summary;
        }

        public bool CheckClientStatus(IMcpClientConfigurator configurator, bool attemptAutoRewrite = true)
        {
            var previous = configurator.Status;
            var current = configurator.CheckStatus(attemptAutoRewrite);
            return current != previous;
        }

    }
}
