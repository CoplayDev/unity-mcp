using System;
using System.IO;
using System.Linq;
using MCPForUnity.Editor.Clients;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Models;
using MCPForUnity.Editor.Services;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

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
                return; // Already processed for this package version
            }

            // Client configuration is no longer supported - just record the version
            try
            {
                EditorPrefs.SetString(LastUpgradeKey, currentVersion);
            }
            catch { }

            McpLog.Info($"Package version {currentVersion} detected. Note: Client configuration is now handled by the MCP client (e.g., Cursor's mcp.json).");
        }

        private static bool ConfigUsesStdIo(McpClient client)
        {
            return JsonConfigUsesStdIo(client);
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
                if (client.IsVsCodeLayout)
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

    }
}
