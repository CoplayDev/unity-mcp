using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Models;
using Newtonsoft.Json.Linq;

namespace MCPForUnity.Editor.Clients.Configurators
{
    public class ZCodeConfigurator : JsonFileMcpConfigurator
    {
        public ZCodeConfigurator() : base(new McpClient
        {
            name = "ZCode",
            windowsConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".zcode", "cli", "config.json"),
            macConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".zcode", "cli", "config.json"),
            linuxConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".zcode", "cli", "config.json"),
            ServerContainerKey = "mcp.servers",
        })
        { }

        public override bool IsInstalled => ParentDirectoryExists(Path.GetDirectoryName(GetConfigPath()));

        internal string AgentsConfigPath { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".agents", "mcp.json");

        internal static IList<string> GetShadowedServerNames(string nativeConfigPath, string agentsConfigPath)
        {
            try
            {
                if (NativeDefinesServers(nativeConfigPath) || !File.Exists(agentsConfigPath))
                    return new List<string>();
                var agents = JObject.Parse(File.ReadAllText(agentsConfigPath));
                if (!(agents["mcpServers"] is JObject agentsServers)) return new List<string>();

                var names = new List<string>();
                foreach (var server in agentsServers.Properties())
                {
                    if (!string.Equals(server.Name, "unityMCP", StringComparison.OrdinalIgnoreCase))
                        names.Add(server.Name);
                }
                return names;
            }
            catch
            {
                return new List<string>();
            }
        }

        // Configure() rewrites an unreadable native file or a non-object container as a fresh
        // object, so only a parseable `mcp.servers` object with entries counts as defining servers.
        private static bool NativeDefinesServers(string nativeConfigPath)
        {
            try
            {
                return File.Exists(nativeConfigPath)
                    && (JObject.Parse(File.ReadAllText(nativeConfigPath))["mcp"] as JObject)?["servers"] is JObject servers
                    && servers.HasValues;
            }
            catch
            {
                return false;
            }
        }

        public override void Configure()
        {
            var shadowedNames = GetShadowedServerNames(GetConfigPath(), AgentsConfigPath);
            base.Configure();
            if (shadowedNames.Count > 0)
            {
                McpLog.Warn($"ZCode now skips '~/.agents/mcp.json'; these servers will stop loading: {string.Join(", ", shadowedNames)}. "
                    + "To keep them, add them in ZCode Settings > MCP Servers, or merge them into 'mcp.servers' of '~/.zcode/cli/config.json'. "
                    + "ZCode reads '~/.agents/mcp.json' again if the native file defines no servers.");
            }
        }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "Install and launch ZCode to create its '~/.zcode' directory",
            "Click Configure to add UnityMCP to '~/.zcode/cli/config.json'",
            "Open ZCode Settings > MCP Servers and enable UnityMCP",
            "If you keep servers in '~/.agents/mcp.json', ZCode stops reading it once the native config defines a server; add those servers in ZCode Settings > MCP Servers",
            "Restart the ZCode session or reconnect the server if needed"
        };
    }
}
