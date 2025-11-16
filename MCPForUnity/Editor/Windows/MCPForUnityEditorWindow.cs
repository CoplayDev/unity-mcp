using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.UIElements; // For Unity 2021 compatibility
using UnityEngine;
using UnityEngine.UIElements;
using MCPForUnity.Editor.Data;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Models;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Services.Transport;

namespace MCPForUnity.Editor.Windows
{
    public class MCPForUnityEditorWindow : EditorWindow
    {
        // Transport protocol enum
        private enum TransportProtocol
        {
            HTTP,
            Stdio
        }

        // Settings UI Elements
        private Label versionLabel;
        private Toggle debugLogsToggle;
        private EnumField validationLevelField;
        private Label validationDescription;
        private Foldout advancedSettingsFoldout;
        private TextField uvxPathOverride;
        private Button browseUvxButton;
        private Button clearUvxButton;
        private Button clearUvxCacheButton;
        private VisualElement uvxPathStatus;
        private TextField gitUrlOverride;
        private Button clearGitUrlButton;

        // Connection UI Elements
        private EnumField transportDropdown;
        private VisualElement httpUrlRow;
        private VisualElement startHttpRow;
        private Foldout httpServerCommandFoldout;
        private TextField httpServerCommandField;
        private Button copyHttpServerCommandButton;
        private Label httpServerCommandHint;
        private TextField httpUrlField;
        private Button startHttpServerButton;
        private VisualElement unitySocketPortRow;
        private TextField unityPortField;
        private VisualElement statusIndicator;
        private Label connectionStatusLabel;
        private Button connectionToggleButton;
        private VisualElement healthIndicator;
        private Label healthStatusLabel;
        private Button testConnectionButton;

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
        private bool connectionToggleInProgress;
        private static readonly HashSet<MCPForUnityEditorWindow> OpenWindows = new();

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
            UpdateClientStatus();
            UpdatePathOverrides();
            // Technically not required to connect, but if we don't do this, the UI will be blank
            UpdateManualConfiguration();
            UpdateClaudeCliPathVisibility();
        }

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            OpenWindows.Add(this);
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            OpenWindows.Remove(this);
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
                _ = VerifyBridgeConnectionAsync();
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

