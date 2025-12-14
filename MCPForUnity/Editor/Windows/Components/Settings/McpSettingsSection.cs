using System;
using System.IO;
using System.Runtime.InteropServices;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace MCPForUnity.Editor.Windows.Components.Settings
{
    /// <summary>
    /// Controller for the Settings section of the MCP For Unity editor window.
    /// Handles version display, debug logs, validation level.
    /// Advanced settings and overrides are handled in McpSetupSection or McpClientConfigSection.
    /// </summary>
    public class McpSettingsSection
    {
        // UI Elements
        private Label versionLabel;
        private Toggle debugLogsToggle;
        private EnumField validationLevelField;
        private Label validationDescription;

        // Data
        private ValidationLevel currentValidationLevel = ValidationLevel.Standard;

        // Events


        // Validation levels
        private enum ValidationLevel
        {
            Basic,
            Standard,
            Comprehensive,
            Strict
        }

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
            validationLevelField = Root.Q<EnumField>("validation-level");
            validationDescription = Root.Q<Label>("validation-description");
        }

        private void InitializeUI()
        {
            UpdateVersionLabel();

            bool debugEnabled = EditorPrefs.GetBool(EditorPrefKeys.DebugLogs, false);
            debugLogsToggle.value = debugEnabled;
            McpLog.SetDebugLoggingEnabled(debugEnabled);

            validationLevelField.Init(ValidationLevel.Standard);
            int savedLevel = EditorPrefs.GetInt(EditorPrefKeys.ValidationLevel, 1);
            currentValidationLevel = (ValidationLevel)Mathf.Clamp(savedLevel, 0, 3);
            validationLevelField.value = currentValidationLevel;
            UpdateValidationDescription();
        }

        private void RegisterCallbacks()
        {
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
        }



        private void UpdateVersionLabel()
        {
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
    }
}
