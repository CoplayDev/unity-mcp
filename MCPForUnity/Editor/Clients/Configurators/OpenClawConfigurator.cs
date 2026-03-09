using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Models;
using MCPForUnity.Editor.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;

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
        private const string HttpTransportName = "http";
        private const string StdioTransportName = "stdio";
        private const string StdioUrl = "stdio://local";

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

                if (pluginEntry == null || unityServer == null)
                {
                    client.SetStatus(McpStatus.MissingConfig);
                    client.configuredTransport = ConfiguredTransport.Unknown;
                    return client.status;
                }

                if (!IsEnabled(pluginEntry) || !IsEnabled(unityServer))
                {
                    client.SetStatus(McpStatus.NotConfigured);
                    client.configuredTransport = ConfiguredTransport.Unknown;
                    return client.status;
                }

                bool matches = ServerMatchesCurrentEndpoint(unityServer);
                if (matches)
                {
                    client.SetStatus(McpStatus.Configured);
                    client.configuredTransport = ResolveTransport(unityServer);
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
            if (EditorPrefs.GetBool(EditorPrefKeys.LockCursorConfig, false))
                return;

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
            pluginConfig.Remove("timeout");  // removed in openclaw-mcp-bridge v2+
            pluginConfig.Remove("retries");  // removed in openclaw-mcp-bridge v2+
            pluginConfig["servers"] = UpsertUnityServer(pluginConfig["servers"]);

            McpConfigurationHelper.WriteAtomicFile(path, root.ToString(Formatting.Indented));
            client.SetStatus(McpStatus.Configured);
            client.configuredTransport = HttpEndpointUtility.GetCurrentServerTransport();
        }

        public override string GetManualSnippet()
        {
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
            "OpenClaw uses the currently selected MCP for Unity transport (HTTP or stdio)",
            "OpenClaw exposes a proxy tool such as unityMCP__call for Unity MCP access",
            "Restart OpenClaw if the plugin does not hot-reload the new config"
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
            JObject desiredEntry = BuildUnityServerEntry();

            entry.Remove("name");
            entry.Remove("prefix");
            entry.Remove("healthCheck");
            entry.Remove("command");
            entry.Remove("args");
            entry.Remove("env");
            entry.Remove("connectTimeoutMs");

            foreach (var property in desiredEntry.Properties())
            {
                entry[property.Name] = property.Value.DeepClone();
            }

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
            ConfiguredTransport transport = HttpEndpointUtility.GetCurrentServerTransport();
            if (transport == ConfiguredTransport.Stdio)
            {
                var (uvxPath, _, packageName) = AssetPathUtility.GetUvxCommandParts();
                if (string.IsNullOrWhiteSpace(uvxPath))
                {
                    throw new InvalidOperationException("uvx not found. Install uv/uvx or set the override in Advanced Settings.");
                }

                var args = new JArray();
                foreach (string value in AssetPathUtility.GetUvxDevFlagsList())
                {
                    args.Add(value);
                }
                foreach (string value in AssetPathUtility.GetBetaServerFromArgsList())
                {
                    args.Add(value);
                }
                args.Add(packageName);
                args.Add("--transport");
                args.Add("stdio");

                return new JObject
                {
                    ["enabled"] = true,
                    ["url"] = StdioUrl,
                    ["transport"] = StdioTransportName,
                    ["command"] = uvxPath,
                    ["args"] = args,
                    ["toolPrefix"] = ServerName,
                    ["requestTimeoutMs"] = 60000,
                    ["connectTimeoutMs"] = 15000
                };
            }

            return new JObject
            {
                ["enabled"] = true,
                ["url"] = HttpEndpointUtility.GetMcpRpcUrl(),
                ["transport"] = HttpTransportName,
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

            ConfiguredTransport expectedTransport = HttpEndpointUtility.GetCurrentServerTransport();
            ConfiguredTransport configuredTransport = ResolveTransport(server);
            if (configuredTransport != expectedTransport)
            {
                return false;
            }

            if (configuredTransport == ConfiguredTransport.Stdio)
            {
                string configuredUrl = server["url"]?.ToString();
                string command = server["command"]?.ToString();
                if (!UrlsEqual(configuredUrl, StdioUrl) || string.IsNullOrWhiteSpace(command))
                {
                    return false;
                }

                // Validate the --from package source hasn't drifted (e.g. stable vs prerelease switch)
                string[] args = (server["args"] as JArray)?.ToObject<string[]>();
                string configuredSource = McpConfigurationHelper.ExtractUvxUrl(args);
                string expectedSource = GetExpectedPackageSourceForValidation();
                if (!string.IsNullOrEmpty(configuredSource) && !string.IsNullOrEmpty(expectedSource) &&
                    !McpConfigurationHelper.PathsEqual(configuredSource, expectedSource))
                {
                    return false;
                }
            }
            else
            {
                string configuredUrl = server["url"]?.ToString();
                if (string.IsNullOrWhiteSpace(configuredUrl) ||
                    (!UrlsEqual(configuredUrl, HttpEndpointUtility.GetLocalMcpRpcUrl()) &&
                     !UrlsEqual(configuredUrl, HttpEndpointUtility.GetRemoteMcpRpcUrl())))
                {
                    return false;
                }
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

        private ConfiguredTransport ResolveTransport(JObject server)
        {
            string configuredTransport = server?["transport"]?.ToString();
            string configuredUrl = server?["url"]?.ToString();

            if (string.Equals(configuredTransport, StdioTransportName, StringComparison.OrdinalIgnoreCase) ||
                UrlsEqual(configuredUrl, StdioUrl))
            {
                return ConfiguredTransport.Stdio;
            }

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