            UpdateStartHttpButtonState();
        }

        private void CacheUIElements()
        {
            // Settings
            versionLabel = rootVisualElement.Q<Label>("version-label");
            debugLogsToggle = rootVisualElement.Q<Toggle>("debug-logs-toggle");
            validationLevelField = rootVisualElement.Q<EnumField>("validation-level");
            validationDescription = rootVisualElement.Q<Label>("validation-description");
            advancedSettingsFoldout = rootVisualElement.Q<Foldout>("advanced-settings-foldout");
            uvxPathOverride = rootVisualElement.Q<TextField>("uv-path-override");
            browseUvxButton = rootVisualElement.Q<Button>("browse-uv-button");
            clearUvxButton = rootVisualElement.Q<Button>("clear-uv-button");
            uvxPathStatus = rootVisualElement.Q<VisualElement>("uv-path-status");
            gitUrlOverride = rootVisualElement.Q<TextField>("git-url-override");
            clearGitUrlButton = rootVisualElement.Q<Button>("clear-git-url-button");
            clearUvxCacheButton = rootVisualElement.Q<Button>("clear-uvx-cache-button");

            // Connection
            transportDropdown = rootVisualElement.Q<EnumField>("transport-dropdown");
            httpUrlRow = rootVisualElement.Q<VisualElement>("http-url-row");
            startHttpRow = rootVisualElement.Q<VisualElement>("start-http-row");
            httpServerCommandFoldout = rootVisualElement.Q<Foldout>("http-server-command-foldout");
            httpServerCommandField = rootVisualElement.Q<TextField>("http-server-command");
            copyHttpServerCommandButton = rootVisualElement.Q<Button>("copy-http-server-command-button");
            httpServerCommandHint = rootVisualElement.Q<Label>("http-server-command-hint");
            httpUrlField = rootVisualElement.Q<TextField>("http-url");
            startHttpServerButton = rootVisualElement.Q<Button>("start-http-server-button");
            unitySocketPortRow = rootVisualElement.Q<VisualElement>("unity-socket-port-row");
            unityPortField = rootVisualElement.Q<TextField>("unity-port");
            statusIndicator = rootVisualElement.Q<VisualElement>("status-indicator");
            connectionStatusLabel = rootVisualElement.Q<Label>("connection-status");
            connectionToggleButton = rootVisualElement.Q<Button>("connection-toggle");
            healthIndicator = rootVisualElement.Q<VisualElement>("health-indicator");
            healthStatusLabel = rootVisualElement.Q<Label>("health-status");
            testConnectionButton = rootVisualElement.Q<Button>("test-connection-button");

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
            bool debugEnabled = EditorPrefs.GetBool(EditorPrefKeys.DebugLogs, false);
            debugLogsToggle.value = debugEnabled;
            McpLog.SetDebugLoggingEnabled(debugEnabled);

            validationLevelField.Init(ValidationLevel.Standard);
            int savedLevel = EditorPrefs.GetInt(EditorPrefKeys.ValidationLevel, 1);
            currentValidationLevel = (ValidationLevel)Mathf.Clamp(savedLevel, 0, 3);
            validationLevelField.value = currentValidationLevel;
            UpdateValidationDescription();

            // Advanced settings starts collapsed
            advancedSettingsFoldout.value = false;

            // Load Git URL override
            gitUrlOverride.value = EditorPrefs.GetString(EditorPrefKeys.GitUrlOverride, "");

            // Connection Section
            transportDropdown.Init(TransportProtocol.HTTP);
            bool useHttpTransport = EditorPrefs.GetBool(EditorPrefKeys.UseHttpTransport, true);
            transportDropdown.value = useHttpTransport ? TransportProtocol.HTTP : TransportProtocol.Stdio;

            // HTTP configuration
            httpUrlField.value = HttpEndpointUtility.GetBaseUrl();

            // Unity socket port (editable)
            int unityPort = EditorPrefs.GetInt(EditorPrefKeys.UnitySocketPort, 0);
            if (unityPort == 0)
            {
                unityPort = MCPServiceLocator.Bridge.CurrentPort;
            }
            unityPortField.value = unityPort.ToString();

            // Update HTTP field visibility
            UpdateHttpFieldVisibility();
            UpdateStartHttpButtonState();
            UpdateHttpServerCommandDisplay();

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
                McpLog.SetDebugLoggingEnabled(evt.newValue);
            });

            validationLevelField.RegisterValueChangedCallback(evt =>
            {
                currentValidationLevel = (ValidationLevel)evt.newValue;
                EditorPrefs.SetInt(EditorPrefKeys.ValidationLevel, (int)currentValidationLevel);
                UpdateValidationDescription();
            });

            // Transport callbacks
            transportDropdown.RegisterValueChangedCallback(evt =>
            {
                bool useHttp = (TransportProtocol)evt.newValue == TransportProtocol.HTTP;
                EditorPrefs.SetBool(EditorPrefKeys.UseHttpTransport, useHttp);
                UpdateHttpFieldVisibility();
                UpdateManualConfiguration(); // Refresh config display
                McpLog.Info($"Transport changed to: {evt.newValue}");
            });

            httpUrlField.RegisterValueChangedCallback(evt =>
            {
                HttpEndpointUtility.SaveBaseUrl(evt.newValue);
                httpUrlField.value = HttpEndpointUtility.GetBaseUrl();
                UpdateManualConfiguration(); // Refresh config display
                UpdateHttpServerCommandDisplay();
            });

            if (startHttpServerButton != null)
            {
                startHttpServerButton.clicked += OnStartLocalHttpServerClicked;
            }

            if (copyHttpServerCommandButton != null)
            {
                copyHttpServerCommandButton.clicked += () =>
                {
                    if (!string.IsNullOrEmpty(httpServerCommandField?.value) && copyHttpServerCommandButton.enabledSelf)
                    {
                        EditorGUIUtility.systemCopyBuffer = httpServerCommandField.value;
                        McpLog.Info("HTTP server command copied to clipboard.");
                    }
                };
            }

            unityPortField.RegisterCallback<FocusOutEvent>(_ => PersistUnityPortFromField());
            unityPortField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    PersistUnityPortFromField();
                    evt.StopPropagation();
                }
            });

            // Advanced settings callbacks
            browseUvxButton.clicked += OnBrowseUvxClicked;
            clearUvxButton.clicked += OnClearUvxClicked;
            if (clearUvxCacheButton != null)
            {
                clearUvxCacheButton.clicked += OnClearUvxCacheClicked;
            }

            // Git URL override callbacks
            gitUrlOverride.RegisterValueChangedCallback(evt =>
            {
                string url = evt.newValue?.Trim();
                if (string.IsNullOrEmpty(url))
                {
                    EditorPrefs.DeleteKey(EditorPrefKeys.GitUrlOverride);
                }
                else
                {
                    EditorPrefs.SetString(EditorPrefKeys.GitUrlOverride, url);
                }
                UpdateManualConfiguration(); // Refresh config display
            });

            clearGitUrlButton.clicked += () =>
            {
                gitUrlOverride.value = string.Empty;
                EditorPrefs.DeleteKey(EditorPrefKeys.GitUrlOverride);
                UpdateManualConfiguration();
            };

            // Connection callbacks
            connectionToggleButton.clicked += OnConnectionToggleClicked;
            testConnectionButton.clicked += OnTestConnectionClicked;

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

        private void OnStartLocalHttpServerClicked()
        {
            try
            {
                MCPServiceLocator.Server.StartLocalHttpServer();
            }
            finally
            {
                UpdateStartHttpButtonState();
            }
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
                connectionStatusLabel.text = "Session Active";
                statusIndicator.RemoveFromClassList("disconnected");
                statusIndicator.AddToClassList("connected");
                connectionToggleButton.text = "End Session";
            }
            else
            {
                connectionStatusLabel.text = "No Session";
                statusIndicator.RemoveFromClassList("connected");
                statusIndicator.AddToClassList("disconnected");
                connectionToggleButton.text = "Start Session";

                // Reset health status when disconnected
                healthStatusLabel.text = "Unknown";
                healthIndicator.RemoveFromClassList("healthy");
                healthIndicator.RemoveFromClassList("warning");
                healthIndicator.AddToClassList("unknown");
            }

            // Update ports
            int savedPort = EditorPrefs.GetInt(EditorPrefKeys.UnitySocketPort, 0);
            if (savedPort == 0)
            {
                unityPortField.value = bridgeService.CurrentPort.ToString();
            }
        }

        private void UpdateHttpFieldVisibility()
        {
            bool useHttp = (TransportProtocol)transportDropdown.value == TransportProtocol.HTTP;

            // Show HTTP URL only in HTTP mode
            httpUrlRow.style.display = useHttp ? DisplayStyle.Flex : DisplayStyle.None;
            if (startHttpRow != null)
            {
                startHttpRow.style.display = useHttp ? DisplayStyle.Flex : DisplayStyle.None;
            }

            // Show Unity Socket Port only in stdio mode (HTTP uses the same URL/port as MCP client)
            unitySocketPortRow.style.display = useHttp ? DisplayStyle.None : DisplayStyle.Flex;

            UpdateStartHttpButtonState();
            UpdateHttpServerCommandDisplay();
        }

        private void UpdateStartHttpButtonState()
        {
            if (startHttpServerButton == null)
                return;

            bool useHttp = transportDropdown != null && (TransportProtocol)transportDropdown.value == TransportProtocol.HTTP;
            if (!useHttp)
            {
                startHttpServerButton.SetEnabled(false);
                startHttpServerButton.tooltip = string.Empty;
                UpdateHttpServerCommandDisplay();
                return;
            }

            bool canStart = MCPServiceLocator.Server.CanStartLocalServer();
            startHttpServerButton.SetEnabled(canStart);
            startHttpServerButton.tooltip = canStart
                ? string.Empty
                : "Start Local HTTP Server is available only for localhost URLs.";
            UpdateHttpServerCommandDisplay();
        }

        private void UpdateHttpServerCommandDisplay()
        {
            if (httpServerCommandFoldout == null || httpServerCommandField == null)
            {
                return;
            }

            bool useHttp = transportDropdown != null && (TransportProtocol)transportDropdown.value == TransportProtocol.HTTP;

            if (!useHttp)
            {
                httpServerCommandFoldout.style.display = DisplayStyle.None;
                httpServerCommandField.value = string.Empty;
                httpServerCommandField.tooltip = string.Empty;
                if (httpServerCommandHint != null)
                {
                    httpServerCommandHint.text = string.Empty;
                }
                if (copyHttpServerCommandButton != null)
                {
                    copyHttpServerCommandButton.SetEnabled(false);
                }
                return;
            }

            httpServerCommandFoldout.style.display = DisplayStyle.Flex;

            if (MCPServiceLocator.Server.TryGetLocalHttpServerCommand(out var command, out var error))
            {
                httpServerCommandField.value = command;
                httpServerCommandField.tooltip = command;
                if (httpServerCommandHint != null)
                {
                    httpServerCommandHint.text = "Run this command in your shell if you prefer to start the server manually.";
                }
                if (copyHttpServerCommandButton != null)
                {
                    copyHttpServerCommandButton.SetEnabled(true);
                }
            }
            else
            {
                httpServerCommandField.value = string.Empty;
                httpServerCommandField.tooltip = string.Empty;
                if (httpServerCommandHint != null)
                {
                    httpServerCommandHint.text = error ?? "The command is not available with the current configuration.";
                }
                if (copyHttpServerCommandButton != null)
                {
                    copyHttpServerCommandButton.SetEnabled(false);
                }
            }
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

        private void PersistUnityPortFromField()
        {
            if (unityPortField == null)
            {
                return;
            }

            string input = unityPortField.text?.Trim();
            if (!int.TryParse(input, out int requestedPort) || requestedPort <= 0)
            {
                unityPortField.value = MCPServiceLocator.Bridge.CurrentPort.ToString();
                return;
            }

            try
            {
                int storedPort = PortManager.SetPreferredPort(requestedPort);
                EditorPrefs.SetInt(EditorPrefKeys.UnitySocketPort, storedPort);
                unityPortField.value = storedPort.ToString();
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Failed to persist Unity socket port: {ex.Message}");
                unityPortField.value = MCPServiceLocator.Bridge.CurrentPort.ToString();
            }
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

            // UV Path
            bool hasOverride = pathService.HasUvxPathOverride;
            string uvxPath = hasOverride ? pathService.GetUvxPath() : null;
            uvxPathOverride.value = hasOverride
                ? (uvxPath ?? "(override set but invalid)")
                : "uvx (uses PATH)";

            // Update status indicator
            uvxPathStatus.RemoveFromClassList("valid");
            uvxPathStatus.RemoveFromClassList("invalid");
            if (hasOverride)
            {
                if (!string.IsNullOrEmpty(uvxPath) && File.Exists(uvxPath))
                {
                    uvxPathStatus.AddToClassList("valid");
                }
                else
                {
                    uvxPathStatus.AddToClassList("invalid");
                }
            }
            else
            {
                uvxPathStatus.AddToClassList("valid");
            }

            // Git URL Override - refresh from EditorPrefs
            gitUrlOverride.value = EditorPrefs.GetString(EditorPrefKeys.GitUrlOverride, "");
        }

        // Button callbacks
        private async void OnConnectionToggleClicked()
        {
            if (connectionToggleInProgress)
            {
                return;
            }

            var bridgeService = MCPServiceLocator.Bridge;
            connectionToggleInProgress = true;
            connectionToggleButton?.SetEnabled(false);

            try
            {
                if (bridgeService.IsRunning)
                {
                    await bridgeService.StopAsync();
                }
                else
                {
                    bool started = await bridgeService.StartAsync();
                    if (started)
                    {
                        await VerifyBridgeConnectionAsync();
                    }
                    else
                    {
                        McpLog.Warn("Failed to start MCP bridge");
                    }
                }
            }
            catch (Exception ex)
            {
                McpLog.Error($"Connection toggle failed: {ex.Message}");
                EditorUtility.DisplayDialog("Connection Error",
                    $"Failed to toggle the MCP connection:\n\n{ex.Message}",
                    "OK");
            }
            finally
            {
                connectionToggleInProgress = false;
                connectionToggleButton?.SetEnabled(true);
                UpdateConnectionStatus();
            }
        }

        private async void OnTestConnectionClicked()
        {
            await VerifyBridgeConnectionAsync();
        }

        private async Task VerifyBridgeConnectionAsync()
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

            // Use async verification that works for both HTTP and stdio
            var result = await bridgeService.VerifyAsync();

            healthIndicator.RemoveFromClassList("healthy");
            healthIndicator.RemoveFromClassList("warning");
            healthIndicator.RemoveFromClassList("unknown");

            if (result.Success && result.PingSucceeded)
            {
                healthStatusLabel.text = "Healthy";
                healthIndicator.AddToClassList("healthy");
                McpLog.Debug($"Connection verification successful: {result.Message}");

                var mode = bridgeService.ActiveMode;
                if (mode == TransportMode.Http)
                {
                    CustomToolRegistrationProcessor.NotifyHttpConnectionHealthy();
                }
            }
            else if (result.HandshakeValid)
            {
                healthStatusLabel.text = "Ping Failed";
                healthIndicator.AddToClassList("warning");
                McpLog.Warn($"Connection verification warning: {result.Message}");
            }
            else
            {
                healthStatusLabel.text = "Unhealthy";
                healthIndicator.AddToClassList("warning");
                McpLog.Error($"Connection verification failed: {result.Message}");
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

        private void OnBrowseUvxClicked()
        {
            string suggested = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? "/opt/homebrew/bin"
                : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string picked = EditorUtility.OpenFilePanel("Select UV Executable", suggested, "");
            if (!string.IsNullOrEmpty(picked))
            {
                try
                {
                    MCPServiceLocator.Paths.SetUvxPathOverride(picked);
                    UpdatePathOverrides();
                    McpLog.Info($"UV path override set to: {picked}");
                }
                catch (Exception ex)
                {
                    EditorUtility.DisplayDialog("Invalid Path", ex.Message, "OK");
                }
            }
        }

        private void OnClearUvxClicked()
        {
            MCPServiceLocator.Paths.ClearUvxPathOverride();
            UpdatePathOverrides();
            McpLog.Info("UV path override cleared");
        }

        private void OnClearUvxCacheClicked()
        {
            if (EditorUtility.DisplayDialog("Clear uv Cache",
                "This will clear the local uv cache for the MCP server package. The server will be re-downloaded on next launch.\n\nContinue?",
                "Clear Cache",
                "Cancel"))
            {
                bool success = MCPServiceLocator.Cache.ClearUvxCache();

                if (success)
                {
                    McpLog.Info("uv cache cleared successfully");
                }
                else
                {
                    McpLog.Error("Failed to clear uv cache");
                    EditorUtility.DisplayDialog("Error",
                        "Failed to clear uv cache. Check the console for details.",
                        "OK");
                }
            }
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

        internal static void RequestHealthVerification()
        {
            foreach (var window in OpenWindows.ToArray())
            {
                window?.ScheduleHealthCheck();
            }
        }

        private void ScheduleHealthCheck()
        {
            EditorApplication.delayCall += async () =>
            {
                if (this == null)
                {
                    return;
                }
                await VerifyBridgeConnectionAsync();
            };
        }
    }
}
