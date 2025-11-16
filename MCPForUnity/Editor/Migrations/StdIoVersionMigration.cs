using System;
using System.IO;
using MCPForUnity.Editor.Data;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Models;
using MCPForUnity.Editor.Services;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using MCPForUnity.Editor.Constants;

namespace MCPForUnity.Editor.Migrations
{
    /// <summary>
    /// Keeps stdio MCP clients in sync with the current package version by rewriting their configs when the package updates.
    /// </summary>
    [InitializeOnLoad]
    internal static class StdIoVersionMigration
    {
        private const string LastUpgradeKey = EditorPrefKeys.LastStdIoUpgradeVersion;

        static StdIoVersionMigration()
        {
            if (Application.isBatchMode)
                return;

            EditorApplication.delayCall += RunMigrationIfNeeded;
        }

        private static void RunMigrationIfNeeded()
        {
            EditorApplication.delayCall -= RunMigrationIfNeeded;

            string currentVersion = AssetPathUtility.GetPackageVersion();
            if (string.IsNullOrEmpty(currentVersion) || string.Equals(currentVersion, "unknown", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string lastUpgradeVersion = string.Empty;
            try { lastUpgradeVersion = EditorPrefs.GetString(LastUpgradeKey, string.Empty); } catch { }

            if (string.Equals(lastUpgradeVersion, currentVersion, StringComparison.OrdinalIgnoreCase))
            {
                return; // Already refreshed for this package version
            }

            bool hadFailures = false;
            bool touchedAny = false;

            var clients = new McpClients().clients;
            foreach (var client in clients)
            {
                try
                {
                    if (!ConfigUsesStdIo(client))
                        continue;

                    MCPServiceLocator.Client.ConfigureClient(client);
                    touchedAny = true;
                }
                catch (Exception ex)
                {
                    hadFailures = true;
                    McpLog.Warn($"Failed to refresh stdio config for {client.name}: {ex.Message}");
                }
            }

            if (!touchedAny)
            {
                // Nothing needed refreshing; still record version so we don't rerun every launch
                try { EditorPrefs.SetString(LastUpgradeKey, currentVersion); } catch { }
                return;
            }

            if (hadFailures)
            {
                McpLog.Warn("Stdio MCP upgrade encountered errors; will retry next session.");
                return;
            }

            try
            {
                EditorPrefs.SetString(LastUpgradeKey, currentVersion);
            }
            catch { }

            McpLog.Info($"Updated stdio MCP configs to package version {currentVersion}.");
        }

        private static bool ConfigUsesStdIo(McpClient client)
        {
            switch (client.mcpType)
            {
                case McpTypes.Codex:
                    return CodexConfigUsesStdIo(client);
                default:
                    return JsonConfigUsesStdIo(client);
            }
        }

        private static bool JsonConfigUsesStdIo(McpClient client)
        {
            string configPath = McpConfigurationHelper.GetClientConfigPath(client);
            if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
            {
                return false;
            }

            try
            {
                var root = JObject.Parse(File.ReadAllText(configPath));

                JToken unityNode = null;
                if (client.mcpType == McpTypes.VSCode)
                {
                    unityNode = root.SelectToken("servers.unityMCP")
                               ?? root.SelectToken("mcp.servers.unityMCP");
                }
                else
                {
                    unityNode = root.SelectToken("mcpServers.unityMCP");
                }

                if (unityNode == null) return false;

                return unityNode["command"] != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool CodexConfigUsesStdIo(McpClient client)
        {
            try
            {
                string configPath = McpConfigurationHelper.GetClientConfigPath(client);
                if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
                {
                    return false;
                }

                string toml = File.ReadAllText(configPath);
                return CodexConfigHelper.TryParseCodexServer(toml, out var command, out _)
                       && !string.IsNullOrEmpty(command);
            }
            catch
            {
                return false;
            }
        }
    }
}
