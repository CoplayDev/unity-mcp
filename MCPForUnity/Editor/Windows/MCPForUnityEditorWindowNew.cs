using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using MCPForUnity.Editor.Data;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Models;

namespace MCPForUnity.Editor.Windows
{
    public class MCPForUnityEditorWindowNew : EditorWindow
    {
        // Protocol enum for future HTTP support
        private enum ConnectionProtocol
        {
            Stdio,
            // HTTPStreaming // Future
        }

        // UI Elements
        private Toggle debugLogsToggle;
        private EnumField validationLevelField;
        private Label validationDescription;
        private EnumField protocolDropdown;
        private TextField unityPortField;
        private TextField serverPortField;
        private VisualElement statusIndicator;
        private Label connectionStatusLabel;
        private Button connectionToggleButton;
        private Button rebuildServerButton;
        private DropdownField clientDropdown;
        private VisualElement clientStatusIndicator;
        private Label clientStatusLabel;
        private Button configureButton;
        private Foldout manualConfigFoldout;
        private TextField configPathField;
        private Button copyPathButton;
        private Button openFileButton;
        private TextField configJsonField;
        private Button copyJsonButton;
        private Label installationStepsLabel;

        // Data
        private readonly McpClients mcpClients = new();
        private int selectedClientIndex = 0;
        private ValidationLevel currentValidationLevel = ValidationLevel.Standard;

        // Validation levels matching the existing enum
        private enum ValidationLevel
        {
            Basic,
            Standard,
            Comprehensive,
            Strict
        }

        public static void ShowWindow()
        {
            var window = GetWindow<MCPForUnityEditorWindowNew>("MCP For Unity");
            window.minSize = new Vector2(500, 600);
        }

        public void CreateGUI()
        {
            // Load UXML
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Packages/com.coplaydev.unity-mcp/Editor/Windows/MCPForUnityEditorWindowNew.uxml"
            );
            visualTree.CloneTree(rootVisualElement);

            // Load USS
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.coplaydev.unity-mcp/Editor/Windows/MCPForUnityEditorWindowNew.uss"
            );
            rootVisualElement.styleSheets.Add(styleSheet);

            // Cache UI elements
            CacheUIElements();

            // Initialize UI
            InitializeUI();

            // Register callbacks
            RegisterCallbacks();

