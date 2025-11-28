using System;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
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

                var summary = MCPServiceLocator.Client.ConfigureAllDetectedClients();

                if (summary.FailureCount > 0 || summary.SuccessCount == 0)
                {
                    McpLog.Warn($"Legacy configuration migration incomplete ({summary.GetSummaryMessage()}). Will retry next session.");
                    return;
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

                McpLog.Info($"Legacy configuration migration complete ({summary.GetSummaryMessage()})");
            }
            catch (Exception ex)
            {
                McpLog.Error($"Legacy MCP server migration failed: {ex.Message}");
            }
        }
    }
}
