using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MCPForUnity.Editor.Clients.Configurators
{
    /// <summary>
    /// Configurator for OpenCode (opencode.ai) - a Go-based terminal AI coding assistant.
    /// OpenCode uses ~/.config/opencode/opencode.json with a custom "mcp" format.
    /// </summary>
    public class OpenCodeConfigurator : McpClientConfiguratorBase
    {
        private const string ServerName = "unityMCP";
        private const string SchemaUrl = "https://opencode.ai/config.json";

        public OpenCodeConfigurator() : base(new McpClient
        {
            name = "OpenCode",
            windowsConfigPath = BuildConfigPath(),
            macConfigPath = BuildConfigPath(),
            linuxConfigPath = BuildConfigPath()
        })
        { }

        private static string BuildConfigPath()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".config", "opencode", "opencode.json");
        }

        public override string GetConfigPath() => CurrentOsPath();

        public override McpStatus CheckStatus(bool attemptAutoRewrite = true)
        {
            try
            {
                string path = GetConfigPath();
                if (!File.Exists(path))
                {
                    client.SetStatus(McpStatus.NotConfigured);
                    return client.status;
                }

                var config = JsonConvert.DeserializeObject<JObject>(File.ReadAllText(path));
                var unityMcp = config?["mcp"]?[ServerName] as JObject;

                if (unityMcp == null)
                {
                    client.SetStatus(McpStatus.NotConfigured);
                    return client.status;
                }

                string configuredUrl = unityMcp["url"]?.ToString();
                string expectedUrl = HttpEndpointUtility.GetMcpRpcUrl();

                if (UrlsEqual(configuredUrl, expectedUrl))
                {
                    client.SetStatus(McpStatus.Configured);
                }
                else if (attemptAutoRewrite)
                {
                    Configure();
                }
                else
                {
                    client.SetStatus(McpStatus.IncorrectPath);
                }
            }
            catch (Exception ex)
            {
                client.SetStatus(McpStatus.Error, ex.Message);
            }

            return client.status;
        }

        public override void Configure()
        {
            string path = GetConfigPath();
            McpConfigurationHelper.EnsureConfigDirectoryExists(path);

            JObject config = File.Exists(path)
                ? JsonConvert.DeserializeObject<JObject>(File.ReadAllText(path)) ?? new JObject()
                : new JObject { ["$schema"] = SchemaUrl };

            var mcpSection = config["mcp"] as JObject ?? new JObject();
            config["mcp"] = mcpSection;

            mcpSection[ServerName] = BuildServerEntry();

            McpConfigurationHelper.WriteAtomicFile(path, JsonConvert.SerializeObject(config, Formatting.Indented));
            client.SetStatus(McpStatus.Configured);
        }

        public override string GetManualSnippet()
        {
            var snippet = new JObject
            {
                ["mcp"] = new JObject { [ServerName] = BuildServerEntry() }
            };
            return JsonConvert.SerializeObject(snippet, Formatting.Indented);
        }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "Install OpenCode (https://opencode.ai)",
            "Click Configure to add Unity MCP to ~/.config/opencode/opencode.json",
            "Restart OpenCode",
            "The Unity MCP server should be detected automatically"
        };

        private static JObject BuildServerEntry() => new JObject
        {
            ["type"] = "remote",
            ["url"] = HttpEndpointUtility.GetMcpRpcUrl(),
            ["enabled"] = true
        };
    }
}
