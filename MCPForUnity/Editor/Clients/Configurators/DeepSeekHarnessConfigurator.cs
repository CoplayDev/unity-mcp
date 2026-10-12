using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Models;
using MCPForUnity.Editor.Services;
using Newtonsoft.Json.Linq;

namespace MCPForUnity.Editor.Clients.Configurators
{
    /// <summary>
    /// Configurator for the DeepSeek Harness (<c>dsh</c> CLI). DSH mounts external MCP
    /// servers as Cordis rows for its <c>@deepseek-ai/dsh-mcp-client</c> plugin. This
    /// configurator writes a configuration-only DSH bundle (manifest + patch) under
    /// <c>$DSH_HOME/bundles/unity-mcp/</c> and installs it into a named profile via
    /// <c>dsh plugin --profile &lt;name&gt; add &lt;bundle-dir&gt;</c>. The bundle is
    /// profile-scoped and removable through the plugin manager, unlike the root home
    /// patch which applies to every profile. After a DSH reload/restart, tools surface
    /// as <c>mcp__unityMCP__&lt;tool&gt;</c>.
    /// </summary>
    public class DeepSeekHarnessConfigurator : McpClientConfiguratorBase
    {
        private readonly string _dshHomeOverride;
        private readonly string _profileOverride;

        /// <summary>
        /// Injectable command runner for <c>dsh</c> CLI calls. Tests swap this to avoid
        /// spawning real processes; production uses <see cref="RunDshCommand"/>.
        /// </summary>
        internal static Func<string[], (int ExitCode, string Output)> DshCommandRunner = RunDshCommand;

        public DeepSeekHarnessConfigurator() : this(null, null) { }

        internal DeepSeekHarnessConfigurator(string dshHomeOverride, string profileOverride = null)
            : base(new McpClient
            {
                name = "DeepSeek Harness",
                windowsConfigPath = BuildConfigPath(dshHomeOverride),
                macConfigPath = BuildConfigPath(dshHomeOverride),
                linuxConfigPath = BuildConfigPath(dshHomeOverride)
            })
        {
            _dshHomeOverride = dshHomeOverride;
            _profileOverride = profileOverride;
        }

        /// <summary>
        /// Bundle install runs a CLI and must not happen automatically. The startup sweep
        /// (<c>StartupConfigRewrite</c>) calls <see cref="CheckStatus"/> with
        /// <c>attemptAutoRewrite: true</c>; if we auto-wrote here, merely opening a project
        /// would install the bundle without the user asking. Bundle install is a user
        /// action only.
        /// </summary>
        public override bool SupportsAutoConfigure => false;

        /// <summary>
        /// DSH is "installed" when its home directory exists (created on first DSH run).
        /// This is not derived from the config path because the config path lives inside
        /// the managed bundle directory, which only exists after Configure runs.
        /// </summary>
        public override bool IsInstalled => Directory.Exists(ResolveDshHome(_dshHomeOverride));

        // ------------------------------------------------------------------ paths

        private static string BuildConfigPath(string dshHomeOverride)
        {
            return Path.Combine(
                DshBundleWriter.GetBundleDir(ResolveDshHome(dshHomeOverride)),
                "cordis.patch.yml");
        }

        public override string GetConfigPath() => CurrentOsPath();

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

        /// <summary>
        /// Resolves the DSH profile name: an explicit override wins, then the
        /// <c>DSH_PROFILE</c> environment variable, then <c>"desktop"</c>.
        /// </summary>
        internal static string ResolveDshProfile(string profileOverride)
        {
            if (!string.IsNullOrEmpty(profileOverride)) return profileOverride;

            string envProfile = Environment.GetEnvironmentVariable("DSH_PROFILE");
            if (!string.IsNullOrEmpty(envProfile)) return envProfile;

            return "desktop";
        }

        // ------------------------------------------------------------------ status

        public override string GetConfigureActionLabel()
            => client.status == McpStatus.Configured ? "Unregister" : "Configure";

