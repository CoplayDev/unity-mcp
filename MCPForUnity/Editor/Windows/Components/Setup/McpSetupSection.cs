using System;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Dependencies;
using MCPForUnity.Editor.Dependencies.Models;
using MCPForUnity.Editor.Helpers;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MCPForUnity.Editor.Windows.Components.Setup
{
    public class McpSetupSection
    {
        private readonly VisualElement _root;
        
        // UI Elements
        private Label pythonStatus;
        private Button pythonInstallBtn;
        private Button pythonLocateBtn;
        private VisualElement pythonPathRow;
        private Label pythonPathLabel;
        private Button pythonClearBtn;

        private Label nodeStatus;
        private Button nodeInstallBtn;
        private Button nodeLocateBtn;
        private VisualElement nodePathRow;
        private Label nodePathLabel;
        private Button nodeClearBtn;
        
        private Label uvStatus;
        private Button uvInstallBtn;
        private Button uvLocateBtn;
        private VisualElement uvPathRow;
        private Label uvPathLabel;
        private Button uvClearBtn;

        private Button refreshBtn;

        private VisualElement step2Container;
        private Label serverStatusText;
        private Button installServerBtn;

        // Server Config Elements
        private Button serverConfigBtn;
        private VisualElement serverConfigRow;
        private TextField gitUrlOverride;
        private Button clearGitUrlButton;

        // Deployment Elements
        private Button deployToggleBtn;
        private VisualElement deployPanel;
        private TextField deploySourcePath;
        private Button browseDeploySourceButton;
        private Button clearDeploySourceButton;
        private Button deployButton;
        private Button deployRestoreButton;
        private Label deployStatusLabel;
        private Label deployBackupLabel;

        private DependencyCheckResult _dependencyResult;
        private bool _wasSetupComplete = false;
        
        public event Action OnSetupComplete;

        public McpSetupSection(VisualElement root)
        {
            _root = root;
            Initialize();
        }

        private void Initialize()
        {
            // Query Elements
            pythonStatus = _root.Q<Label>("python-status");
            pythonInstallBtn = _root.Q<Button>("python-install-btn");
            pythonLocateBtn = _root.Q<Button>("python-locate-btn");
            pythonPathRow = _root.Q<VisualElement>("python-path-row");
            pythonPathLabel = _root.Q<Label>("python-path-label");
            pythonClearBtn = _root.Q<Button>("python-clear-btn");

            nodeStatus = _root.Q<Label>("node-status");
            nodeInstallBtn = _root.Q<Button>("node-install-btn");
            nodeLocateBtn = _root.Q<Button>("node-locate-btn");
            nodePathRow = _root.Q<VisualElement>("node-path-row");
            nodePathLabel = _root.Q<Label>("node-path-label");
            nodeClearBtn = _root.Q<Button>("node-clear-btn");
            
            uvStatus = _root.Q<Label>("uv-status");
            uvInstallBtn = _root.Q<Button>("uv-install-btn");
            uvLocateBtn = _root.Q<Button>("uv-locate-btn");
            uvPathRow = _root.Q<VisualElement>("uv-path-row");
            uvPathLabel = _root.Q<Label>("uv-path-label");
            uvClearBtn = _root.Q<Button>("uv-clear-btn");

            refreshBtn = _root.Q<Button>("refresh-dependencies-btn");

            step2Container = _root.Q<VisualElement>("step-2-container");
            serverStatusText = _root.Q<Label>("server-status-text");
            installServerBtn = _root.Q<Button>("install-server-btn");
            serverConfigBtn = _root.Q<Button>("server-config-btn");
            serverConfigRow = _root.Q<VisualElement>("server-config-row");

            gitUrlOverride = _root.Q<TextField>("git-url-override");
            clearGitUrlButton = _root.Q<Button>("clear-git-url-button");

            // Deployment UI
            deployToggleBtn = _root.Q<Button>("deploy-toggle-btn");
            deployPanel = _root.Q<VisualElement>("deploy-panel");
            deploySourcePath = _root.Q<TextField>("deploy-source-path");
            browseDeploySourceButton = _root.Q<Button>("browse-deploy-source-button");
            clearDeploySourceButton = _root.Q<Button>("clear-deploy-source-button");
            deployButton = _root.Q<Button>("deploy-button");
            deployRestoreButton = _root.Q<Button>("deploy-restore-button");
            deployStatusLabel = _root.Q<Label>("deploy-status-label");
            deployBackupLabel = _root.Q<Label>("deploy-backup-label");

            // Bind Events
            pythonInstallBtn.clicked += () => Application.OpenURL(DependencyManager.GetInstallationUrls().pythonUrl);
            pythonLocateBtn.clicked += OnPythonLocateClicked;
            pythonClearBtn.clicked += OnPythonClearClicked;

            nodeInstallBtn.clicked += () => Application.OpenURL("https://nodejs.org/");
            nodeLocateBtn.clicked += OnNodeLocateClicked;
            nodeClearBtn.clicked += OnNodeClearClicked;
            
            uvInstallBtn.clicked += OnUvInstallClicked;
            uvLocateBtn.clicked += OnUvLocateClicked;
            uvClearBtn.clicked += OnUvClearClicked;

            refreshBtn.clicked += RefreshStatus;
            installServerBtn.clicked += OnInstallServerClicked;
            
            if (serverConfigBtn != null && serverConfigRow != null)
            {
                serverConfigBtn.clicked += () => 
                {
                    serverConfigRow.style.display = serverConfigRow.style.display == DisplayStyle.None ? DisplayStyle.Flex : DisplayStyle.None;
                };
            }
            
            if (deployToggleBtn != null && deployPanel != null)
            {
                deployToggleBtn.clicked += () => 
                {
                    bool isVisible = deployPanel.style.display == DisplayStyle.Flex;
                    deployPanel.style.display = isVisible ? DisplayStyle.None : DisplayStyle.Flex;
                    deployToggleBtn.text = isVisible ? "▼" : "▲";
                };
            }

            if (gitUrlOverride != null)
            {
                gitUrlOverride.value = EditorPrefs.GetString(EditorPrefKeys.GitUrlOverride, "");
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
                });
            }

            if (clearGitUrlButton != null)
            {
                clearGitUrlButton.clicked += () =>
                {
                    if (gitUrlOverride != null) gitUrlOverride.value = string.Empty;
                    EditorPrefs.DeleteKey(EditorPrefKeys.GitUrlOverride);
                };
            }

            if (browseDeploySourceButton != null) browseDeploySourceButton.clicked += OnBrowseDeploySourceClicked;
            if (clearDeploySourceButton != null) clearDeploySourceButton.clicked += OnClearDeploySourceClicked;
            if (deployButton != null) deployButton.clicked += OnDeployClicked;
            if (deployRestoreButton != null) deployRestoreButton.clicked += OnRestoreBackupClicked;

            // Initialize Advanced UI State
            UpdateAdvancedSettingsUI();
        }

        public void RefreshStatus()
        {
            _dependencyResult = DependencyManager.CheckAllDependencies();
            UpdateUI();
            UpdateAdvancedSettingsUI();
        }
        
        public void RefreshStatus(DependencyCheckResult result)
        {
            _dependencyResult = result;
            UpdateUI();
            UpdateAdvancedSettingsUI();
        }

        private void UpdateUI()
        {
            if (_dependencyResult == null) return;

            // --- Step 1: Prerequisites ---
            var pythonDep = _dependencyResult.Dependencies.Find(d => d.Name == "Python");
            var nodeDep = _dependencyResult.Dependencies.Find(d => d.Name == "Node.js");
            var uvDep = _dependencyResult.Dependencies.Find(d => d.Name == "uv Package Manager");

            bool pythonReady = pythonDep?.IsAvailable == true;
            bool nodeReady = nodeDep?.IsAvailable == true;
            bool uvReady = uvDep?.IsAvailable == true;

            var pathService = MCPForUnity.Editor.Services.MCPServiceLocator.Paths;

            // Python UI
            bool hasPythonOverride = pathService.HasPythonPathOverride;
            if (pythonReady)
            {
                pythonStatus.text = $"OK (v{pythonDep.Version})";
                pythonStatus.style.color = new StyleColor(new Color(0.3f, 0.7f, 0.3f));
                pythonInstallBtn.style.display = DisplayStyle.None;
            }
            else
            {
                pythonStatus.text = "Missing";
                pythonStatus.style.color = new StyleColor(new Color(0.8f, 0.3f, 0.3f));
                pythonInstallBtn.style.display = DisplayStyle.Flex;
            }
            
            if (hasPythonOverride)
            {
                pythonPathRow.style.display = DisplayStyle.Flex;
                pythonPathLabel.text = pathService.GetPythonPath();
            }
            else
            {
                pythonPathRow.style.display = DisplayStyle.None;
            }

            // Node UI
            bool hasNodeOverride = pathService.HasNodePathOverride;
            if (nodeReady)
            {
                nodeStatus.text = $"OK (v{nodeDep.Version})";
                nodeStatus.style.color = new StyleColor(new Color(0.3f, 0.7f, 0.3f));
                nodeInstallBtn.style.display = DisplayStyle.None;
            }
            else
            {
                nodeStatus.text = "Missing";
                nodeStatus.style.color = new StyleColor(new Color(0.8f, 0.3f, 0.3f));
                nodeInstallBtn.style.display = DisplayStyle.Flex;
            }
            
            if (hasNodeOverride)
            {
                nodePathRow.style.display = DisplayStyle.Flex;
                nodePathLabel.text = pathService.GetNodePath();
            }
            else
            {
                nodePathRow.style.display = DisplayStyle.None;
            }
            
            // uv UI
            bool hasUvOverride = pathService.HasUvxPathOverride;
            if (uvReady)
            {
                string uvVersion = uvDep.Version ?? "installed";
                uvStatus.text = $"OK (v{uvVersion})";
                uvStatus.style.color = new StyleColor(new Color(0.3f, 0.7f, 0.3f));
                uvInstallBtn.style.display = DisplayStyle.None;
            }
            else
            {
                uvStatus.text = "Missing";
                uvStatus.style.color = new StyleColor(new Color(0.8f, 0.3f, 0.3f));
                uvInstallBtn.style.display = DisplayStyle.Flex;
            }
            
            if (hasUvOverride)
            {
                uvPathRow.style.display = DisplayStyle.Flex;
                uvPathLabel.text = pathService.GetUvxPath();
            }
            else
            {
                uvPathRow.style.display = DisplayStyle.None;
            }

            bool step1Complete = pythonReady && nodeReady;

            // --- Step 2: Server Environment ---
            step2Container.SetEnabled(step1Complete);
            
            var serverDep = _dependencyResult.Dependencies.Find(d => d.Name == "Server Environment");
            bool serverReady = serverDep?.IsAvailable == true;

            if (serverReady)
            {
                serverStatusText.text = "Server Environment Installed";
                serverStatusText.style.color = new StyleColor(new Color(0.3f, 0.7f, 0.3f));
                installServerBtn.text = "Reinstall Server";
            }
            else
            {
                if (step1Complete)
                {
                    serverStatusText.text = "Required";
                    serverStatusText.style.color = new StyleColor(new Color(0.9f, 0.4f, 0.4f)); // Red
                }
                else
                {
                    serverStatusText.text = "Install dependencies first";
                    serverStatusText.style.color = new StyleColor(Color.gray);
                }
                installServerBtn.text = "Install Server";
            }
            
            bool isSetupComplete = step1Complete && serverReady;
            if (isSetupComplete && !_wasSetupComplete)
            {
                _wasSetupComplete = true;
                OnSetupComplete?.Invoke();
            }
            else if (!isSetupComplete)
            {
                _wasSetupComplete = false;
            }
        }

        private void UpdateAdvancedSettingsUI()
        {
            // Deployment Section
            var deployService = MCPForUnity.Editor.Services.MCPServiceLocator.Deployment;

            if (deploySourcePath != null)
            {
                string sourcePath = deployService.GetStoredSourcePath();
                deploySourcePath.value = string.IsNullOrEmpty(sourcePath) ? "Not set" : sourcePath;
            }

            if (deployBackupLabel != null)
            {
                 string backupPath = deployService.GetLastBackupPath();
                 deployBackupLabel.text = deployService.HasBackup() ? $"Last backup: {backupPath}" : "Last backup: none";
            }

            if (deployRestoreButton != null) deployRestoreButton.SetEnabled(deployService.HasBackup());
            
            // Update status label to show if source is configured
            if (deployStatusLabel != null)
            {
                string sourcePath = deployService.GetStoredSourcePath();
                if (!string.IsNullOrEmpty(sourcePath))
                {
                    // Extract folder name from path
                    string folderName = System.IO.Path.GetFileName(sourcePath);
                    deployStatusLabel.text = $"Source: {folderName}";
                    deployStatusLabel.style.color = new StyleColor(new Color(0.5f, 0.8f, 1f)); // Light blue
                }
                else
                {
                    deployStatusLabel.text = "Optional";
                    deployStatusLabel.style.color = new StyleColor(new Color(0.5f, 0.5f, 0.5f)); // Gray
                }
            }
        }

        // --- Event Handlers ---

        private void OnPythonLocateClicked()
        {
            string path = EditorUtility.OpenFilePanel("Locate Python Executable", "", "exe");
            if (!string.IsNullOrEmpty(path))
            {
                MCPForUnity.Editor.Services.MCPServiceLocator.Paths.SetPythonPathOverride(path);
                RefreshStatus();
            }
        }

        private void OnPythonClearClicked()
        {
            MCPForUnity.Editor.Services.MCPServiceLocator.Paths.ClearPythonPathOverride();
            RefreshStatus();
        }

        private void OnNodeLocateClicked()
        {
            string path = EditorUtility.OpenFilePanel("Locate Node.js Executable", "", "exe");
            if (!string.IsNullOrEmpty(path))
            {
                MCPForUnity.Editor.Services.MCPServiceLocator.Paths.SetNodePathOverride(path);
                RefreshStatus();
            }
        }

        private void OnNodeClearClicked()
        {
            MCPForUnity.Editor.Services.MCPServiceLocator.Paths.ClearNodePathOverride();
            RefreshStatus();
        }
        
        private void OnUvLocateClicked()
        {
            string path = EditorUtility.OpenFilePanel("Locate uv Executable", "", "exe");
            if (!string.IsNullOrEmpty(path))
            {
                MCPForUnity.Editor.Services.MCPServiceLocator.Paths.SetUvxPathOverride(path);
                RefreshStatus();
            }
        }

        private void OnUvClearClicked()
        {
            MCPForUnity.Editor.Services.MCPServiceLocator.Paths.ClearUvxPathOverride();
            RefreshStatus();
        }
        
        private void OnUvInstallClicked()
        {
            EditorUtility.DisplayProgressBar("Installing uv", "Installing uv via pip...", 0.5f);
            try
            {
                string result = MCPForUnity.Editor.Setup.ServerEnvironmentSetup.InstallUvExplicitly();
                if (result != null)
                {
                    EditorUtility.DisplayDialog("Success", "uv installed successfully!", "OK");
                }
                else
                {
                    EditorUtility.DisplayDialog("Error", "Failed to install uv. Check console.", "OK");
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                RefreshStatus();
            }
        }

        private void OnInstallServerClicked()
        {
            MCPForUnity.Editor.Setup.ServerEnvironmentSetup.InstallServerEnvironment();
            RefreshStatus();
            OnSetupComplete?.Invoke(); // Trigger main window refresh
        }

        // --- Advanced Settings Handlers ---

        private void OnBrowseDeploySourceClicked()
        {
            string picked = EditorUtility.OpenFolderPanel("Select MCPForUnity folder", string.Empty, string.Empty);
            if (!string.IsNullOrEmpty(picked))
            {
                MCPForUnity.Editor.Services.MCPServiceLocator.Deployment.SetStoredSourcePath(picked);
                UpdateAdvancedSettingsUI();
            }
        }

        private void OnClearDeploySourceClicked()
        {
            MCPForUnity.Editor.Services.MCPServiceLocator.Deployment.ClearStoredSourcePath();
            UpdateAdvancedSettingsUI();
        }

        private void OnDeployClicked()
        {
            var result = MCPForUnity.Editor.Services.MCPServiceLocator.Deployment.DeployFromStoredSource();
            if (deployStatusLabel != null)
            {
                deployStatusLabel.text = result.Message;
                deployStatusLabel.style.color = result.Success ? StyleKeyword.Null : new StyleColor(Color.red);
            }
            EditorUtility.DisplayDialog(result.Success ? "Success" : "Failed", result.Message, "OK");
            UpdateAdvancedSettingsUI();
        }

        private void OnRestoreBackupClicked()
        {
             var result = MCPForUnity.Editor.Services.MCPServiceLocator.Deployment.RestoreLastBackup();
             if (deployStatusLabel != null)
            {
                deployStatusLabel.text = result.Message;
                deployStatusLabel.style.color = result.Success ? StyleKeyword.Null : new StyleColor(Color.red);
            }
            EditorUtility.DisplayDialog(result.Success ? "Success" : "Failed", result.Message, "OK");
            UpdateAdvancedSettingsUI();
        }
    }
}
