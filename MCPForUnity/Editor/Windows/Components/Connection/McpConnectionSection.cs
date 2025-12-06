using System;
using System.Threading.Tasks;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MCPForUnity.Editor.Windows.Components.Connection
{
    /// <summary>
    /// Controller for the Connection section of the MCP For Unity editor window.
    /// Handles stdio transport connection status and health checks.
    /// </summary>
    public class McpConnectionSection
    {
        // UI Elements
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
            int unityPort = EditorPrefs.GetInt(EditorPrefKeys.UnitySocketPort, 0);
            if (unityPort == 0)
            {
                unityPort = MCPServiceLocator.Bridge.CurrentPort;
            }
            unityPortField.value = unityPort.ToString();
        }

        private void RegisterCallbacks()
        {
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
    }
}
