using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using MCPForUnity.Editor.Clients;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Models;
using MCPForUnity.Editor.Services;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MCPForUnity.Editor.Windows.Components.ClientConfig
{
    /// <summary>
    /// Controller for the Client Configuration section of the MCP For Unity editor window.
    /// Handles client selection, configuration, status display, and manual configuration details.
    /// </summary>
    public class McpClientConfigSection
    {
        // UI Elements
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
        private readonly List<IMcpClientConfigurator> configurators;
        private readonly Dictionary<IMcpClientConfigurator, DateTime> lastStatusChecks = new();
        private readonly HashSet<IMcpClientConfigurator> statusRefreshInFlight = new();
        private static readonly TimeSpan StatusRefreshInterval = TimeSpan.FromSeconds(45);
        private int selectedClientIndex = 0;

        // Events
        /// <summary>
        /// Fired when the selected client's configured transport is detected/updated.
        /// The parameter contains the client name and its configured transport.
        /// </summary>
        public event Action<string, ConfiguredTransport> OnClientTransportDetected;

        public VisualElement Root { get; private set; }

        public McpClientConfigSection(VisualElement root)
        {
            Root = root;
            configurators = MCPServiceLocator.Client.GetAllClients().ToList();
            CacheUIElements();
            InitializeUI();
            RegisterCallbacks();
        }

        private void CacheUIElements()
        {
            clientDropdown = Root.Q<DropdownField>("client-dropdown");
            configureAllButton = Root.Q<Button>("configure-all-button");
            clientStatusIndicator = Root.Q<VisualElement>("client-status-indicator");
            clientStatusLabel = Root.Q<Label>("client-status");
            configureButton = Root.Q<Button>("configure-button");
            claudeCliPathRow = Root.Q<VisualElement>("claude-cli-path-row");
            claudeCliPath = Root.Q<TextField>("claude-cli-path");
            browseClaudeButton = Root.Q<Button>("browse-claude-button");
            manualConfigFoldout = Root.Q<Foldout>("manual-config-foldout");
            configPathField = Root.Q<TextField>("config-path");
            copyPathButton = Root.Q<Button>("copy-path-button");
            openFileButton = Root.Q<Button>("open-file-button");
            configJsonField = Root.Q<TextField>("config-json");
            copyJsonButton = Root.Q<Button>("copy-json-button");
            installationStepsLabel = Root.Q<Label>("installation-steps");
        }

        private void InitializeUI()
        {
            // Ensure manual config foldout starts collapsed
            if (manualConfigFoldout != null)
            {
                manualConfigFoldout.value = false;
            }

            var clientNames = configurators.Select(c => c.DisplayName).ToList();
            clientDropdown.choices = clientNames;
            if (clientNames.Count > 0)
            {
                clientDropdown.index = 0;
            }

            claudeCliPathRow.style.display = DisplayStyle.None;

            // Initialize the configuration display for the first selected client
            UpdateClientStatus();
            UpdateManualConfiguration();
            UpdateClaudeCliPathVisibility();
        }

        private void RegisterCallbacks()
        {
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

        public void UpdateClientStatus()
        {
            if (selectedClientIndex < 0 || selectedClientIndex >= configurators.Count)
                return;

            var client = configurators[selectedClientIndex];
            RefreshClientStatus(client);
        }

        private string GetStatusDisplayString(McpStatus status)
        {
            return status switch
            {
                McpStatus.NotConfigured => "Not Configured",
                McpStatus.Configured => "Configured",
                McpStatus.Running => "Running",
                McpStatus.Connected => "Connected",
                McpStatus.IncorrectPath => "Incorrect Path",
                McpStatus.CommunicationError => "Communication Error",
                McpStatus.NoResponse => "No Response",
                McpStatus.UnsupportedOS => "Unsupported OS",
                McpStatus.MissingConfig => "Missing MCPForUnity Config",
                McpStatus.Error => "Error",
                _ => "Unknown",
            };
        }

        public void UpdateManualConfiguration()
        {
            if (selectedClientIndex < 0 || selectedClientIndex >= configurators.Count)
                return;

            var client = configurators[selectedClientIndex];

            string configPath = client.GetConfigPath();
            configPathField.value = configPath;

            string configJson = client.GetManualSnippet();
            configJsonField.value = configJson;

            var steps = client.GetInstallationSteps();
            if (steps != null && steps.Count > 0)
            {
                var numbered = steps.Select((s, i) => $"{i + 1}. {s}");
                installationStepsLabel.text = string.Join("\n", numbered);
            }
            else
            {
                installationStepsLabel.text = "Configuration steps not available for this client.";
            }
        }

        private void UpdateClaudeCliPathVisibility()
        {
            if (selectedClientIndex < 0 || selectedClientIndex >= configurators.Count)
                return;

            var client = configurators[selectedClientIndex];

            if (client is ClaudeCliMcpConfigurator)
            {
                string claudePath = MCPServiceLocator.Paths.GetClaudeCliPath();
                if (string.IsNullOrEmpty(claudePath))
                {
                    claudeCliPathRow.style.display = DisplayStyle.Flex;
                    claudeCliPath.value = "Not found - click Browse to select";
                }
                else
                {
                    claudeCliPathRow.style.display = DisplayStyle.Flex;
                    claudeCliPath.value = claudePath;
                }
            }
            else
            {
                claudeCliPathRow.style.display = DisplayStyle.None;
            }
        }

        private void OnConfigureAllClientsClicked()
        {
            try
            {
                var summary = MCPServiceLocator.Client.ConfigureAllDetectedClients();

                string message = summary.GetSummaryMessage() + "\n\n";
                foreach (var msg in summary.Messages)
                {
                    message += msg + "\n";
                }

                EditorUtility.DisplayDialog("Configure All Clients", message, "OK");

                if (selectedClientIndex >= 0 && selectedClientIndex < configurators.Count)
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
            if (selectedClientIndex < 0 || selectedClientIndex >= configurators.Count)
                return;

            var client = configurators[selectedClientIndex];

            // Handle CLI configurators asynchronously
            if (client is ClaudeCliMcpConfigurator)
            {
                ConfigureClaudeCliAsync(client);
                return;
            }

            try
            {
                MCPServiceLocator.Client.ConfigureClient(client);
                lastStatusChecks.Remove(client);
                RefreshClientStatus(client, forceImmediate: true);
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

        private void ConfigureClaudeCliAsync(IMcpClientConfigurator client)
        {
            if (statusRefreshInFlight.Contains(client))
                return;

            statusRefreshInFlight.Add(client);
            bool isCurrentlyConfigured = client.Status == McpStatus.Configured;
            ApplyStatusToUi(client, showChecking: true, customMessage: isCurrentlyConfigured ? "Unregistering..." : "Registering...");

            // Capture ALL main-thread-only values before async task
            string projectDir = Path.GetDirectoryName(Application.dataPath);
            bool useHttpTransport = EditorConfigurationCache.Instance.UseHttpTransport;
            string claudePath = MCPServiceLocator.Paths.GetClaudeCliPath();
            string httpUrl = HttpEndpointUtility.GetMcpRpcUrl();
            var (uvxPath, gitUrl, packageName) = AssetPathUtility.GetUvxCommandParts();
            bool shouldForceRefresh = AssetPathUtility.ShouldForceUvxRefresh();
            string apiKey = EditorPrefs.GetString(EditorPrefKeys.ApiKey, string.Empty);

            // Compute pathPrepend on main thread
            string pathPrepend = null;
            if (Application.platform == RuntimePlatform.OSXEditor)
                pathPrepend = "/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin";
            else if (Application.platform == RuntimePlatform.LinuxEditor)
                pathPrepend = "/usr/local/bin:/usr/bin:/bin";
            try
            {
                string claudeDir = Path.GetDirectoryName(claudePath);
                if (!string.IsNullOrEmpty(claudeDir))
                    pathPrepend = string.IsNullOrEmpty(pathPrepend) ? claudeDir : $"{claudeDir}:{pathPrepend}";
            }
            catch { }

            Task.Run(() =>
            {
                try
                {
                    if (client is ClaudeCliMcpConfigurator cliConfigurator)
                    {
                        var serverTransport = HttpEndpointUtility.GetCurrentServerTransport();
                        cliConfigurator.ConfigureWithCapturedValues(
                            projectDir, claudePath, pathPrepend,
                            useHttpTransport, httpUrl,
                            uvxPath, gitUrl, packageName, shouldForceRefresh,
                            apiKey, serverTransport);
                    }
                    return (success: true, error: (string)null);
                }
                catch (Exception ex)
                {
                    return (success: false, error: ex.Message);
                }
            }).ContinueWith(t =>
            {
                string errorMessage = null;
                if (t.IsFaulted && t.Exception != null)
                {
                    errorMessage = t.Exception.GetBaseException()?.Message ?? "Configuration failed";
                }
                else if (!t.Result.success)
                {
                    errorMessage = t.Result.error;
                }

                EditorApplication.delayCall += () =>
                {
                    statusRefreshInFlight.Remove(client);
                    lastStatusChecks.Remove(client);

                    if (errorMessage != null)
                    {
                        if (client is McpClientConfiguratorBase baseConfigurator)
                        {
                            baseConfigurator.Client.SetStatus(McpStatus.Error, errorMessage);
                        }
                        McpLog.Error($"Configuration failed: {errorMessage}");
                        RefreshClientStatus(client, forceImmediate: true);
                    }
                    else
                    {
                        // Registration succeeded - trust the status set by RegisterWithCapturedValues
                        // and update UI without re-verifying (which could fail due to CLI timing/scope issues)
                        lastStatusChecks[client] = DateTime.UtcNow;
                        ApplyStatusToUi(client);
                    }
                    UpdateManualConfiguration();
                };
            });
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

        public void RefreshSelectedClient(bool forceImmediate = false)
        {
            if (selectedClientIndex >= 0 && selectedClientIndex < configurators.Count)
            {
                var client = configurators[selectedClientIndex];
                // Force immediate for non-Claude CLI, or when explicitly requested
                bool shouldForceImmediate = forceImmediate || client is not ClaudeCliMcpConfigurator;
                RefreshClientStatus(client, shouldForceImmediate);
                UpdateManualConfiguration();
                UpdateClaudeCliPathVisibility();
            }
        }

        private void RefreshClientStatus(IMcpClientConfigurator client, bool forceImmediate = false)
        {
            if (client is ClaudeCliMcpConfigurator)
            {
                RefreshClaudeCliStatus(client, forceImmediate);
                return;
            }

            if (forceImmediate || ShouldRefreshClient(client))
            {
                MCPServiceLocator.Client.CheckClientStatus(client);
                lastStatusChecks[client] = DateTime.UtcNow;
            }

            ApplyStatusToUi(client);
        }

        private void RefreshClaudeCliStatus(IMcpClientConfigurator client, bool forceImmediate)
        {
            bool hasStatus = lastStatusChecks.ContainsKey(client);
            bool needsRefresh = !hasStatus || ShouldRefreshClient(client);

            if (!hasStatus)
            {
                ApplyStatusToUi(client, showChecking: true);
            }
            else
            {
                ApplyStatusToUi(client);
            }

            if ((forceImmediate || needsRefresh) && !statusRefreshInFlight.Contains(client))
            {
                statusRefreshInFlight.Add(client);
                ApplyStatusToUi(client, showChecking: true);

                // Capture main-thread-only values before async task
                string projectDir = Path.GetDirectoryName(Application.dataPath);
                bool useHttpTransport = EditorConfigurationCache.Instance.UseHttpTransport;
                string claudePath = MCPServiceLocator.Paths.GetClaudeCliPath();

                Task.Run(() =>
                {
                    // Defensive: RefreshClientStatus routes Claude CLI clients here, but avoid hard-cast
                    // so accidental future call sites can't crash the UI.
                    if (client is ClaudeCliMcpConfigurator claudeConfigurator)
                    {
                        // Use thread-safe version with captured main-thread values
                        claudeConfigurator.CheckStatusWithProjectDir(projectDir, useHttpTransport, claudePath, attemptAutoRewrite: false);
                    }
                }).ContinueWith(t =>
                {
                    bool faulted = false;
                    string errorMessage = null;
                    if (t.IsFaulted && t.Exception != null)
                    {
                        var baseException = t.Exception.GetBaseException();
                        errorMessage = baseException?.Message ?? "Status check failed";
                        McpLog.Error($"Failed to refresh Claude CLI status: {errorMessage}");
                        faulted = true;
                    }

                    EditorApplication.delayCall += () =>
                    {
                        statusRefreshInFlight.Remove(client);
                        lastStatusChecks[client] = DateTime.UtcNow;
                        if (faulted)
                        {
                            if (client is McpClientConfiguratorBase baseConfigurator)
                            {
                                baseConfigurator.Client.SetStatus(McpStatus.Error, errorMessage ?? "Status check failed");
                            }
                        }
                        ApplyStatusToUi(client);
                    };
                });
            }
        }

        private bool ShouldRefreshClient(IMcpClientConfigurator client)
        {
            if (!lastStatusChecks.TryGetValue(client, out var last))
            {
                return true;
            }

            return (DateTime.UtcNow - last) > StatusRefreshInterval;
        }

        private void ApplyStatusToUi(IMcpClientConfigurator client, bool showChecking = false, string customMessage = null)
        {
            if (selectedClientIndex < 0 || selectedClientIndex >= configurators.Count)
                return;

            if (!ReferenceEquals(configurators[selectedClientIndex], client))
                return;

            clientStatusIndicator.RemoveFromClassList("configured");
            clientStatusIndicator.RemoveFromClassList("not-configured");
            clientStatusIndicator.RemoveFromClassList("warning");

            if (showChecking)
            {
                clientStatusLabel.text = customMessage ?? "Checking...";
                clientStatusLabel.style.color = StyleKeyword.Null;
                clientStatusIndicator.AddToClassList("warning");
                configureButton.text = client.GetConfigureActionLabel();
                return;
            }

            // Check for transport mismatch (3-way: Stdio, Http, HttpRemote)
            bool hasTransportMismatch = false;
            if (client.ConfiguredTransport != ConfiguredTransport.Unknown)
            {
                ConfiguredTransport serverTransport = HttpEndpointUtility.GetCurrentServerTransport();
                hasTransportMismatch = client.ConfiguredTransport != serverTransport;
            }

            // If configured but with transport mismatch, show warning state
            if (hasTransportMismatch && (client.Status == McpStatus.Configured || client.Status == McpStatus.Running || client.Status == McpStatus.Connected))
            {
                clientStatusLabel.text = "Transport Mismatch";
                clientStatusIndicator.AddToClassList("warning");
            }
            else
            {
                clientStatusLabel.text = GetStatusDisplayString(client.Status);

                switch (client.Status)
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

            clientStatusLabel.style.color = StyleKeyword.Null;
            configureButton.text = client.GetConfigureActionLabel();

            // Notify listeners about the client's configured transport
            OnClientTransportDetected?.Invoke(client.DisplayName, client.ConfiguredTransport);
        }
    }
}
