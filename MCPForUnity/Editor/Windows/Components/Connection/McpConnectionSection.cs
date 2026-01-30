using System;
using System.Threading.Tasks;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Models;
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
            HTTPLocal,
            HTTPRemote,
            Stdio
        }

        // UI Elements
        private EnumField transportDropdown;
        private VisualElement transportMismatchWarning;
        private Label transportMismatchText;
        private VisualElement httpUrlRow;
        private VisualElement httpServerControlRow;
        private Foldout manualCommandFoldout;
        private VisualElement httpServerCommandSection;
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

        // API Key UI Elements (for remote-hosted mode)
        private VisualElement apiKeyRow;
        private TextField apiKeyField;
        private Button getApiKeyButton;
        private Button clearApiKeyButton;
        private string cachedLoginUrl;

        private bool connectionToggleInProgress;
        private bool httpServerToggleInProgress;
        private Task verificationTask;
        private string lastHealthStatus;
        private double lastLocalServerRunningPollTime;
        private bool lastLocalServerRunning;

        // Reference to Advanced section for health status updates
        private Action<bool, string> onHealthStatusUpdate;

        // Events
        public event Action OnManualConfigUpdateRequested;
        public event Action OnTransportChanged;

        public VisualElement Root { get; private set; }

        public void SetHealthStatusUpdateCallback(Action<bool, string> callback)
        {
            onHealthStatusUpdate = callback;
        }

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
            transportMismatchWarning = Root.Q<VisualElement>("transport-mismatch-warning");
            transportMismatchText = Root.Q<Label>("transport-mismatch-text");
            httpUrlRow = Root.Q<VisualElement>("http-url-row");
            httpServerControlRow = Root.Q<VisualElement>("http-server-control-row");
            manualCommandFoldout = Root.Q<Foldout>("manual-command-foldout");
            httpServerCommandSection = Root.Q<VisualElement>("http-server-command-section");
            httpServerCommandField = Root.Q<TextField>("http-server-command");
            copyHttpServerCommandButton = Root.Q<Button>("copy-http-server-command-button");
            httpServerCommandHint = Root.Q<Label>("http-server-command-hint");
            httpUrlField = Root.Q<TextField>("http-url");
            startHttpServerButton = Root.Q<Button>("start-http-server-button");
            unitySocketPortRow = Root.Q<VisualElement>("unity-socket-port-row");
            unityPortField = Root.Q<TextField>("unity-port");
            statusIndicator = Root.Q<VisualElement>("status-indicator");
            connectionStatusLabel = Root.Q<Label>("connection-status");
            connectionToggleButton = Root.Q<Button>("connection-toggle");

            // API Key UI Elements
            apiKeyRow = Root.Q<VisualElement>("api-key-row");
            apiKeyField = Root.Q<TextField>("api-key-field");
            getApiKeyButton = Root.Q<Button>("get-api-key-button");
            clearApiKeyButton = Root.Q<Button>("clear-api-key-button");
        }

        private void InitializeUI()
        {
            // Ensure manual command foldout starts collapsed
            if (manualCommandFoldout != null)
            {
                manualCommandFoldout.value = false;
            }

            transportDropdown.Init(TransportProtocol.HTTPLocal);
            bool useHttpTransport = EditorConfigurationCache.Instance.UseHttpTransport;
            if (!useHttpTransport)
            {
                transportDropdown.value = TransportProtocol.Stdio;
            }
            else
            {
                // Back-compat: if scope pref isn't set yet, infer from current URL.
                string scope = EditorPrefs.GetString(EditorPrefKeys.HttpTransportScope, string.Empty);
                if (string.IsNullOrEmpty(scope))
                {
                    scope = MCPServiceLocator.Server.IsLocalUrl() ? "local" : "remote";
                    try
                    {
                        EditorPrefs.SetString(EditorPrefKeys.HttpTransportScope, scope);
                    }
                    catch
                    {
                        McpLog.Debug("Failed to set HttpTransportScope pref.");
                    }
                }

                transportDropdown.value = scope == "remote" ? TransportProtocol.HTTPRemote : TransportProtocol.HTTPLocal;
            }

            // Set tooltips
            if (httpUrlField != null)
                httpUrlField.tooltip = "HTTP endpoint URL for the MCP server. Use localhost for local servers.";
            if (unityPortField != null)
                unityPortField.tooltip = "Port for Unity's internal MCP bridge socket. Used for stdio transport.";
            if (connectionToggleButton != null)
                connectionToggleButton.tooltip = "Start or end the MCP session between Unity and the server.";

            httpUrlField.value = HttpEndpointUtility.GetBaseUrl();

            // Initialize API key field
            if (apiKeyField != null)
            {
                apiKeyField.value = EditorPrefs.GetString(EditorPrefKeys.ApiKey, string.Empty);
                apiKeyField.tooltip = "API key for remote-hosted MCP server authentication";
                apiKeyField.isPasswordField = true;
                apiKeyField.maskChar = '*';
            }

            int unityPort = EditorPrefs.GetInt(EditorPrefKeys.UnitySocketPort, 0);
            if (unityPort == 0)
            {
                unityPort = MCPServiceLocator.Bridge.CurrentPort;
            }
            unityPortField.value = unityPort.ToString();

            UpdateHttpFieldVisibility();
            RefreshHttpUi();
            UpdateConnectionStatus();
        }

        private void RegisterCallbacks()
        {
            transportDropdown.RegisterValueChangedCallback(evt =>
            {
                var previous = (TransportProtocol)evt.previousValue;
                var selected = (TransportProtocol)evt.newValue;
                bool useHttp = selected != TransportProtocol.Stdio;
                EditorConfigurationCache.Instance.SetUseHttpTransport(useHttp);

                // Clear any stale resume flags when user manually changes transport
                try { EditorPrefs.DeleteKey(EditorPrefKeys.ResumeStdioAfterReload); } catch { }
                try { EditorPrefs.DeleteKey(EditorPrefKeys.ResumeHttpAfterReload); } catch { }

                if (useHttp)
                {
                    string scope = selected == TransportProtocol.HTTPRemote ? "remote" : "local";
                    EditorConfigurationCache.Instance.SetHttpTransportScope(scope);
                }

                // Swap the displayed URL to match the newly selected scope
                SyncUrlFieldToScope();
                UpdateHttpFieldVisibility();
                RefreshHttpUi();
                UpdateConnectionStatus();
                OnManualConfigUpdateRequested?.Invoke();
                OnTransportChanged?.Invoke();
                McpLog.Info($"Transport changed to: {evt.newValue}");

                // Best-effort: stop the deselected transport to avoid leaving duplicated sessions running.
                // (Switching between HttpLocal/HttpRemote does not require stopping.)
                bool prevWasHttp = previous != TransportProtocol.Stdio;
                bool nextIsHttp = selected != TransportProtocol.Stdio;
                if (prevWasHttp != nextIsHttp)
                {
                    var stopMode = nextIsHttp ? TransportMode.Stdio : TransportMode.Http;
                    try
                    {
                        var stopTask = MCPServiceLocator.TransportManager.StopAsync(stopMode);
                        stopTask.ContinueWith(t =>
                        {
                            try
                            {
                                if (t.IsFaulted)
                                {
                                    var msg = t.Exception?.GetBaseException()?.Message ?? "Unknown error";
                                    McpLog.Warn($"Async stop of {stopMode} transport failed: {msg}");
                                }
                            }
                            catch { }
                        }, TaskScheduler.Default);
                    }
                    catch (Exception ex)
                    {
                        McpLog.Warn($"Failed to stop previous transport ({stopMode}) after selection change: {ex.Message}");
                    }
                }
            });

            // Don't normalize/overwrite the URL on every keystroke (it fights the user and can duplicate schemes).
            // Instead, persist + normalize on focus-out / Enter, then update UI once.
            httpUrlField.RegisterCallback<FocusOutEvent>(_ => PersistHttpUrlFromField());
            httpUrlField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    PersistHttpUrlFromField();
                    evt.StopPropagation();
                }
            });

            if (startHttpServerButton != null)
            {
                startHttpServerButton.clicked += OnHttpServerToggleClicked;
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

            // API Key field callbacks
            if (apiKeyField != null)
            {
                apiKeyField.RegisterCallback<FocusOutEvent>(_ => PersistApiKeyFromField());
                apiKeyField.RegisterCallback<KeyDownEvent>(evt =>
                {
                    if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                    {
                        PersistApiKeyFromField();
                        evt.StopPropagation();
                    }
                });
            }

            if (getApiKeyButton != null)
            {
                getApiKeyButton.clicked += OnGetApiKeyClicked;
            }

            if (clearApiKeyButton != null)
            {
                clearApiKeyButton.clicked += OnClearApiKeyClicked;
            }
        }

        private void PersistHttpUrlFromField()
        {
            if (httpUrlField == null)
            {
                return;
            }

            HttpEndpointUtility.SaveBaseUrl(httpUrlField.text);
            // Update displayed value to normalized form without re-triggering callbacks/caret jumps.
            httpUrlField.SetValueWithoutNotify(HttpEndpointUtility.GetBaseUrl());
            // Invalidate cached login URL so it is re-fetched for the new base URL.
            cachedLoginUrl = null;
            OnManualConfigUpdateRequested?.Invoke();
            RefreshHttpUi();
        }

        public void UpdateConnectionStatus()
        {
            var bridgeService = MCPServiceLocator.Bridge;
            bool isRunning = bridgeService.IsRunning;
            bool showLocalServerControls = IsHttpLocalSelected();
            bool debugMode = EditorPrefs.GetBool(EditorPrefKeys.DebugLogs, false);
            // EditorConfigurationCache is the source of truth for transport selection after domain reload
            // (EditorPrefs is still used for debugMode and other UI-only state)
            bool stdioSelected = !EditorConfigurationCache.Instance.UseHttpTransport;

            // Keep the Start/Stop Server button label in sync even when the session is not running
            // (e.g., orphaned server after a domain reload).
            // NOTE: This also updates lastLocalServerRunning which is used below for session toggle visibility.
            UpdateStartHttpButtonState();

            // Detect orphaned session: if HTTP Local session thinks it's running but the server is gone,
            // automatically end the session to keep UI in sync with reality.
            if (showLocalServerControls && isRunning && !lastLocalServerRunning && !connectionToggleInProgress)
            {
                McpLog.Info("Server no longer running; ending orphaned session.");
                _ = EndOrphanedSessionAsync();
                isRunning = false; // Update local state for the rest of this method
            }

            // For HTTP Local: show session toggle button only when server is running (so user can manually start/end session).
            // For Stdio/HTTP Remote: always show the session toggle button.
            // This separates server lifecycle from session lifecycle for multi-instance scenarios.
            // We use lastLocalServerRunning which was just refreshed by UpdateStartHttpButtonState() above.
            if (connectionToggleButton != null)
            {
                bool showSessionToggle = !showLocalServerControls || lastLocalServerRunning;
                connectionToggleButton.style.display = showSessionToggle ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (isRunning)
            {
                // Show instance name (project folder name) for better identification in multi-instance scenarios.
                // Defensive: handle edge cases where path parsing might return null/empty.
                string projectDir = System.IO.Path.GetDirectoryName(Application.dataPath);
                string instanceName = !string.IsNullOrEmpty(projectDir)
                    ? System.IO.Path.GetFileName(projectDir)
                    : "Unity";
                if (string.IsNullOrEmpty(instanceName)) instanceName = "Unity";
                connectionStatusLabel.text = $"Session Active ({instanceName})";
                statusIndicator.RemoveFromClassList("disconnected");
                statusIndicator.AddToClassList("connected");
                connectionToggleButton.text = "End Session";
                connectionToggleButton.SetEnabled(true); // Re-enable in case it was disabled during resumption

                // Force the UI to reflect the actual port being used
                unityPortField.value = bridgeService.CurrentPort.ToString();
                unityPortField.SetEnabled(false);
            }
            else
            {
                // Check if we're resuming the stdio bridge after a domain reload.
                // During this brief window, show "Resuming..." instead of "No Session" to avoid UI flicker.
                bool isStdioResuming = stdioSelected
                    && EditorPrefs.GetBool(EditorPrefKeys.ResumeStdioAfterReload, false);

                if (isStdioResuming)
                {
                    connectionStatusLabel.text = "Resuming...";
                    // Keep the indicator in a neutral/transitional state
                    statusIndicator.RemoveFromClassList("connected");
                    statusIndicator.RemoveFromClassList("disconnected");
                    connectionToggleButton.text = "Start Session";
                    connectionToggleButton.SetEnabled(false);
                }
                else
                {
                    connectionStatusLabel.text = "No Session";
                    statusIndicator.RemoveFromClassList("connected");
                    statusIndicator.AddToClassList("disconnected");
                    connectionToggleButton.text = "Start Session";

                    // Disable Start Session for HTTP Remote when no API key is set
                    bool httpRemoteNeedsKey = transportDropdown != null
                        && (TransportProtocol)transportDropdown.value == TransportProtocol.HTTPRemote
                        && string.IsNullOrEmpty(EditorPrefs.GetString(EditorPrefKeys.ApiKey, string.Empty));
                    connectionToggleButton.SetEnabled(!httpRemoteNeedsKey);
                    connectionToggleButton.tooltip = httpRemoteNeedsKey
                        ? "An API key is required for HTTP Remote. Enter one above."
                        : string.Empty;
                }

                unityPortField.SetEnabled(!isStdioResuming);

                int savedPort = EditorPrefs.GetInt(EditorPrefKeys.UnitySocketPort, 0);
                unityPortField.value = (savedPort == 0
                    ? bridgeService.CurrentPort
                    : savedPort).ToString();
            }

            // For stdio session toggling, make End Session visually "danger" (red).
            // (HTTP Local uses the consolidated Start/Stop Server button instead.)
            connectionToggleButton?.EnableInClassList("server-running", isRunning && stdioSelected);
        }

        public void UpdateHttpServerCommandDisplay()
        {
            if (httpServerCommandSection == null || httpServerCommandField == null)
            {
                return;
            }

            bool useHttp = transportDropdown != null && (TransportProtocol)transportDropdown.value != TransportProtocol.Stdio;
            bool httpLocalSelected = IsHttpLocalSelected();
            bool isLocalHttpUrl = MCPServiceLocator.Server.IsLocalUrl();

            // Only show the local-server helper UI when HTTP Local is selected.
            if (!useHttp || !httpLocalSelected)
            {
                httpServerCommandSection.style.display = DisplayStyle.None;
                httpServerCommandField.value = string.Empty;
                httpServerCommandField.tooltip = string.Empty;
                httpServerCommandField.SetEnabled(false);
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

            if (!isLocalHttpUrl)
            {
                httpServerCommandField.value = string.Empty;
                httpServerCommandField.tooltip = string.Empty;
                httpServerCommandField.SetEnabled(false);
                httpServerCommandSection.EnableInClassList("http-local-invalid-url", true);
                if (httpServerCommandHint != null)
                {
                    httpServerCommandHint.text = "⚠ HTTP Local requires a localhost URL (localhost/127.0.0.1/0.0.0.0/::1).";
                    httpServerCommandHint.AddToClassList("http-local-url-error");
                }
                copyHttpServerCommandButton?.SetEnabled(false);
                return;
            }

            httpServerCommandSection.EnableInClassList("http-local-invalid-url", false);
            httpServerCommandField.SetEnabled(true);
            if (httpServerCommandHint != null)
            {
                httpServerCommandHint.RemoveFromClassList("http-local-url-error");
            }

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

        private void UpdateHttpFieldVisibility()
        {
            bool useHttp = (TransportProtocol)transportDropdown.value != TransportProtocol.Stdio;
            bool httpLocalSelected = IsHttpLocalSelected();
            bool httpRemoteSelected = transportDropdown != null && (TransportProtocol)transportDropdown.value == TransportProtocol.HTTPRemote;

            httpUrlRow.style.display = useHttp ? DisplayStyle.Flex : DisplayStyle.None;
            httpServerControlRow.style.display = useHttp && httpLocalSelected ? DisplayStyle.Flex : DisplayStyle.None;
            unitySocketPortRow.style.display = useHttp ? DisplayStyle.None : DisplayStyle.Flex;

            // Manual Server Launch foldout only relevant for HTTP Local
            if (manualCommandFoldout != null)
                manualCommandFoldout.style.display = httpLocalSelected ? DisplayStyle.Flex : DisplayStyle.None;

            // API key fields only visible in HTTP Remote mode
            if (apiKeyRow != null)
                apiKeyRow.style.display = httpRemoteSelected ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private bool IsHttpLocalSelected()
        {
            return transportDropdown != null && (TransportProtocol)transportDropdown.value == TransportProtocol.HTTPLocal;
        }

        private void SyncUrlFieldToScope()
        {
            if (httpUrlField == null) return;
            httpUrlField.SetValueWithoutNotify(HttpEndpointUtility.GetBaseUrl());
            cachedLoginUrl = null;
        }

        private void UpdateStartHttpButtonState()
        {
            if (startHttpServerButton == null)
                return;

            bool useHttp = transportDropdown != null && (TransportProtocol)transportDropdown.value != TransportProtocol.Stdio;
            if (!useHttp)
            {
                startHttpServerButton.SetEnabled(false);
                startHttpServerButton.tooltip = string.Empty;
                return;
            }

            bool httpLocalSelected = IsHttpLocalSelected();
            bool canStartLocalServer = httpLocalSelected && MCPServiceLocator.Server.IsLocalUrl();
            bool localServerRunning = false;

            // Avoid running expensive port/PID checks every UI tick; use a fast socket probe for UI state.
            if (httpLocalSelected)
            {
                double now = EditorApplication.timeSinceStartup;
                if ((now - lastLocalServerRunningPollTime) > 0.75f || httpServerToggleInProgress)
                {
                    lastLocalServerRunningPollTime = now;
                    lastLocalServerRunning = MCPServiceLocator.Server.IsLocalHttpServerReachable();
                }
                localServerRunning = lastLocalServerRunning;
            }

            // Server button only controls server lifecycle (Start/Stop Server).
            // Session lifecycle is handled by the separate connectionToggleButton.
            bool shouldShowStop = localServerRunning;
            startHttpServerButton.text = shouldShowStop ? "Stop Server" : "Start Server";
            // Note: Server logs may contain transient HTTP 400s on /mcp during startup probing and
            // CancelledError stack traces on shutdown when streaming requests are cancelled; this is expected.
            startHttpServerButton.EnableInClassList("server-running", localServerRunning);
            startHttpServerButton.SetEnabled(
                !httpServerToggleInProgress && (shouldShowStop || canStartLocalServer));
            startHttpServerButton.tooltip = httpLocalSelected
                ? (canStartLocalServer ? string.Empty : "HTTP Local requires a localhost URL (localhost/127.0.0.1/0.0.0.0/::1).")
                : string.Empty;
        }

        private void RefreshHttpUi()
        {
            UpdateStartHttpButtonState();
            UpdateHttpServerCommandDisplay();
        }

        private async void OnHttpServerToggleClicked()
        {
            if (httpServerToggleInProgress)
            {
                return;
            }

            var bridgeService = MCPServiceLocator.Bridge;
            httpServerToggleInProgress = true;
            startHttpServerButton?.SetEnabled(false);

            try
            {
                // Check if a local server is running.
                bool serverRunning = IsHttpLocalSelected() && MCPServiceLocator.Server.IsLocalHttpServerReachable();

                if (serverRunning)
                {
                    // Stop Server: end session first (if active), then stop the server.
                    if (bridgeService.IsRunning)
                    {
                        await bridgeService.StopAsync();
                    }
                    bool stopped = MCPServiceLocator.Server.StopLocalHttpServer();
                    if (!stopped)
                    {
                        McpLog.Warn("Failed to stop HTTP server or no server was running");
                    }
                }
                else
                {
                    // Start Server: launch the local HTTP server.
                    // When WE start the server, auto-start our session (we clearly want to use it).
                    // This differs from detecting an already-running server, where we require manual session start.
                    bool serverStarted = MCPServiceLocator.Server.StartLocalHttpServer();
                    if (serverStarted)
                    {
                        await TryAutoStartSessionAsync();
                    }
                    else
                    {
                        McpLog.Warn("Failed to start local HTTP server");
                    }
                }
            }
            catch (Exception ex)
            {
                McpLog.Error($"HTTP server toggle failed: {ex.Message}");
                EditorUtility.DisplayDialog("Error", $"Failed to toggle local HTTP server:\n\n{ex.Message}", "OK");
            }
            finally
            {
                httpServerToggleInProgress = false;
                RefreshHttpUi();
                UpdateConnectionStatus();
            }
        }

        private async Task TryAutoStartSessionAsync()
        {
            // Wait briefly for the HTTP server to become ready, then start the session.
            // This is called when THIS instance starts the server (not when detecting an external server).
            var bridgeService = MCPServiceLocator.Bridge;
            // Windows/dev mode may take much longer due to uv package resolution, fresh downloads, antivirus scans, etc.
            const int maxAttempts = 30;
            // Use shorter delays initially, then longer delays to allow server startup
            var shortDelay = TimeSpan.FromMilliseconds(500);
            var longDelay = TimeSpan.FromSeconds(3);

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                var delay = attempt < 6 ? shortDelay : longDelay;

                // Check if server is actually accepting connections
                bool serverDetected = MCPServiceLocator.Server.IsLocalHttpServerReachable();

                if (serverDetected)
                {
                    // Server detected - try to connect
                    bool started = await bridgeService.StartAsync();
                    if (started)
                    {
                        await VerifyBridgeConnectionAsync();
                        UpdateConnectionStatus();
                        return;
                    }
                }
                else if (attempt >= 20)
                {
                    // After many attempts without detection, try connecting anyway as a last resort.
                    // This handles cases where process detection fails but the server is actually running.
                    // Only try once every 3 attempts to avoid spamming connection errors (at attempts 20, 23, 26, 29).
                    if ((attempt - 20) % 3 != 0) continue;

                    bool started = await bridgeService.StartAsync();
                    if (started)
                    {
                        await VerifyBridgeConnectionAsync();
                        UpdateConnectionStatus();
                        return;
                    }
                }

                if (attempt < maxAttempts - 1)
                {
                    await Task.Delay(delay);
                }
            }

            McpLog.Warn("Failed to auto-start session after launching the HTTP server.");
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
                    // Clear any resume flags when user manually ends the session to prevent
                    // getting stuck in "Resuming..." state (the flag may have been set by a
                    // domain reload that started just before the user clicked End Session)
                    try { EditorPrefs.DeleteKey(EditorPrefKeys.ResumeStdioAfterReload); } catch { }
                    try { EditorPrefs.DeleteKey(EditorPrefKeys.ResumeHttpAfterReload); } catch { }

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
                        var mode = EditorConfigurationCache.Instance.UseHttpTransport
                            ? TransportMode.Http : TransportMode.Stdio;
                        var state = MCPServiceLocator.TransportManager.GetState(mode);
                        string errorMsg = state?.Error
                            ?? "Failed to start the MCP session. Check the server URL and that the server is running.";
                        EditorUtility.DisplayDialog("Connection Failed", errorMsg, "OK");
                        McpLog.Warn($"Failed to start MCP bridge: {errorMsg}");
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

        private async Task EndOrphanedSessionAsync()
        {
            // Fire-and-forget cleanup of orphaned session when server is no longer running.
            // This prevents the UI from showing "Session Active" when the underlying server is gone.
            try
            {
                connectionToggleInProgress = true;
                connectionToggleButton?.SetEnabled(false);

                // Clear resume flags to prevent getting stuck in "Resuming..." state
                try { EditorPrefs.DeleteKey(EditorPrefKeys.ResumeStdioAfterReload); } catch { }
                try { EditorPrefs.DeleteKey(EditorPrefKeys.ResumeHttpAfterReload); } catch { }

                await MCPServiceLocator.Bridge.StopAsync();
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Failed to end orphaned session: {ex.Message}");
            }
            finally
            {
                connectionToggleInProgress = false;
                connectionToggleButton?.SetEnabled(true);
                UpdateConnectionStatus();
            }
        }

        private void PersistApiKeyFromField()
        {
            if (apiKeyField == null)
            {
                return;
            }

            string apiKey = apiKeyField.text?.Trim() ?? string.Empty;
            string existingKey = EditorPrefs.GetString(EditorPrefKeys.ApiKey, string.Empty);

            if (apiKey != existingKey)
            {
                EditorPrefs.SetString(EditorPrefKeys.ApiKey, apiKey);
                OnManualConfigUpdateRequested?.Invoke();
                UpdateConnectionStatus();
                McpLog.Info(string.IsNullOrEmpty(apiKey) ? "API key cleared" : "API key updated");
            }
        }

        private async void OnGetApiKeyClicked()
        {
            if (getApiKeyButton != null)
            {
                getApiKeyButton.SetEnabled(false);
            }

            try
            {
                string loginUrl = await GetLoginUrlAsync();
                if (string.IsNullOrEmpty(loginUrl))
                {
                    EditorUtility.DisplayDialog("API Key",
                        "API key management is not available for this server. Contact your server administrator.",
                        "OK");
                    return;
                }
                Application.OpenURL(loginUrl);
            }
            catch (Exception ex)
            {
                McpLog.Error($"Failed to get login URL: {ex.Message}");
                EditorUtility.DisplayDialog("Error",
                    $"Failed to get API key login URL:\n\n{ex.Message}",
                    "OK");
            }
            finally
            {
                if (getApiKeyButton != null)
                {
                    getApiKeyButton.SetEnabled(true);
                }
            }
        }

        private async Task<string> GetLoginUrlAsync()
        {
            if (!string.IsNullOrEmpty(cachedLoginUrl))
            {
                return cachedLoginUrl;
            }

            string baseUrl = HttpEndpointUtility.GetBaseUrl();
            string loginUrlEndpoint = $"{baseUrl.TrimEnd('/')}/api/auth/login-url";

            try
            {
                using (var client = new System.Net.Http.HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                    var response = await client.GetAsync(loginUrlEndpoint);

                    if (response.IsSuccessStatusCode)
                    {
                        string json = await response.Content.ReadAsStringAsync();
                        var result = Newtonsoft.Json.Linq.JObject.Parse(json);

                        if (result.Value<bool>("success"))
                        {
                            cachedLoginUrl = result.Value<string>("login_url");
                            return cachedLoginUrl;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                McpLog.Debug($"Failed to fetch login URL from {loginUrlEndpoint}: {ex.Message}");
            }

            return null;
        }

        private void OnClearApiKeyClicked()
        {
            EditorPrefs.SetString(EditorPrefKeys.ApiKey, string.Empty);
            if (apiKeyField != null)
            {
                apiKeyField.SetValueWithoutNotify(string.Empty);
            }
            OnManualConfigUpdateRequested?.Invoke();
            UpdateConnectionStatus();
            McpLog.Info("API key cleared");
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
                onHealthStatusUpdate?.Invoke(false, HealthStatus.Unknown);

                // Only log if state changed
                if (lastHealthStatus != HealthStatus.Unknown)
                {
                    McpLog.Warn("Cannot verify connection: Bridge is not running");
                    lastHealthStatus = HealthStatus.Unknown;
                }
                return;
            }

            var result = await bridgeService.VerifyAsync();

            string newStatus;
            bool isHealthy;
            if (result.Success && result.PingSucceeded)
            {
                newStatus = HealthStatus.Healthy;
                isHealthy = true;

                // Only log if state changed
                if (lastHealthStatus != newStatus)
                {
                    McpLog.Debug($"Connection verification successful: {result.Message}");
                    lastHealthStatus = newStatus;
                }
            }
            else if (result.HandshakeValid)
            {
                newStatus = HealthStatus.PingFailed;
                isHealthy = false;

                // Log once per distinct warning state
                if (lastHealthStatus != newStatus)
                {
                    McpLog.Warn($"Connection verification warning: {result.Message}");
                    lastHealthStatus = newStatus;
                }
            }
            else
            {
                newStatus = HealthStatus.Unhealthy;
                isHealthy = false;

                // Log once per distinct error state
                if (lastHealthStatus != newStatus)
                {
                    McpLog.Error($"Connection verification failed: {result.Message}");
                    lastHealthStatus = newStatus;
                }
            }

            onHealthStatusUpdate?.Invoke(isHealthy, newStatus);
        }

        /// <summary>
        /// Updates the transport mismatch warning banner based on the client's configured transport.
        /// Shows a warning if the client's transport doesn't match the server's current transport setting.
        /// </summary>
        /// <param name="clientName">The display name of the client being checked.</param>
        /// <param name="clientTransport">The transport the client is configured to use.</param>
        public void UpdateTransportMismatchWarning(string clientName, ConfiguredTransport clientTransport)
        {
            if (transportMismatchWarning == null || transportMismatchText == null)
                return;

            // If client transport is unknown, hide the warning (we can't determine mismatch)
            if (clientTransport == ConfiguredTransport.Unknown)
            {
                transportMismatchWarning.RemoveFromClassList("visible");
                return;
            }

            // Determine the server's current transport setting (3-way: Stdio, Http, HttpRemote)
            ConfiguredTransport serverTransport = HttpEndpointUtility.GetCurrentServerTransport();

            // Check for mismatch
            bool hasMismatch = clientTransport != serverTransport;

            if (hasMismatch)
            {
                string clientTransportName = TransportDisplayName(clientTransport);
                string serverTransportName = TransportDisplayName(serverTransport);

                transportMismatchText.text = $"⚠ {clientName} is configured for \"{clientTransportName}\" but server is set to \"{serverTransportName}\". " +
                    "Click \"Configure\" in Client Configuration to update.";
                transportMismatchWarning.AddToClassList("visible");
            }
            else
            {
                transportMismatchWarning.RemoveFromClassList("visible");
            }
        }

        /// <summary>
        /// Clears the transport mismatch warning banner.
        /// </summary>
        public void ClearTransportMismatchWarning()
        {
            transportMismatchWarning?.RemoveFromClassList("visible");
        }

        private static string TransportDisplayName(ConfiguredTransport transport)
        {
            return transport switch
            {
                ConfiguredTransport.Stdio => "stdio",
                ConfiguredTransport.Http => "HTTP Local",
                ConfiguredTransport.HttpRemote => "HTTP Remote",
                _ => "unknown"
            };
        }
    }
}