        /// <summary>
        /// Validates that the bundle is installed and its managed block matches the
        /// current transport. Never calls <see cref="Configure"/>, regardless of
        /// <paramref name="attemptAutoRewrite"/> — bundle install is a user action only.
        /// When validation would want a rewrite, returns <see cref="McpStatus.IncorrectPath"/>
        /// (or <see cref="McpStatus.VersionMismatch"/>) instead.
        /// </summary>
        public override McpStatus CheckStatus(bool attemptAutoRewrite = true)
        {
            try
            {
                string dshHome = ResolveDshHome(_dshHomeOverride);
                string profile = ResolveDshProfile(_profileOverride);

                // Bundle files must exist.
                string bundlePatchPath = Path.Combine(
                    DshBundleWriter.GetBundleDir(dshHome), "cordis.patch.yml");
                if (!File.Exists(bundlePatchPath))
                {
                    client.SetStatus(McpStatus.NotConfigured);
                    client.configuredTransport = Models.ConfiguredTransport.Unknown;
                    return client.status;
                }

                // Profile package.json must list the bundle.
                if (!ProfileListsBundle(dshHome, profile, DshBundleWriter.BundleName))
                {
                    client.SetStatus(McpStatus.NotConfigured);
                    client.configuredTransport = Models.ConfiguredTransport.Unknown;
                    return client.status;
                }

                // Parse the bundle's managed block and validate transport/url/source.
                string fileText = File.ReadAllText(bundlePatchPath);
                string block = DshConfigHelper.ExtractManagedBlock(fileText);
                if (block == null)
                {
                    client.SetStatus(McpStatus.IncorrectPath);
                    client.configuredTransport = Models.ConfiguredTransport.Unknown;
                    return client.status;
                }

                bool parsed = DshConfigHelper.TryParseManagedBlock(
                    block, out string transport, out string url, out _, out string[] args,
                    out _, out _);
                if (!parsed)
                {
                    client.SetStatus(McpStatus.IncorrectPath);
                    client.configuredTransport = Models.ConfiguredTransport.Unknown;
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
                client.SetStatus(McpStatus.IncorrectPath);
            }
            catch (Exception ex)
            {
                client.SetStatus(McpStatus.Error, ex.Message);
                client.configuredTransport = Models.ConfiguredTransport.Unknown;
            }

            return client.status;
        }

        // ------------------------------------------------------------------ configure / unregister

        public override void Configure()
        {
            string dshHome = ResolveDshHome(_dshHomeOverride);
            string profile = ResolveDshProfile(_profileOverride);
            string block = BuildBlock();
            string bundleDir = DshBundleWriter.WriteBundle(dshHome, block);

            // Install into the named DSH profile through the plugin manager.
            string manualCommand = $"dsh plugin --profile {profile} add {bundleDir}";
            (int exitCode, string output) = DshCommandRunner(new[]
            {
                "plugin", "--profile", profile, "add", bundleDir
            });
            if (exitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Failed to install DSH bundle via '{manualCommand}' (exit code {exitCode}): {output}\n" +
                    $"Run this command manually: {manualCommand}");
            }

            // Migration: remove any legacy root-patch managed block to prevent duplicate rows.
            RemoveLegacyRootPatchBlock(dshHome);

            client.SetStatus(McpStatus.Configured);
            client.configuredTransport = HttpEndpointUtility.GetCurrentServerTransport();
        }

        public override void Unregister()
        {
            string dshHome = ResolveDshHome(_dshHomeOverride);
            string profile = ResolveDshProfile(_profileOverride);

            // Best-effort: remove from DSH plugin manager (ignore failure if dsh is missing).
            try
            {
                DshCommandRunner(new[]
                {
                    "plugin", "--profile", profile, "remove", DshBundleWriter.BundleName
                });
            }
            catch { }

            DshBundleWriter.TryDeleteBundle(dshHome);
            RemoveLegacyRootPatchBlock(dshHome);

            client.SetStatus(McpStatus.NotConfigured);
            client.configuredTransport = Models.ConfiguredTransport.Unknown;
        }

        // ------------------------------------------------------------------ manual snippet / steps

