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
            string xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            string configBase = !string.IsNullOrEmpty(xdgConfigHome)
                ? xdgConfigHome
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            return Path.Combine(configBase, "opencode", "opencode.json");
        }

        public override string GetConfigPath() => CurrentOsPath();

        /// <summary>
        /// Attempts to load and parse the config file.
        /// Returns null if file doesn't exist or cannot be read.
        /// Returns parsed JObject if valid JSON found.
        /// Logs warning if file exists but contains malformed JSON.
        /// </summary>
        private JObject TryLoadConfig(string path)
        {
            if (!File.Exists(path))
                return null;

            string content;
            try
            {
                content = File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[OpenCodeConfigurator] Failed to read config file {path}: {ex.Message}");
                return null;
            }

            try
            {
                return JsonConvert.DeserializeObject<JObject>(content) ?? new JObject();
            }
            catch (JsonException ex)
            {
                // Malformed JSON - log warning and return null.
                // When Configure() receives null, it will do: TryLoadConfig(path) ?? new JObject()
                // This creates a fresh empty JObject, which replaces the entire file with only the unityMCP section.
                // Existing config sections are lost. To preserve sections, a different recovery strategy
                // (e.g., line-by-line parsing, JSON repair, or manual user intervention) would be needed.
                UnityEngine.Debug.LogWarning($"[OpenCodeConfigurator] Malformed JSON in {path}: {ex.Message}");
                return null;
            }
        }

        public override McpStatus CheckStatus(bool attemptAutoRewrite = true)
        {
            try
            {
                string path = GetConfigPath();
                var config = TryLoadConfig(path);

                if (config == null)
                {
                    client.SetStatus(McpStatus.NotConfigured);
                    return client.status;
                }

                var unityMcp = config["mcp"]?[ServerName] as JObject;

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
                McpConfigurationHelper.EnsureConfigDirectoryExists(path);

                // Load existing config or start fresh, preserving all other properties and MCP servers
                var config = TryLoadConfig(path) ?? new JObject();

                // Only add $schema if creating a new file
                if (!File.Exists(path))
                {
                    config["$schema"] = SchemaUrl;
                }

                // Preserve existing mcp section and only update our server entry
                var mcpSection = config["mcp"] as JObject ?? new JObject();
                config["mcp"] = mcpSection;

                mcpSection[ServerName] = BuildServerEntry();

                McpConfigurationHelper.WriteAtomicFile(path, JsonConvert.SerializeObject(config, Formatting.Indented));
                client.SetStatus(McpStatus.Configured);
            }
            catch (Exception ex)
            {
                client.SetStatus(McpStatus.Error, ex.Message);
            }
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
