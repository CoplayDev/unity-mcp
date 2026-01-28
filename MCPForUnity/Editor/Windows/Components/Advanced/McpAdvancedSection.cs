using System;
using System.IO;
using System.Runtime.InteropServices;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MCPForUnity.Editor.Windows.Components.Advanced
{
    /// <summary>
    /// Controller for the Advanced Settings section.
    /// Handles path overrides, server source configuration, dev mode, and package deployment.
    /// </summary>
    public class McpAdvancedSection
    {
        // UI Elements
        private TextField uvxPathOverride;
        private Button browseUvxButton;
        private Button clearUvxButton;
        private VisualElement uvxPathStatus;
        private TextField gitUrlOverride;
        private Button browseGitUrlButton;
        private Button clearGitUrlButton;
        private Toggle debugLogsToggle;
        private Toggle devModeForceRefreshToggle;
        private Toggle useBetaServerToggle;
        private TextField deploySourcePath;
        private Button browseDeploySourceButton;
        private Button clearDeploySourceButton;
        private Button deployButton;
        private Button deployRestoreButton;
        private Label deployTargetLabel;
        private Label deployBackupLabel;
        private Label deployStatusLabel;
        private VisualElement healthIndicator;
        private Label healthStatus;
        private Button testConnectionButton;

        // Events
        public event Action OnGitUrlChanged;
        public event Action OnHttpServerCommandUpdateRequested;
        public event Action OnTestConnectionRequested;
        public event Action<bool> OnBetaModeChanged;

        public VisualElement Root { get; private set; }

        public McpAdvancedSection(VisualElement root)
        {
            Root = root;
            CacheUIElements();
            InitializeUI();
            RegisterCallbacks();
        }

        private void CacheUIElements()
        {
            uvxPathOverride = Root.Q<TextField>("uv-path-override");
            browseUvxButton = Root.Q<Button>("browse-uv-button");
            clearUvxButton = Root.Q<Button>("clear-uv-button");
            uvxPathStatus = Root.Q<VisualElement>("uv-path-status");
            gitUrlOverride = Root.Q<TextField>("git-url-override");
            browseGitUrlButton = Root.Q<Button>("browse-git-url-button");
            clearGitUrlButton = Root.Q<Button>("clear-git-url-button");
            debugLogsToggle = Root.Q<Toggle>("debug-logs-toggle");
            devModeForceRefreshToggle = Root.Q<Toggle>("dev-mode-force-refresh-toggle");
            useBetaServerToggle = Root.Q<Toggle>("use-beta-server-toggle");
            deploySourcePath = Root.Q<TextField>("deploy-source-path");
            browseDeploySourceButton = Root.Q<Button>("browse-deploy-source-button");
            clearDeploySourceButton = Root.Q<Button>("clear-deploy-source-button");
            deployButton = Root.Q<Button>("deploy-button");
            deployRestoreButton = Root.Q<Button>("deploy-restore-button");
            deployTargetLabel = Root.Q<Label>("deploy-target-label");
            deployBackupLabel = Root.Q<Label>("deploy-backup-label");
            deployStatusLabel = Root.Q<Label>("deploy-status-label");
            healthIndicator = Root.Q<VisualElement>("health-indicator");
            healthStatus = Root.Q<Label>("health-status");
            testConnectionButton = Root.Q<Button>("test-connection-button");
        }

        private void InitializeUI()
        {
            // Set tooltips for fields
            if (uvxPathOverride != null)
                uvxPathOverride.tooltip = "Override path to uvx executable. Leave empty for auto-detection.";
            if (gitUrlOverride != null)
                gitUrlOverride.tooltip = "Override server source for uvx --from. Leave empty to use default PyPI package. Example local dev: /path/to/unity-mcp/Server";
            if (debugLogsToggle != null)
            {
                debugLogsToggle.tooltip = "Enable verbose debug logging to the Unity Console.";
                var debugLabel = debugLogsToggle?.parent?.Q<Label>();
                if (debugLabel != null)
                    debugLabel.tooltip = debugLogsToggle.tooltip;
            }
            if (devModeForceRefreshToggle != null)
            {
                devModeForceRefreshToggle.tooltip = "When enabled, generated uvx commands add '--no-cache --refresh' before launching (slower startup, but avoids stale cached builds while iterating on the Server).";
                var forceRefreshLabel = devModeForceRefreshToggle?.parent?.Q<Label>();
                if (forceRefreshLabel != null)
                    forceRefreshLabel.tooltip = devModeForceRefreshToggle.tooltip;
            }
            if (useBetaServerToggle != null)
            {
                useBetaServerToggle.tooltip = "When enabled, uvx will fetch the latest beta server version from PyPI. Enable this on the beta branch to get the matching server version.";
                var betaServerLabel = useBetaServerToggle?.parent?.Q<Label>();
                if (betaServerLabel != null)
                    betaServerLabel.tooltip = useBetaServerToggle.tooltip;
            }
            if (testConnectionButton != null)
                testConnectionButton.tooltip = "Test the connection between Unity and the MCP server.";
            if (deploySourcePath != null)
                deploySourcePath.tooltip = "Copy a MCPForUnity folder into this project's package location.";

            // Set tooltips for buttons
            if (browseUvxButton != null)
                browseUvxButton.tooltip = "Browse for uvx executable";
            if (clearUvxButton != null)
                clearUvxButton.tooltip = "Clear override and use auto-detection";
            if (browseGitUrlButton != null)
                browseGitUrlButton.tooltip = "Select local server source folder";
            if (clearGitUrlButton != null)
                clearGitUrlButton.tooltip = "Clear override and use default PyPI package";
            if (browseDeploySourceButton != null)
                browseDeploySourceButton.tooltip = "Select MCPForUnity source folder";
            if (clearDeploySourceButton != null)
                clearDeploySourceButton.tooltip = "Clear deployment source path";
            if (deployButton != null)
                deployButton.tooltip = "Copy MCPForUnity to this project's package location";
            if (deployRestoreButton != null)
                deployRestoreButton.tooltip = "Restore the last backup before deployment";

            gitUrlOverride.value = EditorPrefs.GetString(EditorPrefKeys.GitUrlOverride, "");

            bool debugEnabled = EditorPrefs.GetBool(EditorPrefKeys.DebugLogs, false);
            debugLogsToggle.value = debugEnabled;
            McpLog.SetDebugLoggingEnabled(debugEnabled);

            devModeForceRefreshToggle.value = EditorPrefs.GetBool(EditorPrefKeys.DevModeForceServerRefresh, false);
            useBetaServerToggle.value = EditorPrefs.GetBool(EditorPrefKeys.UseBetaServer, true);
            UpdatePathOverrides();
            UpdateDeploymentSection();
        }

        private void RegisterCallbacks()
        {
            browseUvxButton.clicked += OnBrowseUvxClicked;
            clearUvxButton.clicked += OnClearUvxClicked;
            browseGitUrlButton.clicked += OnBrowseGitUrlClicked;

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
                OnGitUrlChanged?.Invoke();
                OnHttpServerCommandUpdateRequested?.Invoke();
            });

            clearGitUrlButton.clicked += () =>
            {
                gitUrlOverride.value = string.Empty;
                EditorPrefs.DeleteKey(EditorPrefKeys.GitUrlOverride);
                OnGitUrlChanged?.Invoke();
                OnHttpServerCommandUpdateRequested?.Invoke();
            };

            debugLogsToggle.RegisterValueChangedCallback(evt =>
            {
                McpLog.SetDebugLoggingEnabled(evt.newValue);
            });

            devModeForceRefreshToggle.RegisterValueChangedCallback(evt =>
            {
                EditorPrefs.SetBool(EditorPrefKeys.DevModeForceServerRefresh, evt.newValue);
                OnHttpServerCommandUpdateRequested?.Invoke();
            });

            useBetaServerToggle.RegisterValueChangedCallback(evt =>
            {
                EditorPrefs.SetBool(EditorPrefKeys.UseBetaServer, evt.newValue);
                OnHttpServerCommandUpdateRequested?.Invoke();
                OnBetaModeChanged?.Invoke(evt.newValue);
            });

            deploySourcePath.RegisterValueChangedCallback(evt =>
            {
                string path = evt.newValue?.Trim();
                if (string.IsNullOrEmpty(path) || path == "Not set")
                {
                    return;
                }

                try
                {
                    MCPServiceLocator.Deployment.SetStoredSourcePath(path);
                }
                catch (Exception ex)
                {
                    EditorUtility.DisplayDialog("Invalid Source", ex.Message, "OK");
                    UpdateDeploymentSection();
                }
            });

            browseDeploySourceButton.clicked += OnBrowseDeploySourceClicked;
            clearDeploySourceButton.clicked += OnClearDeploySourceClicked;
            deployButton.clicked += OnDeployClicked;
            deployRestoreButton.clicked += OnRestoreBackupClicked;
            testConnectionButton.clicked += () => OnTestConnectionRequested?.Invoke();
        }

        public void UpdatePathOverrides()
        {
            var pathService = MCPServiceLocator.Paths;

            bool hasOverride = pathService.HasUvxPathOverride;
            bool hasFallback = pathService.HasUvxPathFallback;
            string uvxPath = hasOverride ? pathService.GetUvxPath() : null;

            // Determine display text based on override and fallback status
            if (hasOverride)
            {
                if (hasFallback)
                {
                    // Override path invalid, using system fallback
                    string overridePath = EditorPrefs.GetString(EditorPrefKeys.UvxPathOverride, string.Empty);
                    uvxPathOverride.value = $"Invalid override path: {overridePath} (fallback to uvx path) {uvxPath}";
                }
                else if (!string.IsNullOrEmpty(uvxPath))
                {
                    // Override path valid
                    uvxPathOverride.value = uvxPath;
                }
                else
                {
                    // Override set but invalid, no fallback available
                    string overridePath = EditorPrefs.GetString(EditorPrefKeys.UvxPathOverride, string.Empty);
                    uvxPathOverride.value = $"Invalid override path: {overridePath}, no uv found";
                }
            }
            else
            {
                uvxPathOverride.value = "uvx (uses PATH)";
            }

            uvxPathStatus.RemoveFromClassList("valid");
            uvxPathStatus.RemoveFromClassList("invalid");
            uvxPathStatus.RemoveFromClassList("warning");

            if (hasOverride)
            {
                if (hasFallback)
                {
                    // Using fallback - show as warning (yellow)
                    uvxPathStatus.AddToClassList("warning");
                }
                else
                {
                    // Override mode: validate the override path
                    string overridePath = EditorPrefs.GetString(EditorPrefKeys.UvxPathOverride, string.Empty);
                    if (pathService.TryValidateUvxExecutable(overridePath, out _))
                    {
                        uvxPathStatus.AddToClassList("valid");
                    }
                    else
                    {
                        uvxPathStatus.AddToClassList("invalid");
                    }
                }
            }
            else
            {
                // PATH mode: validate system uvx
                string systemUvxPath = pathService.GetUvxPath();
                if (!string.IsNullOrEmpty(systemUvxPath) && pathService.TryValidateUvxExecutable(systemUvxPath, out _))
                {
                    uvxPathStatus.AddToClassList("valid");
                }
                else
                {
                    uvxPathStatus.AddToClassList("invalid");
                }
            }

            gitUrlOverride.value = EditorPrefs.GetString(EditorPrefKeys.GitUrlOverride, "");
            debugLogsToggle.value = EditorPrefs.GetBool(EditorPrefKeys.DebugLogs, false);
            devModeForceRefreshToggle.value = EditorPrefs.GetBool(EditorPrefKeys.DevModeForceServerRefresh, false);
            useBetaServerToggle.value = EditorPrefs.GetBool(EditorPrefKeys.UseBetaServer, true);
            UpdateDeploymentSection();
        }

        private void OnBrowseUvxClicked()
        {
            string suggested = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? "/opt/homebrew/bin"
                : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string picked = EditorUtility.OpenFilePanel("Select uv Executable", suggested, "");
            if (!string.IsNullOrEmpty(picked))
            {
                try
                {
                    MCPServiceLocator.Paths.SetUvxPathOverride(picked);
                    UpdatePathOverrides();
                    McpLog.Info($"uv path override set to: {picked}");
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
            McpLog.Info("uv path override cleared");
        }

        private void OnBrowseGitUrlClicked()
        {
            string picked = EditorUtility.OpenFolderPanel("Select Server folder", string.Empty, string.Empty);
            if (!string.IsNullOrEmpty(picked))
            {
                gitUrlOverride.value = picked;
                EditorPrefs.SetString(EditorPrefKeys.GitUrlOverride, picked);
                OnGitUrlChanged?.Invoke();
                OnHttpServerCommandUpdateRequested?.Invoke();
                McpLog.Info($"Server source override set to: {picked}");
            }
        }

        private void UpdateDeploymentSection()
        {
            var deployService = MCPServiceLocator.Deployment;

            string sourcePath = deployService.GetStoredSourcePath();
            deploySourcePath.value = sourcePath ?? string.Empty;

            deployTargetLabel.text = $"Target: {deployService.GetTargetDisplayPath()}";

            string backupPath = deployService.GetLastBackupPath();
            if (deployService.HasBackup())
            {
                // Use forward slashes to avoid backslash escape sequence issues in UI text
                deployBackupLabel.text = $"Last backup: {backupPath?.Replace('\\', '/')}";
            }
            else
            {
                deployBackupLabel.text = "Last backup: none";
            }

            deployRestoreButton?.SetEnabled(deployService.HasBackup());
        }

        private void OnBrowseDeploySourceClicked()
        {
            string picked = EditorUtility.OpenFolderPanel("Select MCPForUnity folder", string.Empty, string.Empty);
            if (string.IsNullOrEmpty(picked))
            {
                return;
            }

            try
            {
                MCPServiceLocator.Deployment.SetStoredSourcePath(picked);
                SetDeployStatus($"Source set: {picked}");
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Invalid Source", ex.Message, "OK");
                SetDeployStatus("Source selection failed");
            }

            UpdateDeploymentSection();
        }

        private void OnClearDeploySourceClicked()
        {
            MCPServiceLocator.Deployment.ClearStoredSourcePath();
            UpdateDeploymentSection();
            SetDeployStatus("Source cleared");
        }

        private void OnDeployClicked()
        {
            var result = MCPServiceLocator.Deployment.DeployFromStoredSource();
            SetDeployStatus(result.Message, !result.Success);

            if (!result.Success)
            {
                EditorUtility.DisplayDialog("Deployment Failed", result.Message, "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Deployment Complete", result.Message + (string.IsNullOrEmpty(result.BackupPath) ? string.Empty : $"\nBackup: {result.BackupPath}"), "OK");
            }

            UpdateDeploymentSection();
        }

        private void OnRestoreBackupClicked()
        {
            var result = MCPServiceLocator.Deployment.RestoreLastBackup();
            SetDeployStatus(result.Message, !result.Success);

            if (!result.Success)
            {
                EditorUtility.DisplayDialog("Restore Failed", result.Message, "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Restore Complete", result.Message, "OK");
            }

            UpdateDeploymentSection();
        }

        private void SetDeployStatus(string message, bool isError = false)
        {
            if (deployStatusLabel == null)
            {
                return;
            }

            deployStatusLabel.text = message;
            deployStatusLabel.style.color = isError
                ? new StyleColor(new Color(0.85f, 0.2f, 0.2f))
                : StyleKeyword.Null;
        }

        public void UpdateHealthStatus(bool isHealthy, string statusText)
        {
            if (healthStatus != null)
            {
                healthStatus.text = statusText;
            }

            if (healthIndicator != null)
            {
                healthIndicator.RemoveFromClassList("healthy");
                healthIndicator.RemoveFromClassList("disconnected");
                healthIndicator.RemoveFromClassList("unknown");

                if (isHealthy)
                {
                    healthIndicator.AddToClassList("healthy");
                }
                else if (statusText == HealthStatus.Unknown)
                {
                    healthIndicator.AddToClassList("unknown");
                }
                else
                {
                    healthIndicator.AddToClassList("disconnected");
                }
            }
        }
    }
}
