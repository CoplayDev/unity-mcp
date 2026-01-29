using System;
using MCPForUnity.Editor.Dependencies;
using MCPForUnity.Editor.Dependencies.Models;
using MCPForUnity.Editor.Helpers;
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
        private Button refreshButton;
        private Button doneButton;

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
            refreshButton = rootVisualElement.Q<Button>("refresh-button");
            doneButton = rootVisualElement.Q<Button>("done-button");

            // Register callbacks
            refreshButton.clicked += OnRefreshClicked;
            doneButton.clicked += OnDoneClicked;
            openPythonLinkButton.clicked += OnOpenPythonInstallClicked;
            openUvLinkButton.clicked += OnOpenUvInstallClicked;

            // Initial update
            UpdateUI();
        }

        private void OnEnable()
        {
            if (_dependencyResult == null)
            {
                _dependencyResult = DependencyManager.CheckAllDependencies();
            }
        }

        private void OnRefreshClicked()
        {
            _dependencyResult = DependencyManager.CheckAllDependencies();
            UpdateUI();
        }

        private void OnDoneClicked()
        {
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
