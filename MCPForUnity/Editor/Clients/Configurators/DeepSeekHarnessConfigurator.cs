using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Models;
using MCPForUnity.Editor.Services;
using UnityEditor;

namespace MCPForUnity.Editor.Clients.Configurators
{
    /// <summary>
    /// Configurator for the DeepSeek Harness (<c>dsh</c> CLI). DSH mounts external MCP
    /// servers as Cordis rows for its <c>@deepseek-ai/dsh-mcp-client</c> plugin, stored
    /// in the user's YAML patch layer — by default <c>$DSH_HOME/cordis.patch.yml</c>
    /// (with <c>$DSH_HOME</c> defaulting to <c>~/.dsh</c>), which applies to every DSH
    /// profile on the machine. The row is written as a marker-fenced block managed by
    /// <see cref="DshConfigHelper"/> so unrelated user patches are never parsed or
    /// rewritten. After a DSH reload/restart, tools surface as
    /// <c>mcp__unityMCP__&lt;tool&gt;</c>.
    /// </summary>
    public class DeepSeekHarnessConfigurator : McpClientConfiguratorBase
    {
        public DeepSeekHarnessConfigurator() : this(null) { }

        internal DeepSeekHarnessConfigurator(string dshHomeOverride)
            : base(new McpClient
            {
                name = "DeepSeek Harness",
                windowsConfigPath = BuildConfigPath(dshHomeOverride),
                macConfigPath = BuildConfigPath(dshHomeOverride),
                linuxConfigPath = BuildConfigPath(dshHomeOverride)
            })
        {
        }

        private static string BuildConfigPath(string dshHomeOverride)
        {
            return Path.Combine(ResolveDshHome(dshHomeOverride), "cordis.patch.yml");
        }

        /// <summary>
        /// Resolves the DSH home directory: an explicit override wins, then the
        /// <c>DSH_HOME</c> environment variable, then <c>~/.dsh</c> (DSH's own default).
        /// </summary>
        internal static string ResolveDshHome(string dshHomeOverride)
        {
            if (!string.IsNullOrEmpty(dshHomeOverride)) return dshHomeOverride;

            string envHome = Environment.GetEnvironmentVariable("DSH_HOME");
            if (!string.IsNullOrEmpty(envHome)) return envHome;

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".dsh");
        }

        public override string GetConfigPath() => CurrentOsPath();

        public override string GetConfigureActionLabel()
            => client.status == McpStatus.Configured ? "Unregister" : "Configure";

        public override McpStatus CheckStatus(bool attemptAutoRewrite = true)
        {
            try
            {
                string path = GetConfigPath();
                string fileText = File.Exists(path) ? File.ReadAllText(path) : null;
                string block = fileText == null ? null : DshConfigHelper.ExtractManagedBlock(fileText);

                if (block == null)
                {
                    client.SetStatus(McpStatus.NotConfigured);
                    client.configuredTransport = Models.ConfiguredTransport.Unknown;
                    if (attemptAutoRewrite)
                    {
                        Configure();
                    }
                    return client.status;
                }

                bool parsed = DshConfigHelper.TryParseManagedBlock(
                    block, out string transport, out string url, out string command, out string[] args,
                    out _, out _);
                if (!parsed)
                {
                    // Block exists but is not in our known shape (hand-edited) — rewrite it.
                    client.SetStatus(McpStatus.IncorrectPath);
                    client.configuredTransport = Models.ConfiguredTransport.Unknown;
                    if (attemptAutoRewrite)
                    {
                        Configure();
                    }
                    return client.status;
                }

                ConfiguredTransport current = HttpEndpointUtility.GetCurrentServerTransport();
                bool blockIsHttp = string.Equals(transport, "streamable-http", StringComparison.OrdinalIgnoreCase);

                if (blockIsHttp && current != Models.ConfiguredTransport.Stdio)
                {
                    client.configuredTransport = HttpEndpointUtility.IsRemoteScope()
                        ? Models.ConfiguredTransport.HttpRemote
                        : Models.ConfiguredTransport.Http;

                    if (UrlsEqual(url, HttpEndpointUtility.GetMcpRpcUrl()))
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
                    return client.status;
                }

                if (!blockIsHttp && current == Models.ConfiguredTransport.Stdio)
                {
                    client.configuredTransport = Models.ConfiguredTransport.Stdio;

                    string expected = GetExpectedPackageSourceForValidation();
                    string configured = McpConfigurationHelper.ExtractUvxUrl(args);
                    if (!string.IsNullOrEmpty(configured) && !string.IsNullOrEmpty(expected)
                        && McpConfigurationHelper.PathsEqual(configured, expected))
                    {
                        client.SetStatus(McpStatus.Configured);
                    }
                    else if (attemptAutoRewrite)
                    {
                        Configure();
                    }
                    else if (IsBetaPackageSource(configured) != IsBetaPackageSource(expected))
                    {
                        client.SetStatus(McpStatus.VersionMismatch, IsBetaPackageSource(configured)
                            ? "Configured for prerelease server, but this package is stable. Re-configure to switch to stable."
                            : "Configured for stable server, but this package is prerelease. Re-configure to switch to prerelease.");
                    }
                    else
                    {
                        client.SetStatus(McpStatus.IncorrectPath);
                    }
                    return client.status;
                }

                // Block transport disagrees with the plugin's current transport preference.
                client.configuredTransport = blockIsHttp
                    ? Models.ConfiguredTransport.Http
                    : Models.ConfiguredTransport.Stdio;
                if (attemptAutoRewrite)
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
                client.configuredTransport = Models.ConfiguredTransport.Unknown;
            }

            return client.status;
        }

