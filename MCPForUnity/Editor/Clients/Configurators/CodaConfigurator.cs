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
    /// Configurator for Coda CLI (Globant CODA Coding Agent).
    /// Coda stores individual MCP server configs in ~/.coda/.tools/mcp/servers.d/ as separate JSON files.
    /// Supports both stdio and HTTP transports.
    /// </summary>
    public class CodaConfigurator : McpClientConfiguratorBase
    {
        private const string ServerFileName = "unity-mcp.json";

        public CodaConfigurator() : base(new McpClient
        {
            name = "Coda",
            windowsConfigPath = BuildConfigPath(),
            macConfigPath = BuildConfigPath(),
            linuxConfigPath = BuildConfigPath(),
            SupportsHttpTransport = true
        })
        { }

        public override bool SupportsSkills => true;

        public override string GetSkillInstallPath()
        {
            var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(userHome, ".coda", "skills", "unity-mcp-skill");
        }

        private static string BuildConfigPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".coda", ".tools", "mcp", "servers.d", ServerFileName);
        }

        public override string GetConfigPath() => CurrentOsPath();

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
                UnityEngine.Debug.LogWarning($"[CodaConfigurator] Failed to read config file {path}: {ex.Message}");
                return null;
            }

            try
            {
                return JsonConvert.DeserializeObject<JObject>(content) ?? new JObject();
            }
            catch (JsonException ex)
            {
                UnityEngine.Debug.LogWarning($"[CodaConfigurator] Malformed JSON in {path}: {ex.Message}");
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

                // Check HTTP configuration
                string configuredUrl = config["url"]?.ToString();
                if (!string.IsNullOrEmpty(configuredUrl))
                {
                    string expectedUrl = HttpEndpointUtility.GetMcpRpcUrl();
                    if (UrlsEqual(configuredUrl, expectedUrl))
                    {
                        client.SetStatus(McpStatus.Configured);
                        client.configuredTransport = ConfiguredTransport.Http;
                    }
                    else if (attemptAutoRewrite)
                    {
                        Configure();
                    }
                    else
                    {
                        client.SetStatus(McpStatus.IncorrectPath);
                    }
                    return client.status;
                }

                // Check stdio configuration
                string command = config["command"]?.ToString();
                if (!string.IsNullOrEmpty(command))
                {
                    string[] args = config["args"]?.ToObject<string[]>();
                    string expectedSource = GetExpectedPackageSourceForValidation();
                    string configuredSource = McpConfigurationHelper.ExtractUvxUrl(args);

                    if (configuredSource != null && expectedSource != null &&
                        McpConfigurationHelper.PathsEqual(configuredSource, expectedSource))
                    {
                        client.SetStatus(McpStatus.Configured);
                        client.configuredTransport = ConfiguredTransport.Stdio;
                    }
                    else if (attemptAutoRewrite)
                    {
                        Configure();
                    }
                    else
                    {
                        client.SetStatus(McpStatus.VersionMismatch,
                            $"Configured source: {configuredSource ?? "(none)"}, expected: {expectedSource}");
                    }
                    return client.status;
                }

                // File exists but doesn't have the expected shape
                if (attemptAutoRewrite)
                {
                    Configure();
                }
                else
                {
                    client.SetStatus(McpStatus.NotConfigured);
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

                var serverEntry = BuildServerEntry();
                McpConfigurationHelper.WriteAtomicFile(path, JsonConvert.SerializeObject(serverEntry, Formatting.Indented));
                client.SetStatus(McpStatus.Configured);
            }
            catch (Exception ex)
            {
                client.SetStatus(McpStatus.Error, ex.Message);
            }
        }

        public override string GetManualSnippet()
        {
            return JsonConvert.SerializeObject(BuildServerEntry(), Formatting.Indented);
        }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "Install Coda CLI (Globant CODA Coding Agent)",
            "Click Configure to create the server config in ~/.coda/.tools/mcp/servers.d/",
            "Restart Coda CLI",
            "The Unity MCP server should be detected automatically"
        };

        private JObject BuildServerEntry()
        {
            bool useHttp = EditorConfigurationCache.Instance.UseHttpTransport;

            if (useHttp)
            {
                return new JObject
                {
                    ["url"] = HttpEndpointUtility.GetMcpRpcUrl(),
                    ["enabled"] = true
                };
            }

            string uvxPath = GetUvxPathOrError();
            string packageSource = AssetPathUtility.GetMcpServerPackageSource();

            return new JObject
            {
                ["command"] = uvxPath,
                ["args"] = new JArray("--from", packageSource, "mcpforunityserver"),
                ["enabled"] = true
            };
        }
    }
}
