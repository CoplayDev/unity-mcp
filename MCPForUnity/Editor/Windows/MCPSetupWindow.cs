using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MCPForUnity.Editor.Clients;
using MCPForUnity.Editor.Dependencies;
using MCPForUnity.Editor.Dependencies.Models;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Windows.Components.Branding;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MCPForUnity.Editor.Windows
{
    /// <summary>
    /// Setup window for checking and guiding dependency installation
    /// </summary>
    public class MCPSetupWindow : EditorWindow
    {
        // UI Elements
        private VisualElement pythonIndicator;
        private Label pythonVersion;
        private Label pythonDetails;
        private VisualElement uvIndicator;
        private Label uvVersion;
        private Label uvDetails;
        private Label statusMessage;
        private VisualElement installationSection;
        private Label installationInstructions;
        private Button openPythonLinkButton;
        private Button openUvLinkButton;
        private Button installUvButton;
        private Button refreshButton;
        private Button doneButton;

        // Tracks an in-flight uv install so completion is handled on the main thread.
        private Task<UvInstaller.UvInstallResult> _uvInstallTask;

        // Step 2 (Configure Clients) UI elements
        private VisualElement stepDeps;
        private VisualElement stepClients;
        private VisualElement clientsList;
        private Button skipClientsButton;
        private Button configureSelectedButton;
        private readonly List<(IMcpClientConfigurator client, Toggle toggle)> clientToggles = new();

        private DependencyCheckResult _dependencyResult;

        public static void ShowWindow(DependencyCheckResult dependencyResult = null)
        {
            var window = GetWindow<MCPSetupWindow>("MCP Setup");
            window.minSize = new Vector2(480, 320);
            window._dependencyResult = dependencyResult ?? DependencyManager.CheckAllDependencies();
            window.Show();
        }

        public void CreateGUI()
        {
            string basePath = AssetPathUtility.GetMcpPackageRootPath();

            // Load UXML
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                $"{basePath}/Editor/Windows/MCPSetupWindow.uxml"
            );

            if (visualTree == null)
            {
                McpLog.Error($"Failed to load UXML at: {basePath}/Editor/Windows/MCPSetupWindow.uxml");
                return;
            }

            visualTree.CloneTree(rootVisualElement);

            // Embed the Ocean brand mark beside the title
            var setupHeader = rootVisualElement.Q<VisualElement>("setup-header");
            if (setupHeader != null && setupHeader.Q<OceanMark>() == null)
            {
                var logo = new OceanMark { name = "setup-logo" };
                logo.AddToClassList("setup-logo");
                setupHeader.Insert(0, logo);
            }

            // Cache UI elements
            pythonIndicator = rootVisualElement.Q<VisualElement>("python-indicator");
            pythonVersion = rootVisualElement.Q<Label>("python-version");
            pythonDetails = rootVisualElement.Q<Label>("python-details");
            uvIndicator = rootVisualElement.Q<VisualElement>("uv-indicator");
            uvVersion = rootVisualElement.Q<Label>("uv-version");
            uvDetails = rootVisualElement.Q<Label>("uv-details");
            statusMessage = rootVisualElement.Q<Label>("status-message");
            installationSection = rootVisualElement.Q<VisualElement>("installation-section");
            installationInstructions = rootVisualElement.Q<Label>("installation-instructions");
            openPythonLinkButton = rootVisualElement.Q<Button>("open-python-link-button");
            openUvLinkButton = rootVisualElement.Q<Button>("open-uv-link-button");
            installUvButton = rootVisualElement.Q<Button>("install-uv-button");
            refreshButton = rootVisualElement.Q<Button>("refresh-button");
            doneButton = rootVisualElement.Q<Button>("done-button");
            stepDeps = rootVisualElement.Q<VisualElement>("step-deps");
            stepClients = rootVisualElement.Q<VisualElement>("step-clients");
            clientsList = rootVisualElement.Q<VisualElement>("clients-list");
            skipClientsButton = rootVisualElement.Q<Button>("skip-clients-button");
            configureSelectedButton = rootVisualElement.Q<Button>("configure-selected-button");

            // Register callbacks
            refreshButton.clicked += OnRefreshClicked;
            doneButton.clicked += OnDoneClicked;
            openPythonLinkButton.clicked += OnOpenPythonInstallClicked;
            openUvLinkButton.clicked += OnOpenUvInstallClicked;
            if (installUvButton != null) installUvButton.clicked += OnInstallUvClicked;
            skipClientsButton.clicked += OnSkipClientsClicked;
            configureSelectedButton.clicked += OnConfigureSelectedClicked;

            // Initial update
            UpdateUI();
        }

        private void OnEnable()
        {
            if (_dependencyResult == null)
            {
                _dependencyResult = DependencyManager.CheckAllDependencies();
            }
            // Resume polling if a uv install was still in flight when the window was last disabled,
            // so its completion is still processed (button reset, dependencies re-checked).
            if (_uvInstallTask != null)
            {
                EditorApplication.update -= PollUvInstall;
                EditorApplication.update += PollUvInstall;
            }
        }

        private void OnRefreshClicked()
        {
            _dependencyResult = DependencyManager.CheckAllDependencies();
            UpdateUI();
        }

        private void OnDoneClicked()
        {
            if (_dependencyResult != null && _dependencyResult.IsSystemReady)
            {
                ShowClientsStep();
            }
            else
            {
                Setup.SetupWindowService.MarkSetupDismissed();
                Close();
            }
        }

        private void ShowClientsStep()
        {
            stepDeps.style.display = DisplayStyle.None;
            stepClients.style.display = DisplayStyle.Flex;
            PopulateClientsList();
        }

        private void PopulateClientsList()
        {
            clientsList.Clear();
            clientToggles.Clear();
            foreach (var c in McpClientRegistry.All)
            {
                if (!c.IsInstalled) continue;
                var toggle = new Toggle(c.DisplayName)
                {
                    value = true,
                    tooltip = c.GetConfigPath()
                };
                clientToggles.Add((c, toggle));
                clientsList.Add(toggle);
            }
            if (clientToggles.Count == 0)
            {
                clientsList.Add(new Label("No supported MCP clients detected on this machine. You can configure clients later from Tools → MCP for Unity."));
                configureSelectedButton.SetEnabled(false);
            }
        }

        private void OnSkipClientsClicked()
        {
            Setup.SetupWindowService.MarkSetupCompleted();
            Close();
        }

        private void OnConfigureSelectedClicked()
        {
            int success = 0, failure = 0;
            var failures = new List<string>();
            foreach (var (c, toggle) in clientToggles)
            {
                if (!toggle.value) continue;
                try
                {
                    MCPServiceLocator.Client.ConfigureClient(c);
                    success++;
                }
                catch (System.Exception ex)
                {
                    failure++;
                    failures.Add($"⚠ {c.DisplayName}: {ex.Message}");
                }
            }
            if (success == 0 && failure == 0)
            {
                EditorUtility.DisplayDialog(
                    "Client Configuration",
                    "No clients were selected. Tick at least one client to continue, or close the window to skip setup.",
                    "OK");
                return;
            }
            // Keep the summary short: a count, only the failures (if any), and the next step —
            // no need to enumerate every successfully-configured client.
            string failureList = failures.Count > 0 ? "\n\n" + string.Join("\n", failures) : "";
            string nextStep = (failure == 0 && success > 0)
                ? "\n\nYou're all set. Ask your AI assistant to create a GameObject in the open scene to confirm the connection."
                : "";
            EditorUtility.DisplayDialog(
                "Client Configuration",
                $"{success} configured, {failure} failed.{failureList}{nextStep}",
                "OK");
            Setup.SetupWindowService.MarkSetupCompleted();
            Close();
        }

        private void OnOpenPythonInstallClicked()
        {
            var (pythonUrl, _) = DependencyManager.GetInstallationUrls();
            Application.OpenURL(pythonUrl);
        }

        private void OnOpenUvInstallClicked()
        {
            var (_, uvUrl) = DependencyManager.GetInstallationUrls();
            Application.OpenURL(uvUrl);
        }

        private void OnInstallUvClicked()
        {
            if (_uvInstallTask != null) return; // already running

            bool proceed = EditorUtility.DisplayDialog(
                "Install UV",
                "This will download and run the official uv installer:\n\n" +
                UvInstaller.DescribeCommand() +
                "\n\nContinue?",
                "Install",
                "Cancel");
            if (!proceed) return;

            installUvButton.SetEnabled(false);
            installUvButton.text = "Installing UV…";
            statusMessage.text = "Installing uv… this can take a moment.";
            statusMessage.style.color = new StyleColor(new Color(1f, 0.6f, 0f));

            _uvInstallTask = Task.Run(() => UvInstaller.Run());
            EditorApplication.update += PollUvInstall;
        }

        private void PollUvInstall()
        {
            // The window/UI may have been torn down while the task ran — stop polling and drop it
            // (guards against dereferencing UI fields after teardown).
            if (installUvButton == null || installUvButton.panel == null)
            {
                EditorApplication.update -= PollUvInstall;
                return;
            }
            if (_uvInstallTask == null || !_uvInstallTask.IsCompleted) return;

            EditorApplication.update -= PollUvInstall;
            var task = _uvInstallTask;
            _uvInstallTask = null;

            installUvButton.SetEnabled(true);
            installUvButton.text = "Install UV Automatically";

            // UvInstaller.Run catches its own exceptions, so the task always completes with a result.
            UvInstaller.UvInstallResult result = task.Result;

            if (result.Success)
            {
                _dependencyResult = DependencyManager.CheckAllDependencies();
                UpdateUI();
                if (!_dependencyResult.IsSystemReady)
                {
                    EditorUtility.DisplayDialog(
                        "Install UV",
                        "uv installed, but it isn't visible on PATH yet. Restart Unity (or your terminal) so it picks up the new PATH, then click Refresh.\n\n" +
                        result.Output,
                        "OK");
                }
            }
            else
            {
                // Reset the status label off the "Installing…" state before reporting the failure.
                UpdateUI();
                EditorUtility.DisplayDialog(
                    "Install UV Failed",
                    "The installer did not complete successfully. You can install uv manually via \"Open UV Install Page\".\n\n" +
                    result.Output,
                    "OK");
            }
        }

        private void OnDisable()
        {
            EditorApplication.update -= PollUvInstall;
        }

        private void UpdateUI()
        {
            if (_dependencyResult == null)
                return;

            // Update Python status
            var pythonDep = _dependencyResult.Dependencies.Find(d => d.Name == "Python");
            if (pythonDep != null)
            {
                UpdateDependencyStatus(pythonIndicator, pythonVersion, pythonDetails, pythonDep);
            }

            // Update uv status
            var uvDep = _dependencyResult.Dependencies.Find(d => d.Name == "uv Package Manager");
            if (uvDep != null)
            {
                UpdateDependencyStatus(uvIndicator, uvVersion, uvDetails, uvDep);
            }

            // Offer the one-click uv installer only when uv is actually missing
            bool uvMissing = uvDep != null && !uvDep.IsAvailable;
            if (installUvButton != null)
            {
                bool showInstall = uvMissing && UvInstaller.IsSupported && _uvInstallTask == null;
                installUvButton.style.display = showInstall ? DisplayStyle.Flex : DisplayStyle.None;
            }

            // Update overall status
            if (_dependencyResult.IsSystemReady)
            {
                statusMessage.text = "✓ All requirements met! MCP for Unity is ready to use.";
                statusMessage.style.color = new StyleColor(Color.green);
                installationSection.style.display = DisplayStyle.None;
            }
            else
            {
                statusMessage.text = "⚠ Missing dependencies. MCP for Unity requires all dependencies to function.";
                statusMessage.style.color = new StyleColor(new Color(1f, 0.6f, 0f)); // Orange
                installationSection.style.display = DisplayStyle.Flex;
                installationInstructions.text = DependencyManager.GetInstallationRecommendations();
            }
        }

        private void UpdateDependencyStatus(VisualElement indicator, Label versionLabel, Label detailsLabel, DependencyStatus dep)
        {
            if (dep.IsAvailable)
            {
                indicator.RemoveFromClassList("invalid");
                indicator.AddToClassList("valid");
                versionLabel.text = $"v{dep.Version}";
                detailsLabel.text = dep.Details ?? "Available";
                detailsLabel.style.color = new StyleColor(Color.gray);
            }
            else
            {
                indicator.RemoveFromClassList("valid");
                indicator.AddToClassList("invalid");
                versionLabel.text = "Not Found";
                detailsLabel.text = dep.ErrorMessage ?? "Not available";
                detailsLabel.style.color = new StyleColor(Color.red);
            }
        }
    }
}
