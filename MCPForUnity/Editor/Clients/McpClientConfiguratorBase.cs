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
                throw new InvalidOperationException("uv not found. Install uv/uvx or set the override in Advanced Settings.");
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
                        configExists = true;
                    }
                }

                if (!configExists)
                {
                    client.SetStatus(McpStatus.MissingConfig);
                    return client.status;
                }

                bool matches = false;
                if (args != null && args.Length > 0)
                {
                    string expectedUvxUrl = AssetPathUtility.GetMcpServerPackageSource();
                    string configuredUvxUrl = McpConfigurationHelper.ExtractUvxUrl(args);
                    matches = !string.IsNullOrEmpty(configuredUvxUrl) &&
                              McpConfigurationHelper.PathsEqual(configuredUvxUrl, expectedUvxUrl);
                }
                else if (!string.IsNullOrEmpty(configuredUrl))
                {
                    string expectedUrl = HttpEndpointUtility.GetMcpRpcUrl();
                    matches = UrlsEqual(configuredUrl, expectedUrl);
                }

                if (matches)
                {
                    client.SetStatus(McpStatus.Configured);
                    return client.status;
                }

                if (attemptAutoRewrite)
                {
                    var result = McpConfigurationHelper.WriteMcpConfiguration(path, client);
                    if (result == "Configured successfully")
                    {
                        client.SetStatus(McpStatus.Configured);
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
                    return client.status;
                }

                string toml = File.ReadAllText(path);
                if (CodexConfigHelper.TryParseCodexServer(toml, out _, out var args, out var url))
                {
                    bool matches = false;
                    if (!string.IsNullOrEmpty(url))
                    {
                        matches = UrlsEqual(url, HttpEndpointUtility.GetMcpRpcUrl());
                    }
                    else if (args != null && args.Length > 0)
                    {
                        string expected = AssetPathUtility.GetMcpServerPackageSource();
                        string configured = McpConfigurationHelper.ExtractUvxUrl(args);
                        matches = !string.IsNullOrEmpty(configured) &&
                                  McpConfigurationHelper.PathsEqual(configured, expected);
                    }

                    if (matches)
                    {
                        client.SetStatus(McpStatus.Configured);
                        return client.status;
                    }
                }

                if (attemptAutoRewrite)
                {
                    string result = McpConfigurationHelper.ConfigureCodexClient(path, client);
                    if (result == "Configured successfully")
                    {
                        client.SetStatus(McpStatus.Configured);
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
        public override string GetConfigureActionLabel() => client.status == McpStatus.Configured ? "Unregister" : "Register";

        public override string GetConfigPath() => "Managed via Claude CLI";

        /// <summary>
        /// Checks the Claude CLI registration status.
        /// MUST be called from the main Unity thread due to EditorPrefs and Application.dataPath access.
        /// </summary>
        public override McpStatus CheckStatus(bool attemptAutoRewrite = true)
        {
            // Capture main-thread-only values before delegating to thread-safe method
            string projectDir = Path.GetDirectoryName(Application.dataPath);
            bool useHttpTransport = EditorPrefs.GetBool(EditorPrefKeys.UseHttpTransport, true);
            // Resolve claudePath on the main thread (EditorPrefs access)
            string claudePath = MCPServiceLocator.Paths.GetClaudeCliPath();
            return CheckStatusWithProjectDir(projectDir, useHttpTransport, claudePath, attemptAutoRewrite);
        }

        /// <summary>
        /// Internal thread-safe version of CheckStatus.
        /// Can be called from background threads because all main-thread-only values are passed as parameters.
        /// projectDir, useHttpTransport, and claudePath are REQUIRED (non-nullable) to enforce thread safety at compile time.
        /// </summary>
        internal McpStatus CheckStatusWithProjectDir(string projectDir, bool useHttpTransport, string claudePath, bool attemptAutoRewrite = true)
        {
            try
            {
                if (string.IsNullOrEmpty(claudePath))
                {
                    client.SetStatus(McpStatus.NotConfigured, "Claude CLI not found");
                    return client.status;
                }

                // projectDir is required - no fallback to Application.dataPath
                if (string.IsNullOrEmpty(projectDir))
                {
                    throw new ArgumentNullException(nameof(projectDir), "Project directory must be provided for thread-safe execution");
                }

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

                // Check if UnityMCP exists
                if (ExecPath.TryRun(claudePath, "mcp list", projectDir, out var listStdout, out var listStderr, 10000, pathPrepend))
                {
                    if (!string.IsNullOrEmpty(listStdout) && listStdout.IndexOf("UnityMCP", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        // UnityMCP is registered - now verify transport mode matches
                        // useHttpTransport parameter is required (non-nullable) to ensure thread safety
                        bool currentUseHttp = useHttpTransport;

                        // Get detailed info about the registration to check transport type
                        if (ExecPath.TryRun(claudePath, "mcp get UnityMCP", projectDir, out var getStdout, out var getStderr, 7000, pathPrepend))
                        {
                            // Parse the output to determine registered transport mode
                            // The CLI output format contains "Type: http" or "Type: stdio"
                            bool registeredWithHttp = getStdout.Contains("Type: http", StringComparison.OrdinalIgnoreCase);
                            bool registeredWithStdio = getStdout.Contains("Type: stdio", StringComparison.OrdinalIgnoreCase);

                            // Check for transport mismatch
                            if ((currentUseHttp && registeredWithStdio) || (!currentUseHttp && registeredWithHttp))
                            {
                                string registeredTransport = registeredWithHttp ? "HTTP" : "stdio";
                                string currentTransport = currentUseHttp ? "HTTP" : "stdio";
                                string errorMsg = $"Transport mismatch: Claude Code is registered with {registeredTransport} but current setting is {currentTransport}. Click Configure to re-register.";
                                client.SetStatus(McpStatus.Error, errorMsg);
                                McpLog.Warn(errorMsg);
                                return client.status;
                            }
                        }

                        client.SetStatus(McpStatus.Configured);
                        return client.status;
                    }
                }

                client.SetStatus(McpStatus.NotConfigured);
            }
            catch (Exception ex)
            {
                client.SetStatus(McpStatus.Error, ex.Message);
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

        private void Register()
        {
            var pathService = MCPServiceLocator.Paths;
            string claudePath = pathService.GetClaudeCliPath();
            if (string.IsNullOrEmpty(claudePath))
            {
                throw new InvalidOperationException("Claude CLI not found. Please install Claude Code first.");
            }

            bool useHttpTransport = EditorPrefs.GetBool(EditorPrefKeys.UseHttpTransport, true);

            string args;
            if (useHttpTransport)
            {
                string httpUrl = HttpEndpointUtility.GetMcpRpcUrl();
                args = $"mcp add --transport http UnityMCP {httpUrl}";
            }
            else
            {
                var (uvxPath, gitUrl, packageName) = AssetPathUtility.GetUvxCommandParts();
                // Use central helper that checks both DevModeForceServerRefresh AND local path detection.
                string devFlags = AssetPathUtility.ShouldForceUvxRefresh() ? "--no-cache --refresh " : string.Empty;
                args = $"mcp add --transport stdio UnityMCP -- \"{uvxPath}\" {devFlags}--from \"{gitUrl}\" {packageName}";
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

            // Check if UnityMCP already exists and remove it first to ensure clean registration
            // This ensures we always use the current transport mode setting
            bool serverExists = ExecPath.TryRun(claudePath, "mcp get UnityMCP", projectDir, out _, out _, 7000, pathPrepend);
            if (serverExists)
            {
                McpLog.Info("Existing UnityMCP registration found - removing to ensure transport mode is up-to-date");
                if (!ExecPath.TryRun(claudePath, "mcp remove UnityMCP", projectDir, out var removeStdout, out var removeStderr, 10000, pathPrepend))
                {
                    McpLog.Warn($"Failed to remove existing UnityMCP registration: {removeStderr}. Attempting to register anyway...");
                }
            }

            // Now add the registration with the current transport mode
            if (!ExecPath.TryRun(claudePath, args, projectDir, out var stdout, out var stderr, 15000, pathPrepend))
            {
                throw new InvalidOperationException($"Failed to register with Claude Code:\n{stderr}\n{stdout}");
            }

            McpLog.Info($"Successfully registered with Claude Code using {(useHttpTransport ? "HTTP" : "stdio")} transport.");

            // Set status to Configured immediately after successful registration
            // The UI will trigger an async verification check separately to avoid blocking
            client.SetStatus(McpStatus.Configured);
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

            bool serverExists = ExecPath.TryRun(claudePath, "mcp get UnityMCP", projectDir, out _, out _, 7000, pathPrepend);

            if (!serverExists)
            {
                client.SetStatus(McpStatus.NotConfigured);
                McpLog.Info("No MCP for Unity server found - already unregistered.");
                return;
            }

            if (ExecPath.TryRun(claudePath, "mcp remove UnityMCP", projectDir, out var stdout, out var stderr, 10000, pathPrepend))
            {
                McpLog.Info("MCP server successfully unregistered from Claude Code.");
            }
            else
            {
                throw new InvalidOperationException($"Failed to unregister: {stderr}");
            }

            client.SetStatus(McpStatus.NotConfigured);
            // Status is already set - no need for blocking CheckStatus() call
        }

        public override string GetManualSnippet()
        {
            string uvxPath = MCPServiceLocator.Paths.GetUvxPath();
            bool useHttpTransport = EditorPrefs.GetBool(EditorPrefKeys.UseHttpTransport, true);

            if (useHttpTransport)
            {
                string httpUrl = HttpEndpointUtility.GetMcpRpcUrl();
                return "# Register the MCP server with Claude Code:\n" +
                       $"claude mcp add --transport http UnityMCP {httpUrl}\n\n" +
                       "# Unregister the MCP server:\n" +
                       "claude mcp remove UnityMCP\n\n" +
                       "# List registered servers:\n" +
                       "claude mcp list # Only works when claude is run in the project's directory";
            }

            if (string.IsNullOrEmpty(uvxPath))
            {
                return "# Error: Configuration not available - check paths in Advanced Settings";
            }

            string packageSource = AssetPathUtility.GetMcpServerPackageSource();
            // Use central helper that checks both DevModeForceServerRefresh AND local path detection.
            string devFlags = AssetPathUtility.ShouldForceUvxRefresh() ? "--no-cache --refresh " : string.Empty;

            return "# Register the MCP server with Claude Code:\n" +
                   $"claude mcp add --transport stdio UnityMCP -- \"{uvxPath}\" {devFlags}--from \"{packageSource}\" mcp-for-unity\n\n" +
                   "# Unregister the MCP server:\n" +
                   "claude mcp remove UnityMCP\n\n" +
                   "# List registered servers:\n" +
                   "claude mcp list # Only works when claude is run in the project's directory";
        }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "Ensure Claude CLI is installed",
            "Use Register to add UnityMCP (or run claude mcp add UnityMCP)",
            "Restart Claude Code"
        };
    }
}
