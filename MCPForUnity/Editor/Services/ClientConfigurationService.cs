using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using MCPForUnity.Editor.Data;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Implementation of client configuration service
    /// </summary>
    public class ClientConfigurationService : IClientConfigurationService
    {
        private readonly Data.McpClients mcpClients = new();

        public void ConfigureClient(McpClient client)
        {
            var pathService = MCPServiceLocator.Paths;
            string uvxPath = pathService.GetUvxPath();

            string configPath = McpConfigurationHelper.GetClientConfigPath(client);
            McpConfigurationHelper.EnsureConfigDirectoryExists(configPath);

            string result = client.mcpType == McpTypes.Codex
                ? McpConfigurationHelper.ConfigureCodexClient(configPath, client)
                : McpConfigurationHelper.WriteMcpConfiguration(configPath, client);

            if (result == "Configured successfully")
            {
                client.SetStatus(McpStatus.Configured);
            }
            else
            {
                client.SetStatus(McpStatus.NotConfigured);
                throw new InvalidOperationException($"Configuration failed: {result}");
            }
        }

        public ClientConfigurationSummary ConfigureAllDetectedClients()
        {
            var summary = new ClientConfigurationSummary();
            var pathService = MCPServiceLocator.Paths;

            foreach (var client in mcpClients.clients)
            {
                try
                {
                    // Always re-run configuration so core fields stay current
                    CheckClientStatus(client, attemptAutoRewrite: false);

                    // Check if required tools are available
                    if (client.mcpType == McpTypes.ClaudeCode)
                    {
                        if (!pathService.IsClaudeCliDetected())
                        {
                            summary.SkippedCount++;
                            summary.Messages.Add($"➜ {client.name}: Claude CLI not found");
                            continue;
                        }

                        // Force a fresh registration so transport settings stay current
                        UnregisterClaudeCode();
                        RegisterClaudeCode();
                        summary.SuccessCount++;
                        summary.Messages.Add($"✓ {client.name}: Re-registered successfully");
                    }
                    else
                    {
                        // Other clients require uvx
                        if (!pathService.IsUvxDetected())
                        {
                            summary.SkippedCount++;
                            summary.Messages.Add($"➜ {client.name}: uvx not found");
                            continue;
                        }

                        ConfigureClient(client);
                        summary.SuccessCount++;
                        summary.Messages.Add($"✓ {client.name}: Configured successfully");
                    }
                }
                catch (Exception ex)
                {
                    summary.FailureCount++;
                    summary.Messages.Add($"⚠ {client.name}: {ex.Message}");
                }
            }

            return summary;
        }

        public bool CheckClientStatus(McpClient client, bool attemptAutoRewrite = true)
        {
            var previousStatus = client.status;

            try
            {
                // Special handling for Claude Code
                if (client.mcpType == McpTypes.ClaudeCode)
                {
                    CheckClaudeCodeConfiguration(client);
                    return client.status != previousStatus;
                }

                string configPath = McpConfigurationHelper.GetClientConfigPath(client);

                if (!File.Exists(configPath))
                {
                    client.SetStatus(McpStatus.NotConfigured);
                    return client.status != previousStatus;
                }

                string configJson = File.ReadAllText(configPath);
                // Check configuration based on client type
                string[] args = null;
                string configuredUrl = null;
                bool configExists = false;

                switch (client.mcpType)
                {
                    case McpTypes.VSCode:
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
                        break;

                    case McpTypes.Codex:
                        if (CodexConfigHelper.TryParseCodexServer(configJson, out _, out var codexArgs, out var codexUrl))
                        {
                            args = codexArgs;
                            configuredUrl = codexUrl;
                            configExists = true;
                        }
                        break;

                    default:
                        McpConfig standardConfig = JsonConvert.DeserializeObject<McpConfig>(configJson);
                        if (standardConfig?.mcpServers?.unityMCP != null)
                        {
                            args = standardConfig.mcpServers.unityMCP.args;
                            configExists = true;
                        }
                        break;
                }

                if (configExists)
                {
                    bool matches = false;

                    if (args != null && args.Length > 0)
                    {
                        string expectedUvxUrl = AssetPathUtility.GetMcpServerGitUrl();
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
                    }
                    else if (attemptAutoRewrite)
                    {
                        // Attempt auto-rewrite if path mismatch detected
                        try
                        {
                            string rewriteResult = client.mcpType == McpTypes.Codex
                                ? McpConfigurationHelper.ConfigureCodexClient(configPath, client)
                                : McpConfigurationHelper.WriteMcpConfiguration(configPath, client);

                            if (rewriteResult == "Configured successfully")
                            {
                                bool debugLogsEnabled = EditorPrefs.GetBool(EditorPrefKeys.DebugLogs, false);
                                if (debugLogsEnabled)
                                {
                                    string targetDescriptor = args != null && args.Length > 0
                                        ? AssetPathUtility.GetMcpServerGitUrl()
                                        : HttpEndpointUtility.GetMcpRpcUrl();
                                    McpLog.Info($"Auto-updated MCP config for '{client.name}' to new version: {targetDescriptor}", always: false);
                                }
                                client.SetStatus(McpStatus.Configured);
                            }
                            else
                            {
                                client.SetStatus(McpStatus.IncorrectPath);
                            }
                        }
                        catch
                        {
                            client.SetStatus(McpStatus.IncorrectPath);
                        }
                    }
                    else
                    {
                        client.SetStatus(McpStatus.IncorrectPath);
                    }
                }
                else
                {
                    client.SetStatus(McpStatus.MissingConfig);
                }
            }
            catch (Exception ex)
            {
                client.SetStatus(McpStatus.Error, ex.Message);
            }

            return client.status != previousStatus;
        }

        public void RegisterClaudeCode()
        {
            var pathService = MCPServiceLocator.Paths;
            string claudePath = pathService.GetClaudeCliPath();
            if (string.IsNullOrEmpty(claudePath))
            {
                throw new InvalidOperationException("Claude CLI not found. Please install Claude Code first.");
            }

            // Check transport preference
            bool useHttpTransport = EditorPrefs.GetBool(EditorPrefKeys.UseHttpTransport, true);

            string args;
            if (useHttpTransport)
            {
                // HTTP mode: Use --transport http with URL
                string httpUrl = HttpEndpointUtility.GetMcpRpcUrl();
                args = $"mcp add --transport http UnityMCP {httpUrl}";
            }
            else
            {
                // Stdio mode: Use command with uvx
                var (uvxPath, gitUrl, packageName) = AssetPathUtility.GetUvxCommandParts();
                args = $"mcp add --transport stdio UnityMCP -- \"{uvxPath}\" --from \"{gitUrl}\" {packageName}";
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

            // Add the directory containing Claude CLI to PATH (for node/nvm scenarios)
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

            if (!ExecPath.TryRun(claudePath, args, projectDir, out var stdout, out var stderr, 15000, pathPrepend))
            {
                string combined = ($"{stdout}\n{stderr}") ?? string.Empty;
                if (combined.IndexOf("already exists", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    McpLog.Info("MCP for Unity already registered with Claude Code.");
                }
                else
                {
                    throw new InvalidOperationException($"Failed to register with Claude Code:\n{stderr}\n{stdout}");
                }
                return;
            }

            McpLog.Info("Successfully registered with Claude Code.");

            // Update status
            var claudeClient = mcpClients.clients.FirstOrDefault(c => c.mcpType == McpTypes.ClaudeCode);
            if (claudeClient != null)
            {
                CheckClaudeCodeConfiguration(claudeClient);
            }
        }

        public void UnregisterClaudeCode()
        {
            var pathService = MCPServiceLocator.Paths;
            string claudePath = pathService.GetClaudeCliPath();

            if (string.IsNullOrEmpty(claudePath))
            {
                throw new InvalidOperationException("Claude CLI not found. Please install Claude Code first.");
            }

            string projectDir = Path.GetDirectoryName(Application.dataPath);
            string pathPrepend = Application.platform == RuntimePlatform.OSXEditor
                ? "/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin"
                : null;

            // Check if UnityMCP server exists (fixed - only check for "UnityMCP")
            bool serverExists = ExecPath.TryRun(claudePath, "mcp get UnityMCP", projectDir, out _, out _, 7000, pathPrepend);

            if (!serverExists)
            {
                // Nothing to unregister
                var claudeClient = mcpClients.clients.FirstOrDefault(c => c.mcpType == McpTypes.ClaudeCode);
                if (claudeClient != null)
                {
                    claudeClient.SetStatus(McpStatus.NotConfigured);
                }
                McpLog.Info("No MCP for Unity server found - already unregistered.");
                return;
            }

            // Remove the server
            if (ExecPath.TryRun(claudePath, "mcp remove UnityMCP", projectDir, out var stdout, out var stderr, 10000, pathPrepend))
            {
                McpLog.Info("MCP server successfully unregistered from Claude Code.");
            }
            else
            {
                throw new InvalidOperationException($"Failed to unregister: {stderr}");
            }

            // Update status
            var client = mcpClients.clients.FirstOrDefault(c => c.mcpType == McpTypes.ClaudeCode);
            if (client != null)
            {
                client.SetStatus(McpStatus.NotConfigured);
                CheckClaudeCodeConfiguration(client);
            }
        }

        public string GetConfigPath(McpClient client)
        {
            // Claude Code is managed via CLI, not config files
            if (client.mcpType == McpTypes.ClaudeCode)
            {
                return "Not applicable (managed via Claude CLI)";
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return client.windowsConfigPath;
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return client.macConfigPath;
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return client.linuxConfigPath;

            return "Unknown";
        }

        public string GenerateConfigJson(McpClient client)
        {
            string uvxPath = MCPServiceLocator.Paths.GetUvxPath();

            // Claude Code uses CLI commands, not JSON config
            if (client.mcpType == McpTypes.ClaudeCode)
            {
                // Check transport preference
                bool useHttpTransport = EditorPrefs.GetBool(EditorPrefKeys.UseHttpTransport, true);

                string registerCommand;
                if (useHttpTransport)
                {
                    // HTTP mode
                    string httpUrl = HttpEndpointUtility.GetMcpRpcUrl();
                    registerCommand = $"claude mcp add --transport http UnityMCP {httpUrl}";
                }
                else
                {
                    // Stdio mode
                    if (string.IsNullOrEmpty(uvxPath))
                    {
                        return "# Error: Configuration not available - check paths in Advanced Settings";
                    }

                    string gitUrl = AssetPathUtility.GetMcpServerGitUrl();
                    registerCommand = $"claude mcp add --transport stdio UnityMCP -- \"{uvxPath}\" --from \"{gitUrl}\" mcp-for-unity";
                }

                return "# Register the MCP server with Claude Code:\n" +
                       $"{registerCommand}\n\n" +
                       "# Unregister the MCP server:\n" +
                       "claude mcp remove UnityMCP\n\n" +
                       "# List registered servers:\n" +
                       "claude mcp list # Only works when claude is run in the project's directory";
            }

            if (string.IsNullOrEmpty(uvxPath))
                return "{ \"error\": \"Configuration not available - check paths in Advanced Settings\" }";

            try
            {
                if (client.mcpType == McpTypes.Codex)
                {
                    return CodexConfigHelper.BuildCodexServerBlock(uvxPath);
                }
                else
                {
                    return ConfigJsonBuilder.BuildManualConfigJson(uvxPath, client);
                }
            }
            catch (Exception ex)
            {
                return $"{{ \"error\": \"{ex.Message}\" }}";
            }
        }

        public string GetInstallationSteps(McpClient client)
        {
            string baseSteps = client.mcpType switch
            {
                McpTypes.ClaudeDesktop =>
                    "1. Open Claude Desktop\n" +
                    "2. Go to Settings > Developer > Edit Config\n" +
                    "   OR open the config file at the path above\n" +
                    "3. Paste the configuration JSON\n" +
                    "4. Save and restart Claude Desktop",

                McpTypes.Cursor =>
                    "1. Open Cursor\n" +
                    "2. Go to File > Preferences > Cursor Settings > MCP > Add new global MCP server\n" +
                    "   OR open the config file at the path above\n" +
                    "3. Paste the configuration JSON\n" +
                    "4. Save and restart Cursor",

                McpTypes.Windsurf =>
                    "1. Open Windsurf\n" +
                    "2. Go to File > Preferences > Windsurf Settings > MCP > Manage MCPs > View raw config\n" +
                    "   OR open the config file at the path above\n" +
                    "3. Paste the configuration JSON\n" +
                    "4. Save and restart Windsurf",

                McpTypes.VSCode =>
                    "1. Ensure VSCode and GitHub Copilot extension are installed\n" +
                    "2. Open or create mcp.json at the path above\n" +
                    "3. Paste the configuration JSON\n" +
                    "4. Save and restart VSCode",

                McpTypes.Kiro =>
                    "1. Open Kiro\n" +
                    "2. Go to File > Settings > Settings > Search for \"MCP\" > Open Workspace MCP Config\n" +
                    "   OR open the config file at the path above\n" +
                    "3. Paste the configuration JSON\n" +
                    "4. Save and restart Kiro",

                McpTypes.Codex =>
                    "1. Run 'codex config edit' in a terminal\n" +
                    "   OR open the config file at the path above\n" +
                    "2. Paste the configuration TOML\n" +
                    "3. Save and restart Codex",

                McpTypes.ClaudeCode =>
                    "1. Ensure Claude CLI is installed\n" +
                    "2. Use the Register button to register automatically\n" +
                    "   OR manually run: claude mcp add UnityMCP\n" +
                    "3. Restart Claude Code",

                McpTypes.Trae =>
                    "1. Open Trae and go to Settings > MCP\n" +
                    "2. Select Add Server > Add Manually\n" +
                    "3. Paste the JSON or point to the mcp.json file\n" +
                    "   Windows: %AppData%\\Trae\\mcp.json\n" +
                    "   macOS: ~/Library/Application Support/Trae/mcp.json\n" +
                    "   Linux: ~/.config/Trae/mcp.json\n" +
                    "4. For local servers, Node.js (npx) or uvx must be installed\n" +
                    "5. Save and restart Trae",

                _ => "Configuration steps not available for this client."
            };

            return baseSteps;
        }

        private void CheckClaudeCodeConfiguration(McpClient client)
        {
            try
            {
                var pathService = MCPServiceLocator.Paths;
                string claudePath = pathService.GetClaudeCliPath();
                
                if (string.IsNullOrEmpty(claudePath))
                {
                    client.SetStatus(McpStatus.NotConfigured, "Claude CLI not found");
                    return;
                }

                // Use 'claude mcp list' to check if UnityMCP is registered
                string args = "mcp list";
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

                // Add the directory containing Claude CLI to PATH
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

                if (ExecPath.TryRun(claudePath, args, projectDir, out var stdout, out var stderr, 10000, pathPrepend))
                {
                    // Check if UnityMCP is in the output
                    if (!string.IsNullOrEmpty(stdout) && stdout.IndexOf("UnityMCP", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        client.SetStatus(McpStatus.Configured);
                        return;
                    }
                }

                client.SetStatus(McpStatus.NotConfigured);
            }
            catch (Exception ex)
            {
                client.SetStatus(McpStatus.Error, ex.Message);
            }
        }

        private static bool UrlsEqual(string a, string b)
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
}
