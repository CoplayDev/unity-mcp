using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using MCPForUnity.Editor.Data;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Models;
using Newtonsoft.Json;
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
            try
            {
                string configPath = McpConfigurationHelper.GetClientConfigPath(client);
                McpConfigurationHelper.EnsureConfigDirectoryExists(configPath);

                string pythonDir = MCPServiceLocator.Paths.GetMcpServerPath();

                if (pythonDir == null || !File.Exists(Path.Combine(pythonDir, "server.py")))
                {
                    throw new InvalidOperationException("Server not found. Please use manual configuration or set server path in Advanced Settings.");
                }

                string result = client.mcpType == McpTypes.Codex
                    ? McpConfigurationHelper.ConfigureCodexClient(pythonDir, configPath, client)
                    : McpConfigurationHelper.WriteMcpConfiguration(pythonDir, configPath, client);

                if (result == "Configured successfully")
                {
                    client.SetStatus(McpStatus.Configured);
                    Debug.Log($"<b><color=#2EA3FF>MCP-FOR-UNITY</color></b>: {client.name} configured successfully");
                }
                else
                {
                    Debug.LogWarning($"Configuration completed with message: {result}");
                }

                CheckClientStatus(client);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to configure {client.name}: {ex.Message}");
                throw;
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
                    // Skip if already configured
                    CheckClientStatus(client, attemptAutoRewrite: false);
                    if (client.status == McpStatus.Configured)
                    {
                        summary.SkippedCount++;
                        summary.Messages.Add($"✓ {client.name}: Already configured");
                        continue;
                    }

                    // Check if required tools are available
                    if (client.mcpType == McpTypes.ClaudeCode)
                    {
                        if (!pathService.IsClaudeCliDetected())
                        {
                            summary.SkippedCount++;
                            summary.Messages.Add($"➜ {client.name}: Claude CLI not found");
                            continue;
                        }

                        RegisterClaudeCode();
                        summary.SuccessCount++;
                        summary.Messages.Add($"✓ {client.name}: Registered successfully");
                    }
                    else
                    {
                        // Other clients require UV
                        if (!pathService.IsUvDetected())
                        {
                            summary.SkippedCount++;
                            summary.Messages.Add($"➜ {client.name}: UV not found");
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
                string pythonDir = MCPServiceLocator.Paths.GetMcpServerPath();

                // Check configuration based on client type
                string[] args = null;
                bool configExists = false;

                switch (client.mcpType)
                {
                    case McpTypes.VSCode:
                        dynamic vsConfig = JsonConvert.DeserializeObject(configJson);
                        if (vsConfig?.servers?.unityMCP != null)
                        {
                            args = vsConfig.servers.unityMCP.args.ToObject<string[]>();
                            configExists = true;
                        }
                        else if (vsConfig?.mcp?.servers?.unityMCP != null)
                        {
                            args = vsConfig.mcp.servers.unityMCP.args.ToObject<string[]>();
                            configExists = true;
                        }
                        break;

                    case McpTypes.Codex:
                        if (CodexConfigHelper.TryParseCodexServer(configJson, out _, out var codexArgs))
                        {
                            args = codexArgs;
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
                    string configuredDir = McpConfigurationHelper.ExtractDirectoryArg(args);
                    bool matches = !string.IsNullOrEmpty(configuredDir) &&
                                   McpConfigurationHelper.PathsEqual(configuredDir, pythonDir);

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
                                ? McpConfigurationHelper.ConfigureCodexClient(pythonDir, configPath, client)
                                : McpConfigurationHelper.WriteMcpConfiguration(pythonDir, configPath, client);

                            if (rewriteResult == "Configured successfully")
                            {
                                bool debugLogsEnabled = EditorPrefs.GetBool("MCPForUnity.DebugLogs", false);
                                if (debugLogsEnabled)
                                {
                                    McpLog.Info($"Auto-updated MCP config for '{client.name}' to new path: {pythonDir}", always: false);
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
            string pythonDir = pathService.GetMcpServerPath();
            
            if (string.IsNullOrEmpty(pythonDir))
            {
                throw new InvalidOperationException("Cannot register: Python directory not found");
            }

            string claudePath = pathService.GetClaudeCliPath();
            if (string.IsNullOrEmpty(claudePath))
            {
                throw new InvalidOperationException("Claude CLI not found. Please install Claude Code first.");
            }

            string uvPath = pathService.GetUvPath() ?? "uv";
            string args = $"mcp add UnityMCP -- \"{uvPath}\" run --directory \"{pythonDir}\" server.py";
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
                    Debug.Log("<b><color=#2EA3FF>MCP-FOR-UNITY</color></b>: MCP for Unity already registered with Claude Code.");
                }
                else
                {
                    throw new InvalidOperationException($"Failed to register with Claude Code:\n{stderr}\n{stdout}");
                }
                return;
            }

            Debug.Log("<b><color=#2EA3FF>MCP-FOR-UNITY</color></b>: Successfully registered with Claude Code.");

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
                Debug.Log("<b><color=#2EA3FF>MCP-FOR-UNITY</color></b>: No MCP for Unity server found - already unregistered.");
                return;
            }

            // Remove the server
            if (ExecPath.TryRun(claudePath, "mcp remove UnityMCP", projectDir, out var stdout, out var stderr, 10000, pathPrepend))
            {
                Debug.Log("<b><color=#2EA3FF>MCP-FOR-UNITY</color></b>: MCP server successfully unregistered from Claude Code.");
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
            string pythonDir = MCPServiceLocator.Paths.GetMcpServerPath();
            string uvPath = MCPServiceLocator.Paths.GetUvPath();

            // Claude Code uses CLI commands, not JSON config
            if (client.mcpType == McpTypes.ClaudeCode)
            {
                if (string.IsNullOrEmpty(pythonDir) || string.IsNullOrEmpty(uvPath))
                {
                    return "# Error: Configuration not available - check paths in Advanced Settings";
                }

                // Show the actual command that RegisterClaudeCode() uses
                string registerCommand = $"claude mcp add UnityMCP -- \"{uvPath}\" run --directory \"{pythonDir}\" server.py";

                return "# Register the MCP server with Claude Code:\n" +
                       $"{registerCommand}\n\n" +
                       "# Unregister the MCP server:\n" +
                       "claude mcp remove UnityMCP\n\n" +
                       "# List registered servers:\n" +
                       "claude mcp list # Only works when claude is run in the project's directory";
            }

            if (string.IsNullOrEmpty(pythonDir) || string.IsNullOrEmpty(uvPath))
                return "{ \"error\": \"Configuration not available - check paths in Advanced Settings\" }";

            try
            {
                if (client.mcpType == McpTypes.Codex)
                {
                    return CodexConfigHelper.BuildCodexServerBlock(uvPath,
                        McpConfigurationHelper.ResolveServerDirectory(pythonDir, null));
                }
                else
                {
                    return ConfigJsonBuilder.BuildManualConfigJson(uvPath, pythonDir, client);
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
                string configPath = McpConfigurationHelper.GetClientConfigPath(client);

                if (!File.Exists(configPath))
                {
                    client.SetStatus(McpStatus.NotConfigured);
                    return;
                }

                string configJson = File.ReadAllText(configPath);
                dynamic claudeConfig = JsonConvert.DeserializeObject(configJson);

                if (claudeConfig?.mcpServers != null)
                {
                    var servers = claudeConfig.mcpServers;
                    // Only check for UnityMCP (fixed - removed candidate hacks)
                    if (servers.UnityMCP != null)
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
    }
}
