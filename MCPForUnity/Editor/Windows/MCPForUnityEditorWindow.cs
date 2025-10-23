using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEditor.UIElements; // For Unity 2021 compatibility
using UnityEngine;
using UnityEngine.UIElements;
using MCPForUnity.Editor.Data;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Models;
using MCPForUnity.Editor.Services;

namespace MCPForUnity.Editor.Windows
{
    public class MCPForUnityEditorWindow : EditorWindow
    {
        // Protocol enum for future HTTP support
        private enum ConnectionProtocol
        {
            Stdio,
            // HTTPStreaming // Future
        }

        // Settings UI Elements
        private Label versionLabel;
        private Toggle debugLogsToggle;
        private EnumField validationLevelField;
        private Label validationDescription;
        private Foldout advancedSettingsFoldout;
        private TextField mcpServerPathOverride;
        private TextField uvPathOverride;
        private Button browsePythonButton;
        private Button clearPythonButton;
        private Button browseUvButton;
        private Button clearUvButton;
        private VisualElement mcpServerPathStatus;
        private VisualElement uvPathStatus;

        // Connection UI Elements
        private EnumField protocolDropdown;
        private TextField unityPortField;
        private TextField serverPortField;
        private VisualElement statusIndicator;
        private Label connectionStatusLabel;
        private Button connectionToggleButton;
        private VisualElement healthIndicator;
        private Label healthStatusLabel;
        private Button testConnectionButton;
        private VisualElement serverStatusBanner;
        private Label serverStatusMessage;
        private Button downloadServerButton;
        private Button rebuildServerButton;

        // Client UI Elements
        private DropdownField clientDropdown;
        private Button configureAllButton;
        private VisualElement clientStatusIndicator;
        private Label clientStatusLabel;
        private Button configureButton;
        private VisualElement claudeCliPathRow;
        private TextField claudeCliPath;
        private Button browseClaudeButton;
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
            var window = GetWindow<MCPForUnityEditorWindow>("MCP For Unity");
            window.minSize = new Vector2(500, 600);
        }
        public void CreateGUI()
        {
            // Determine base path (Package Manager vs Asset Store install)
            string basePath = AssetPathUtility.GetMcpPackageRootPath();

            // Load UXML
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                $"{basePath}/Editor/Windows/MCPForUnityEditorWindow.uxml"
            );

            if (visualTree == null)
            {
                McpLog.Error($"Failed to load UXML at: {basePath}/Editor/Windows/MCPForUnityEditorWindow.uxml");
                return;
            }

            visualTree.CloneTree(rootVisualElement);

