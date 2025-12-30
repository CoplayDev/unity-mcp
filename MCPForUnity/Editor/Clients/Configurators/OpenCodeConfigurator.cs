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
            string configDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(configDir, ".config", "opencode", "opencode.json");
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

                string json = File.ReadAllText(path);
                var config = JsonConvert.DeserializeObject<JObject>(json);
                var mcpSection = config?["mcp"] as JObject;

                if (mcpSection == null || mcpSection[ServerName] == null)
                {
                    client.SetStatus(McpStatus.NotConfigured);
                    return client.status;
                }

                var unityMcp = mcpSection[ServerName] as JObject;
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
            try
            {
                string path = GetConfigPath();
                string dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                JObject config;
                if (File.Exists(path))
                {
                    string existingJson = File.ReadAllText(path);
                    config = JsonConvert.DeserializeObject<JObject>(existingJson) ?? new JObject();
                }
                else
                {
                    config = new JObject
                    {
                        ["$schema"] = "https://opencode.ai/config.json"
                    };
                }

                if (config["mcp"] == null)
                {
                    config["mcp"] = new JObject();
                }

                var mcpSection = config["mcp"] as JObject;
                string httpUrl = HttpEndpointUtility.GetMcpRpcUrl();

                mcpSection[ServerName] = new JObject
                {
                    ["type"] = "remote",
                    ["url"] = httpUrl,
                    ["enabled"] = true
                };

                string output = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(path, output);

                client.SetStatus(McpStatus.Configured);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to configure OpenCode: {ex.Message}");
            }
        }

        public override string GetManualSnippet()
        {
            string httpUrl = HttpEndpointUtility.GetMcpRpcUrl();
            var snippet = new JObject
            {
                ["mcp"] = new JObject
                {
                    [ServerName] = new JObject
                    {
                        ["type"] = "remote",
                        ["url"] = httpUrl,
                        ["enabled"] = true
                    }
                }
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
    }
}
