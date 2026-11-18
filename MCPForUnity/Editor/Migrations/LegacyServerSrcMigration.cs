using System;
using MCPForUnity.Editor.Clients;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Models;
using MCPForUnity.Editor.Services;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Migrations
{
    /// <summary>
    /// Detects legacy embedded-server preferences and migrates configs to the new uvx/stdio path once.
    /// </summary>
    [InitializeOnLoad]
    internal static class LegacyServerSrcMigration
    {
        private const string ServerSrcKey = EditorPrefKeys.ServerSrc;
        private const string UseEmbeddedKey = EditorPrefKeys.UseEmbeddedServer;

        static LegacyServerSrcMigration()
        {
            if (Application.isBatchMode)
                return;

            EditorApplication.delayCall += RunMigrationIfNeeded;
        }

        internal static ClientConfigurationSummary MigrateDetectedClients(IClientConfigurationService clientService)
        {
            var summary = new ClientConfigurationSummary();
            foreach (var configurator in clientService.GetAllClients())
            {
                if (!configurator.IsInstalled)
                {
                    summary.SkippedCount++;
                    continue;
                }
                try
                {
                    // Automatic migration must leave user-managed launchers untouched.
                    // Explicit Configure All continues to replace them.
                    if (configurator is JsonFileMcpConfigurator json && json.HasCustomStdioConfiguration())
                    {
                        summary.SkippedCount++;
                        continue;
                    }
                    var status = configurator.CheckStatus(attemptAutoRewrite: false);
                    if (configurator is JsonFileMcpConfigurator && status == McpStatus.Error)
                    {
                        summary.FailureCount++;
                        summary.Messages.Add($"⚠ {configurator.DisplayName}: Configuration is invalid; update it manually before migrating.");
                        continue;
                    }
                    // Match the CLI's automatic re-registration path, without changing its explicit toggle.
                    if (configurator is ClaudeCliMcpConfigurator cli && status == McpStatus.Configured)
                        cli.Client.SetStatus(McpStatus.IncorrectPath);
                    clientService.ConfigureClient(configurator);
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

        private static void RunMigrationIfNeeded()
        {
            EditorApplication.delayCall -= RunMigrationIfNeeded;

            bool hasServerSrc = EditorPrefs.HasKey(ServerSrcKey);
            bool hasUseEmbedded = EditorPrefs.HasKey(UseEmbeddedKey);

            if (!hasServerSrc && !hasUseEmbedded)
            {
                return;
            }

            try
            {
                McpLog.Info("Detected legacy embedded MCP server configuration. Updating all client configs...");

                var summary = MigrateDetectedClients(MCPServiceLocator.Client);

                if (summary.FailureCount > 0)
                {
                    McpLog.Warn($"Legacy configuration migration finished with errors ({summary.GetSummaryMessage()}). details:");
                    if (summary.Messages != null)
                    {
                        foreach (var message in summary.Messages)
                        {
                            McpLog.Warn($"  {message}");
                        }
                    }
                    McpLog.Warn("Legacy keys will be removed to prevent migration loop. Please configure failing clients manually.");
                }
                else
                {
                    McpLog.Info($"Legacy configuration migration complete ({summary.GetSummaryMessage()})");
                }

                if (hasServerSrc)
                {
                    EditorPrefs.DeleteKey(ServerSrcKey);
                    McpLog.Info("  ✓ Removed legacy key: MCPForUnity.ServerSrc");
                }

                if (hasUseEmbedded)
                {
                    EditorPrefs.DeleteKey(UseEmbeddedKey);
                    McpLog.Info("  ✓ Removed legacy key: MCPForUnity.UseEmbeddedServer");
                }
            }
            catch (Exception ex)
            {
                McpLog.Error($"Legacy MCP server migration failed: {ex.Message}");
            }
        }
    }
}
