using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Models;
using MCPForUnity.Editor.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Clients
{
    /// <summary>Shared base class for MCP configurators.</summary>
    public abstract class McpClientConfiguratorBase : IMcpClientConfigurator
    {
        protected readonly McpClient client;

        protected McpClientConfiguratorBase(McpClient client)
        {
            this.client = client;
        }

        internal McpClient Client => client;

        public string Id => client.name.Replace(" ", "").ToLowerInvariant();
        public virtual string DisplayName => client.name;
        public McpStatus Status => client.status;
        public ConfiguredTransport ConfiguredTransport => client.configuredTransport;
        public virtual bool SupportsAutoConfigure => true;
        public virtual string GetConfigureActionLabel() => "Configure";

        public abstract string GetConfigPath();
        public abstract McpStatus CheckStatus(bool attemptAutoRewrite = true);
        public abstract void Configure();
        public abstract string GetManualSnippet();
        public abstract IList<string> GetInstallationSteps();

        protected string GetUvxPathOrError()
        {
            string uvx = MCPServiceLocator.Paths.GetUvxPath();
            if (string.IsNullOrEmpty(uvx))
            {
                throw new InvalidOperationException("uvx not found. Install uv/uvx or set the override in Advanced Settings.");
            }
            return uvx;
        }

        protected string CurrentOsPath()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return client.windowsConfigPath;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return client.macConfigPath;
            return client.linuxConfigPath;
        }

        protected bool UrlsEqual(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            {
                return false;
            }

            if (Uri.TryCreate(a.Trim(), UriKind.Absolute, out var uriA) &&
                Uri.TryCreate(b.Trim(), UriKind.Absolute, out var uriB))
            {
                return Uri.Compare(
                           uriA,
                           uriB,
                           UriComponents.HttpRequestUrl,
                           UriFormat.SafeUnescaped,
                           StringComparison.OrdinalIgnoreCase) == 0;
            }

            string Normalize(string value) => value.Trim().TrimEnd('/');
            return string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Gets the expected package source for validation, accounting for beta mode.
        /// This should match what Configure() would actually use for the --from argument.
        /// MUST be called from the main thread due to EditorPrefs access.
        /// </summary>
        protected static string GetExpectedPackageSourceForValidation()
        {
            // Check for explicit override first
            string gitUrlOverride = EditorPrefs.GetString(EditorPrefKeys.GitUrlOverride, "");
            if (!string.IsNullOrEmpty(gitUrlOverride))
            {
                return gitUrlOverride;
            }

            // Check beta mode using the same logic as GetUseBetaServerWithDynamicDefault
            // (bypass cache to ensure fresh read)
            bool useBetaServer;
            bool hasPrefKey = EditorPrefs.HasKey(EditorPrefKeys.UseBetaServer);
            if (hasPrefKey)
            {
                useBetaServer = EditorPrefs.GetBool(EditorPrefKeys.UseBetaServer, false);
            }
            else
            {
                // Dynamic default based on package version
                useBetaServer = AssetPathUtility.IsPreReleaseVersion();
            }

            if (useBetaServer)
            {
                return "mcpforunityserver>=0.0.0a0";
            }

            // Standard mode uses exact version from package.json
            return AssetPathUtility.GetMcpServerPackageSource();
        }

        /// <summary>
        /// Checks if a package source string represents a beta/prerelease version.
        /// Beta versions include:
        /// - PyPI beta: "mcpforunityserver==9.4.0b20250203..." (contains 'b' before timestamp)
        /// - PyPI prerelease range: "mcpforunityserver>=0.0.0a0" (used when beta mode is enabled)
        /// - Git beta branch: contains "@beta" or "-beta"
        /// </summary>
        protected static bool IsBetaPackageSource(string packageSource)
        {
            if (string.IsNullOrEmpty(packageSource))
                return false;

            // PyPI beta format: mcpforunityserver==X.Y.Zb<timestamp>
            // The 'b' suffix before numbers indicates a PEP 440 beta version
            if (System.Text.RegularExpressions.Regex.IsMatch(packageSource, @"==\d+\.\d+\.\d+b\d+"))
                return true;

            // PyPI prerelease range: >=0.0.0a0 (used when "Use Beta Server" is enabled in Unity settings)
            if (packageSource.Contains(">=0.0.0a0", StringComparison.OrdinalIgnoreCase))
                return true;

            // Git-based beta references
            if (packageSource.Contains("@beta", StringComparison.OrdinalIgnoreCase))
                return true;

            if (packageSource.Contains("-beta", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }
    }

    /// <summary>JSON-file based configurator (Cursor, Windsurf, VS Code, etc.).</summary>
    public abstract class JsonFileMcpConfigurator : McpClientConfiguratorBase
    {
        public JsonFileMcpConfigurator(McpClient client) : base(client) { }

        public override string GetConfigPath() => CurrentOsPath();

        public override McpStatus CheckStatus(bool attemptAutoRewrite = true)
        {
            try
            {
                string path = GetConfigPath();
                if (!File.Exists(path))
                {
                    client.SetStatus(McpStatus.NotConfigured);
                    client.configuredTransport = Models.ConfiguredTransport.Unknown;
                    return client.status;
                }

                string configJson = File.ReadAllText(path);
                string[] args = null;
                string configuredUrl = null;
                bool configExists = false;

                if (client.IsVsCodeLayout)
                {
                    var vsConfig = JsonConvert.DeserializeObject<JToken>(configJson) as JObject;
                    if (vsConfig != null)
                    {
                        var unityToken =
                            vsConfig["servers"]?["unityMCP"]
                            ?? vsConfig["mcp"]?["servers"]?["unityMCP"];

                        if (unityToken is JObject unityObj)
                        {
                            configExists = true;

                            var argsToken = unityObj["args"];
                            if (argsToken is JArray)
                            {
                                args = argsToken.ToObject<string[]>();
                            }

                            var urlToken = unityObj["url"] ?? unityObj["serverUrl"];
                            if (urlToken != null && urlToken.Type != JTokenType.Null)
                            {
                                configuredUrl = urlToken.ToString();
                            }
                        }
                    }
                }
                else
                {
                    McpConfig standardConfig = JsonConvert.DeserializeObject<McpConfig>(configJson);
                    if (standardConfig?.mcpServers?.unityMCP != null)
                    {
                        args = standardConfig.mcpServers.unityMCP.args;
                        configuredUrl = standardConfig.mcpServers.unityMCP.url;
                        configExists = true;
                    }
                }

                if (!configExists)
                {
                    client.SetStatus(McpStatus.MissingConfig);
                    client.configuredTransport = Models.ConfiguredTransport.Unknown;
                    return client.status;
                }

                // Determine and set the configured transport type
                if (args != null && args.Length > 0)
                {
                    client.configuredTransport = Models.ConfiguredTransport.Stdio;
                }
                else if (!string.IsNullOrEmpty(configuredUrl))
                {
                    // Distinguish HTTP Local from HTTP Remote by matching against both URLs
                    string localRpcUrl = HttpEndpointUtility.GetLocalMcpRpcUrl();
                    string remoteRpcUrl = HttpEndpointUtility.GetRemoteMcpRpcUrl();
                    if (!string.IsNullOrEmpty(remoteRpcUrl) && UrlsEqual(configuredUrl, remoteRpcUrl))
                    {
                        client.configuredTransport = Models.ConfiguredTransport.HttpRemote;
                    }
                    else
                    {
                        client.configuredTransport = Models.ConfiguredTransport.Http;
                    }
                }
                else
                {
                    client.configuredTransport = Models.ConfiguredTransport.Unknown;
                }

                bool matches = false;
                bool hasVersionMismatch = false;
                string mismatchReason = null;

                if (args != null && args.Length > 0)
                {
                    // Use beta-aware expected package source for comparison
                    string expectedUvxUrl = GetExpectedPackageSourceForValidation();
                    string configuredUvxUrl = McpConfigurationHelper.ExtractUvxUrl(args);

                    if (!string.IsNullOrEmpty(configuredUvxUrl) && !string.IsNullOrEmpty(expectedUvxUrl))
                    {
                        if (McpConfigurationHelper.PathsEqual(configuredUvxUrl, expectedUvxUrl))
                        {
                            matches = true;
                        }
                        else
                        {
                            // Check for beta/stable mismatch
                            bool configuredIsBeta = IsBetaPackageSource(configuredUvxUrl);
                            bool expectedIsBeta = IsBetaPackageSource(expectedUvxUrl);

                            if (configuredIsBeta && !expectedIsBeta)
                            {
                                hasVersionMismatch = true;
                                mismatchReason = "Configured for beta server, but 'Use Beta Server' is disabled in Advanced settings.";
                            }
                            else if (!configuredIsBeta && expectedIsBeta)
                            {
                                hasVersionMismatch = true;
                                mismatchReason = "Configured for stable server, but 'Use Beta Server' is enabled in Advanced settings.";
                            }
                            else
                            {
                                hasVersionMismatch = true;
                                mismatchReason = "Server version doesn't match the plugin. Re-configure to update.";
                            }
                        }
                    }
                }
                else if (!string.IsNullOrEmpty(configuredUrl))
                {
                    // Match against the active scope's URL
                    string expectedUrl = HttpEndpointUtility.GetMcpRpcUrl();
                    matches = UrlsEqual(configuredUrl, expectedUrl);
                }

                if (matches)
                {
                    client.SetStatus(McpStatus.Configured);
                    return client.status;
                }

                if (hasVersionMismatch)
                {
                    if (attemptAutoRewrite)
                    {
                        var result = McpConfigurationHelper.WriteMcpConfiguration(path, client);
                        if (result == "Configured successfully")
                        {
                            client.SetStatus(McpStatus.Configured);
                            client.configuredTransport = HttpEndpointUtility.GetCurrentServerTransport();
                        }
                        else
                        {
                            client.SetStatus(McpStatus.VersionMismatch, mismatchReason);
                        }
                    }
                    else
                    {
                        client.SetStatus(McpStatus.VersionMismatch, mismatchReason);
                    }
                }
                else if (attemptAutoRewrite)
                {
                    var result = McpConfigurationHelper.WriteMcpConfiguration(path, client);
                    if (result == "Configured successfully")
                    {
                        client.SetStatus(McpStatus.Configured);
                        client.configuredTransport = HttpEndpointUtility.GetCurrentServerTransport();
                    }
                    else
                    {
                        client.SetStatus(McpStatus.IncorrectPath);
                    }
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
            string result = McpConfigurationHelper.WriteMcpConfiguration(path, client);
            if (result == "Configured successfully")
            {
                client.SetStatus(McpStatus.Configured);
                client.configuredTransport = HttpEndpointUtility.GetCurrentServerTransport();
            }
            else
            {
                throw new InvalidOperationException(result);
            }
        }

        public override string GetManualSnippet()
        {
            try
            {
                string uvx = GetUvxPathOrError();
                return ConfigJsonBuilder.BuildManualConfigJson(uvx, client);
            }
            catch (Exception ex)
            {
                var errorObj = new { error = ex.Message };
                return JsonConvert.SerializeObject(errorObj);
            }
        }

        public override IList<string> GetInstallationSteps() => new List<string> { "Configuration steps not available for this client." };
    }

    /// <summary>Codex (TOML) configurator.</summary>
    public abstract class CodexMcpConfigurator : McpClientConfiguratorBase
    {
        public CodexMcpConfigurator(McpClient client) : base(client) { }

        public override string GetConfigPath() => CurrentOsPath();

        public override McpStatus CheckStatus(bool attemptAutoRewrite = true)
        {
            try
            {
                string path = GetConfigPath();
                if (!File.Exists(path))
                {
                    client.SetStatus(McpStatus.NotConfigured);
                    client.configuredTransport = Models.ConfiguredTransport.Unknown;
                    return client.status;
                }

                string toml = File.ReadAllText(path);
                if (CodexConfigHelper.TryParseCodexServer(toml, out _, out var args, out var url))
                {
                    // Determine and set the configured transport type
                    if (!string.IsNullOrEmpty(url))
                    {
                        // Distinguish HTTP Local from HTTP Remote
                        string remoteRpcUrl = HttpEndpointUtility.GetRemoteMcpRpcUrl();
                        if (!string.IsNullOrEmpty(remoteRpcUrl) && UrlsEqual(url, remoteRpcUrl))
                        {
                            client.configuredTransport = Models.ConfiguredTransport.HttpRemote;
                        }
                        else
                        {
                            client.configuredTransport = Models.ConfiguredTransport.Http;
                        }
                    }
                    else if (args != null && args.Length > 0)
                    {
                        client.configuredTransport = Models.ConfiguredTransport.Stdio;
                    }
                    else
                    {
                        client.configuredTransport = Models.ConfiguredTransport.Unknown;
                    }

                    bool matches = false;
                    bool hasVersionMismatch = false;
                    string mismatchReason = null;

                    if (!string.IsNullOrEmpty(url))
                    {
                        // Match against the active scope's URL
                        matches = UrlsEqual(url, HttpEndpointUtility.GetMcpRpcUrl());
                    }
                    else if (args != null && args.Length > 0)
                    {
                        // Use beta-aware expected package source for comparison
                        string expected = GetExpectedPackageSourceForValidation();
                        string configured = McpConfigurationHelper.ExtractUvxUrl(args);

                        if (!string.IsNullOrEmpty(configured) && !string.IsNullOrEmpty(expected))
                        {
                            if (McpConfigurationHelper.PathsEqual(configured, expected))
                            {
                                matches = true;
                            }
                            else
                            {
                                // Check for beta/stable mismatch
                                bool configuredIsBeta = IsBetaPackageSource(configured);
                                bool expectedIsBeta = IsBetaPackageSource(expected);

                                if (configuredIsBeta && !expectedIsBeta)
                                {
                                    hasVersionMismatch = true;
                                    mismatchReason = "Configured for beta server, but 'Use Beta Server' is disabled in Advanced settings.";
                                }
                                else if (!configuredIsBeta && expectedIsBeta)
                                {
                                    hasVersionMismatch = true;
                                    mismatchReason = "Configured for stable server, but 'Use Beta Server' is enabled in Advanced settings.";
                                }
                                else
                                {
                                    hasVersionMismatch = true;
                                    mismatchReason = "Server version doesn't match the plugin. Re-configure to update.";
                                }
                            }
                        }
                    }

                    if (matches)
                    {
                        client.SetStatus(McpStatus.Configured);
                        return client.status;
                    }

                    if (hasVersionMismatch)
                    {
                        if (attemptAutoRewrite)
                        {
                            string result = McpConfigurationHelper.ConfigureCodexClient(path, client);
                            if (result == "Configured successfully")
                            {
                                client.SetStatus(McpStatus.Configured);
                                client.configuredTransport = HttpEndpointUtility.GetCurrentServerTransport();
                                return client.status;
                            }
                        }
                        client.SetStatus(McpStatus.VersionMismatch, mismatchReason);
                        return client.status;
                    }
                }
                else
                {
                    client.configuredTransport = Models.ConfiguredTransport.Unknown;
                }

                if (attemptAutoRewrite)
                {
                    string result = McpConfigurationHelper.ConfigureCodexClient(path, client);
                    if (result == "Configured successfully")
                    {
                        client.SetStatus(McpStatus.Configured);
                        client.configuredTransport = HttpEndpointUtility.GetCurrentServerTransport();
                    }
                    else
                    {
                        client.SetStatus(McpStatus.IncorrectPath);
                    }
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
            string result = McpConfigurationHelper.ConfigureCodexClient(path, client);
            if (result == "Configured successfully")
            {
                client.SetStatus(McpStatus.Configured);
                client.configuredTransport = HttpEndpointUtility.GetCurrentServerTransport();
            }
            else
            {
                throw new InvalidOperationException(result);
            }
        }

        public override string GetManualSnippet()
        {
            try
            {
                string uvx = GetUvxPathOrError();
                return CodexConfigHelper.BuildCodexServerBlock(uvx);
            }
            catch (Exception ex)
            {
                return $"# error: {ex.Message}";
            }
        }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "Run 'codex config edit' or open the config path",
            "Paste the TOML",
            "Save and restart Codex"
        };
    }

    /// <summary>CLI-based configurator (Claude Code).</summary>
    public abstract class ClaudeCliMcpConfigurator : McpClientConfiguratorBase
    {
        public ClaudeCliMcpConfigurator(McpClient client) : base(client) { }

        public override bool SupportsAutoConfigure => true;
        public override string GetConfigureActionLabel() => client.status == McpStatus.Configured ? "Unregister" : "Configure";

        public override string GetConfigPath() => "Managed via Claude CLI";

        /// <summary>
        /// Checks the Claude CLI registration status.
        /// MUST be called from the main Unity thread due to EditorPrefs and Application.dataPath access.
        /// </summary>
        public override McpStatus CheckStatus(bool attemptAutoRewrite = true)
        {
            // Capture main-thread-only values before delegating to thread-safe method
            string projectDir = Path.GetDirectoryName(Application.dataPath);
            bool useHttpTransport = EditorConfigurationCache.Instance.UseHttpTransport;
            // Resolve claudePath on the main thread (EditorPrefs access)
            string claudePath = MCPServiceLocator.Paths.GetClaudeCliPath();
            RuntimePlatform platform = Application.platform;
            bool isRemoteScope = HttpEndpointUtility.IsRemoteScope();
            // Get expected package source considering beta mode (matches what Register() would use)
            string expectedPackageSource = GetExpectedPackageSourceForValidation();
            return CheckStatusWithProjectDir(projectDir, useHttpTransport, claudePath, platform, isRemoteScope, expectedPackageSource, attemptAutoRewrite);
        }

        /// <summary>
        /// Internal thread-safe version of CheckStatus.
        /// Can be called from background threads because all main-thread-only values are passed as parameters.
        /// projectDir, useHttpTransport, claudePath, platform, isRemoteScope, and expectedPackageSource are REQUIRED
        /// (non-nullable where applicable) to enforce thread safety at compile time.
        /// NOTE: attemptAutoRewrite is NOT fully thread-safe because Configure() requires the main thread.
        /// When called from a background thread, pass attemptAutoRewrite=false and handle re-registration
        /// on the main thread based on the returned status.
        /// </summary>
        internal McpStatus CheckStatusWithProjectDir(
            string projectDir, bool useHttpTransport, string claudePath, RuntimePlatform platform,
            bool isRemoteScope, string expectedPackageSource,
            bool attemptAutoRewrite = false)
        {
            try
            {
                if (string.IsNullOrEmpty(claudePath))
                {
                    client.SetStatus(McpStatus.NotConfigured, "Claude CLI not found");
                    client.configuredTransport = Models.ConfiguredTransport.Unknown;
                    return client.status;
                }

                // projectDir is required - no fallback to Application.dataPath
                if (string.IsNullOrEmpty(projectDir))
                {
                    throw new ArgumentNullException(nameof(projectDir), "Project directory must be provided for thread-safe execution");
                }

                // Read Claude Code config directly from ~/.claude.json instead of using slow CLI
                // This is instant vs 15+ seconds for `claude mcp list` which does health checks
                var configResult = ReadClaudeCodeConfig(projectDir);
                if (configResult.error != null)
                {
                    client.SetStatus(McpStatus.NotConfigured, configResult.error);
                    client.configuredTransport = Models.ConfiguredTransport.Unknown;
                    return client.status;
                }

                if (configResult.serverConfig == null)
                {
                    // UnityMCP not found in config
                    client.SetStatus(McpStatus.NotConfigured);
                    client.configuredTransport = Models.ConfiguredTransport.Unknown;
                    return client.status;
                }

                // UnityMCP is registered - check transport and version
                bool currentUseHttp = useHttpTransport;
                var serverConfig = configResult.serverConfig;

                // Determine registered transport type
                string registeredType = serverConfig["type"]?.ToString()?.ToLowerInvariant() ?? "";
                bool registeredWithHttp = registeredType == "http";
                bool registeredWithStdio = registeredType == "stdio";

                // Set the configured transport based on what we detected
                if (registeredWithHttp)
                {
                    client.configuredTransport = isRemoteScope
                        ? Models.ConfiguredTransport.HttpRemote
                        : Models.ConfiguredTransport.Http;
                }
                else if (registeredWithStdio)
                {
                    client.configuredTransport = Models.ConfiguredTransport.Stdio;
                }
                else
                {
                    client.configuredTransport = Models.ConfiguredTransport.Unknown;
                }

                // Check for transport mismatch
                bool hasTransportMismatch = (currentUseHttp && registeredWithStdio) || (!currentUseHttp && registeredWithHttp);

                // For stdio transport, also check package version
                bool hasVersionMismatch = false;
                string configuredPackageSource = null;
                string mismatchReason = null;
                if (registeredWithStdio)
                {
                    configuredPackageSource = ExtractPackageSourceFromConfig(serverConfig);
                    if (!string.IsNullOrEmpty(configuredPackageSource) && !string.IsNullOrEmpty(expectedPackageSource))
                    {
                        // Check for exact match first
                        if (!string.Equals(configuredPackageSource, expectedPackageSource, StringComparison.OrdinalIgnoreCase))
                        {
                            hasVersionMismatch = true;

                            // Provide more specific mismatch reason for beta/stable differences
                            bool configuredIsBeta = IsBetaPackageSource(configuredPackageSource);
                            bool expectedIsBeta = IsBetaPackageSource(expectedPackageSource);

                            if (configuredIsBeta && !expectedIsBeta)
                            {
                                mismatchReason = "Configured for beta server, but 'Use Beta Server' is disabled in Advanced settings.";
                            }
                            else if (!configuredIsBeta && expectedIsBeta)
                            {
                                mismatchReason = "Configured for stable server, but 'Use Beta Server' is enabled in Advanced settings.";
                            }
                            else
                            {
                                mismatchReason = "Server version doesn't match the plugin. Re-configure to update.";
                            }
                        }
                    }
                }

                // If there's any mismatch and auto-rewrite is enabled, re-register
                if (hasTransportMismatch || hasVersionMismatch)
                {
                    // Configure() requires main thread (accesses EditorPrefs, Application.dataPath)
                    // Only attempt auto-rewrite if we're on the main thread
                    bool isMainThread = System.Threading.Thread.CurrentThread.ManagedThreadId == 1;
                    if (attemptAutoRewrite && isMainThread)
                    {
                        string reason = hasTransportMismatch
                            ? $"Transport mismatch (registered: {(registeredWithHttp ? "HTTP" : "stdio")}, expected: {(currentUseHttp ? "HTTP" : "stdio")})"
                            : mismatchReason ?? $"Package version mismatch";
                        McpLog.Info($"{reason}. Re-registering...");
                        try
                        {
                            // Force re-register by ensuring status is not Configured (which would toggle to Unregister)
                            client.SetStatus(McpStatus.IncorrectPath);
                            Configure();
                            return client.status;
                        }
                        catch (Exception ex)
                        {
                            McpLog.Warn($"Auto-reregister failed: {ex.Message}");
                            client.SetStatus(McpStatus.IncorrectPath, $"Configuration mismatch. Click Configure to re-register.");
                            return client.status;
                        }
                    }
                    else
                    {
                        if (hasTransportMismatch)
                        {
                            string errorMsg = $"Transport mismatch: Claude Code is registered with {(registeredWithHttp ? "HTTP" : "stdio")} but current setting is {(currentUseHttp ? "HTTP" : "stdio")}. Click Configure to re-register.";
                            client.SetStatus(McpStatus.Error, errorMsg);
                            McpLog.Warn(errorMsg);
                        }
                        else
                        {
                            client.SetStatus(McpStatus.VersionMismatch, mismatchReason);
                        }
                        return client.status;
                    }
                }

                client.SetStatus(McpStatus.Configured);
                return client.status;
            }
            catch (Exception ex)
            {
                McpLog.Warn($"[Claude Code] CheckStatus exception: {ex.GetType().Name}: {ex.Message}");
                client.SetStatus(McpStatus.Error, ex.Message);
                client.configuredTransport = Models.ConfiguredTransport.Unknown;
            }

            return client.status;
        }

        public override void Configure()
        {
            if (client.status == McpStatus.Configured)
            {
                Unregister();
            }
            else
            {
                Register();
            }
        }

        /// <summary>
        /// Thread-safe version of Configure that uses pre-captured main-thread values.
        /// All parameters must be captured on the main thread before calling this method.
        /// </summary>
        public void ConfigureWithCapturedValues(
            string projectDir, string claudePath, string pathPrepend,
            bool useHttpTransport, string httpUrl,
            string uvxPath, string fromArgs, string packageName, bool shouldForceRefresh,
            string apiKey,
            Models.ConfiguredTransport serverTransport)
        {
            if (client.status == McpStatus.Configured)
            {
                UnregisterWithCapturedValues(projectDir, claudePath, pathPrepend);
            }
            else
            {
                RegisterWithCapturedValues(projectDir, claudePath, pathPrepend,
                    useHttpTransport, httpUrl, uvxPath, fromArgs, packageName, shouldForceRefresh,
                    apiKey, serverTransport);
            }
        }

        /// <summary>
        /// Thread-safe registration using pre-captured values.
        /// </summary>
        private void RegisterWithCapturedValues(
            string projectDir, string claudePath, string pathPrepend,
            bool useHttpTransport, string httpUrl,
            string uvxPath, string fromArgs, string packageName, bool shouldForceRefresh,
            string apiKey,
            Models.ConfiguredTransport serverTransport)
        {
            if (string.IsNullOrEmpty(claudePath))
            {
                throw new InvalidOperationException("Claude CLI not found. Please install Claude Code first.");
            }

            string args;
            if (useHttpTransport)
            {
                // Only include API key header for remote-hosted mode
                // Use --scope local to register in the project-local config, avoiding conflicts with user-level config (#664)
                if (serverTransport == Models.ConfiguredTransport.HttpRemote && !string.IsNullOrEmpty(apiKey))
                {
                    string safeKey = SanitizeShellHeaderValue(apiKey);
                    args = $"mcp add --scope local --transport http UnityMCP {httpUrl} --header \"{AuthConstants.ApiKeyHeader}: {safeKey}\"";
                }
                else
                {
                    args = $"mcp add --scope local --transport http UnityMCP {httpUrl}";
                }
            }
            else
            {
                // Note: --reinstall is not supported by uvx, use --no-cache --refresh instead
                string devFlags = shouldForceRefresh ? "--no-cache --refresh " : string.Empty;
                // Use --scope local to register in the project-local config, avoiding conflicts with user-level config (#664)
                args = $"mcp add --scope local --transport stdio UnityMCP -- \"{uvxPath}\" {devFlags}{fromArgs} {packageName}";
            }

            // Remove any existing registrations from ALL scopes to prevent stale config conflicts (#664)
            McpLog.Info("Removing any existing UnityMCP registrations from all scopes before adding...");
            RemoveFromAllScopes(claudePath, projectDir, pathPrepend);

            // Now add the registration
            if (!ExecPath.TryRun(claudePath, args, projectDir, out var stdout, out var stderr, 15000, pathPrepend))
            {
                throw new InvalidOperationException($"Failed to register with Claude Code:\n{stderr}\n{stdout}");
            }

            McpLog.Info($"Successfully registered with Claude Code using {(useHttpTransport ? "HTTP" : "stdio")} transport.");
            client.SetStatus(McpStatus.Configured);
            client.configuredTransport = serverTransport;
        }

        /// <summary>
        /// Thread-safe unregistration using pre-captured values.
        /// </summary>
        private void UnregisterWithCapturedValues(string projectDir, string claudePath, string pathPrepend)
        {
            if (string.IsNullOrEmpty(claudePath))
            {
                throw new InvalidOperationException("Claude CLI not found. Please install Claude Code first.");
            }

            // Remove from ALL scopes to ensure complete cleanup (#664)
            McpLog.Info("Removing all UnityMCP registrations from all scopes...");
            RemoveFromAllScopes(claudePath, projectDir, pathPrepend);

            McpLog.Info("MCP server successfully unregistered from Claude Code.");
            client.SetStatus(McpStatus.NotConfigured);
            client.configuredTransport = Models.ConfiguredTransport.Unknown;
        }

        private void Register()
        {
            var pathService = MCPServiceLocator.Paths;
            string claudePath = pathService.GetClaudeCliPath();
            if (string.IsNullOrEmpty(claudePath))
            {
                throw new InvalidOperationException("Claude CLI not found. Please install Claude Code first.");
            }

            bool useHttpTransport = EditorConfigurationCache.Instance.UseHttpTransport;

            string args;
            if (useHttpTransport)
            {
                string httpUrl = HttpEndpointUtility.GetMcpRpcUrl();
                // Only include API key header for remote-hosted mode
                // Use --scope local to register in the project-local config, avoiding conflicts with user-level config (#664)
                if (HttpEndpointUtility.IsRemoteScope())
                {
                    string apiKey = EditorPrefs.GetString(EditorPrefKeys.ApiKey, string.Empty);
                    if (!string.IsNullOrEmpty(apiKey))
                    {
                        string safeKey = SanitizeShellHeaderValue(apiKey);
                        args = $"mcp add --scope local --transport http UnityMCP {httpUrl} --header \"{AuthConstants.ApiKeyHeader}: {safeKey}\"";
                    }
                    else
                    {
                        args = $"mcp add --scope local --transport http UnityMCP {httpUrl}";
                    }
                }
                else
                {
                    args = $"mcp add --scope local --transport http UnityMCP {httpUrl}";
                }
            }
            else
            {
                var (uvxPath, _, packageName) = AssetPathUtility.GetUvxCommandParts();
                // Use central helper that checks both DevModeForceServerRefresh AND local path detection.
                // Note: --reinstall is not supported by uvx, use --no-cache --refresh instead
                string devFlags = AssetPathUtility.ShouldForceUvxRefresh() ? "--no-cache --refresh " : string.Empty;
                string fromArgs = AssetPathUtility.GetBetaServerFromArgs(quoteFromPath: true);
                // Use --scope local to register in the project-local config, avoiding conflicts with user-level config (#664)
                args = $"mcp add --scope local --transport stdio UnityMCP -- \"{uvxPath}\" {devFlags}{fromArgs} {packageName}";
            }

            string projectDir = Path.GetDirectoryName(Application.dataPath);

            string pathPrepend = null;
            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                pathPrepend = "/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin";
            }
            else if (Application.platform == RuntimePlatform.LinuxEditor)
            {
                pathPrepend = "/usr/local/bin:/usr/bin:/bin";
            }

            try
            {
                string claudeDir = Path.GetDirectoryName(claudePath);
                if (!string.IsNullOrEmpty(claudeDir))
                {
                    pathPrepend = string.IsNullOrEmpty(pathPrepend)
                        ? claudeDir
                        : $"{claudeDir}:{pathPrepend}";
                }
            }
            catch { }

            // Remove any existing registrations from ALL scopes to prevent stale config conflicts (#664)
            McpLog.Info("Removing any existing UnityMCP registrations from all scopes before adding...");
            RemoveFromAllScopes(claudePath, projectDir, pathPrepend);

            // Now add the registration with the current transport mode
            if (!ExecPath.TryRun(claudePath, args, projectDir, out var stdout, out var stderr, 15000, pathPrepend))
            {
                throw new InvalidOperationException($"Failed to register with Claude Code:\n{stderr}\n{stdout}");
            }

            McpLog.Info($"Successfully registered with Claude Code using {(useHttpTransport ? "HTTP" : "stdio")} transport.");

            // Set status to Configured immediately after successful registration
            // The UI will trigger an async verification check separately to avoid blocking
            client.SetStatus(McpStatus.Configured);
            client.configuredTransport = HttpEndpointUtility.GetCurrentServerTransport();
        }

        private void Unregister()
        {
            var pathService = MCPServiceLocator.Paths;
            string claudePath = pathService.GetClaudeCliPath();

            if (string.IsNullOrEmpty(claudePath))
            {
                throw new InvalidOperationException("Claude CLI not found. Please install Claude Code first.");
            }

            string projectDir = Path.GetDirectoryName(Application.dataPath);
            string pathPrepend = null;
            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                pathPrepend = "/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin";
            }
            else if (Application.platform == RuntimePlatform.LinuxEditor)
            {
                pathPrepend = "/usr/local/bin:/usr/bin:/bin";
            }

            // Remove from ALL scopes to ensure complete cleanup (#664)
            McpLog.Info("Removing all UnityMCP registrations from all scopes...");
            RemoveFromAllScopes(claudePath, projectDir, pathPrepend);

            McpLog.Info("MCP server successfully unregistered from Claude Code.");
            client.SetStatus(McpStatus.NotConfigured);
            client.configuredTransport = Models.ConfiguredTransport.Unknown;
        }

        public override string GetManualSnippet()
        {
            string uvxPath = MCPServiceLocator.Paths.GetUvxPath();
            bool useHttpTransport = EditorConfigurationCache.Instance.UseHttpTransport;

            if (useHttpTransport)
            {
                string httpUrl = HttpEndpointUtility.GetMcpRpcUrl();
                // Only include API key header for remote-hosted mode
                string headerArg = "";
                if (HttpEndpointUtility.IsRemoteScope())
                {
                    string apiKey = EditorPrefs.GetString(EditorPrefKeys.ApiKey, string.Empty);
                    headerArg = !string.IsNullOrEmpty(apiKey) ? $" --header \"{AuthConstants.ApiKeyHeader}: {SanitizeShellHeaderValue(apiKey)}\"" : "";
                }
                return "# Register the MCP server with Claude Code:\n" +
                       $"claude mcp add --scope local --transport http UnityMCP {httpUrl}{headerArg}\n\n" +
                       "# Unregister the MCP server (from all scopes to clean up any stale configs):\n" +
                       "claude mcp remove --scope local UnityMCP\n" +
                       "claude mcp remove --scope user UnityMCP\n" +
                       "claude mcp remove --scope project UnityMCP\n\n" +
                       "# List registered servers:\n" +
                       "claude mcp list";
            }

            if (string.IsNullOrEmpty(uvxPath))
            {
                return "# Error: Configuration not available - check paths in Advanced Settings";
            }

            // Use central helper that checks both DevModeForceServerRefresh AND local path detection.
            // Note: --reinstall is not supported by uvx, use --no-cache --refresh instead
            string devFlags = AssetPathUtility.ShouldForceUvxRefresh() ? "--no-cache --refresh " : string.Empty;
            string fromArgs = AssetPathUtility.GetBetaServerFromArgs(quoteFromPath: true);

            return "# Register the MCP server with Claude Code:\n" +
                   $"claude mcp add --scope local --transport stdio UnityMCP -- \"{uvxPath}\" {devFlags}{fromArgs} mcp-for-unity\n\n" +
                   "# Unregister the MCP server (from all scopes to clean up any stale configs):\n" +
                   "claude mcp remove --scope local UnityMCP\n" +
                   "claude mcp remove --scope user UnityMCP\n" +
                   "claude mcp remove --scope project UnityMCP\n\n" +
                   "# List registered servers:\n" +
                   "claude mcp list";
        }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "Ensure Claude CLI is installed",
            "Use Configure to add UnityMCP (or run claude mcp add UnityMCP)",
            "Restart Claude Code"
        };

        /// <summary>
        /// Removes UnityMCP registration from all Claude Code configuration scopes (local, user, project).
        /// This ensures no stale or conflicting configurations remain across different scopes.
        /// Also handles legacy "unityMCP" naming convention.
        /// </summary>
        private static void RemoveFromAllScopes(string claudePath, string projectDir, string pathPrepend)
        {
            // Remove from all three scopes to prevent stale configs causing connection issues.
            // See GitHub issue #664 - conflicting configs at different scopes can cause
            // Claude Code to connect with outdated/incorrect configuration.
            string[] scopes = { "local", "user", "project" };
            string[] names = { "UnityMCP", "unityMCP" }; // Include legacy naming

            foreach (var scope in scopes)
            {
                foreach (var name in names)
                {
                    ExecPath.TryRun(claudePath, $"mcp remove --scope {scope} {name}", projectDir, out _, out _, 5000, pathPrepend);
                }
            }
        }

        /// <summary>
        /// Sanitizes a value for safe inclusion inside a double-quoted shell argument.
        /// Escapes characters that are special within double quotes (", \, `, $, !)
        /// to prevent shell injection or argument splitting.
        /// </summary>
        private static string SanitizeShellHeaderValue(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            var sb = new System.Text.StringBuilder(value.Length);
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"':
                    case '\\':
                    case '`':
                    case '$':
                    case '!':
                        sb.Append('\\');
                        sb.Append(c);
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Extracts the package source (--from argument value) from claude mcp get output.
        /// The output format includes args like: --from "mcpforunityserver==9.0.1"
        /// </summary>
        private static string ExtractPackageSourceFromCliOutput(string cliOutput)
        {
            if (string.IsNullOrEmpty(cliOutput))
                return null;

            // Look for --from followed by the package source
            // The CLI output may have it quoted or unquoted
            int fromIndex = cliOutput.IndexOf("--from", StringComparison.OrdinalIgnoreCase);
            if (fromIndex < 0)
                return null;

            // Move past "--from" and any whitespace
            int startIndex = fromIndex + 6;
            while (startIndex < cliOutput.Length && char.IsWhiteSpace(cliOutput[startIndex]))
                startIndex++;

            if (startIndex >= cliOutput.Length)
                return null;

            // Check if value is quoted
            char quoteChar = cliOutput[startIndex];
            if (quoteChar == '"' || quoteChar == '\'')
            {
                startIndex++;
                int endIndex = cliOutput.IndexOf(quoteChar, startIndex);
                if (endIndex > startIndex)
                    return cliOutput.Substring(startIndex, endIndex - startIndex);
            }
            else
            {
                // Unquoted - read until whitespace or end of line
                int endIndex = startIndex;
                while (endIndex < cliOutput.Length && !char.IsWhiteSpace(cliOutput[endIndex]))
                    endIndex++;

                if (endIndex > startIndex)
                    return cliOutput.Substring(startIndex, endIndex - startIndex);
            }

            return null;
        }

        /// <summary>
        /// Reads Claude Code configuration directly from ~/.claude.json file.
        /// This is much faster than running `claude mcp list` which does health checks on all servers.
        /// </summary>
        private static (JObject serverConfig, string error) ReadClaudeCodeConfig(string projectDir)
        {
            try
            {
                // Find the Claude config file
                string homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string configPath = Path.Combine(homeDir, ".claude.json");

                if (!File.Exists(configPath))
                {
                    // Missing config file is "not configured", not an error
                    // (Claude Code may not be installed or just hasn't been configured yet)
                    return (null, null);
                }

                string configJson = File.ReadAllText(configPath);
                var config = JObject.Parse(configJson);

                var projects = config["projects"] as JObject;
                if (projects == null)
                {
                    return (null, null); // No projects configured
                }

                // Build a dictionary of normalized paths for quick lookup
                // Use last entry for duplicates (forward/backslash variants) as it's typically more recent
                var normalizedProjects = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
                foreach (var project in projects.Properties())
                {
                    string normalizedPath = NormalizePath(project.Name);
                    normalizedProjects[normalizedPath] = project.Value as JObject;
                }

                // Walk up the directory tree to find a matching project config
                // Claude Code may be configured at a parent directory (e.g., repo root)
                // while Unity project is in a subdirectory (e.g., TestProjects/UnityMCPTests)
                string currentDir = NormalizePath(projectDir);
                while (!string.IsNullOrEmpty(currentDir))
                {
                    if (normalizedProjects.TryGetValue(currentDir, out var projectConfig))
                    {
                        var mcpServers = projectConfig?["mcpServers"] as JObject;
                        if (mcpServers != null)
                        {
                            // Look for UnityMCP (case-insensitive)
                            foreach (var server in mcpServers.Properties())
                            {
                                if (string.Equals(server.Name, "UnityMCP", StringComparison.OrdinalIgnoreCase))
                                {
                                    return (server.Value as JObject, null);
                                }
                            }
                        }
                        // Found the project but no UnityMCP - don't continue walking up
                        return (null, null);
                    }

                    // Move up one directory
                    int lastSlash = currentDir.LastIndexOf('/');
                    if (lastSlash <= 0)
                        break;
                    currentDir = currentDir.Substring(0, lastSlash);
                }

                return (null, null); // Project not found in config
            }
            catch (Exception ex)
            {
                return (null, $"Error reading Claude config: {ex.Message}");
            }
        }

        /// <summary>
        /// Normalizes a file path for comparison (handles forward/back slashes, trailing slashes).
        /// </summary>
        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            // Replace backslashes with forward slashes and remove trailing slashes
            return path.Replace('\\', '/').TrimEnd('/');
        }

        /// <summary>
        /// Extracts the package source from Claude Code JSON config.
        /// For stdio servers, this is in the args array after "--from".
        /// </summary>
        private static string ExtractPackageSourceFromConfig(JObject serverConfig)
        {
            if (serverConfig == null)
                return null;

            var args = serverConfig["args"] as JArray;
            if (args == null)
                return null;

            // Look for --from argument (either "--from VALUE" or "--from=VALUE" format)
            bool foundFrom = false;
            foreach (var arg in args)
            {
                string argStr = arg?.ToString();
                if (argStr == null)
                    continue;

                if (foundFrom)
                {
                    // This is the package source following --from
                    return argStr;
                }

                if (argStr == "--from")
                {
                    foundFrom = true;
                }
                else if (argStr.StartsWith("--from=", StringComparison.OrdinalIgnoreCase))
                {
                    // Handle --from=VALUE format
                    return argStr.Substring(7).Trim('"', '\'');
                }
            }

            return null;
        }
    }
}
