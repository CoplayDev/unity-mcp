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
using MCPForUnity.Editor.Setup;
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
        private McpAdvancedSection advancedSection;
        private McpToolsSection toolsSection;
        private McpResourcesSection resourcesSection;

        // UI Elements
        private Label versionLabel;
        private VisualElement updateNotification;
        private Label updateNotificationText;

        private ToolbarToggle clientsTabToggle;
        private ToolbarToggle depsTabToggle;
        private ToolbarToggle advancedTabToggle;
        private ToolbarToggle toolsTabToggle;
        private ToolbarToggle resourcesTabToggle;
        private VisualElement clientsPanel;
        private VisualElement depsPanel;
        private VisualElement advancedPanel;
        private VisualElement toolsPanel;
        private VisualElement resourcesPanel;

        private static readonly HashSet<MCPForUnityEditorWindow> OpenWindows = new();
        private bool guiCreated = false;
        private bool toolsLoaded = false;
        private bool resourcesLoaded = false;
        private double lastRefreshTime = 0;
        private const double RefreshDebounceSeconds = 0.5;
        private bool updateCheckQueued = false;
        private bool updateCheckInFlight = false;

        private enum ActivePanel
        {
            Clients,
            Deps,
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
            var existingWindows = UnityEngine.Resources.FindObjectsOfTypeAll<MCPForUnityEditorWindow>();
            MCPForUnityEditorWindow window = null;

            if (existingWindows.Length > 0)
            {
                window = existingWindows[0];

                // If multiple instances exist, keep one and close the extras to avoid stale hidden tabs.
                for (int i = 1; i < existingWindows.Length; i++)
                {
                    try
                    {
                        existingWindows[i].Close();
                    }
                    catch (Exception ex)
                    {
                        McpLog.Warn($"Error closing duplicate MCP window: {ex.Message}");
                    }
                }
            }
            else
            {
                window = GetWindow<MCPForUnityEditorWindow>("MCP For Unity");
            }

            window.titleContent = new GUIContent("MCP For Unity");
            window.minSize = new Vector2(500, 340);

            if (window.position.width < 100 || window.position.height < 100)
            {
                window.position = new Rect(120, 120, 900, 700);
            }

            window.Show();
            window.ShowTab();
            window.Focus();
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

            rootVisualElement.Clear();
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
            depsPanel = rootVisualElement.Q<VisualElement>("deps-panel");
            advancedPanel = rootVisualElement.Q<VisualElement>("advanced-panel");
            toolsPanel = rootVisualElement.Q<VisualElement>("tools-panel");
            resourcesPanel = rootVisualElement.Q<VisualElement>("resources-panel");
            var clientsContainer = rootVisualElement.Q<VisualElement>("clients-container");
            var depsContainer = rootVisualElement.Q<VisualElement>("deps-container");
            var advancedContainer = rootVisualElement.Q<VisualElement>("advanced-container");
            var toolsContainer = rootVisualElement.Q<VisualElement>("tools-container");
            var resourcesContainer = rootVisualElement.Q<VisualElement>("resources-container");

            if (clientsPanel == null || depsPanel == null || advancedPanel == null || toolsPanel == null || resourcesPanel == null)
            {
                McpLog.Error("Failed to find tab panels in UXML");
                return;
            }

            if (clientsContainer == null)
            {
                McpLog.Error("Failed to find clients-container in UXML");
                return;
            }

            if (depsContainer == null)
            {
                McpLog.Error("Failed to find deps-container in UXML");
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
            UpdateVersionLabel();

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

                // Wire up version mismatch detection: when client status is checked,
                // update the connection section's warning banner if there's a version mismatch
                clientConfigSection.OnClientConfigMismatch += (clientName, mismatchMessage) =>
                    connectionSection?.UpdateVersionMismatchWarning(clientName, mismatchMessage);
            }

            // Build Dependencies section (replaces old Roslyn + Validation in Deps tab)
            BuildDependenciesSection(depsContainer);

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
                {
                    connectionSection?.UpdateHttpServerCommandDisplay();
                    connectionSection?.UpdateConnectionStatus();
                };
                advancedSection.OnTestConnectionRequested += async () =>
                {
                    if (connectionSection != null)
                        await connectionSection.VerifyBridgeConnectionAsync();
                };
                advancedSection.OnPackageDeployed += () =>
                {
                    UpdateVersionLabel();
                    QueueUpdateCheck();
                };
                // Wire up health status updates from Connection to Advanced
                connectionSection?.SetHealthStatusUpdateCallback((isHealthy, statusText) =>
                    advancedSection?.UpdateHealthStatus(isHealthy, statusText));
            }

            // Load Validation section into Advanced tab
            var validationTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                $"{basePath}/Editor/Windows/Components/Validation/McpValidationSection.uxml"
            );
            if (validationTree != null)
            {
                var validationRoot = validationTree.Instantiate();
                advancedContainer.Add(validationRoot);
                new McpValidationSection(validationRoot);
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
            QueueUpdateCheck();
        }

        private void UpdateVersionLabel()
        {
            if (versionLabel == null)
            {
                return;
            }

            string version = AssetPathUtility.GetPackageVersion();
            versionLabel.text = $"v{version}";
            versionLabel.tooltip = AssetPathUtility.IsPreReleaseVersion()
                ? $"MCP For Unity v{version} (pre-release package, using prerelease server channel)"
                : $"MCP For Unity v{version}";
        }

        private void QueueUpdateCheck()
        {
            if (updateCheckQueued || updateCheckInFlight)
            {
                return;
            }

            updateCheckQueued = true;
            EditorApplication.delayCall += CheckForPackageUpdates;
        }

        private void CheckForPackageUpdates()
        {
            updateCheckQueued = false;

            if (updateNotification == null || updateNotificationText == null)
            {
                return;
            }

            string currentVersion = AssetPathUtility.GetPackageVersion();
            if (string.IsNullOrEmpty(currentVersion) || currentVersion == "unknown")
            {
                updateNotification.RemoveFromClassList("visible");
                return;
            }

            // Main thread: resolve service + read EditorPrefs cache (both require main thread)
            var updateService = MCPServiceLocator.Updates;
            var cachedResult = updateService.TryGetCachedResult(currentVersion);
            if (cachedResult != null)
            {
                ApplyUpdateCheckResult(cachedResult, currentVersion);
                return;
            }

            // Main thread: pre-compute installation info (uses main-thread-only Unity APIs)
            bool isGitInstallation = updateService.IsGitInstallation();
            string gitBranch = isGitInstallation ? updateService.GetGitUpdateBranch(currentVersion) : "main";

            // Background thread: network I/O only (no EditorPrefs or Unity API access)
            updateCheckInFlight = true;
            Task.Run(() =>
            {
                try
                {
                    return updateService.FetchAndCompare(currentVersion, isGitInstallation, gitBranch);
                }
                catch (Exception ex)
                {
                    McpLog.Info($"Package update check skipped: {ex.Message}");
                    return null;
                }
            }).ContinueWith(t =>
            {
                EditorApplication.delayCall += () =>
                {
                    updateCheckInFlight = false;

                    // Main thread: cache the result in EditorPrefs
                    var result = t.Status == TaskStatus.RanToCompletion ? t.Result : null;
                    if (result != null && result.CheckSucceeded && !string.IsNullOrEmpty(result.LatestVersion))
                    {
                        updateService.CacheFetchResult(currentVersion, result.LatestVersion);
                    }

                    if (this == null || updateNotification == null || updateNotificationText == null)
                    {
                        return;
                    }

                    ApplyUpdateCheckResult(result, currentVersion);
                };
            }, TaskScheduler.Default);
        }

        private void ApplyUpdateCheckResult(UpdateCheckResult result, string currentVersion)
        {
            if (result != null && result.CheckSucceeded && result.UpdateAvailable && !string.IsNullOrEmpty(result.LatestVersion))
            {
                updateNotificationText.text = $"Update available: v{result.LatestVersion}  (current: v{currentVersion})";
                updateNotificationText.tooltip = $"Latest version: v{result.LatestVersion}\nCurrent version: v{currentVersion}";
                updateNotification.AddToClassList("visible");
            }
            else
            {
                updateNotification.RemoveFromClassList("visible");
            }
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
            depsTabToggle = rootVisualElement.Q<ToolbarToggle>("deps-tab");
            advancedTabToggle = rootVisualElement.Q<ToolbarToggle>("advanced-tab");
            toolsTabToggle = rootVisualElement.Q<ToolbarToggle>("tools-tab");
            resourcesTabToggle = rootVisualElement.Q<ToolbarToggle>("resources-tab");

            clientsPanel?.RemoveFromClassList("hidden");
            depsPanel?.RemoveFromClassList("hidden");
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

            if (depsTabToggle != null)
            {
                depsTabToggle.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue) SwitchPanel(ActivePanel.Deps);
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
            // Migrate old "Validation" saved value to "Deps"
            if (savedPanel == "Validation") savedPanel = "Deps";
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

            if (depsPanel != null)
            {
                depsPanel.style.display = DisplayStyle.None;
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
                    // Refresh client status when switching to Connect tab (e.g., after package/version changes).
                    clientConfigSection?.RefreshSelectedClient(forceImmediate: true);
                    break;
                case ActivePanel.Deps:
                    if (depsPanel != null) depsPanel.style.display = DisplayStyle.Flex;
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
            depsTabToggle?.SetValueWithoutNotify(panel == ActivePanel.Deps);
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

        private static void BuildDependenciesSection(VisualElement container)
        {
            var section = new VisualElement();
            section.AddToClassList("section");

            var title = new Label("Optional Dependencies");
            title.AddToClassList("section-title");
            section.Add(title);

            var content = new VisualElement();
            content.AddToClassList("section-content");

            var desc = new Label("Some tool groups require optional packages. Install them to unlock additional capabilities.");
            desc.AddToClassList("validation-description");
            desc.style.marginBottom = 4;
            content.Add(desc);

            // Install All / Uninstall All buttons
            var bulkRow = new VisualElement();
            bulkRow.style.flexDirection = FlexDirection.Row;
            bulkRow.style.marginBottom = 8;

            var upmPackages = new[] { "com.unity.probuilder", "com.unity.cinemachine", "com.unity.visualeffectgraph" };

            Button installAllButton = null;
            installAllButton = new Button(() =>
            {
                if (!EditorUtility.DisplayDialog("Install All Dependencies",
                    "This will install Roslyn DLLs, ProBuilder, Cinemachine, and VFX Graph. Continue?",
                    "Install All", "Cancel")) return;
                installAllButton.SetEnabled(false);
                installAllButton.text = "Installing...";
                if (!RoslynInstaller.IsInstalled()) RoslynInstaller.Install(interactive: false);
                BatchUpmAdd(upmPackages, () =>
                {
                    installAllButton.SetEnabled(true);
                    installAllButton.text = "Install All";
                });
            });
            installAllButton.text = "Install All";
            installAllButton.AddToClassList("action-button");
            installAllButton.style.marginRight = 4;
            bulkRow.Add(installAllButton);

            Button uninstallAllButton = null;
            uninstallAllButton = new Button(() =>
            {
                if (!EditorUtility.DisplayDialog("Uninstall All Dependencies",
                    "This will remove Roslyn DLLs, ProBuilder, Cinemachine, and VFX Graph. Continue?",
                    "Uninstall All", "Cancel")) return;
                uninstallAllButton.SetEnabled(false);
                uninstallAllButton.text = "Removing...";
                UninstallRoslyn();
                BatchUpmRemove(upmPackages, () =>
                {
                    uninstallAllButton.SetEnabled(true);
                    uninstallAllButton.text = "Uninstall All";
                });
            });
            uninstallAllButton.text = "Uninstall All";
            uninstallAllButton.AddToClassList("action-button");
            bulkRow.Add(uninstallAllButton);

            content.Add(bulkRow);

            // Roslyn — for execute_code modern C# support
            // Check if Roslyn types are actually loaded (covers NuGet, Plugins folder, etc.)
            bool roslynLoaded = Type.GetType("Microsoft.CodeAnalysis.CSharp.CSharpCompilation, Microsoft.CodeAnalysis.CSharp") != null;
            bool roslynInstalledLocally = RoslynInstaller.IsInstalled();
            AddDependencyRow(content,
                "Roslyn (C# 12+ Compiler)",
                "Enables modern C# syntax in execute_code tool (scripting_ext group).",
                roslynLoaded,
                roslynInstalledLocally
                    ? "Installed via Plugins/Roslyn \u2014 execute_code uses Roslyn"
                    : "Available (loaded from NuGet/external) \u2014 execute_code uses Roslyn",
                "Not installed \u2014 execute_code falls back to C# 6 (CodeDom)",
                () => RoslynInstaller.Install(interactive: true),
                roslynInstalledLocally ? (Action)(() => UninstallRoslyn()) : null);

            // ProBuilder
            bool hasProBuilder = Type.GetType("UnityEngine.ProBuilder.ProBuilderMesh, Unity.ProBuilder") != null;
            AddDependencyRow(content,
                "ProBuilder",
                "Required for the manage_probuilder tool (probuilder group).",
                hasProBuilder,
                "Installed",
                "Not installed",
                () => InstallUpmPackage("com.unity.probuilder"),
                () => RemoveUpmPackage("com.unity.probuilder"));

            // Cinemachine
            bool hasCinemachine = Type.GetType("Unity.Cinemachine.CinemachineCamera, Unity.Cinemachine") != null
                || Type.GetType("Cinemachine.CinemachineVirtualCamera, Cinemachine") != null;
            AddDependencyRow(content,
                "Cinemachine",
                "Enhances manage_camera with virtual camera support (core group).",
                hasCinemachine,
                "Installed",
                "Not installed \u2014 camera tool works without it",
                () => InstallUpmPackage("com.unity.cinemachine"),
                () => RemoveUpmPackage("com.unity.cinemachine"));

            // VFX Graph — uses preprocessor symbol, so check via UPM package list
            bool hasVfxGraph = IsUpmPackageInstalled("com.unity.visualeffectgraph");
            AddDependencyRow(content,
                "VFX Graph",
                "Enables VisualEffect support in manage_vfx tool (vfx group).",
                hasVfxGraph,
                "Installed",
                "Not installed \u2014 VFX tool falls back to ParticleSystem/LineRenderer",
                () => InstallUpmPackage("com.unity.visualeffectgraph"),
                () => RemoveUpmPackage("com.unity.visualeffectgraph"));

            section.Add(content);
            container.Add(section);
        }

        private static void AddDependencyRow(VisualElement parent, string name, string description,
            bool isInstalled, string installedText, string missingText,
            Action installAction, Action uninstallAction)
        {
            var row = new VisualElement();
            row.style.marginBottom = 8;
            row.style.paddingBottom = 8;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = new Color(0.3f, 0.3f, 0.3f, 0.3f);

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 2;

            var nameLabel = new Label(name);
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.flexGrow = 1;
            header.Add(nameLabel);

            var statusIcon = new Label(isInstalled ? "\u2713" : "\u2717");
            statusIcon.style.color = isInstalled ? new Color(0.4f, 0.8f, 0.4f) : new Color(0.8f, 0.4f, 0.4f);
            statusIcon.style.fontSize = 14;
            header.Add(statusIcon);

            row.Add(header);

            var descLabel = new Label(description);
            descLabel.AddToClassList("validation-description");
            descLabel.style.marginBottom = 2;
            row.Add(descLabel);

            var statusText = new Label(isInstalled ? installedText : missingText);
            statusText.style.fontSize = 11;
            statusText.style.color = isInstalled ? new Color(0.6f, 0.8f, 0.6f) : new Color(0.8f, 0.7f, 0.5f);
            row.Add(statusText);

            var buttonRow = new VisualElement();
            buttonRow.style.flexDirection = FlexDirection.Row;
            buttonRow.style.marginTop = 4;

            if (!isInstalled && installAction != null)
            {
                Button btn = null;
                btn = new Button(() =>
                {
                    btn.SetEnabled(false);
                    btn.text = "Installing...";
                    try { installAction(); }
                    catch (Exception e)
                    {
                        Debug.LogError($"[MCP] Install failed: {e.Message}");
                        btn.SetEnabled(true);
                        btn.text = "Install";
                    }
                });
                btn.text = "Install";
                btn.AddToClassList("action-button");
                buttonRow.Add(btn);
            }

            if (isInstalled && uninstallAction != null)
            {
                Button btn = null;
                btn = new Button(() =>
                {
                    if (!EditorUtility.DisplayDialog("Remove " + name,
                        $"Are you sure you want to remove {name}?", "Remove", "Cancel")) return;
                    btn.SetEnabled(false);
                    btn.text = "Removing...";
                    try { uninstallAction(); }
                    catch (Exception e)
                    {
                        Debug.LogError($"[MCP] Uninstall failed: {e.Message}");
                        btn.SetEnabled(true);
                        btn.text = "Uninstall";
                    }
                });
                btn.text = "Uninstall";
                btn.AddToClassList("action-button");
                buttonRow.Add(btn);
            }

            if (buttonRow.childCount > 0)
                row.Add(buttonRow);

            parent.Add(row);
        }

        private static void InstallUpmPackage(string packageId, Action onComplete = null)
        {
            BatchUpmAdd(new[] { packageId }, onComplete);
        }

        private static void RemoveUpmPackage(string packageId, Action onComplete = null)
        {
            BatchUpmRemove(new[] { packageId }, onComplete);
        }

        private static void BatchUpmAdd(string[] packageIds, Action onComplete = null)
        {
            var request = UnityEditor.PackageManager.Client.AddAndRemove(packageIds, null);
            EditorUtility.DisplayProgressBar("Installing Packages", $"Installing {packageIds.Length} package(s)...", 0.5f);
            PollUpmRequest(request, "install", onComplete);
        }

        private static void BatchUpmRemove(string[] packageIds, Action onComplete = null)
        {
            var request = UnityEditor.PackageManager.Client.AddAndRemove(null, packageIds);
            EditorUtility.DisplayProgressBar("Removing Packages", $"Removing {packageIds.Length} package(s)...", 0.5f);
            PollUpmRequest(request, "remove", onComplete);
        }

        private static void PollUpmRequest(UnityEditor.PackageManager.Requests.AddAndRemoveRequest request, string verb, Action onComplete)
        {
            EditorApplication.CallbackFunction pollCallback = null;
            pollCallback = () =>
            {
                if (!request.IsCompleted) return;
                EditorApplication.update -= pollCallback;
                EditorUtility.ClearProgressBar();
                if (request.Status == UnityEditor.PackageManager.StatusCode.Success)
                    Debug.Log($"[MCP] Package {verb} succeeded.");
                else
                    Debug.LogError($"[MCP] Package {verb} failed: {request.Error?.message}");
                onComplete?.Invoke();
            };
            EditorApplication.update += pollCallback;
        }

        private static void UninstallRoslyn()
        {
            string folder = System.IO.Path.Combine(Application.dataPath, "Plugins/Roslyn");
            if (System.IO.Directory.Exists(folder))
            {
                System.IO.Directory.Delete(folder, true);
                string metaPath = folder + ".meta";
                if (System.IO.File.Exists(metaPath))
                    System.IO.File.Delete(metaPath);
                AssetDatabase.Refresh();
                Debug.Log("[MCP] Roslyn DLLs removed from Assets/Plugins/Roslyn/");
            }
        }

        private static bool IsUpmPackageInstalled(string packageId)
        {
            // Check manifest.json directly — faster than async UPM API
            string manifestPath = System.IO.Path.Combine(Application.dataPath, "../Packages/manifest.json");
            if (!System.IO.File.Exists(manifestPath)) return false;
            string manifest = System.IO.File.ReadAllText(manifestPath);
            return manifest.Contains($"\"{packageId}\"");
        }
    }
}
