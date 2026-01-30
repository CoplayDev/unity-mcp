using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Windows.Components.Advanced;
using MCPForUnity.Editor.Windows.Components.ClientConfig;
using MCPForUnity.Editor.Windows.Components.Connection;
using MCPForUnity.Editor.Windows.Components.Resources;
using MCPForUnity.Editor.Windows.Components.Tools;
using MCPForUnity.Editor.Windows.Components.Validation;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace MCPForUnity.Editor.Windows
{
    public class MCPForUnityEditorWindow : EditorWindow
    {
        // Section controllers
        private McpConnectionSection connectionSection;
        private McpClientConfigSection clientConfigSection;
        private McpValidationSection validationSection;
        private McpAdvancedSection advancedSection;
        private McpToolsSection toolsSection;
        private McpResourcesSection resourcesSection;

        // UI Elements
        private Label versionLabel;
        private VisualElement updateNotification;
        private Label updateNotificationText;

        private ToolbarToggle clientsTabToggle;
        private ToolbarToggle validationTabToggle;
        private ToolbarToggle advancedTabToggle;
        private ToolbarToggle toolsTabToggle;
        private ToolbarToggle resourcesTabToggle;
        private VisualElement clientsPanel;
        private VisualElement validationPanel;
        private VisualElement advancedPanel;
        private VisualElement toolsPanel;
        private VisualElement resourcesPanel;

        private static readonly HashSet<MCPForUnityEditorWindow> OpenWindows = new();
        private bool guiCreated = false;
        private bool toolsLoaded = false;
        private bool resourcesLoaded = false;
        private double lastRefreshTime = 0;
        private const double RefreshDebounceSeconds = 0.5;

        private enum ActivePanel
        {
            Clients,
            Validation,
            Advanced,
            Tools,
            Resources
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
            window.minSize = new Vector2(500, 340);
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

            // Cache UI elements
            versionLabel = rootVisualElement.Q<Label>("version-label");
            updateNotification = rootVisualElement.Q<VisualElement>("update-notification");
            updateNotificationText = rootVisualElement.Q<Label>("update-notification-text");

            clientsPanel = rootVisualElement.Q<VisualElement>("clients-panel");
            validationPanel = rootVisualElement.Q<VisualElement>("validation-panel");
            advancedPanel = rootVisualElement.Q<VisualElement>("advanced-panel");
            toolsPanel = rootVisualElement.Q<VisualElement>("tools-panel");
            resourcesPanel = rootVisualElement.Q<VisualElement>("resources-panel");
            var clientsContainer = rootVisualElement.Q<VisualElement>("clients-container");
            var validationContainer = rootVisualElement.Q<VisualElement>("validation-container");
            var advancedContainer = rootVisualElement.Q<VisualElement>("advanced-container");
            var toolsContainer = rootVisualElement.Q<VisualElement>("tools-container");
            var resourcesContainer = rootVisualElement.Q<VisualElement>("resources-container");

            if (clientsPanel == null || validationPanel == null || advancedPanel == null || toolsPanel == null || resourcesPanel == null)
            {
                McpLog.Error("Failed to find tab panels in UXML");
                return;
            }

            if (clientsContainer == null)
            {
                McpLog.Error("Failed to find clients-container in UXML");
                return;
            }

            if (validationContainer == null)
            {
                McpLog.Error("Failed to find validation-container in UXML");
                return;
            }

            if (advancedContainer == null)
            {
                McpLog.Error("Failed to find advanced-container in UXML");
                return;
            }

            if (toolsContainer == null)
            {
                McpLog.Error("Failed to find tools-container in UXML");
                return;
            }

            if (resourcesContainer == null)
            {
                McpLog.Error("Failed to find resources-container in UXML");
                return;
            }

            // Initialize version label
            UpdateVersionLabel(EditorPrefs.GetBool(EditorPrefKeys.UseBetaServer, true));

            SetupTabs();

            // Load and initialize Connection section
            var connectionTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                $"{basePath}/Editor/Windows/Components/Connection/McpConnectionSection.uxml"
            );
            if (connectionTree != null)
            {
                var connectionRoot = connectionTree.Instantiate();
                clientsContainer.Add(connectionRoot);
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
                clientsContainer.Add(clientConfigRoot);
                clientConfigSection = new McpClientConfigSection(clientConfigRoot);

                // Wire up transport mismatch detection: when client status is checked,
                // update the connection section's warning banner if there's a mismatch
                clientConfigSection.OnClientTransportDetected += (clientName, transport) =>
                    connectionSection?.UpdateTransportMismatchWarning(clientName, transport);
            }

            // Load and initialize Validation section
            var validationTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                $"{basePath}/Editor/Windows/Components/Validation/McpValidationSection.uxml"
            );
            if (validationTree != null)
            {
                var validationRoot = validationTree.Instantiate();
                validationContainer.Add(validationRoot);
                validationSection = new McpValidationSection(validationRoot);
            }

            // Load and initialize Advanced section
            var advancedTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                $"{basePath}/Editor/Windows/Components/Advanced/McpAdvancedSection.uxml"
            );
            if (advancedTree != null)
            {
                var advancedRoot = advancedTree.Instantiate();
                advancedContainer.Add(advancedRoot);
                advancedSection = new McpAdvancedSection(advancedRoot);

                // Wire up events from Advanced section
                advancedSection.OnGitUrlChanged += () =>
                    clientConfigSection?.UpdateManualConfiguration();
                advancedSection.OnHttpServerCommandUpdateRequested += () =>
                    connectionSection?.UpdateHttpServerCommandDisplay();
                advancedSection.OnTestConnectionRequested += async () =>
                {
                    if (connectionSection != null)
                        await connectionSection.VerifyBridgeConnectionAsync();
                };
                advancedSection.OnBetaModeChanged += UpdateVersionLabel;

                // Wire up health status updates from Connection to Advanced
                connectionSection?.SetHealthStatusUpdateCallback((isHealthy, statusText) =>
                    advancedSection?.UpdateHealthStatus(isHealthy, statusText));
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

            // Load and initialize Resources section
            var resourcesTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                $"{basePath}/Editor/Windows/Components/Resources/McpResourcesSection.uxml"
            );
            if (resourcesTree != null)
            {
                var resourcesRoot = resourcesTree.Instantiate();
                resourcesContainer.Add(resourcesRoot);
                resourcesSection = new McpResourcesSection(resourcesRoot);

                if (resourcesTabToggle != null && resourcesTabToggle.value)
                {
                    EnsureResourcesLoaded();
                }
            }
            else
            {
                McpLog.Warn("Failed to load resources section UXML. Resource configuration will be unavailable.");
            }

            // Apply .section-last class to last section in each stack
            // (Unity UI Toolkit doesn't support :last-child pseudo-class)
            ApplySectionLastClasses();

            guiCreated = true;

            // Initial updates
            RefreshAllData();
        }

        private void UpdateVersionLabel(bool useBetaServer)
        {
            if (versionLabel == null)
            {
                return;
            }

            string version = AssetPathUtility.GetPackageVersion();
            versionLabel.text = useBetaServer ? $"v{version} β" : $"v{version}";
            versionLabel.tooltip = useBetaServer
                ? "Beta server mode - fetching pre-release server versions from PyPI"
                : $"MCP For Unity v{version}";
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

        private void EnsureResourcesLoaded()
        {
            if (resourcesLoaded)
            {
                return;
            }

            if (resourcesSection == null)
            {
                return;
            }

            resourcesLoaded = true;
            resourcesSection.Refresh();
        }

        /// <summary>
        /// Applies the .section-last class to the last .section element in each .section-stack container.
        /// This is a workaround for Unity UI Toolkit not supporting the :last-child pseudo-class.
        /// </summary>
        private void ApplySectionLastClasses()
        {
            var sectionStacks = rootVisualElement.Query<VisualElement>(className: "section-stack").ToList();
            foreach (var stack in sectionStacks)
            {
                var sections = stack.Children().Where(c => c.ClassListContains("section")).ToList();
                if (sections.Count > 0)
                {
                    // Remove class from all sections first (in case of refresh)
                    foreach (var section in sections)
                    {
                        section.RemoveFromClassList("section-last");
                    }
                    // Add class to the last section
                    sections[sections.Count - 1].AddToClassList("section-last");
                }
            }
        }

        // Throttle OnEditorUpdate to avoid per-frame overhead (GitHub issue #577).
        // Connection status polling every frame caused expensive network checks 60+ times/sec.
        private double _lastEditorUpdateTime;
        private const double EditorUpdateIntervalSeconds = 2.0;

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
            resourcesLoaded = false;
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
            // Throttle to 2-second intervals instead of every frame.
            // This prevents the expensive IsLocalHttpServerReachable() socket checks from running
            // 60+ times per second, which caused main thread blocking and GC pressure.
            double now = EditorApplication.timeSinceStartup;
            if (now - _lastEditorUpdateTime < EditorUpdateIntervalSeconds)
            {
                return;
            }
            _lastEditorUpdateTime = now;

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

            advancedSection?.UpdatePathOverrides();
            clientConfigSection?.RefreshSelectedClient();
        }

        private void SetupTabs()
        {
            clientsTabToggle = rootVisualElement.Q<ToolbarToggle>("clients-tab");
            validationTabToggle = rootVisualElement.Q<ToolbarToggle>("validation-tab");
            advancedTabToggle = rootVisualElement.Q<ToolbarToggle>("advanced-tab");
            toolsTabToggle = rootVisualElement.Q<ToolbarToggle>("tools-tab");
            resourcesTabToggle = rootVisualElement.Q<ToolbarToggle>("resources-tab");

            clientsPanel?.RemoveFromClassList("hidden");
            validationPanel?.RemoveFromClassList("hidden");
            advancedPanel?.RemoveFromClassList("hidden");
            toolsPanel?.RemoveFromClassList("hidden");
            resourcesPanel?.RemoveFromClassList("hidden");

            if (clientsTabToggle != null)
            {
                clientsTabToggle.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue) SwitchPanel(ActivePanel.Clients);
                });
            }

            if (validationTabToggle != null)
            {
                validationTabToggle.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue) SwitchPanel(ActivePanel.Validation);
                });
            }

            if (advancedTabToggle != null)
            {
                advancedTabToggle.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue) SwitchPanel(ActivePanel.Advanced);
                });
            }

            if (toolsTabToggle != null)
            {
                toolsTabToggle.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue) SwitchPanel(ActivePanel.Tools);
                });
            }

            if (resourcesTabToggle != null)
            {
                resourcesTabToggle.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue) SwitchPanel(ActivePanel.Resources);
                });
            }

            var savedPanel = EditorPrefs.GetString(EditorPrefKeys.EditorWindowActivePanel, ActivePanel.Clients.ToString());
            if (!Enum.TryParse(savedPanel, out ActivePanel initialPanel))
            {
                initialPanel = ActivePanel.Clients;
            }

            SwitchPanel(initialPanel);
        }

        private void SwitchPanel(ActivePanel panel)
        {
            // Hide all panels
            if (clientsPanel != null)
            {
                clientsPanel.style.display = DisplayStyle.None;
            }

            if (validationPanel != null)
            {
                validationPanel.style.display = DisplayStyle.None;
            }

            if (advancedPanel != null)
            {
                advancedPanel.style.display = DisplayStyle.None;
            }

            if (toolsPanel != null)
            {
                toolsPanel.style.display = DisplayStyle.None;
            }

            if (resourcesPanel != null)
            {
                resourcesPanel.style.display = DisplayStyle.None;
            }

            // Show selected panel
            switch (panel)
            {
                case ActivePanel.Clients:
                    if (clientsPanel != null) clientsPanel.style.display = DisplayStyle.Flex;
                    break;
                case ActivePanel.Validation:
                    if (validationPanel != null) validationPanel.style.display = DisplayStyle.Flex;
                    break;
                case ActivePanel.Advanced:
                    if (advancedPanel != null) advancedPanel.style.display = DisplayStyle.Flex;
                    break;
                case ActivePanel.Tools:
                    if (toolsPanel != null) toolsPanel.style.display = DisplayStyle.Flex;
                    EnsureToolsLoaded();
                    break;
                case ActivePanel.Resources:
                    if (resourcesPanel != null) resourcesPanel.style.display = DisplayStyle.Flex;
                    EnsureResourcesLoaded();
                    break;
            }

            // Update toggle states
            clientsTabToggle?.SetValueWithoutNotify(panel == ActivePanel.Clients);
            validationTabToggle?.SetValueWithoutNotify(panel == ActivePanel.Validation);
            advancedTabToggle?.SetValueWithoutNotify(panel == ActivePanel.Advanced);
            toolsTabToggle?.SetValueWithoutNotify(panel == ActivePanel.Tools);
            resourcesTabToggle?.SetValueWithoutNotify(panel == ActivePanel.Resources);

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
