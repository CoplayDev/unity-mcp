using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Models;
using MCPForUnity.Editor.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MCPForUnity.Editor.Clients.Configurators
{
    /// <summary>
    /// Configurator for OpenClaw via the openclaw-mcp-bridge plugin.
    /// OpenClaw stores config at ~/.openclaw/openclaw.json.
    /// </summary>
    public class OpenClawConfigurator : McpClientConfiguratorBase
    {
        private const string PluginName = "openclaw-mcp-bridge";
        private const string ServerName = "unityMCP";

        public OpenClawConfigurator() : base(new McpClient
        {
            name = "OpenClaw",
            windowsConfigPath = BuildConfigPath(),
            macConfigPath = BuildConfigPath(),
            linuxConfigPath = BuildConfigPath()
        })
        { }

        private static string BuildConfigPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".openclaw",
                "openclaw.json");
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
                    client.configuredTransport = ConfiguredTransport.Unknown;
                    return client.status;
                }

                JObject root = LoadConfig(path);
                JObject unityServer = FindUnityServer(root);

                if (unityServer == null)
                {
                    client.SetStatus(McpStatus.NotConfigured);
                    client.configuredTransport = ConfiguredTransport.Unknown;
                    return client.status;
                }

                string configuredUrl = unityServer["url"]?.ToString();
                bool matches = UrlMatchesCurrentEndpoint(configuredUrl);
                if (matches)
                {
                    client.SetStatus(McpStatus.Configured);
                    client.configuredTransport = ResolveTransport(configuredUrl);
                    return client.status;
                }

                if (attemptAutoRewrite)
                {
                    Configure();
                }
                else
                {
                    client.SetStatus(McpStatus.IncorrectPath);
                    client.configuredTransport = ConfiguredTransport.Unknown;
                }
            }
            catch (Exception ex)
            {
                client.SetStatus(McpStatus.Error, ex.Message);
                client.configuredTransport = ConfiguredTransport.Unknown;
            }

            return client.status;
        }

        public override void Configure()
        {
            if (!EditorConfigurationCache.Instance.UseHttpTransport)
            {
                throw new InvalidOperationException(
                    "OpenClaw uses HTTP MCP via openclaw-mcp-bridge. Switch transport to HTTP in MCP for Unity settings first.");
            }

            string path = GetConfigPath();
            McpConfigurationHelper.EnsureConfigDirectoryExists(path);

            JObject root = File.Exists(path) ? LoadConfig(path) : new JObject();

            JObject plugins = root["plugins"] as JObject ?? new JObject();
            root["plugins"] = plugins;

            JObject entries = plugins["entries"] as JObject ?? new JObject();
            plugins["entries"] = entries;

            JObject pluginEntry = entries[PluginName] as JObject ?? new JObject();
            entries[PluginName] = pluginEntry;
            pluginEntry["enabled"] = true;

            JObject pluginConfig = pluginEntry["config"] as JObject ?? new JObject();
            pluginEntry["config"] = pluginConfig;
            pluginConfig["servers"] = UpsertUnityServer(pluginConfig["servers"] as JArray ?? new JArray());

            if (pluginConfig["timeout"] == null)
            {
                pluginConfig["timeout"] = 30000;
            }

            if (pluginConfig["retries"] == null)
            {
                pluginConfig["retries"] = 1;
            }

            McpConfigurationHelper.WriteAtomicFile(path, root.ToString(Formatting.Indented));
            client.SetStatus(McpStatus.Configured);
            client.configuredTransport = HttpEndpointUtility.GetCurrentServerTransport();
        }

        public override string GetManualSnippet()
        {
            if (!EditorConfigurationCache.Instance.UseHttpTransport)
            {
                return "# OpenClaw integration requires HTTP transport.\n"
                    + "# Switch to HTTP in MCP for Unity settings, then configure OpenClaw again.";
            }

            JObject snippet = new JObject
            {
                ["plugins"] = new JObject
                {
                    ["entries"] = new JObject
                    {
                        [PluginName] = new JObject
                        {
                            ["enabled"] = true,
                            ["config"] = new JObject
                            {
                                ["servers"] = new JArray(BuildUnityServerEntry()),
                                ["timeout"] = 30000,
                                ["retries"] = 1
                            }
                        }
                    }
                }
            };

            return snippet.ToString(Formatting.Indented);
        }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "Install OpenClaw",
            "Install the bridge plugin: npm install -g openclaw-mcp-bridge (or pnpm add -g openclaw-mcp-bridge)",
            "In MCP for Unity, choose OpenClaw and click Configure",
            "Restart OpenClaw"
        };

        private JObject LoadConfig(string path)
        {
            string text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text))
            {
                return new JObject();
            }

            try
            {
                return JsonConvert.DeserializeObject<JObject>(text) ?? new JObject();
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    $"OpenClaw config contains non-JSON content and cannot be safely auto-edited: {ex.Message}");
            }
        }

        private JObject FindUnityServer(JObject root)
        {
            JArray servers = root["plugins"]?["entries"]?[PluginName]?["config"]?["servers"] as JArray;
            if (servers == null)
            {
                return null;
            }

            foreach (JToken token in servers)
            {
                JObject server = token as JObject;
                if (server == null)
                {
                    continue;
                }

                string name = server["name"]?.ToString();
                if (string.Equals(name, ServerName, StringComparison.OrdinalIgnoreCase))
                {
                    return server;
                }
            }

            return null;
        }

        private JArray UpsertUnityServer(JArray servers)
        {
            JObject existing = null;
            foreach (JToken token in servers)
            {
                JObject candidate = token as JObject;
                if (candidate == null)
                {
                    continue;
                }

                string name = candidate["name"]?.ToString();
                if (string.Equals(name, ServerName, StringComparison.OrdinalIgnoreCase))
                {
                    existing = candidate;
                    break;
                }
            }

            JObject entry = existing ?? BuildUnityServerEntry();
            entry["name"] = ServerName;
            entry["url"] = HttpEndpointUtility.GetBaseUrl();
            entry["prefix"] = "unity";
            entry["healthCheck"] = true;

            if (existing == null)
            {
                servers.Add(entry);
            }

            return servers;
        }

        private static JObject BuildUnityServerEntry()
        {
            return new JObject
            {
                ["name"] = ServerName,
                ["url"] = HttpEndpointUtility.GetBaseUrl(),
                ["prefix"] = "unity",
                ["healthCheck"] = true
            };
        }

        private bool UrlMatchesCurrentEndpoint(string configuredUrl)
        {
            if (string.IsNullOrWhiteSpace(configuredUrl))
            {
                return false;
            }

            string baseUrl = HttpEndpointUtility.GetBaseUrl();
            string rpcUrl = HttpEndpointUtility.GetMcpRpcUrl();
            return UrlsEqual(configuredUrl, baseUrl) || UrlsEqual(configuredUrl, rpcUrl);
        }

        private ConfiguredTransport ResolveTransport(string configuredUrl)
        {
            if (UrlsEqual(configuredUrl, HttpEndpointUtility.GetRemoteBaseUrl()) ||
                UrlsEqual(configuredUrl, HttpEndpointUtility.GetRemoteMcpRpcUrl()))
            {
                return ConfiguredTransport.HttpRemote;
            }

            if (UrlsEqual(configuredUrl, HttpEndpointUtility.GetLocalBaseUrl()) ||
                UrlsEqual(configuredUrl, HttpEndpointUtility.GetLocalMcpRpcUrl()))
            {
                return ConfiguredTransport.Http;
            }

            return ConfiguredTransport.Unknown;
        }
    }
}
