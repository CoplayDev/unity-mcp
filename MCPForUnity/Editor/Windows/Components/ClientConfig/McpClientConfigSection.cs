using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using MCPForUnity.Editor.Clients;
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

        public VisualElement Root { get; private set; }

        public McpClientConfigSection(VisualElement root)
        {
            Root = root;
            // Client configuration service is no longer available
            // This section should not be loaded, but handle gracefully if it is
            configurators = new List<IMcpClientConfigurator>();
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
            configPathField = Root.Q<TextField>("config-path");
            copyPathButton = Root.Q<Button>("copy-path-button");
            openFileButton = Root.Q<Button>("open-file-button");
            configJsonField = Root.Q<TextField>("config-json");
            copyJsonButton = Root.Q<Button>("copy-json-button");
            installationStepsLabel = Root.Q<Label>("installation-steps");
        }

        private void InitializeUI()
        {
            var clientNames = configurators.Select(c => c.DisplayName).ToList();
            clientDropdown.choices = clientNames;
            if (clientNames.Count > 0)
            {
                clientDropdown.index = 0;
            }

            claudeCliPathRow.style.display = DisplayStyle.None;
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
            EditorUtility.DisplayDialog("Client Configuration", 
                "Client configuration is no longer supported by this plugin.\n\n" +
                "Please configure your MCP client (e.g., Cursor) directly using its configuration file (e.g., mcp.json).", 
                "OK");
        }

        private void OnConfigureClicked()
        {
            EditorUtility.DisplayDialog("Client Configuration", 
                "Client configuration is no longer supported by this plugin.\n\n" +
                "Please configure your MCP client (e.g., Cursor) directly using its configuration file (e.g., mcp.json).", 
                "OK");
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

        public void RefreshSelectedClient()
        {
            if (selectedClientIndex >= 0 && selectedClientIndex < configurators.Count)
            {
                var client = configurators[selectedClientIndex];
                RefreshClientStatus(client, forceImmediate: true);
                UpdateManualConfiguration();
                UpdateClaudeCliPathVisibility();
            }
        }

        private void RefreshClientStatus(IMcpClientConfigurator client, bool forceImmediate = false)
        {
            // Client configuration service is no longer available
            // Just show not configured status
            if (client is McpClientConfiguratorBase baseConfigurator)
            {
                baseConfigurator.Client.SetStatus(McpStatus.NotConfigured, "Client configuration not supported");
            }
            ApplyStatusToUi(client);
        }

        private void RefreshClaudeCliStatus(IMcpClientConfigurator client, bool forceImmediate)
        {
            // Client configuration service is no longer available
            // Just show not configured status
            if (client is McpClientConfiguratorBase baseConfigurator)
            {
                baseConfigurator.Client.SetStatus(McpStatus.NotConfigured, "Client configuration not supported");
            }
            ApplyStatusToUi(client);
        }

        private bool ShouldRefreshClient(IMcpClientConfigurator client)
        {
            if (!lastStatusChecks.TryGetValue(client, out var last))
            {
                return true;
            }

            return (DateTime.UtcNow - last) > StatusRefreshInterval;
        }

        private void ApplyStatusToUi(IMcpClientConfigurator client, bool showChecking = false)
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
                clientStatusLabel.text = "Checking...";
                clientStatusLabel.style.color = StyleKeyword.Null;
                clientStatusIndicator.AddToClassList("warning");
                configureButton.text = client.GetConfigureActionLabel();
                return;
            }

            clientStatusLabel.text = GetStatusDisplayString(client.Status);
            clientStatusLabel.style.color = StyleKeyword.Null;

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

            configureButton.text = client.GetConfigureActionLabel();
        }
    }
}
