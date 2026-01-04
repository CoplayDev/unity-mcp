using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Windows.Components.ClientConfig;
using MCPForUnity.Editor.Windows.Components.Connection;
using MCPForUnity.Editor.Windows.Components.Settings;
using MCPForUnity.Editor.Windows.Components.Tools;
using UnityEditor;
using UnityEditor.UIElements;
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
        private McpToolsSection toolsSection;

        private ToolbarToggle settingsTabToggle;
        private ToolbarToggle toolsTabToggle;
        private VisualElement settingsPanel;
        private VisualElement toolsPanel;

        private static readonly HashSet<MCPForUnityEditorWindow> OpenWindows = new();
        private bool guiCreated = false;
        private bool toolsLoaded = false;
        private double lastRefreshTime = 0;
        private const double RefreshDebounceSeconds = 0.5;

        private enum ActivePanel
        {
            Settings,
            Tools
        }

        internal static void CloseAllWindows()
        {
            var windows = OpenWindows.Where(window => window != null).ToArray();
            foreach (var window in windows)
            {
                window.Close();
            }
        }

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
                    McpLog.Warn($"Error closing MCP window: {ex.Message}");
                }
            }
        }

        public void CreateGUI()
        {
            // Guard against repeated CreateGUI calls (e.g., domain reloads)
            if (guiCreated)
                return;

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

            settingsPanel = rootVisualElement.Q<VisualElement>("settings-panel");
            toolsPanel = rootVisualElement.Q<VisualElement>("tools-panel");
            var settingsContainer = rootVisualElement.Q<VisualElement>("settings-container");
            var toolsContainer = rootVisualElement.Q<VisualElement>("tools-container");

            if (settingsPanel == null || toolsPanel == null)
            {
                McpLog.Error("Failed to find tab panels in UXML");
                return;
            }

            if (settingsContainer == null)
            {
                McpLog.Error("Failed to find settings-container in UXML");
                return;
            }

            if (toolsContainer == null)
            {
                McpLog.Error("Failed to find tools-container in UXML");
                return;
            }

            SetupTabs();

            // Load and initialize Settings section
            var settingsTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                $"{basePath}/Editor/Windows/Components/Settings/McpSettingsSection.uxml"
            );
            if (settingsTree != null)
            {
                var settingsRoot = settingsTree.Instantiate();
                settingsContainer.Add(settingsRoot);
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
                settingsContainer.Add(connectionRoot);
                connectionSection = new McpConnectionSection(connectionRoot);
                connectionSection.OnManualConfigUpdateRequested += () =>
                    clientConfigSection?.UpdateManualConfiguration();
                connectionSection.OnTransportChanged += () =>
                    clientConfigSection?.RefreshSelectedClient(forceImmediate: true);
            }

            // Load and initialize Client Configuration section
            var clientConfigTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                $"{basePath}/Editor/Windows/Components/ClientConfig/McpClientConfigSection.uxml"
            );
            if (clientConfigTree != null)
            {
                var clientConfigRoot = clientConfigTree.Instantiate();
                settingsContainer.Add(clientConfigRoot);
                clientConfigSection = new McpClientConfigSection(clientConfigRoot);
            }

            // Load and initialize Tools section
            var toolsTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                $"{basePath}/Editor/Windows/Components/Tools/McpToolsSection.uxml"
            );
            if (toolsTree != null)
            {
                var toolsRoot = toolsTree.Instantiate();
                toolsContainer.Add(toolsRoot);
                toolsSection = new McpToolsSection(toolsRoot);

                if (toolsTabToggle != null && toolsTabToggle.value)
                {
                    EnsureToolsLoaded();
                }
            }
            else
            {
                McpLog.Warn("Failed to load tools section UXML. Tool configuration will be unavailable.");
            }

            guiCreated = true;

            // Initial updates
            RefreshAllData();
        }

        private void EnsureToolsLoaded()
        {
            if (toolsLoaded)
            {
                return;
            }

            if (toolsSection == null)
            {
                return;
            }

            toolsLoaded = true;
            toolsSection.Refresh();
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
            guiCreated = false;
            toolsLoaded = false;
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
            // Debounce rapid successive calls (e.g., from OnFocus being called multiple times)
            double currentTime = EditorApplication.timeSinceStartup;
            if (currentTime - lastRefreshTime < RefreshDebounceSeconds)
            {
                return;
            }
            lastRefreshTime = currentTime;

            connectionSection?.UpdateConnectionStatus();

            if (MCPServiceLocator.Bridge.IsRunning)
            {
                _ = connectionSection?.VerifyBridgeConnectionAsync();
            }

            settingsSection?.UpdatePathOverrides();
            clientConfigSection?.RefreshSelectedClient();
        }

        private void SetupTabs()
        {
            settingsTabToggle = rootVisualElement.Q<ToolbarToggle>("settings-tab");
            toolsTabToggle = rootVisualElement.Q<ToolbarToggle>("tools-tab");

            settingsPanel?.RemoveFromClassList("hidden");
            toolsPanel?.RemoveFromClassList("hidden");

            if (settingsTabToggle != null)
            {
                settingsTabToggle.RegisterValueChangedCallback(evt =>
                {
                    if (!evt.newValue)
                    {
                        if (toolsTabToggle != null && !toolsTabToggle.value)
                        {
                            settingsTabToggle.SetValueWithoutNotify(true);
                        }
                        return;
                    }

                    SwitchPanel(ActivePanel.Settings);
                });
            }

            if (toolsTabToggle != null)
            {
                toolsTabToggle.RegisterValueChangedCallback(evt =>
                {
                    if (!evt.newValue)
                    {
                        if (settingsTabToggle != null && !settingsTabToggle.value)
                        {
                            toolsTabToggle.SetValueWithoutNotify(true);
                        }
                        return;
                    }

                    SwitchPanel(ActivePanel.Tools);
                });
            }

            var savedPanel = EditorPrefs.GetString(EditorPrefKeys.EditorWindowActivePanel, ActivePanel.Settings.ToString());
            if (!Enum.TryParse(savedPanel, out ActivePanel initialPanel))
            {
                initialPanel = ActivePanel.Settings;
            }

            SwitchPanel(initialPanel);
        }

        private void SwitchPanel(ActivePanel panel)
        {
            bool showSettings = panel == ActivePanel.Settings;

            if (settingsPanel != null)
            {
                settingsPanel.style.display = showSettings ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (toolsPanel != null)
            {
                toolsPanel.style.display = showSettings ? DisplayStyle.None : DisplayStyle.Flex;
            }

            settingsTabToggle?.SetValueWithoutNotify(showSettings);
            toolsTabToggle?.SetValueWithoutNotify(!showSettings);

            if (!showSettings)
            {
                EnsureToolsLoaded();
            }

            EditorPrefs.SetString(EditorPrefKeys.EditorWindowActivePanel, panel.ToString());
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
                // Ensure window and components are still valid before execution
                if (this == null || connectionSection == null)
                {
                    return;
                }

                try
                {
                    await connectionSection.VerifyBridgeConnectionAsync();
                }
                catch (Exception ex)
                {
                    // Log but don't crash if verification fails during cleanup
                    McpLog.Warn($"Health check verification failed: {ex.Message}");
                }
            };
        }
    }
}
