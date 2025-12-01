using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Windows.Components.ClientConfig;
using MCPForUnity.Editor.Windows.Components.Connection;
using MCPForUnity.Editor.Windows.Components.Settings;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MCPForUnity.Editor.Windows
{
    public class MCPForUnityEditorWindow : EditorWindow
    {
        // Section controllers
        private McpSettingsSection settingsSection;
        private McpConnectionSection connectionSection;
        private McpClientConfigSection clientConfigSection;

        private static readonly HashSet<MCPForUnityEditorWindow> OpenWindows = new();

        public static void ShowWindow()
        {
            var window = GetWindow<MCPForUnityEditorWindow>("MCP For Unity");
            window.minSize = new Vector2(500, 600);
        }

        // Helper to check and manage open windows from other classes
        public static bool HasAnyOpenWindow()
        {
            return OpenWindows.Count > 0;
        }

        public static void CloseAllOpenWindows()
        {
            if (OpenWindows.Count == 0)
                return;

            // Copy to array to avoid modifying the collection while iterating
            var arr = new MCPForUnityEditorWindow[OpenWindows.Count];
            OpenWindows.CopyTo(arr);
            foreach (var window in arr)
            {
                try
                {
                    window?.Close();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Error closing MCP window: {ex.Message}");
                }
            }
        }

        public void CreateGUI()
        {
            string basePath = AssetPathUtility.GetMcpPackageRootPath();

            // Load main window UXML
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                $"{basePath}/Editor/Windows/MCPForUnityEditorWindow.uxml"
            );

            if (visualTree == null)
            {
                McpLog.Error(
                    $"Failed to load UXML at: {basePath}/Editor/Windows/MCPForUnityEditorWindow.uxml"
                );
                return;
            }

            visualTree.CloneTree(rootVisualElement);

            // Load main window USS
            var mainStyleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                $"{basePath}/Editor/Windows/MCPForUnityEditorWindow.uss"
            );
            if (mainStyleSheet != null)
            {
                rootVisualElement.styleSheets.Add(mainStyleSheet);
            }

            // Load common USS
            var commonStyleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                $"{basePath}/Editor/Windows/Components/Common.uss"
            );
            if (commonStyleSheet != null)
            {
                rootVisualElement.styleSheets.Add(commonStyleSheet);
            }

            // Get sections container
            var sectionsContainer = rootVisualElement.Q<VisualElement>("sections-container");
            if (sectionsContainer == null)
            {
                McpLog.Error("Failed to find sections-container in UXML");
                return;
            }

            // Load and initialize Settings section
            var settingsTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                $"{basePath}/Editor/Windows/Components/Settings/McpSettingsSection.uxml"
            );
            if (settingsTree != null)
            {
                var settingsRoot = settingsTree.Instantiate();
                sectionsContainer.Add(settingsRoot);
                settingsSection = new McpSettingsSection(settingsRoot);
                settingsSection.OnGitUrlChanged += () =>
                    clientConfigSection?.UpdateManualConfiguration();
                settingsSection.OnHttpServerCommandUpdateRequested += () =>
                    connectionSection?.UpdateHttpServerCommandDisplay();
            }

            // Load and initialize Connection section
            var connectionTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                $"{basePath}/Editor/Windows/Components/Connection/McpConnectionSection.uxml"
            );
            if (connectionTree != null)
            {
                var connectionRoot = connectionTree.Instantiate();
                sectionsContainer.Add(connectionRoot);
                connectionSection = new McpConnectionSection(connectionRoot);
                connectionSection.OnManualConfigUpdateRequested += () =>
                    clientConfigSection?.UpdateManualConfiguration();
            }

            // Load and initialize Client Configuration section
            var clientConfigTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                $"{basePath}/Editor/Windows/Components/ClientConfig/McpClientConfigSection.uxml"
            );
            if (clientConfigTree != null)
            {
                var clientConfigRoot = clientConfigTree.Instantiate();
                sectionsContainer.Add(clientConfigRoot);
                clientConfigSection = new McpClientConfigSection(clientConfigRoot);
            }

            // Initial updates
            RefreshAllData();
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
            if (rootVisualElement == null || rootVisualElement.childCount == 0)
                return;

            connectionSection?.UpdateConnectionStatus();
        }

        private void RefreshAllData()
        {
            connectionSection?.UpdateConnectionStatus();

            if (MCPServiceLocator.Bridge.IsRunning)
            {
                _ = connectionSection?.VerifyBridgeConnectionAsync();
            }

            settingsSection?.UpdatePathOverrides();
            clientConfigSection?.RefreshSelectedClient();
        }

        internal static void RequestHealthVerification()
        {
            foreach (var window in OpenWindows)
            {
                window?.ScheduleHealthCheck();
            }
        }

        private void ScheduleHealthCheck()
        {
            EditorApplication.delayCall += async () =>
            {
                if (this == null || connectionSection == null)
                {
                    return;
                }
                await connectionSection.VerifyBridgeConnectionAsync();
            };
        }
    }
}
