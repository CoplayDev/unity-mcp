using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MCPForUnity.Editor.Windows.Components.Settings
{
    /// <summary>
    /// Controller for the Settings section of the MCP For Unity editor window.
    /// Handles version display and debug logs.
    /// </summary>
    public class McpSettingsSection
    {
        // UI Elements
        private Label versionLabel;
        private Toggle debugLogsToggle;

        public VisualElement Root { get; private set; }

        public McpSettingsSection(VisualElement root)
        {
            Root = root;
            CacheUIElements();
            InitializeUI();
            RegisterCallbacks();
        }

        private void CacheUIElements()
        {
            versionLabel = Root.Q<Label>("version-label");
            debugLogsToggle = Root.Q<Toggle>("debug-logs-toggle");
        }

        private void InitializeUI()
        {
            if (versionLabel != null)
            {
                UpdateVersionLabel();
            }

            if (debugLogsToggle != null)
            {
                bool debugEnabled = EditorPrefs.GetBool(EditorPrefKeys.DebugLogs, false);
                debugLogsToggle.value = debugEnabled;
                McpLog.SetDebugLoggingEnabled(debugEnabled);
            }
        }

        private void RegisterCallbacks()
        {
            debugLogsToggle.RegisterValueChangedCallback(evt =>
            {
                McpLog.SetDebugLoggingEnabled(evt.newValue);
            });
        }

        private void UpdateVersionLabel()
        {
            if (versionLabel == null)
                return;

            string currentVersion = AssetPathUtility.GetPackageVersion();
            versionLabel.text = $"v{currentVersion}";

            var updateCheck = MCPServiceLocator.Updates.CheckForUpdate(currentVersion);

            if (updateCheck.UpdateAvailable && !string.IsNullOrEmpty(updateCheck.LatestVersion))
            {
                versionLabel.text = $"\u2191 v{currentVersion} (Update available: v{updateCheck.LatestVersion})";
                versionLabel.style.color = new Color(1f, 0.7f, 0f);
                versionLabel.tooltip = $"Version {updateCheck.LatestVersion} is available. Update via Package Manager.\n\nGit URL: https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity";
            }
            else
            {
                versionLabel.style.color = StyleKeyword.Null;
                versionLabel.tooltip = $"Current version: {currentVersion}";
            }
        }

    }
}