        public override string GetManualSnippet()
        {
            try
            {
                string dshHome = ResolveDshHome(_dshHomeOverride);
                string profile = ResolveDshProfile(_profileOverride);
                string bundleDir = DshBundleWriter.GetBundleDir(dshHome);

                var sb = new StringBuilder();
                sb.AppendLine("# Mount the Unity MCP server in DeepSeek Harness as a native DSH bundle.");
                sb.AppendLine("# Configure writes these files and runs 'dsh plugin add' to install them.");
                sb.AppendLine("# $DSH_HOME defaults to ~/.dsh");
                sb.AppendLine();
                sb.AppendLine($"# --- {bundleDir}/package.json ---");
                sb.AppendLine(DshBundleWriter.ManifestJson.TrimEnd());
                sb.AppendLine();
                sb.AppendLine($"# --- {bundleDir}/cordis.patch.yml ---");
                sb.Append(BuildBlock());
                sb.AppendLine();
                sb.AppendLine("# Install command (run by Configure):");
                sb.AppendLine($"#   dsh plugin --profile {profile} add {bundleDir}");
                sb.AppendLine("# Remove command:");
                sb.AppendLine($"#   dsh plugin --profile {profile} remove {DshBundleWriter.BundleName}");
                sb.AppendLine();
                sb.AppendLine("# Per-profile alternative (no bundle, not uninstallable via plugin manager):");
                sb.AppendLine("#   Write the block above into $DSH_HOME/profiles/<name>/cordis.patch.yml");
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
            "Click Configure to write the bundle and install it into your DSH profile via dsh plugin add",
            "Restart DSH (or reload its configuration) so the new MCP server mounts",
            "Unity tools appear as mcp__unityMCP__<tool>"
        };

        // ------------------------------------------------------------------ internals

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
                    apiKey = UnityEditor.EditorPrefs.GetString(EditorPrefKeys.ApiKey, string.Empty);
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

        /// <summary>
        /// Removes the managed block from the root home patch (legacy target). This
        /// prevents duplicate rows when migrating from the root-patch approach to the
        /// bundle approach.
        /// </summary>
        private static void RemoveLegacyRootPatchBlock(string dshHome)
        {
            string rootPatch = Path.Combine(dshHome, "cordis.patch.yml");
            if (!File.Exists(rootPatch)) return;

            string fileText = File.ReadAllText(rootPatch);
            if (!DshConfigHelper.HasManagedBlock(fileText)) return;

            string updated = DshConfigHelper.RemoveManagedBlock(fileText);
            McpConfigurationHelper.WriteAtomicFile(rootPatch, updated);
        }

        /// <summary>
        /// Checks whether the target profile's <c>package.json</c> lists the given bundle
        /// name in <c>dsh.profile.bundles</c>.
        /// </summary>
        private static bool ProfileListsBundle(string dshHome, string profile, string bundleName)
        {
            string profilePackagePath = Path.Combine(dshHome, "profiles", profile, "package.json");
            if (!File.Exists(profilePackagePath)) return false;

            try
            {
                var json = JObject.Parse(File.ReadAllText(profilePackagePath));
                var bundles = json["dsh"]?["profile"]?["bundles"] as JArray;
                if (bundles == null) return false;
                foreach (var item in bundles)
                {
                    if (string.Equals(item.ToString(), bundleName, StringComparison.Ordinal))
                        return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Default <c>dsh</c> command runner using <see cref="Process"/>. Captures
        /// stdout+stderr, enforces a 60-second timeout, and returns -1 on any failure
        /// (including missing <c>dsh</c>) so callers can surface the manual command.
        /// </summary>
        private static (int ExitCode, string Output) RunDshCommand(string[] args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "dsh",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    Arguments = BuildArgumentsString(args)
                };

                using (var process = Process.Start(psi))
                {
                    if (process == null) return (-1, "Failed to start dsh process");

                    string output = process.StandardOutput.ReadToEnd()
                        + process.StandardError.ReadToEnd();
                    if (!process.WaitForExit(60_000))
                    {
                        try { process.Kill(); } catch { }
                        return (-1, output + "\n(dsh timed out after 60s)");
                    }
                    return (process.ExitCode, output);
                }
            }
            catch (Exception ex)
            {
                return (-1, $"Failed to run 'dsh': {ex.Message}");
            }
        }

        /// <summary>Builds a process argument string with Windows-compatible quoting.</summary>
        private static string BuildArgumentsString(string[] args)
        {
            if (args == null || args.Length == 0) return string.Empty;
            var sb = new StringBuilder();
            for (int i = 0; i < args.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                string arg = args[i] ?? string.Empty;
                if (arg.IndexOfAny(new[] { ' ', '\t', '"' }) >= 0)
                {
                    sb.Append('"').Append(arg.Replace("\"", "\\\"")).Append('"');
                }
                else
                {
                    sb.Append(arg);
                }
            }
            return sb.ToString();
        }
    }
}