            // Initial update
            UpdateConnectionStatus();
            UpdateClientStatus();
        }

        private void CacheUIElements()
        {
            debugLogsToggle = rootVisualElement.Q<Toggle>("debug-logs-toggle");
            validationLevelField = rootVisualElement.Q<EnumField>("validation-level");
            validationDescription = rootVisualElement.Q<Label>("validation-description");
            protocolDropdown = rootVisualElement.Q<EnumField>("protocol-dropdown");
            unityPortField = rootVisualElement.Q<TextField>("unity-port");
            serverPortField = rootVisualElement.Q<TextField>("server-port");
            statusIndicator = rootVisualElement.Q<VisualElement>("status-indicator");
            connectionStatusLabel = rootVisualElement.Q<Label>("connection-status");
            connectionToggleButton = rootVisualElement.Q<Button>("connection-toggle");
            rebuildServerButton = rootVisualElement.Q<Button>("rebuild-server-button");
            clientDropdown = rootVisualElement.Q<DropdownField>("client-dropdown");
            clientStatusIndicator = rootVisualElement.Q<VisualElement>("client-status-indicator");
            clientStatusLabel = rootVisualElement.Q<Label>("client-status");
            configureButton = rootVisualElement.Q<Button>("configure-button");
            manualConfigFoldout = rootVisualElement.Q<Foldout>("manual-config-foldout");
            configPathField = rootVisualElement.Q<TextField>("config-path");
            copyPathButton = rootVisualElement.Q<Button>("copy-path-button");
            openFileButton = rootVisualElement.Q<Button>("open-file-button");
            configJsonField = rootVisualElement.Q<TextField>("config-json");
            copyJsonButton = rootVisualElement.Q<Button>("copy-json-button");
            installationStepsLabel = rootVisualElement.Q<Label>("installation-steps");
        }

        private void InitializeUI()
        {
            // Settings Section
            debugLogsToggle.value = EditorPrefs.GetBool("MCPForUnity.DebugLogs", false);

            validationLevelField.Init(ValidationLevel.Standard);
            int savedLevel = EditorPrefs.GetInt("MCPForUnity.ValidationLevel", 1);
            currentValidationLevel = (ValidationLevel)Mathf.Clamp(savedLevel, 0, 3);
            validationLevelField.value = currentValidationLevel;
            UpdateValidationDescription();

            // Connection Section
            protocolDropdown.Init(ConnectionProtocol.Stdio);
            protocolDropdown.SetEnabled(false); // Disabled for now, only stdio supported

            unityPortField.value = MCPForUnityBridge.GetCurrentPort().ToString();
            serverPortField.value = "6500";

            // Client Configuration
            var clientNames = mcpClients.clients.Select(c => c.name).ToList();
            clientDropdown.choices = clientNames;
            if (clientNames.Count > 0)
            {
                clientDropdown.index = 0;
            }

            // Manual config starts collapsed
            manualConfigFoldout.value = false;
        }

        private void RegisterCallbacks()
        {
            // Settings callbacks
            debugLogsToggle.RegisterValueChangedCallback(evt =>
            {
                EditorPrefs.SetBool("MCPForUnity.DebugLogs", evt.newValue);
            });

            validationLevelField.RegisterValueChangedCallback(evt =>
            {
                currentValidationLevel = (ValidationLevel)evt.newValue;
                EditorPrefs.SetInt("MCPForUnity.ValidationLevel", (int)currentValidationLevel);
                UpdateValidationDescription();
            });

            // Connection callbacks
            connectionToggleButton.clicked += OnConnectionToggleClicked;
            rebuildServerButton.clicked += OnRebuildServerClicked;

            // Client callbacks
            clientDropdown.RegisterValueChangedCallback(evt =>
            {
                selectedClientIndex = clientDropdown.index;
                UpdateClientStatus();
                UpdateManualConfiguration();
            });

            configureButton.clicked += OnConfigureClicked;
            copyPathButton.clicked += OnCopyPathClicked;
            openFileButton.clicked += OnOpenFileClicked;
            copyJsonButton.clicked += OnCopyJsonClicked;

            // Update connection status periodically
            EditorApplication.update += UpdateConnectionStatus;
        }

        private void OnDisable()
        {
            EditorApplication.update -= UpdateConnectionStatus;
        }

        private void UpdateValidationDescription()
        {
            validationDescription.text = GetValidationLevelDescription((int)currentValidationLevel);
        }

        private string GetValidationLevelDescription(int index)
        {
            return index switch
            {
                0 => "Only basic syntax checks (braces, quotes, comments)",
                1 => "Syntax checks + Unity best practices and warnings",
                2 => "All checks + semantic analysis and performance warnings",
                3 => "Full semantic validation with namespace/type resolution (requires Roslyn)",
                _ => "Standard validation"
            };
        }

        private void UpdateConnectionStatus()
        {
            bool isRunning = MCPForUnityBridge.IsRunning;

            if (isRunning)
            {
                connectionStatusLabel.text = "Connected";
                statusIndicator.RemoveFromClassList("disconnected");
                statusIndicator.AddToClassList("connected");
                connectionToggleButton.text = "Stop";
            }
            else
            {
                connectionStatusLabel.text = "Disconnected";
                statusIndicator.RemoveFromClassList("connected");
                statusIndicator.AddToClassList("disconnected");
                connectionToggleButton.text = "Start";
            }

            // Update ports
            unityPortField.value = MCPForUnityBridge.GetCurrentPort().ToString();
        }

        private void UpdateClientStatus()
        {
            if (selectedClientIndex < 0 || selectedClientIndex >= mcpClients.clients.Count)
                return;

            var client = mcpClients.clients[selectedClientIndex];
            CheckMcpConfiguration(client);

            clientStatusLabel.text = client.GetStatusDisplayString();

            // Update status indicator color
            clientStatusIndicator.RemoveFromClassList("configured");
            clientStatusIndicator.RemoveFromClassList("not-configured");
            clientStatusIndicator.RemoveFromClassList("warning");

            switch (client.status)
            {
                case McpStatus.Configured:
                case McpStatus.Running:
                case McpStatus.Connected:
                    clientStatusIndicator.AddToClassList("configured");
                    break;
                case McpStatus.IncorrectPath:
                case McpStatus.CommunicationError:
                case McpStatus.NoResponse:
                    clientStatusIndicator.AddToClassList("warning");
                    break;
                default:
                    clientStatusIndicator.AddToClassList("not-configured");
                    break;
            }
        }

        private void UpdateManualConfiguration()
        {
            if (selectedClientIndex < 0 || selectedClientIndex >= mcpClients.clients.Count)
                return;

            var client = mcpClients.clients[selectedClientIndex];

            // Get config path
            string configPath = GetConfigPathForClient(client);
            configPathField.value = configPath;

            // Get config JSON
            string configJson = GenerateConfigJson(client);
            configJsonField.value = configJson;

            // Get installation steps
            string steps = GetInstallationSteps(client);
            installationStepsLabel.text = steps;
        }

        private string GetConfigPathForClient(McpClient client)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return client.windowsConfigPath;
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return client.macConfigPath;
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return client.linuxConfigPath;

            return "Unknown";
        }

        private string GenerateConfigJson(McpClient client)
        {
            string pythonDir = FindPackagePythonDirectory();
            string uvPath = FindUvPath();

            if (string.IsNullOrEmpty(pythonDir) || string.IsNullOrEmpty(uvPath))
                return "{ \"error\": \"Configuration not available\" }";

            try
            {
                // Use the existing config builder
                if (client.mcpType == McpTypes.Codex)
                {
                    return Helpers.CodexConfigHelper.BuildCodexServerBlock(uvPath,
                        Helpers.McpConfigFileHelper.ResolveServerDirectory(pythonDir, null));
                }
                else
                {
                    return Helpers.ConfigJsonBuilder.BuildManualConfigJson(uvPath, pythonDir, client);
                }
            }
            catch (Exception ex)
            {
                return $"{{ \"error\": \"{ex.Message}\" }}";
            }
        }

        private string GetInstallationSteps(McpClient client)
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
                    "2. Use the Configure button to register automatically\n" +
                    "   OR manually edit ~/.claude.json\n" +
                    "3. Restart Claude Code",

                _ => "Configuration steps not available for this client."
            };

            return baseSteps;
        }

        // Button callbacks
        private void OnConnectionToggleClicked()
        {
            if (MCPForUnityBridge.IsRunning)
            {
                MCPForUnityBridge.Stop();
            }
            else
            {
                MCPForUnityBridge.StartAutoConnect();
            }
            UpdateConnectionStatus();
        }

        private void OnRebuildServerClicked()
        {
            try
            {
                ServerInstaller.RebuildMcpServer();
                Debug.Log("MCP Server rebuild initiated");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to rebuild server: {ex.Message}");
            }
        }

        private void OnConfigureClicked()
        {
            if (selectedClientIndex < 0 || selectedClientIndex >= mcpClients.clients.Count)
                return;

            var client = mcpClients.clients[selectedClientIndex];

            try
            {
                if (client.mcpType == McpTypes.ClaudeCode)
                {
                    RegisterWithClaudeCode();
                }
                else
                {
                    ConfigureMcpClient(client);
                }

                UpdateClientStatus();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Configuration failed: {ex.Message}");
            }
        }

        private void OnCopyPathClicked()
        {
            EditorGUIUtility.systemCopyBuffer = configPathField.value;
            Debug.Log("Config path copied to clipboard");
        }

        private void OnOpenFileClicked()
        {
            string path = configPathField.value;
            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = path,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Failed to open file: {ex.Message}");
                }
            }
        }

        private void OnCopyJsonClicked()
        {
            EditorGUIUtility.systemCopyBuffer = configJsonField.value;
            Debug.Log("Configuration copied to clipboard");
        }

        // Helper methods using existing infrastructure
        private void CheckMcpConfiguration(McpClient client)
        {
            try
            {
                // Special handling for Claude Code
                if (client.mcpType == McpTypes.ClaudeCode)
                {
                    CheckClaudeCodeConfiguration(client);
                    return;
                }

                string configPath = Helpers.McpConfigurationHelper.GetClientConfigPath(client);

                if (!File.Exists(configPath))
                {
                    client.SetStatus(McpStatus.NotConfigured);
                    return;
                }

                string configJson = File.ReadAllText(configPath);
                string pythonDir = FindPackagePythonDirectory();

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
                        if (Helpers.CodexConfigHelper.TryParseCodexServer(configJson, out _, out var codexArgs))
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
                    string configuredDir = Helpers.McpConfigFileHelper.ExtractDirectoryArg(args);
                    bool matches = !string.IsNullOrEmpty(configuredDir) &&
                                   Helpers.McpConfigFileHelper.PathsEqual(configuredDir, pythonDir);

                    client.SetStatus(matches ? McpStatus.Configured : McpStatus.IncorrectPath);
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
        }

        private void CheckClaudeCodeConfiguration(McpClient client)
        {
            try
            {
                string configPath = Helpers.McpConfigurationHelper.GetClientConfigPath(client);

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
                    if (servers.UnityMCP != null || servers.unityMCP != null)
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

        private void ConfigureMcpClient(McpClient client)
        {
            try
            {
                string configPath = Helpers.McpConfigurationHelper.GetClientConfigPath(client);
                Helpers.McpConfigurationHelper.EnsureConfigDirectoryExists(configPath);

                string pythonDir = FindPackagePythonDirectory();

                if (pythonDir == null || !File.Exists(Path.Combine(pythonDir, "server.py")))
                {
                    Debug.LogError("Server not found. Please use manual configuration.");
                    return;
                }

                string uvPath = FindUvPath();
                string result = client.mcpType == McpTypes.Codex
                    ? Helpers.McpConfigurationHelper.ConfigureCodexClient(pythonDir, configPath, client)
                    : Helpers.McpConfigurationHelper.WriteMcpConfiguration(pythonDir, configPath, client);

                if (result == "Configured successfully")
                {
                    client.SetStatus(McpStatus.Configured);
                    Debug.Log($"{client.name} configured successfully");
                }
                else
                {
                    Debug.LogWarning($"Configuration completed with message: {result}");
                }

                CheckMcpConfiguration(client);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Configuration failed: {ex.Message}");
            }
        }

        private void RegisterWithClaudeCode()
        {
            string pythonDir = FindPackagePythonDirectory();
            if (string.IsNullOrEmpty(pythonDir))
            {
                Debug.LogError("Cannot register: Python directory not found");
                return;
            }

            string claudePath = ExecPath.ResolveClaude();
            if (string.IsNullOrEmpty(claudePath))
            {
                Debug.LogError("Claude CLI not found. Please install Claude Code first.");
                return;
            }

            string uvPath = ExecPath.ResolveUv() ?? "uv";
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

            if (!ExecPath.TryRun(claudePath, args, projectDir, out var stdout, out var stderr, 15000, pathPrepend))
            {
                string combined = ($"{stdout}\n{stderr}") ?? string.Empty;
                if (combined.IndexOf("already exists", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Debug.Log("MCP for Unity already registered with Claude Code.");
                }
                else
                {
                    Debug.LogError($"Failed to register with Claude Code:\n{stderr}\n{stdout}");
                }
                return;
            }

            Debug.Log("Successfully registered with Claude Code.");

            // Update status
            var claudeClient = mcpClients.clients.FirstOrDefault(c => c.mcpType == McpTypes.ClaudeCode);
            if (claudeClient != null)
            {
                CheckClaudeCodeConfiguration(claudeClient);
                UpdateClientStatus();
            }
        }

        private string FindPackagePythonDirectory()
        {
            return Helpers.McpPathResolver.FindPackagePythonDirectory(false);
        }

        private string FindUvPath()
        {
            try
            {
                return Helpers.ServerInstaller.FindUvPath();
            }
            catch
            {
                return "uv";
            }
        }
    }

}
