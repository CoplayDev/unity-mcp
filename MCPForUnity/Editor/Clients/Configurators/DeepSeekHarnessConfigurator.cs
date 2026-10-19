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
    /// DeepSeek Harness (<c>dsh</c>) has no client config file. MCP servers are mounted
    /// through its plugin manager as a configuration-only bundle (a manifest plus a
    /// <c>cordis.patch.yml</c> row for <c>@deepseek-ai/dsh-mcp-client</c>), so setup is
    /// manual like the other UI-driven clients: the snippet below holds the two bundle
    /// files and the <c>dsh plugin add</c> command that installs them. After a DSH
    /// reload the tools surface as <c>mcp__unityMCP__&lt;tool&gt;</c>. From there DSH's
    /// own plugin manager owns enable/disable and removal.
    /// </summary>
    public class DeepSeekHarnessConfigurator : McpClientConfiguratorBase
    {
        public const string ClientName = "DeepSeek Harness";

        private const string ServerName = "unityMCP";
        private const string BundleName = "@local/unity-mcp-dsh";

        public DeepSeekHarnessConfigurator() : base(new McpClient
        {
            name = ClientName,
            windowsConfigPath = BundlePatchPath(),
            macConfigPath = BundlePatchPath(),
            linuxConfigPath = BundlePatchPath()
        })
        { }

        public override bool SupportsAutoConfigure => false;

        /// <summary>DSH is installed once it has run at least once (its home directory exists).</summary>
        public override bool IsInstalled => Directory.Exists(DshHome());

        public override string GetConfigPath() => BundlePatchPath();

        public override McpStatus CheckStatus(bool attemptAutoRewrite = true)
        {
            client.SetStatus(McpStatus.NotConfigured, "DeepSeek Harness setup is manual; DSH's plugin manager owns the bundle.");
            return client.status;
        }

        public override void Configure()
        {
            throw new InvalidOperationException(
                "DeepSeek Harness setup is manual. Use the Manual Configuration snippet below: " +
                "it holds the two bundle files and the 'dsh plugin add' command to run.");
        }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "Install DeepSeek Harness and run any 'dsh' command once (creates ~/.dsh)",
            "Create the two files shown in the Manual Configuration snippet below",
            "Run the 'dsh plugin add' command from the snippet",
            "Restart DeepSeek Harness; Unity tools appear as mcp__unityMCP__<tool>",
            "DSH's plugin manager can enable/disable or remove the bundle afterwards"
        };

        public override string GetManualSnippet()
        {
            try
            {
                string bundleDir = BundleDir();
                string profile = ProfileName();
                var sb = new StringBuilder();
                sb.AppendLine("# DeepSeek Harness setup. $DSH_HOME defaults to ~/.dsh;");
                sb.AppendLine("# the profile below is 'desktop', swap it if you use another one.");
                sb.AppendLine();
                sb.AppendLine("# --- " + bundleDir + "/package.json ---");
                sb.AppendLine("{");
                sb.AppendLine("  \"name\": \"@local/unity-mcp-dsh\",");
                sb.AppendLine("  \"version\": \"1.0.0\",");
                sb.AppendLine("  \"private\": true,");
                sb.AppendLine("  \"type\": \"module\",");
                sb.AppendLine("  \"dsh\": { \"bundle\": { \"patch\": \"./cordis.patch.yml\" } }");
                sb.AppendLine("}");
                sb.AppendLine();
                sb.AppendLine("# --- " + bundleDir + "/cordis.patch.yml ---");
                sb.Append(Row());
                sb.AppendLine();
                sb.AppendLine("# Install:");
                sb.AppendLine("#   dsh plugin --profile " + ShellQuote(profile) + " add " + ShellQuote(bundleDir));
                sb.AppendLine("# Remove:");
                sb.AppendLine("#   dsh plugin --profile " + ShellQuote(profile) + " remove " + ShellQuote(BundleName));
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return "# error: " + ex.Message;
            }
        }

        /// <summary>Renders the patch row for the transport currently selected in Unity.</summary>
        private static string Row()
        {
            bool useHttp = EditorConfigurationCache.Instance.UseHttpTransport;
            var sb = new StringBuilder();
            sb.AppendLine("# >>> MCP for Unity (unityMCP) managed block >>>");
            sb.AppendLine("- insert:");
            sb.AppendLine("    - id: unitymcp");
            sb.AppendLine("      name: '@deepseek-ai/dsh-mcp-client'");
            sb.AppendLine("      config:");
            sb.AppendLine("        serverName: " + ServerName);

            if (useHttp)
            {
                sb.AppendLine("        transport: streamable-http");
                sb.AppendLine("        url: " + YamlQuote(HttpEndpointUtility.GetMcpRpcUrl()));
                if (HttpEndpointUtility.IsRemoteScope())
                {
                    string apiKey = EditorPrefs.GetString(EditorPrefKeys.ApiKey, string.Empty);
                    if (!string.IsNullOrEmpty(apiKey))
                    {
                        sb.AppendLine("        headers:");
                        sb.AppendLine("          " + AuthConstants.ApiKeyHeader + ": " + YamlQuote(apiKey));
                    }
                }
            }
            else
            {
                var (uvxPath, _, packageName) = AssetPathUtility.GetUvxCommandParts();
                if (string.IsNullOrWhiteSpace(uvxPath))
                {
                    throw new InvalidOperationException("uvx not found. Install uv/uvx or set the override in Advanced Settings.");
                }

                sb.AppendLine("        transport: stdio");
                sb.AppendLine("        command: " + YamlQuote(uvxPath));
                sb.AppendLine("        args:");
                foreach (string flag in AssetPathUtility.GetUvxDevFlagsList())
                {
                    sb.AppendLine("          - " + YamlQuote(flag));
                }
                foreach (string arg in AssetPathUtility.GetBetaServerFromArgsList())
                {
                    sb.AppendLine("          - " + YamlQuote(arg));
                }
                sb.AppendLine("          - " + YamlQuote(packageName));
                sb.AppendLine("          - " + YamlQuote("--transport"));
                sb.AppendLine("          - " + YamlQuote("stdio"));
                if (MCPServiceLocator.Platform.IsWindows())
                {
                    sb.AppendLine("        env:");
                    sb.AppendLine("          SystemRoot: " + YamlQuote(MCPServiceLocator.Platform.GetSystemRoot()));
                }
            }

            sb.AppendLine("        failOnStartupError: true");
            sb.AppendLine("# <<< MCP for Unity (unityMCP) <<<");
            return sb.ToString();
        }

        /// <summary>DSH home: <c>$DSH_HOME</c> when set, else <c>~/.dsh</c>.</summary>
        private static string DshHome()
        {
            string envHome = Environment.GetEnvironmentVariable("DSH_HOME");
            if (!string.IsNullOrEmpty(envHome)) return envHome;

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return string.IsNullOrEmpty(home) ? string.Empty : Path.Combine(home, ".dsh");
        }

        /// <summary>Target profile: <c>DSH_PROFILE</c> when set, else <c>desktop</c>.</summary>
        private static string ProfileName()
        {
            string envProfile = Environment.GetEnvironmentVariable("DSH_PROFILE");
            return string.IsNullOrEmpty(envProfile) ? "desktop" : envProfile;
        }

        private static string BundleDir() => Path.Combine(DshHome(), "bundles", ProfileName(), "unity-mcp");

        private static string BundlePatchPath() => Path.Combine(BundleDir(), "cordis.patch.yml");

        /// <summary>Single-quotes a YAML scalar; embedded single quotes are doubled.</summary>
        private static string YamlQuote(string value) => "'" + (value ?? string.Empty).Replace("'", "''") + "'";

        /// <summary>Double-quotes a shell argument so cmd.exe metacharacters stay literal.</summary>
        private static string ShellQuote(string value) => "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
    }
}
