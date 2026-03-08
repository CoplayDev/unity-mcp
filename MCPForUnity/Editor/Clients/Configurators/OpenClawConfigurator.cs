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
        private const string TransportName = "http";

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
                JObject pluginEntry = root["plugins"]?["entries"]?[PluginName] as JObject;
                JObject unityServer = FindUnityServer(pluginEntry?["config"]?["servers"]);

                if (pluginEntry == null || unityServer == null || !IsEnabled(pluginEntry) || !IsEnabled(unityServer))
                {
                    client.SetStatus(McpStatus.NotConfigured);
                    client.configuredTransport = ConfiguredTransport.Unknown;
                    return client.status;
                }

                bool matches = ServerMatchesCurrentEndpoint(unityServer);
                if (matches)
                {
                    client.SetStatus(McpStatus.Configured);
                    client.configuredTransport = ResolveTransport(unityServer["url"]?.ToString());
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
            pluginConfig.Remove("timeout");
            pluginConfig.Remove("retries");
            pluginConfig["servers"] = UpsertUnityServer(pluginConfig["servers"]);

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
                                ["servers"] = new JObject
                                {
                                    [ServerName] = BuildUnityServerEntry()
                                }
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
            "OpenClaw exposes a proxy tool such as unityMCP__call for Unity MCP access",
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

        private JObject FindUnityServer(JToken serversToken)
        {
            if (serversToken is JObject serverMap)
            {
                return serverMap[ServerName] as JObject;
            }

            if (serversToken is JArray legacyServers)
            {
                foreach (JToken token in legacyServers)
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
            }

            return null;
        }

        private JObject UpsertUnityServer(JToken serversToken)
        {
            JObject servers = NormalizeServers(serversToken);
            JObject entry = servers[ServerName] as JObject ?? new JObject();

            entry.Remove("name");
            entry.Remove("prefix");
            entry.Remove("healthCheck");
            entry["enabled"] = true;
            entry["url"] = HttpEndpointUtility.GetMcpRpcUrl();
            entry["transport"] = TransportName;
            entry["toolPrefix"] = ServerName;
            servers[ServerName] = entry;

            return servers;
        }

        private static JObject NormalizeServers(JToken serversToken)
        {
            if (serversToken is JObject serverMap)
            {
                return serverMap;
            }

            var normalized = new JObject();
            if (!(serversToken is JArray legacyServers))
            {
                return normalized;
            }

            foreach (JToken token in legacyServers)
            {
                if (!(token is JObject legacyServer))
                {
                    continue;
                }

                string name = legacyServer["name"]?.ToString();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                normalized[name] = legacyServer;
            }

            return normalized;
        }

        private static JObject BuildUnityServerEntry()
        {
            return new JObject
            {
                ["enabled"] = true,
                ["url"] = HttpEndpointUtility.GetMcpRpcUrl(),
                ["transport"] = TransportName,
                ["toolPrefix"] = ServerName,
                ["requestTimeoutMs"] = 30000
            };
        }

        private bool ServerMatchesCurrentEndpoint(JObject server)
        {
            if (server == null)
            {
                return false;
            }

            string configuredUrl = server["url"]?.ToString();
            if (string.IsNullOrWhiteSpace(configuredUrl) ||
                (!UrlsEqual(configuredUrl, HttpEndpointUtility.GetLocalMcpRpcUrl()) &&
                 !UrlsEqual(configuredUrl, HttpEndpointUtility.GetRemoteMcpRpcUrl())))
            {
                return false;
            }

            string configuredTransport = server["transport"]?.ToString();
            if (!string.IsNullOrWhiteSpace(configuredTransport) &&
                !string.Equals(configuredTransport, TransportName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string toolPrefix = server["toolPrefix"]?.ToString();
            return string.IsNullOrWhiteSpace(toolPrefix) ||
                   string.Equals(toolPrefix, ServerName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsEnabled(JObject entry)
        {
            JToken enabledToken = entry["enabled"];
            return enabledToken == null || enabledToken.Type != JTokenType.Boolean || enabledToken.Value<bool>();
        }

        private ConfiguredTransport ResolveTransport(string configuredUrl)
        {
            if (UrlsEqual(configuredUrl, HttpEndpointUtility.GetRemoteMcpRpcUrl()))
            {
                return ConfiguredTransport.HttpRemote;
            }

            if (UrlsEqual(configuredUrl, HttpEndpointUtility.GetLocalMcpRpcUrl()))
            {
                return ConfiguredTransport.Http;
            }

            return ConfiguredTransport.Unknown;
        }
    }
}
