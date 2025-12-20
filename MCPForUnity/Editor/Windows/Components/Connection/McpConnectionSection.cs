using System;
using System.Threading.Tasks;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Services.Transport;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace MCPForUnity.Editor.Windows.Components.Connection
{
    /// <summary>
    /// Controller for the Connection section of the MCP For Unity editor window.
    /// Handles transport protocol, HTTP/stdio configuration, connection status, and health checks.
    /// </summary>
    public class McpConnectionSection
    {
        // Transport protocol enum
        private enum TransportProtocol
        {
            HTTP,
            Stdio
        }

        // UI Elements
        private EnumField transportDropdown;
        private Toggle authEnabledToggle;
        private VisualElement authToggleRow;
        private VisualElement allowedIpsRow;
        private TextField allowedIpsField;
        private VisualElement httpUrlRow;
        private VisualElement httpServerCommandSection;
        private TextField httpServerCommandField;
        private Button copyHttpServerCommandButton;
        private Label httpServerCommandHint;
        private TextField httpUrlField;
        private VisualElement apiKeyRow;
        private TextField apiKeyField;
        private Button copyApiKeyButton;
        private Button regenerateApiKeyButton;
        private Button startHttpServerButton;
        private Button stopHttpServerButton;
        private VisualElement unitySocketPortRow;
        private TextField unityPortField;
        private VisualElement statusIndicator;
        private Label connectionStatusLabel;
        private Button connectionToggleButton;
        private VisualElement healthIndicator;
        private Label healthStatusLabel;
        private Button testConnectionButton;

        private bool connectionToggleInProgress;
        private Task verificationTask;
        private string lastHealthStatus;

        // Health status constants
        private const string HealthStatusUnknown = "Unknown";
        private const string HealthStatusHealthy = "Healthy";
        private const string HealthStatusPingFailed = "Ping Failed";
        private const string HealthStatusUnhealthy = "Unhealthy";

        // Events
        public event Action OnManualConfigUpdateRequested;

        public VisualElement Root { get; private set; }

        public McpConnectionSection(VisualElement root)
        {
            Root = root;
            CacheUIElements();
            InitializeUI();
            RegisterCallbacks();
        }

        private void CacheUIElements()
        {
            transportDropdown = Root.Q<EnumField>("transport-dropdown");
            authEnabledToggle = Root.Q<Toggle>("auth-enabled-toggle");
            authToggleRow = Root.Q<VisualElement>("auth-toggle-row");
            allowedIpsRow = Root.Q<VisualElement>("allowed-ips-row");
            allowedIpsField = Root.Q<TextField>("allowed-ips-field");
            httpUrlRow = Root.Q<VisualElement>("http-url-row");
            httpServerCommandSection = Root.Q<VisualElement>("http-server-command-section");
            httpServerCommandField = Root.Q<TextField>("http-server-command");
            copyHttpServerCommandButton = Root.Q<Button>("copy-http-server-command-button");
            httpServerCommandHint = Root.Q<Label>("http-server-command-hint");
            httpUrlField = Root.Q<TextField>("http-url");
            apiKeyRow = Root.Q<VisualElement>("api-key-row");
            apiKeyField = Root.Q<TextField>("api-key-field");
            copyApiKeyButton = Root.Q<Button>("copy-api-key-button");
            regenerateApiKeyButton = Root.Q<Button>("regenerate-api-key-button");
            startHttpServerButton = Root.Q<Button>("start-http-server-button");
            stopHttpServerButton = Root.Q<Button>("stop-http-server-button");
            unitySocketPortRow = Root.Q<VisualElement>("unity-socket-port-row");
            unityPortField = Root.Q<TextField>("unity-port");
            statusIndicator = Root.Q<VisualElement>("status-indicator");
            connectionStatusLabel = Root.Q<Label>("connection-status");
            connectionToggleButton = Root.Q<Button>("connection-toggle");
            healthIndicator = Root.Q<VisualElement>("health-indicator");
            healthStatusLabel = Root.Q<Label>("health-status");
            testConnectionButton = Root.Q<Button>("test-connection-button");
        }

        private void InitializeUI()
        {
            transportDropdown.Init(TransportProtocol.HTTP);
            bool useHttpTransport = EditorPrefs.GetBool(EditorPrefKeys.UseHttpTransport, true);
            transportDropdown.value = useHttpTransport ? TransportProtocol.HTTP : TransportProtocol.Stdio;

            httpUrlField.value = HttpEndpointUtility.GetBaseUrl();

            bool authEnabled = AuthPreferencesUtility.IsAuthEnabled();
            if (authEnabledToggle != null)
            {
                authEnabledToggle.value = authEnabled;
            }
            if (allowedIpsField != null)
            {
                allowedIpsField.value = AuthPreferencesUtility.GetAllowedIps();
            }

            if (apiKeyField != null)
            {
                apiKeyField.isPasswordField = true;
                apiKeyField.isReadOnly = true;
                RefreshAuthUi();
            }

            int unityPort = EditorPrefs.GetInt(EditorPrefKeys.UnitySocketPort, 0);
            if (unityPort == 0)
            {
                unityPort = MCPServiceLocator.Bridge.CurrentPort;
            }
            unityPortField.value = unityPort.ToString();

            UpdateHttpFieldVisibility();
            RefreshHttpUi();
        }

        private void RegisterCallbacks()
        {
            transportDropdown.RegisterValueChangedCallback(evt =>
            {
                bool useHttp = (TransportProtocol)evt.newValue == TransportProtocol.HTTP;
                EditorPrefs.SetBool(EditorPrefKeys.UseHttpTransport, useHttp);
                UpdateHttpFieldVisibility();
                RefreshHttpUi();
                OnManualConfigUpdateRequested?.Invoke();
                McpLog.Info($"Transport changed to: {evt.newValue}");
            });

            httpUrlField.RegisterValueChangedCallback(evt =>
            {
                HttpEndpointUtility.SaveBaseUrl(evt.newValue);
                httpUrlField.value = HttpEndpointUtility.GetBaseUrl();
                OnManualConfigUpdateRequested?.Invoke();
                RefreshHttpUi();
            });

            if (authEnabledToggle != null)
            {
                authEnabledToggle.RegisterValueChangedCallback(evt =>
                {
                    bool enabled = evt.newValue;
                    AuthPreferencesUtility.SetAuthEnabled(enabled);
                    RefreshAuthUi();
                    OnManualConfigUpdateRequested?.Invoke();
                });
            }

            if (allowedIpsField != null)
            {
                allowedIpsField.RegisterValueChangedCallback(evt =>
                {
                    AuthPreferencesUtility.SetAllowedIps(evt.newValue);
                    OnManualConfigUpdateRequested?.Invoke();
                });
            }

            if (startHttpServerButton != null)
            {
                startHttpServerButton.clicked += OnStartLocalHttpServerClicked;
            }

            if (stopHttpServerButton != null)
            {
                stopHttpServerButton.clicked += OnStopLocalHttpServerClicked;
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

            connectionToggleButton.clicked += OnConnectionToggleClicked;
            testConnectionButton.clicked += OnTestConnectionClicked;

            if (copyApiKeyButton != null)
            {
                copyApiKeyButton.clicked += () =>
                {
                    if (!AuthPreferencesUtility.IsAuthEnabled())
                    {
                        return;
                    }
                    string apiKey = AuthPreferencesUtility.GetApiKey();
                    if (!string.IsNullOrEmpty(apiKey))
                    {
                        EditorGUIUtility.systemCopyBuffer = apiKey;
                        McpLog.Info("API key copied to clipboard.");
                    }
                };
            }

            if (regenerateApiKeyButton != null)
            {
                regenerateApiKeyButton.clicked += () =>
                {
                    if (!AuthPreferencesUtility.IsAuthEnabled())
                    {
                        return;
                    }
                    string newKey = AuthPreferencesUtility.GenerateNewApiKey();
                    AuthPreferencesUtility.SetApiKey(newKey);
                    UpdateApiKeyField();
                    UpdateHttpServerCommandDisplay();
                    OnManualConfigUpdateRequested?.Invoke();
                };
            }
        }

        public void UpdateConnectionStatus()
        {
            var bridgeService = MCPServiceLocator.Bridge;
            bool isRunning = bridgeService.IsRunning;

            if (isRunning)
            {
                connectionStatusLabel.text = "Session Active";
                statusIndicator.RemoveFromClassList("disconnected");
                statusIndicator.AddToClassList("connected");
                connectionToggleButton.text = "End Session";
                
                // Force the UI to reflect the actual port being used
                unityPortField.value = bridgeService.CurrentPort.ToString();
                unityPortField.SetEnabled(false);
            }
            else
            {
                connectionStatusLabel.text = "No Session";
                statusIndicator.RemoveFromClassList("connected");
                statusIndicator.AddToClassList("disconnected");
                connectionToggleButton.text = "Start Session";
                
                unityPortField.SetEnabled(true);

                healthStatusLabel.text = HealthStatusUnknown;
                healthIndicator.RemoveFromClassList("healthy");
                healthIndicator.RemoveFromClassList("warning");
                healthIndicator.AddToClassList("unknown");
                
                int savedPort = EditorPrefs.GetInt(EditorPrefKeys.UnitySocketPort, 0);
                unityPortField.value = (savedPort == 0 
                    ? bridgeService.CurrentPort 
                    : savedPort).ToString();
            }
        }

        public void UpdateHttpServerCommandDisplay()
        {
            if (httpServerCommandSection == null || httpServerCommandField == null)
            {
                return;
            }

            bool useHttp = transportDropdown != null && (TransportProtocol)transportDropdown.value == TransportProtocol.HTTP;

            if (!useHttp)
            {
                httpServerCommandSection.style.display = DisplayStyle.None;
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

            httpServerCommandSection.style.display = DisplayStyle.Flex;
            RefreshAuthUi();

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

        private void RefreshAuthUi()
        {
            bool enabled = AuthPreferencesUtility.IsAuthEnabled();
            bool useHttp = transportDropdown != null && (TransportProtocol)transportDropdown.value == TransportProtocol.HTTP;
            bool active = enabled && useHttp;

            UpdateAuthVisibility(active);
            UpdateAuthControlState(active);

            if (active)
            {
                EnsureApiKeyExists();
                UpdateApiKeyField();
            }
            else if (apiKeyField != null)
            {
                apiKeyField.value = string.Empty;
            }
        }

        private void UpdateAuthVisibility(bool enabled)
        {
            if (allowedIpsRow != null)
            {
                allowedIpsRow.style.display = enabled ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (apiKeyRow != null)
            {
                apiKeyRow.style.display = enabled ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void UpdateAuthControlState(bool enabled)
        {
            allowedIpsField?.SetEnabled(enabled);
            apiKeyField?.SetEnabled(enabled);
            copyApiKeyButton?.SetEnabled(enabled);
            regenerateApiKeyButton?.SetEnabled(enabled);
        }

        private void UpdateHttpFieldVisibility()
        {
            bool useHttp = (TransportProtocol)transportDropdown.value == TransportProtocol.HTTP;

            httpUrlRow.style.display = useHttp ? DisplayStyle.Flex : DisplayStyle.None;
            unitySocketPortRow.style.display = useHttp ? DisplayStyle.None : DisplayStyle.Flex;

            if (authToggleRow != null)
            {
                authToggleRow.style.display = useHttp ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (!useHttp)
            {
                UpdateAuthVisibility(false);
            }
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
                return;
            }

            bool canStart = MCPServiceLocator.Server.CanStartLocalServer();
            startHttpServerButton.SetEnabled(canStart);
            startHttpServerButton.tooltip = canStart
                ? string.Empty
                : "Start Local HTTP Server is available only for localhost URLs.";

            if (stopHttpServerButton != null)
            {
                stopHttpServerButton.SetEnabled(canStart);
                stopHttpServerButton.tooltip = canStart
                    ? string.Empty
                    : "Stop Local HTTP Server is available only for localhost URLs.";
            }
        }

        private void RefreshHttpUi()
        {
            RefreshAuthUi();
            UpdateStartHttpButtonState();
            UpdateHttpServerCommandDisplay();
        }

        private void OnStartLocalHttpServerClicked()
        {
            if (startHttpServerButton != null)
            {
                startHttpServerButton.SetEnabled(false);
            }

            try
            {
                MCPServiceLocator.Server.StartLocalHttpServer();
            }
            finally
            {
                RefreshHttpUi();
            }
        }

        private void OnStopLocalHttpServerClicked()
        {
            if (stopHttpServerButton != null)
            {
                stopHttpServerButton.SetEnabled(false);
            }

            try
            {
                bool stopped = MCPServiceLocator.Server.StopLocalHttpServer();
                if (!stopped)
                {
                    McpLog.Warn("Failed to stop HTTP server or no server was running");
                }
            }
            catch (Exception ex)
            {
                McpLog.Error($"Failed to stop server: {ex.Message}");
                EditorUtility.DisplayDialog("Error", $"Failed to stop server:\n\n{ex.Message}", "OK");
            }
            finally
            {
                RefreshHttpUi();
            }
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
                EditorUtility.DisplayDialog(
                    "Port Unavailable",
                    $"The requested port could not be used:\n\n{ex.Message}\n\nReverting to the active Unity port.",
                    "OK");
                unityPortField.value = MCPServiceLocator.Bridge.CurrentPort.ToString();
            }
        }

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

        public async Task VerifyBridgeConnectionAsync()
        {
            // Prevent concurrent verification calls
            if (verificationTask != null && !verificationTask.IsCompleted)
            {
                return;
            }

            verificationTask = VerifyBridgeConnectionInternalAsync();
            await verificationTask;
        }

        private async Task VerifyBridgeConnectionInternalAsync()
        {
            var bridgeService = MCPServiceLocator.Bridge;
            if (!bridgeService.IsRunning)
            {
                healthStatusLabel.text = HealthStatusUnknown;
                healthIndicator.RemoveFromClassList("healthy");
                healthIndicator.RemoveFromClassList("warning");
                healthIndicator.AddToClassList("unknown");
                
                // Only log if state changed
                if (lastHealthStatus != HealthStatusUnknown)
                {
                    McpLog.Warn("Cannot verify connection: Bridge is not running");
                    lastHealthStatus = HealthStatusUnknown;
                }
                return;
            }

            var result = await bridgeService.VerifyAsync();

            healthIndicator.RemoveFromClassList("healthy");
            healthIndicator.RemoveFromClassList("warning");
            healthIndicator.RemoveFromClassList("unknown");

            string newStatus;
            if (result.Success && result.PingSucceeded)
            {
                newStatus = HealthStatusHealthy;
                healthStatusLabel.text = newStatus;
                healthIndicator.AddToClassList("healthy");
                
                // Only log if state changed
                if (lastHealthStatus != newStatus)
                {
                    McpLog.Debug($"Connection verification successful: {result.Message}");
                    lastHealthStatus = newStatus;
                }
            }
            else if (result.HandshakeValid)
            {
                newStatus = HealthStatusPingFailed;
                healthStatusLabel.text = newStatus;
                healthIndicator.AddToClassList("warning");
                
                // Log once per distinct warning state
                if (lastHealthStatus != newStatus)
                {
                    McpLog.Warn($"Connection verification warning: {result.Message}");
                    lastHealthStatus = newStatus;
                }
            }
            else
            {
                newStatus = HealthStatusUnhealthy;
                healthStatusLabel.text = newStatus;
                healthIndicator.AddToClassList("warning");
                
                // Log once per distinct error state
                if (lastHealthStatus != newStatus)
                {
                    McpLog.Error($"Connection verification failed: {result.Message}");
                    lastHealthStatus = newStatus;
                }
            }

        }

        private void EnsureApiKeyExists()
        {
            if (!AuthPreferencesUtility.IsAuthEnabled())
            {
                return;
            }

            string current = AuthPreferencesUtility.GetApiKey(ensureExists: false);
            if (string.IsNullOrEmpty(current))
            {
                AuthPreferencesUtility.SetApiKey(AuthPreferencesUtility.GenerateNewApiKey());
            }
        }

        private void UpdateApiKeyField()
        {
            if (apiKeyField != null)
            {
                apiKeyField.value = AuthPreferencesUtility.GetApiKey(ensureExists: false);
            }
        }
    }
}