        public override void Configure()
        {
            string path = GetConfigPath();
            McpConfigurationHelper.EnsureConfigDirectoryExists(path);

            string fileText = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
            string block = BuildBlock();
            string updated = DshConfigHelper.UpsertManagedBlock(fileText, block);
            McpConfigurationHelper.WriteAtomicFile(path, updated);

            client.SetStatus(McpStatus.Configured);
            client.configuredTransport = HttpEndpointUtility.GetCurrentServerTransport();
        }

        public override void Unregister()
        {
            string path = GetConfigPath();
            if (!File.Exists(path))
            {
                client.SetStatus(McpStatus.NotConfigured);
                client.configuredTransport = Models.ConfiguredTransport.Unknown;
                return;
            }

            string fileText = File.ReadAllText(path);
            string updated = DshConfigHelper.RemoveManagedBlock(fileText);
            if (!string.Equals(updated, fileText, StringComparison.Ordinal))
            {
                McpConfigurationHelper.WriteAtomicFile(path, updated);
            }

            client.SetStatus(McpStatus.NotConfigured);
            client.configuredTransport = Models.ConfiguredTransport.Unknown;
        }

        public override string GetManualSnippet()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# Mount the Unity MCP server in DeepSeek Harness.");
                sb.AppendLine("# Managed block for $DSH_HOME/cordis.patch.yml ($DSH_HOME defaults to ~/.dsh;");
                sb.AppendLine("# the home patch applies to every profile). Re-run Configure to refresh it.");
                sb.Append(BuildBlock());
                sb.AppendLine();
                sb.AppendLine("# Per-profile alternative instead of the home patch:");
                sb.AppendLine("#   $DSH_HOME/profiles/<name>/cordis.patch.yml");
                sb.AppendLine("# One-shot alternative (no persistence):");
                sb.AppendLine("#   dsh web --patch <file-containing-the-block-above>");
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"# error: {ex.Message}";
            }
        }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "Install DeepSeek Harness and run any 'dsh' command once (creates ~/.dsh)",
            "Click Configure to add UnityMCP to the DSH cordis patch layer",
            "Restart DSH (or reload its configuration) so the new MCP server mounts",
            "Unity tools appear as mcp__unityMCP__<tool>"
        };

        private string BuildBlock()
        {
            var transport = HttpEndpointUtility.GetCurrentServerTransport();
            bool useHttp = transport != Models.ConfiguredTransport.Stdio;

            string httpUrl = null;
            string uvxCommand = null;
            string[] uvxArgs = null;
            string apiKey = null;
            string systemRoot = null;

            if (useHttp)
            {
                httpUrl = HttpEndpointUtility.GetMcpRpcUrl();
                if (HttpEndpointUtility.IsRemoteScope())
                {
                    apiKey = EditorPrefs.GetString(EditorPrefKeys.ApiKey, string.Empty);
                }
            }
            else
            {
                var (uvxPath, _, packageName) = AssetPathUtility.GetUvxCommandParts();
                if (string.IsNullOrWhiteSpace(uvxPath))
                {
                    throw new InvalidOperationException("uvx not found. Install uv/uvx or set the override in Advanced Settings.");
                }

                var args = new List<string>();
                foreach (string flag in AssetPathUtility.GetUvxDevFlagsList())
                {
                    args.Add(flag);
                }
                foreach (string arg in AssetPathUtility.GetBetaServerFromArgsList())
                {
                    args.Add(arg);
                }
                args.Add(packageName);
                args.Add("--transport");
                args.Add("stdio");
                uvxArgs = args.ToArray();
                uvxCommand = uvxPath;

                var platformService = MCPServiceLocator.Platform;
                if (platformService.IsWindows())
                {
                    systemRoot = platformService.GetSystemRoot();
                }
            }

            return DshConfigHelper.BuildUnityMcpBlock(
                useHttp,
                httpUrl,
                uvxCommand,
                uvxArgs,
                string.IsNullOrEmpty(apiKey) ? null : AuthConstants.ApiKeyHeader,
                apiKey,
                systemRoot);
        }
    }
}