            // Load USS
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                $"{basePath}/Editor/Windows/MCPForUnityEditorWindow.uss"
            );

            if (styleSheet != null)
            {
                rootVisualElement.styleSheets.Add(styleSheet);
            }

            // Cache UI elements
            CacheUIElements();

            // Initialize UI
            InitializeUI();

            // Register callbacks
            RegisterCallbacks();

            // Initial update
            UpdateConnectionStatus();
            UpdateServerStatusBanner();
            UpdateClientStatus();
            UpdatePathOverrides();
            // Technically not required to connect, but if we don't do this, the UI will be blank
            UpdateManualConfiguration();
            UpdateClaudeCliPathVisibility();
        }

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnFocus()
        {
            // Only refresh data if UI is built
            if (rootVisualElement == null || rootVisualElement.childCount == 0)
                return;

            RefreshAllData();
        }

        private void OnEditorUpdate()
        {
            // Only update UI if it's built
            if (rootVisualElement == null || rootVisualElement.childCount == 0)
                return;

            UpdateConnectionStatus();
        }

        private void RefreshAllData()
        {
            // Update connection status
            UpdateConnectionStatus();

            // Auto-verify bridge health if connected
            if (MCPServiceLocator.Bridge.IsRunning)
            {
                VerifyBridgeConnection();
            }

            // Update path overrides
            UpdatePathOverrides();

            // Refresh selected client (may have been configured externally)
            if (selectedClientIndex >= 0 && selectedClientIndex < mcpClients.clients.Count)
            {
                var client = mcpClients.clients[selectedClientIndex];
                MCPServiceLocator.Client.CheckClientStatus(client);
                UpdateClientStatus();
                UpdateManualConfiguration();
                UpdateClaudeCliPathVisibility();
            }
        }

        private void CacheUIElements()
        {
            // Settings
            versionLabel = rootVisualElement.Q<Label>("version-label");
            debugLogsToggle = rootVisualElement.Q<Toggle>("debug-logs-toggle");
            validationLevelField = rootVisualElement.Q<EnumField>("validation-level");
            validationDescription = rootVisualElement.Q<Label>("validation-description");
            advancedSettingsFoldout = rootVisualElement.Q<Foldout>("advanced-settings-foldout");
            mcpServerPathOverride = rootVisualElement.Q<TextField>("python-path-override");
            uvPathOverride = rootVisualElement.Q<TextField>("uv-path-override");
            browsePythonButton = rootVisualElement.Q<Button>("browse-python-button");
            clearPythonButton = rootVisualElement.Q<Button>("clear-python-button");
            browseUvButton = rootVisualElement.Q<Button>("browse-uv-button");
            clearUvButton = rootVisualElement.Q<Button>("clear-uv-button");
            mcpServerPathStatus = rootVisualElement.Q<VisualElement>("mcp-server-path-status");
            uvPathStatus = rootVisualElement.Q<VisualElement>("uv-path-status");

            // Connection
            protocolDropdown = rootVisualElement.Q<EnumField>("protocol-dropdown");
            unityPortField = rootVisualElement.Q<TextField>("unity-port");
            serverPortField = rootVisualElement.Q<TextField>("server-port");
            statusIndicator = rootVisualElement.Q<VisualElement>("status-indicator");
            connectionStatusLabel = rootVisualElement.Q<Label>("connection-status");
            connectionToggleButton = rootVisualElement.Q<Button>("connection-toggle");
            healthIndicator = rootVisualElement.Q<VisualElement>("health-indicator");
            healthStatusLabel = rootVisualElement.Q<Label>("health-status");
            testConnectionButton = rootVisualElement.Q<Button>("test-connection-button");
            serverStatusBanner = rootVisualElement.Q<VisualElement>("server-status-banner");
            serverStatusMessage = rootVisualElement.Q<Label>("server-status-message");
            downloadServerButton = rootVisualElement.Q<Button>("download-server-button");
            rebuildServerButton = rootVisualElement.Q<Button>("rebuild-server-button");

            // Client
            clientDropdown = rootVisualElement.Q<DropdownField>("client-dropdown");
            configureAllButton = rootVisualElement.Q<Button>("configure-all-button");
            clientStatusIndicator = rootVisualElement.Q<VisualElement>("client-status-indicator");
            clientStatusLabel = rootVisualElement.Q<Label>("client-status");
            configureButton = rootVisualElement.Q<Button>("configure-button");
            claudeCliPathRow = rootVisualElement.Q<VisualElement>("claude-cli-path-row");
            claudeCliPath = rootVisualElement.Q<TextField>("claude-cli-path");
            browseClaudeButton = rootVisualElement.Q<Button>("browse-claude-button");
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
            UpdateVersionLabel();
            debugLogsToggle.value = EditorPrefs.GetBool("MCPForUnity.DebugLogs", false);

            validationLevelField.Init(ValidationLevel.Standard);
            int savedLevel = EditorPrefs.GetInt("MCPForUnity.ValidationLevel", 1);
            currentValidationLevel = (ValidationLevel)Mathf.Clamp(savedLevel, 0, 3);
            validationLevelField.value = currentValidationLevel;
            UpdateValidationDescription();

            // Advanced settings starts collapsed
            advancedSettingsFoldout.value = false;

            // Connection Section
            protocolDropdown.Init(ConnectionProtocol.Stdio);
            protocolDropdown.SetEnabled(false); // Disabled for now, only stdio supported

            unityPortField.value = MCPServiceLocator.Bridge.CurrentPort.ToString();
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

            // Claude CLI path row hidden by default
            claudeCliPathRow.style.display = DisplayStyle.None;
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

            // Advanced settings callbacks
            browsePythonButton.clicked += OnBrowsePythonClicked;
            clearPythonButton.clicked += OnClearPythonClicked;
            browseUvButton.clicked += OnBrowseUvClicked;
            clearUvButton.clicked += OnClearUvClicked;

            // Connection callbacks
            connectionToggleButton.clicked += OnConnectionToggleClicked;
            testConnectionButton.clicked += OnTestConnectionClicked;
            downloadServerButton.clicked += OnDownloadServerClicked;
            rebuildServerButton.clicked += OnRebuildServerClicked;

            // Client callbacks
            clientDropdown.RegisterValueChangedCallback(evt =>
            {
                selectedClientIndex = clientDropdown.index;
                UpdateClientStatus();
                UpdateManualConfiguration();
                UpdateClaudeCliPathVisibility();
            });

            configureAllButton.clicked += OnConfigureAllClientsClicked;
            configureButton.clicked += OnConfigureClicked;
            browseClaudeButton.clicked += OnBrowseClaudeClicked;
            copyPathButton.clicked += OnCopyPathClicked;
            openFileButton.clicked += OnOpenFileClicked;
            copyJsonButton.clicked += OnCopyJsonClicked;
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
            var bridgeService = MCPServiceLocator.Bridge;
            bool isRunning = bridgeService.IsRunning;

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

                // Reset health status when disconnected
                healthStatusLabel.text = "Unknown";
                healthIndicator.RemoveFromClassList("healthy");
                healthIndicator.RemoveFromClassList("warning");
                healthIndicator.AddToClassList("unknown");
            }

            // Update ports
            unityPortField.value = bridgeService.CurrentPort.ToString();
        }

        private void UpdateClientStatus()
        {
            if (selectedClientIndex < 0 || selectedClientIndex >= mcpClients.clients.Count)
                return;

            var client = mcpClients.clients[selectedClientIndex];
            MCPServiceLocator.Client.CheckClientStatus(client);

            clientStatusLabel.text = client.GetStatusDisplayString();
            
            // Reset inline color style (clear error state from OnConfigureClicked)
            clientStatusLabel.style.color = StyleKeyword.Null;

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

            // Update configure button text for Claude Code
            if (client.mcpType == McpTypes.ClaudeCode)
            {
                bool isConfigured = client.status == McpStatus.Configured;
                configureButton.text = isConfigured ? "Unregister" : "Register";
            }
            else
            {
                configureButton.text = "Configure";
            }
        }

        private void UpdateManualConfiguration()
        {
            if (selectedClientIndex < 0 || selectedClientIndex >= mcpClients.clients.Count)
                return;

            var client = mcpClients.clients[selectedClientIndex];

            // Get config path
            string configPath = MCPServiceLocator.Client.GetConfigPath(client);
            configPathField.value = configPath;

            // Get config JSON
            string configJson = MCPServiceLocator.Client.GenerateConfigJson(client);
            configJsonField.value = configJson;

            // Get installation steps
            string steps = MCPServiceLocator.Client.GetInstallationSteps(client);
            installationStepsLabel.text = steps;
        }

        private void UpdateClaudeCliPathVisibility()
        {
            if (selectedClientIndex < 0 || selectedClientIndex >= mcpClients.clients.Count)
                return;

            var client = mcpClients.clients[selectedClientIndex];

            // Show Claude CLI path only for Claude Code client
            if (client.mcpType == McpTypes.ClaudeCode)
            {
                string claudePath = MCPServiceLocator.Paths.GetClaudeCliPath();
                if (string.IsNullOrEmpty(claudePath))
                {
                    // Show path selector if not found
                    claudeCliPathRow.style.display = DisplayStyle.Flex;
                    claudeCliPath.value = "Not found - click Browse to select";
                }
                else
                {
                    // Show detected path
                    claudeCliPathRow.style.display = DisplayStyle.Flex;
                    claudeCliPath.value = claudePath;
                }
            }
            else
            {
                claudeCliPathRow.style.display = DisplayStyle.None;
            }
        }

        private void UpdatePathOverrides()
        {
            var pathService = MCPServiceLocator.Paths;

            // MCP Server Path
            string mcpServerPath = pathService.GetMcpServerPath();
            if (pathService.HasMcpServerOverride)
            {
                mcpServerPathOverride.value = mcpServerPath ?? "(override set but invalid)";
            }
            else
            {
                mcpServerPathOverride.value = mcpServerPath ?? "(auto-detected)";
            }

            // Update status indicator
            mcpServerPathStatus.RemoveFromClassList("valid");
            mcpServerPathStatus.RemoveFromClassList("invalid");
            if (!string.IsNullOrEmpty(mcpServerPath) && File.Exists(Path.Combine(mcpServerPath, "server.py")))
            {
                mcpServerPathStatus.AddToClassList("valid");
            }
            else
            {
                mcpServerPathStatus.AddToClassList("invalid");
            }

            // UV Path
            string uvPath = pathService.GetUvPath();
            if (pathService.HasUvPathOverride)
            {
                uvPathOverride.value = uvPath ?? "(override set but invalid)";
            }
            else
            {
                uvPathOverride.value = uvPath ?? "(auto-detected)";
            }

            // Update status indicator
            uvPathStatus.RemoveFromClassList("valid");
            uvPathStatus.RemoveFromClassList("invalid");
            if (!string.IsNullOrEmpty(uvPath) && File.Exists(uvPath))
            {
                uvPathStatus.AddToClassList("valid");
            }
            else
            {
                uvPathStatus.AddToClassList("invalid");
            }
        }

        // Button callbacks
        private void OnConnectionToggleClicked()
        {
            var bridgeService = MCPServiceLocator.Bridge;

            if (bridgeService.IsRunning)
            {
                bridgeService.Stop();
            }
            else
            {
                bridgeService.Start();

                // Verify connection after starting (Option C: verify on connect)
                EditorApplication.delayCall += () =>
                {
                    if (bridgeService.IsRunning)
                    {
                        VerifyBridgeConnection();
                    }
                };
            }

            UpdateConnectionStatus();
        }

        private void OnTestConnectionClicked()
        {
            VerifyBridgeConnection();
        }

        private void VerifyBridgeConnection()
        {
            var bridgeService = MCPServiceLocator.Bridge;

            if (!bridgeService.IsRunning)
            {
                healthStatusLabel.text = "Disconnected";
                healthIndicator.RemoveFromClassList("healthy");
                healthIndicator.RemoveFromClassList("warning");
                healthIndicator.AddToClassList("unknown");
                McpLog.Warn("Cannot verify connection: Bridge is not running");
                return;
            }

            var result = bridgeService.Verify(bridgeService.CurrentPort);

            healthIndicator.RemoveFromClassList("healthy");
            healthIndicator.RemoveFromClassList("warning");
            healthIndicator.RemoveFromClassList("unknown");

            if (result.Success && result.PingSucceeded)
            {
                healthStatusLabel.text = "Healthy";
                healthIndicator.AddToClassList("healthy");
                McpLog.Info("Bridge verification successful");
            }
            else if (result.HandshakeValid)
            {
                healthStatusLabel.text = "Ping Failed";
                healthIndicator.AddToClassList("warning");
                McpLog.Warn($"Bridge verification warning: {result.Message}");
            }
            else
            {
                healthStatusLabel.text = "Unhealthy";
                healthIndicator.AddToClassList("warning");
                McpLog.Error($"Bridge verification failed: {result.Message}");
            }
        }

        private void OnDownloadServerClicked()
        {
            if (ServerInstaller.DownloadAndInstallServer())
            {
                UpdateServerStatusBanner();
                UpdatePathOverrides();
                EditorUtility.DisplayDialog(
                    "Download Complete",
                    "Server installed successfully! Start your connection and configure your MCP clients to begin.",
                    "OK"
                );
            }
        }

        private void OnRebuildServerClicked()
        {
            try
            {
                bool success = ServerInstaller.RebuildMcpServer();
                if (success)
                {
                    EditorUtility.DisplayDialog("MCP For Unity", "Server rebuilt successfully.", "OK");
                    UpdateServerStatusBanner();
                    UpdatePathOverrides();
                }
                else
                {
                    EditorUtility.DisplayDialog("MCP For Unity", "Rebuild failed. Please check Console for details.", "OK");
                }
            }
            catch (Exception ex)
            {
                McpLog.Error($"Failed to rebuild server: {ex.Message}");
                EditorUtility.DisplayDialog("MCP For Unity", $"Rebuild failed: {ex.Message}", "OK");
            }
        }

        private void UpdateServerStatusBanner()
        {
            bool hasEmbedded = ServerInstaller.HasEmbeddedServer();
            string installedVer = ServerInstaller.GetInstalledServerVersion();
            string packageVer = AssetPathUtility.GetPackageVersion();

            // Show/hide download vs rebuild buttons
            if (hasEmbedded)
            {
                downloadServerButton.style.display = DisplayStyle.None;
                rebuildServerButton.style.display = DisplayStyle.Flex;
            }
            else
            {
                downloadServerButton.style.display = DisplayStyle.Flex;
                rebuildServerButton.style.display = DisplayStyle.None;
            }

            // Check for installation errors first
            string installError = PackageLifecycleManager.GetLastInstallError();
            if (!string.IsNullOrEmpty(installError))
            {
                serverStatusMessage.text = $"\u274C Server installation failed: {installError}. Click 'Rebuild Server' to retry.";
                serverStatusBanner.style.display = DisplayStyle.Flex;
            }
            // Update banner
            else if (!hasEmbedded && string.IsNullOrEmpty(installedVer))
            {
                serverStatusMessage.text = "\u26A0 Server not installed. Click 'Download & Install Server' to get started.";
                serverStatusBanner.style.display = DisplayStyle.Flex;
            }
            else if (!hasEmbedded && !string.IsNullOrEmpty(installedVer) && installedVer != packageVer)
            {
                serverStatusMessage.text = $"\u26A0 Server update available (v{installedVer} \u2192 v{packageVer}). Update recommended.";
                serverStatusBanner.style.display = DisplayStyle.Flex;
            }
            else
            {
                serverStatusBanner.style.display = DisplayStyle.None;
            }
        }

        private void OnConfigureAllClientsClicked()
        {
            try
            {
                var summary = MCPServiceLocator.Client.ConfigureAllDetectedClients();

                // Build detailed message
                string message = summary.GetSummaryMessage() + "\n\n";
                foreach (var msg in summary.Messages)
                {
                    message += msg + "\n";
                }

                EditorUtility.DisplayDialog("Configure All Clients", message, "OK");

                // Refresh current client status
                if (selectedClientIndex >= 0 && selectedClientIndex < mcpClients.clients.Count)
                {
                    UpdateClientStatus();
                    UpdateManualConfiguration();
                }
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Configuration Failed", ex.Message, "OK");
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
                    bool isConfigured = client.status == McpStatus.Configured;
                    if (isConfigured)
                    {
                        MCPServiceLocator.Client.UnregisterClaudeCode();
                    }
                    else
                    {
                        MCPServiceLocator.Client.RegisterClaudeCode();
                    }
                }
                else
                {
                    MCPServiceLocator.Client.ConfigureClient(client);
                }

                UpdateClientStatus();
                UpdateManualConfiguration();
            }
            catch (Exception ex)
            {
                clientStatusLabel.text = "Error";
                clientStatusLabel.style.color = Color.red;
                McpLog.Error($"Configuration failed: {ex.Message}");
                EditorUtility.DisplayDialog("Configuration Failed", ex.Message, "OK");
            }
        }

        private void OnBrowsePythonClicked()
        {
            string picked = EditorUtility.OpenFolderPanel("Select MCP Server Directory", Application.dataPath, "");
            if (!string.IsNullOrEmpty(picked))
            {
                try
                {
                    MCPServiceLocator.Paths.SetMcpServerOverride(picked);
                    UpdatePathOverrides();
                    McpLog.Info($"MCP server path override set to: {picked}");
                }
                catch (Exception ex)
                {
                    EditorUtility.DisplayDialog("Invalid Path", ex.Message, "OK");
                }
            }
        }

        private void OnClearPythonClicked()
        {
            MCPServiceLocator.Paths.ClearMcpServerOverride();
            UpdatePathOverrides();
            McpLog.Info("MCP server path override cleared");
        }

        private void OnBrowseUvClicked()
        {
            string suggested = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? "/opt/homebrew/bin"
                : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string picked = EditorUtility.OpenFilePanel("Select UV Executable", suggested, "");
            if (!string.IsNullOrEmpty(picked))
            {
                try
                {
                    MCPServiceLocator.Paths.SetUvPathOverride(picked);
                    UpdatePathOverrides();
                    McpLog.Info($"UV path override set to: {picked}");
                }
                catch (Exception ex)
                {
                    EditorUtility.DisplayDialog("Invalid Path", ex.Message, "OK");
                }
            }
        }

        private void OnClearUvClicked()
        {
            MCPServiceLocator.Paths.ClearUvPathOverride();
            UpdatePathOverrides();
            McpLog.Info("UV path override cleared");
        }

        private void OnBrowseClaudeClicked()
        {
            string suggested = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? "/opt/homebrew/bin"
                : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string picked = EditorUtility.OpenFilePanel("Select Claude CLI", suggested, "");
            if (!string.IsNullOrEmpty(picked))
            {
                try
                {
                    MCPServiceLocator.Paths.SetClaudeCliPathOverride(picked);
                    UpdateClaudeCliPathVisibility();
                    UpdateClientStatus();
                    McpLog.Info($"Claude CLI path override set to: {picked}");
                }
                catch (Exception ex)
                {
                    EditorUtility.DisplayDialog("Invalid Path", ex.Message, "OK");
                }
            }
        }

        private void OnCopyPathClicked()
        {
            EditorGUIUtility.systemCopyBuffer = configPathField.value;
            McpLog.Info("Config path copied to clipboard");
        }

        private void OnOpenFileClicked()
        {
            string path = configPathField.value;
            try
            {
                if (!File.Exists(path))
                {
                    EditorUtility.DisplayDialog("Open File", "The configuration file path does not exist.", "OK");
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                McpLog.Error($"Failed to open file: {ex.Message}");
            }
        }

        private void OnCopyJsonClicked()
        {
            EditorGUIUtility.systemCopyBuffer = configJsonField.value;
            McpLog.Info("Configuration copied to clipboard");
        }

        private void UpdateVersionLabel()
        {
            string currentVersion = AssetPathUtility.GetPackageVersion();
            versionLabel.text = $"v{currentVersion}";

            // Check for updates using the service
            var updateCheck = MCPServiceLocator.Updates.CheckForUpdate(currentVersion);

            if (updateCheck.UpdateAvailable && !string.IsNullOrEmpty(updateCheck.LatestVersion))
            {
                // Update available - enhance the label
                versionLabel.text = $"\u2191 v{currentVersion} (Update available: v{updateCheck.LatestVersion})";
                versionLabel.style.color = new Color(1f, 0.7f, 0f); // Orange
                versionLabel.tooltip = $"Version {updateCheck.LatestVersion} is available. Update via Package Manager.\n\nGit URL: https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity";
            }
            else
            {
                versionLabel.style.color = StyleKeyword.Null; // Default color
                versionLabel.tooltip = $"Current version: {currentVersion}";
            }
        }

    }
}
