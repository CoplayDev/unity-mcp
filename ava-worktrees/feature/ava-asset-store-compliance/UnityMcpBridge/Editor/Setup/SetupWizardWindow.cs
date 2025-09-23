using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Dependencies;
using MCPForUnity.Editor.Dependencies.Models;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Installation;
using MCPForUnity.Editor.Data;
using MCPForUnity.Editor.Models;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Setup
{
    /// <summary>
    /// Setup wizard window for guiding users through dependency installation
    /// </summary>
    public class SetupWizardWindow : EditorWindow
    {
        private DependencyCheckResult _dependencyResult;
        private Vector2 _scrollPosition;
        private int _currentStep = 0;
        private bool _isInstalling = false;
        private string _installationStatus = "";
        private InstallationOrchestrator _orchestrator;
        
        // Client configuration state
        private McpClients _mcpClients;
        private int _selectedClientIndex = 0;
        private bool _isConfiguringClients = false;
        private string _clientConfigurationStatus = "";
        private List<McpClient> _availableClients = new List<McpClient>();

        private readonly string[] _stepTitles = {
            "Welcome",
            "Dependency Check",
            "Installation Options",
            "Installation Progress",
            "Client Configuration",
            "Complete"
        };

        public static void ShowWindow(DependencyCheckResult dependencyResult = null)
        {
            var window = GetWindow<SetupWizardWindow>("MCP for Unity Setup");
            window.minSize = new Vector2(500, 400);
            window.maxSize = new Vector2(800, 600);
            window._dependencyResult = dependencyResult ?? DependencyManager.CheckAllDependencies();
            window.Show();
        }

        private void OnEnable()
        {
            if (_dependencyResult == null)
            {
                _dependencyResult = DependencyManager.CheckAllDependencies();
            }
            
            _orchestrator = new InstallationOrchestrator();
            _orchestrator.OnProgressUpdate += OnInstallationProgress;
            _orchestrator.OnInstallationComplete += OnInstallationComplete;
            
            // Initialize MCP clients
            _mcpClients = new McpClients();
            RefreshAvailableClients();
        }

        private void OnDisable()
        {
            if (_orchestrator != null)
            {
                _orchestrator.OnProgressUpdate -= OnInstallationProgress;
                _orchestrator.OnInstallationComplete -= OnInstallationComplete;
            }
        }

        private void OnGUI()
        {
            DrawHeader();
            DrawProgressBar();
            
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            
            switch (_currentStep)
            {
                case 0: DrawWelcomeStep(); break;
                case 1: DrawDependencyCheckStep(); break;
                case 2: DrawInstallationOptionsStep(); break;
                case 3: DrawInstallationProgressStep(); break;
                case 4: DrawClientConfigurationStep(); break;
                case 5: DrawCompleteStep(); break;
            }
            
            EditorGUILayout.EndScrollView();
            
            DrawFooter();
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("MCP for Unity Setup Wizard", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"Step {_currentStep + 1} of {_stepTitles.Length}");
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space();
            
            // Step title
            var titleStyle = new GUIStyle(EditorStyles.largeLabel)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold
            };
            EditorGUILayout.LabelField(_stepTitles[_currentStep], titleStyle);
            EditorGUILayout.Space();
        }

        private void DrawProgressBar()
        {
            var rect = EditorGUILayout.GetControlRect(false, 4);
            var progress = (_currentStep + 1) / (float)_stepTitles.Length;
            EditorGUI.ProgressBar(rect, progress, "");
            EditorGUILayout.Space();
        }

        private void DrawWelcomeStep()
        {
            EditorGUILayout.LabelField("Welcome to MCP for Unity!", EditorStyles.boldLabel);
            EditorGUILayout.Space();
            
            EditorGUILayout.LabelField(
                "This wizard will help you set up the required dependencies for MCP for Unity to work properly.",
                EditorStyles.wordWrappedLabel
            );
            EditorGUILayout.Space();
            
            EditorGUILayout.LabelField("What is MCP for Unity?", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "MCP for Unity is a bridge that connects AI assistants like Claude Desktop to your Unity Editor, " +
                "allowing them to help you with Unity development tasks directly.",
                EditorStyles.wordWrappedLabel
            );
            EditorGUILayout.Space();
            
            EditorGUILayout.LabelField("Setup Process:", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("1. Check and install required dependencies", EditorStyles.label);
            EditorGUILayout.LabelField("2. Configure AI assistants (Claude, Cursor, VSCode, etc.)", EditorStyles.label);
            EditorGUILayout.LabelField("3. Verify everything works together", EditorStyles.label);
            EditorGUILayout.Space();
            
            EditorGUILayout.LabelField("Required Dependencies:", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("• Python 3.10 or later", EditorStyles.label);
            EditorGUILayout.LabelField("• UV package manager", EditorStyles.label);
            EditorGUILayout.Space();
            
            EditorGUILayout.HelpBox(
                "This wizard will guide you through the complete setup process, leaving you ready to use AI assistance in Unity.",
                MessageType.Info
            );
        }

        private void DrawDependencyCheckStep()
        {
            EditorGUILayout.LabelField("Checking Dependencies", EditorStyles.boldLabel);
            EditorGUILayout.Space();
            
            if (GUILayout.Button("Refresh Dependency Check"))
            {
                _dependencyResult = DependencyManager.CheckAllDependencies();
            }
            EditorGUILayout.Space();
            
            // Show dependency status
            foreach (var dep in _dependencyResult.Dependencies)
            {
                DrawDependencyStatus(dep);
            }
            
            EditorGUILayout.Space();
            
            // Overall status
            var statusColor = _dependencyResult.IsSystemReady ? Color.green : Color.red;
            var statusText = _dependencyResult.IsSystemReady ? "✓ System Ready" : "✗ Dependencies Missing";
            
            var originalColor = GUI.color;
            GUI.color = statusColor;
            EditorGUILayout.LabelField(statusText, EditorStyles.boldLabel);
            GUI.color = originalColor;
            
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(_dependencyResult.Summary, EditorStyles.wordWrappedLabel);
            
            if (!_dependencyResult.IsSystemReady)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(
                    "Some dependencies are missing. The next step will help you install them.",
                    MessageType.Warning
                );
            }
        }

        private void DrawDependencyStatus(DependencyStatus dep)
        {
            EditorGUILayout.BeginHorizontal();
            
            // Status icon
            var statusIcon = dep.IsAvailable ? "✓" : "✗";
            var statusColor = dep.IsAvailable ? Color.green : (dep.IsRequired ? Color.red : Color.yellow);
            
            var originalColor = GUI.color;
            GUI.color = statusColor;
            GUILayout.Label(statusIcon, GUILayout.Width(20));
            GUI.color = originalColor;
            
            // Dependency name and details
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(dep.Name, EditorStyles.boldLabel);
            
            if (!string.IsNullOrEmpty(dep.Version))
            {
                EditorGUILayout.LabelField($"Version: {dep.Version}", EditorStyles.miniLabel);
            }
            
            if (!string.IsNullOrEmpty(dep.Details))
            {
                EditorGUILayout.LabelField(dep.Details, EditorStyles.miniLabel);
            }
            
            if (!string.IsNullOrEmpty(dep.ErrorMessage))
            {
                EditorGUILayout.LabelField($"Error: {dep.ErrorMessage}", EditorStyles.miniLabel);
            }
            
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();
        }

        private void DrawInstallationOptionsStep()
        {
            EditorGUILayout.LabelField("Installation Options", EditorStyles.boldLabel);
            EditorGUILayout.Space();
            
            var missingDeps = _dependencyResult.GetMissingRequired();
            if (missingDeps.Count == 0)
            {
                EditorGUILayout.HelpBox("All required dependencies are already available!", MessageType.Info);
                return;
            }
            
            EditorGUILayout.LabelField("Missing Dependencies:", EditorStyles.boldLabel);
            foreach (var dep in missingDeps)
            {
                EditorGUILayout.LabelField($"• {dep.Name}", EditorStyles.label);
            }
            EditorGUILayout.Space();
            
            EditorGUILayout.LabelField("Installation Methods:", EditorStyles.boldLabel);
            
            // Automatic installation option
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Automatic Installation (Recommended)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "The wizard will attempt to install missing dependencies automatically.",
                EditorStyles.wordWrappedLabel
            );
            EditorGUILayout.Space();
            
            if (GUILayout.Button("Start Automatic Installation", GUILayout.Height(30)))
            {
                StartAutomaticInstallation();
            }
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.Space();
            
            // Manual installation option
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Manual Installation", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Install dependencies manually using the platform-specific instructions below.",
                EditorStyles.wordWrappedLabel
            );
            EditorGUILayout.Space();
            
            var recommendations = DependencyManager.GetInstallationRecommendations();
            EditorGUILayout.LabelField(recommendations, EditorStyles.wordWrappedLabel);
            
            EditorGUILayout.Space();
            if (GUILayout.Button("Open Installation URLs"))
            {
                OpenInstallationUrls();
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawInstallationProgressStep()
        {
            EditorGUILayout.LabelField("Installation Progress", EditorStyles.boldLabel);
            EditorGUILayout.Space();
            
            if (_isInstalling)
            {
                EditorGUILayout.LabelField("Installing dependencies...", EditorStyles.boldLabel);
                EditorGUILayout.Space();
                
                // Show progress
                var rect = EditorGUILayout.GetControlRect(false, 20);
                EditorGUI.ProgressBar(rect, 0.5f, "Installing...");
                
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(_installationStatus, EditorStyles.wordWrappedLabel);
                
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(
                    "Please wait while dependencies are being installed. This may take a few minutes.",
                    MessageType.Info
                );
            }
            else
            {
                EditorGUILayout.LabelField("Installation completed!", EditorStyles.boldLabel);
                EditorGUILayout.Space();
                
                if (GUILayout.Button("Check Dependencies Again"))
                {
                    _dependencyResult = DependencyManager.CheckAllDependencies();
                    if (_dependencyResult.IsSystemReady)
                    {
                        _currentStep = 4; // Go to client configuration step
                        RefreshAvailableClients();
                    }
                }
            }
        }

        private void DrawClientConfigurationStep()
        {
            EditorGUILayout.LabelField("MCP Client Configuration", EditorStyles.boldLabel);
            EditorGUILayout.Space();
            
            EditorGUILayout.LabelField(
                "Configure your AI assistants to connect to MCP for Unity. This step will set up the connection " +
                "between your AI tools and Unity.",
                EditorStyles.wordWrappedLabel
            );
            EditorGUILayout.Space();
            
            if (_availableClients.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No compatible AI assistants detected on your system. You can skip this step and configure clients manually later.",
                    MessageType.Info
                );
                
                EditorGUILayout.Space();
                if (GUILayout.Button("Refresh Client Detection"))
                {
                    RefreshAvailableClients();
                }
                return;
            }
            
            // Client selector
            EditorGUILayout.LabelField("Available AI Assistants:", EditorStyles.boldLabel);
            EditorGUILayout.Space();
            
            string[] clientNames = _availableClients.Select(c => c.name).ToArray();
            EditorGUI.BeginChangeCheck();
            _selectedClientIndex = EditorGUILayout.Popup("Select Client", _selectedClientIndex, clientNames);
            if (EditorGUI.EndChangeCheck())
            {
                _selectedClientIndex = Mathf.Clamp(_selectedClientIndex, 0, _availableClients.Count - 1);
            }
            
            EditorGUILayout.Space();
            
            if (_selectedClientIndex < _availableClients.Count)
            {
                var selectedClient = _availableClients[_selectedClientIndex];
                DrawClientConfigurationUI(selectedClient);
            }
            
            EditorGUILayout.Space();
            
            // Configuration status
            if (!string.IsNullOrEmpty(_clientConfigurationStatus))
            {
                EditorGUILayout.LabelField("Status:", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(_clientConfigurationStatus, EditorStyles.wordWrappedLabel);
                EditorGUILayout.Space();
            }
            
            // Auto-configure all button
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Auto-Configure All Detected Clients", GUILayout.Height(30)))
            {
                StartClientAutoConfiguration();
            }
            if (GUILayout.Button("Skip Client Configuration", GUILayout.Height(30)))
            {
                _clientConfigurationStatus = "Client configuration skipped. You can configure clients later from the MCP for Unity window.";
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawCompleteStep()
        {
            EditorGUILayout.LabelField("Setup Complete!", EditorStyles.boldLabel);
            EditorGUILayout.Space();
            
            if (_dependencyResult.IsSystemReady)
            {
                EditorGUILayout.HelpBox(
                    "✓ MCP for Unity setup is complete! Your system is ready to use AI assistance in Unity.",
                    MessageType.Info
                );
                
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("What's Ready:", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("✓ All dependencies are installed and available", EditorStyles.label);
                
                // Show configured clients
                var configuredClients = _availableClients.Where(c => c.status == McpStatus.Configured).ToList();
                if (configuredClients.Count > 0)
                {
                    EditorGUILayout.LabelField($"✓ {configuredClients.Count} AI assistant(s) configured:", EditorStyles.label);
                    foreach (var client in configuredClients)
                    {
                        EditorGUILayout.LabelField($"   • {client.name}", EditorStyles.label);
                    }
                }
                else
                {
                    EditorGUILayout.LabelField("• AI assistants can be configured later", EditorStyles.label);
                }
                
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Next Steps:", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("1. Open your configured AI assistant", EditorStyles.label);
                EditorGUILayout.LabelField("2. Start a conversation and ask for help with Unity", EditorStyles.label);
                EditorGUILayout.LabelField("3. The AI can now read, modify, and create Unity assets!", EditorStyles.label);
                
                EditorGUILayout.Space();
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Open Documentation"))
                {
                    Application.OpenURL("https://github.com/CoplayDev/unity-mcp");
                }
                if (GUILayout.Button("Open MCP Client Configuration"))
                {
                    Windows.MCPForUnityEditorWindow.ShowClientConfiguration();
                }
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Some dependencies are still missing. Please install them manually or restart the setup wizard.",
                    MessageType.Warning
                );
                
                EditorGUILayout.Space();
                if (GUILayout.Button("Restart Setup Wizard"))
                {
                    _currentStep = 0;
                    _dependencyResult = DependencyManager.CheckAllDependencies();
                }
            }
        }

        private void DrawFooter()
        {
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            
            // Back button
            GUI.enabled = _currentStep > 0 && !_isInstalling;
            if (GUILayout.Button("Back"))
            {
                _currentStep--;
            }
            
            GUILayout.FlexibleSpace();
            
            // Skip/Dismiss button
            GUI.enabled = !_isInstalling;
            if (GUILayout.Button("Skip Setup"))
            {
                bool dismiss = EditorUtility.DisplayDialog(
                    "Skip Setup",
                    "Are you sure you want to skip the setup? You can run it again later from the Window menu.",
                    "Skip",
                    "Cancel"
                );
                
                if (dismiss)
                {
                    SetupWizard.MarkSetupDismissed();
                    Close();
                }
            }
            
            // Next/Finish button
            GUI.enabled = !_isInstalling && !_isConfiguringClients;
            string nextButtonText = _currentStep == _stepTitles.Length - 1 ? "Finish" : "Next";
            
            // Special handling for certain steps
            bool canProceed = true;
            if (_currentStep == 1) // Dependency check step
            {
                // Can proceed even if dependencies aren't ready (user can install manually)
                canProceed = true;
            }
            else if (_currentStep == 3) // Installation progress step
            {
                // Can only proceed if not currently installing
                canProceed = !_isInstalling;
            }
            else if (_currentStep == 4) // Client configuration step
            {
                // Can proceed even if no clients are configured
                canProceed = !_isConfiguringClients;
            }
            
            GUI.enabled = canProceed;
            
            if (GUILayout.Button(nextButtonText))
            {
                if (_currentStep == _stepTitles.Length - 1)
                {
                    // Finish setup
                    SetupWizard.MarkSetupCompleted();
                    Close();
                }
                else
                {
                    // Special navigation logic
                    if (_currentStep == 2 && !_dependencyResult.IsSystemReady)
                    {
                        // Skip installation progress if dependencies are ready
                        _currentStep = 4; // Go directly to client configuration
                        RefreshAvailableClients();
                    }
                    else if (_currentStep == 3 && !_isInstalling)
                    {
                        // Move from installation progress to client configuration
                        _currentStep = 4;
                        RefreshAvailableClients();
                    }
                    else
                    {
                        _currentStep++;
                        
                        // Refresh clients when entering client configuration step
                        if (_currentStep == 4)
                        {
                            RefreshAvailableClients();
                        }
                    }
                }
            }
            
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        private void StartAutomaticInstallation()
        {
            _currentStep = 3; // Go to progress step
            _isInstalling = true;
            _installationStatus = "Starting installation...";
            
            var missingDeps = _dependencyResult.GetMissingRequired();
            _orchestrator.StartInstallation(missingDeps);
        }

        private void OpenInstallationUrls()
        {
            var (pythonUrl, uvUrl) = DependencyManager.GetInstallationUrls();
            
            bool openPython = EditorUtility.DisplayDialog(
                "Open Installation URLs",
                "Open Python installation page?",
                "Yes",
                "No"
            );
            
            if (openPython)
            {
                Application.OpenURL(pythonUrl);
            }
            
            bool openUV = EditorUtility.DisplayDialog(
                "Open Installation URLs",
                "Open UV installation page?",
                "Yes",
                "No"
            );
            
            if (openUV)
            {
                Application.OpenURL(uvUrl);
            }
        }

        private void OnInstallationProgress(string status)
        {
            _installationStatus = status;
            Repaint();
        }

        private void OnInstallationComplete(bool success, string message)
        {
            _isInstalling = false;
            _installationStatus = message;
            
            if (success)
            {
                _dependencyResult = DependencyManager.CheckAllDependencies();
                if (_dependencyResult.IsSystemReady)
                {
                    _currentStep = 4; // Go to client configuration step
                    RefreshAvailableClients(); // Refresh clients after dependencies are ready
                }
            }
            
            Repaint();
        }

        /// <summary>
        /// Refresh the list of available MCP clients by detecting installed applications
        /// </summary>
        private void RefreshAvailableClients()
        {
            _availableClients.Clear();
            
            foreach (var client in _mcpClients.clients)
            {
                // Check if the client application appears to be installed
                if (IsClientInstalled(client))
                {
                    // Check current configuration status
                    CheckClientConfiguration(client);
                    _availableClients.Add(client);
                }
            }
            
            // Ensure selected index is valid
            _selectedClientIndex = Mathf.Clamp(_selectedClientIndex, 0, Mathf.Max(0, _availableClients.Count - 1));
        }

        /// <summary>
        /// Check if a client application appears to be installed
        /// </summary>
        private bool IsClientInstalled(McpClient client)
        {
            try
            {
                // For most clients, check if their config directory exists or if they have known installation paths
                switch (client.mcpType)
                {
                    case McpTypes.ClaudeCode:
                        // Check if Claude CLI is available
                        return !string.IsNullOrEmpty(ExecPath.ResolveClaude());
                        
                    case McpTypes.Cursor:
                        // Check if Cursor config directory exists
                        var cursorConfigDir = System.IO.Path.GetDirectoryName(client.windowsConfigPath);
                        return System.IO.Directory.Exists(cursorConfigDir);
                        
                    case McpTypes.VSCode:
                        // Check if VSCode config directory exists
                        var vscodeConfigDir = System.IO.Path.GetDirectoryName(client.windowsConfigPath);
                        return System.IO.Directory.Exists(vscodeConfigDir);
                        
                    case McpTypes.ClaudeDesktop:
                        // Check if Claude Desktop config directory exists
                        var claudeConfigDir = System.IO.Path.GetDirectoryName(client.windowsConfigPath);
                        return System.IO.Directory.Exists(claudeConfigDir);
                        
                    default:
                        // For other clients, assume they might be available
                        return true;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Check the configuration status of a client
        /// </summary>
        private void CheckClientConfiguration(McpClient client)
        {
            try
            {
                // Use the same logic as the main window for checking configuration
                if (client.mcpType == McpTypes.ClaudeCode)
                {
                    CheckClaudeCodeConfiguration(client);
                }
                else
                {
                    CheckStandardClientConfiguration(client);
                }
            }
            catch (Exception ex)
            {
                client.SetStatus(McpStatus.Error, ex.Message);
            }
        }

        /// <summary>
        /// Check Claude Code configuration status
        /// </summary>
        private void CheckClaudeCodeConfiguration(McpClient client)
        {
            string claudePath = ExecPath.ResolveClaude();
            if (string.IsNullOrEmpty(claudePath))
            {
                client.SetStatus(McpStatus.NotConfigured, "Claude CLI not found");
                return;
            }

            // Check if UnityMCP is registered with Claude
            try
            {
                string projectDir = System.IO.Path.GetDirectoryName(Application.dataPath);
                if (ExecPath.TryRun(claudePath, "mcp list", projectDir, out var stdout, out var stderr, 5000))
                {
                    bool isConfigured = (stdout ?? string.Empty).IndexOf("UnityMCP", StringComparison.OrdinalIgnoreCase) >= 0;
                    client.SetStatus(isConfigured ? McpStatus.Configured : McpStatus.NotConfigured);
                }
                else
                {
                    client.SetStatus(McpStatus.NotConfigured, "Could not check Claude configuration");
                }
            }
            catch
            {
                client.SetStatus(McpStatus.NotConfigured, "Error checking Claude configuration");
            }
        }

        /// <summary>
        /// Check standard client configuration status
        /// </summary>
        private void CheckStandardClientConfiguration(McpClient client)
        {
            string configPath = GetClientConfigPath(client);
            
            if (!System.IO.File.Exists(configPath))
            {
                client.SetStatus(McpStatus.NotConfigured);
                return;
            }

            try
            {
                string configJson = System.IO.File.ReadAllText(configPath);
                // Simple check for Unity MCP configuration
                bool hasUnityMCP = configJson.IndexOf("unityMCP", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   configJson.IndexOf("UnityMCP", StringComparison.OrdinalIgnoreCase) >= 0;
                
                client.SetStatus(hasUnityMCP ? McpStatus.Configured : McpStatus.NotConfigured);
            }
            catch
            {
                client.SetStatus(McpStatus.Error, "Error reading configuration file");
            }
        }

        /// <summary>
        /// Get the appropriate config path for a client based on the current OS
        /// </summary>
        private string GetClientConfigPath(McpClient client)
        {
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                return client.windowsConfigPath;
            }
            else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
            {
                return string.IsNullOrEmpty(client.macConfigPath) ? client.linuxConfigPath : client.macConfigPath;
            }
            else
            {
                return client.linuxConfigPath;
            }
        }

        /// <summary>
        /// Draw the client configuration UI for a specific client
        /// </summary>
        private void DrawClientConfigurationUI(McpClient client)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            // Client status
            EditorGUILayout.BeginHorizontal();
            var statusColor = GetStatusColor(client.status);
            var statusText = GetStatusText(client.status);
            
            var originalColor = GUI.color;
            GUI.color = statusColor;
            GUILayout.Label("●", GUILayout.Width(20));
            GUI.color = originalColor;
            
            EditorGUILayout.LabelField($"{client.name}: {statusText}", EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space();
            
            // Configuration actions
            if (client.status == McpStatus.Configured)
            {
                EditorGUILayout.LabelField("✓ Already configured and ready to use!", EditorStyles.label);
                
                if (client.mcpType == McpTypes.ClaudeCode)
                {
                    if (GUILayout.Button("Unregister from Claude Code"))
                    {
                        UnregisterFromClaudeCode(client);
                    }
                }
                else
                {
                    if (GUILayout.Button("Reconfigure"))
                    {
                        ConfigureStandardClient(client);
                    }
                }
            }
            else
            {
                EditorGUILayout.LabelField("Configuration needed to use this AI assistant with Unity.", EditorStyles.label);
                
                if (client.mcpType == McpTypes.ClaudeCode)
                {
                    if (GUILayout.Button("Register with Claude Code"))
                    {
                        RegisterWithClaudeCode(client);
                    }
                }
                else
                {
                    if (GUILayout.Button("Auto-Configure"))
                    {
                        ConfigureStandardClient(client);
                    }
                }
            }
            
            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// Get color for client status
        /// </summary>
        private Color GetStatusColor(McpStatus status)
        {
            return status switch
            {
                McpStatus.Configured => Color.green,
                McpStatus.NotConfigured => Color.yellow,
                McpStatus.Error => Color.red,
                _ => Color.gray
            };
        }

        /// <summary>
        /// Get text for client status
        /// </summary>
        private string GetStatusText(McpStatus status)
        {
            return status switch
            {
                McpStatus.Configured => "Configured",
                McpStatus.NotConfigured => "Not Configured",
                McpStatus.Error => "Error",
                _ => "Unknown"
            };
        }

        /// <summary>
        /// Start automatic configuration of all detected clients
        /// </summary>
        private void StartClientAutoConfiguration()
        {
            _isConfiguringClients = true;
            _clientConfigurationStatus = "Configuring clients...";
            
            int configuredCount = 0;
            int totalCount = _availableClients.Count;
            
            foreach (var client in _availableClients)
            {
                if (client.status != McpStatus.Configured)
                {
                    try
                    {
                        if (client.mcpType == McpTypes.ClaudeCode)
                        {
                            RegisterWithClaudeCode(client);
                        }
                        else
                        {
                            ConfigureStandardClient(client);
                        }
                        
                        // Recheck status
                        CheckClientConfiguration(client);
                        if (client.status == McpStatus.Configured)
                        {
                            configuredCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        McpLog.Error($"Failed to configure {client.name}: {ex.Message}");
                    }
                }
                else
                {
                    configuredCount++;
                }
            }
            
            _isConfiguringClients = false;
            _clientConfigurationStatus = $"Configuration complete! {configuredCount}/{totalCount} clients configured successfully.";
            
            Repaint();
        }

        /// <summary>
        /// Register with Claude Code
        /// </summary>
        private void RegisterWithClaudeCode(McpClient client)
        {
            string claudePath = ExecPath.ResolveClaude();
            if (string.IsNullOrEmpty(claudePath))
            {
                throw new Exception("Claude CLI not found");
            }

            string uvPath = ExecPath.ResolveUv() ?? "uv";
            string pythonDir = FindPackagePythonDirectory();
            
            if (string.IsNullOrEmpty(pythonDir))
            {
                throw new Exception("Python server directory not found");
            }

            string args = $"mcp add UnityMCP -- \"{uvPath}\" run --directory \"{pythonDir}\" server.py";
            string projectDir = System.IO.Path.GetDirectoryName(Application.dataPath);
            
            if (!ExecPath.TryRun(claudePath, args, projectDir, out var stdout, out var stderr, 15000))
            {
                string combined = $"{stdout}\n{stderr}";
                if (combined.IndexOf("already exists", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // Already registered, that's fine
                    return;
                }
                throw new Exception($"Failed to register with Claude Code: {stderr}");
            }
        }

        /// <summary>
        /// Unregister from Claude Code
        /// </summary>
        private void UnregisterFromClaudeCode(McpClient client)
        {
            string claudePath = ExecPath.ResolveClaude();
            if (string.IsNullOrEmpty(claudePath))
            {
                throw new Exception("Claude CLI not found");
            }

            string projectDir = System.IO.Path.GetDirectoryName(Application.dataPath);
            string[] possibleNames = { "UnityMCP", "unityMCP", "unity-mcp", "UnityMcpServer" };
            
            bool success = false;
            foreach (string serverName in possibleNames)
            {
                if (ExecPath.TryRun(claudePath, $"mcp remove {serverName}", projectDir, out var stdout, out var stderr, 10000))
                {
                    success = true;
                    break;
                }
            }
            
            if (!success)
            {
                throw new Exception("Failed to unregister from Claude Code");
            }
        }

        /// <summary>
        /// Configure a standard MCP client
        /// </summary>
        private void ConfigureStandardClient(McpClient client)
        {
            string configPath = GetClientConfigPath(client);
            string pythonDir = FindPackagePythonDirectory();
            
            if (string.IsNullOrEmpty(pythonDir))
            {
                throw new Exception("Python server directory not found");
            }

            // Create directory if it doesn't exist
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(configPath));

            // Use the same configuration logic as the main window
            string result = WriteToConfig(pythonDir, configPath, client);
            if (result != "Configured successfully")
            {
                throw new Exception($"Configuration failed: {result}");
            }
        }

        /// <summary>
        /// Find the package Python directory (simplified version for setup wizard)
        /// </summary>
        private string FindPackagePythonDirectory()
        {
            try
            {
                // Use the server installer to get the server path
                ServerInstaller.EnsureServerInstalled();
                return ServerInstaller.GetServerPath();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Write configuration to client config file (simplified version for setup wizard)
        /// </summary>
        private string WriteToConfig(string pythonDir, string configPath, McpClient client)
        {
            try
            {
                string uvPath = ServerInstaller.FindUvPath();
                if (string.IsNullOrEmpty(uvPath))
                {
                    return "UV package manager not found";
                }

                // Read existing config if it exists
                Newtonsoft.Json.Linq.JObject existingConfig = new Newtonsoft.Json.Linq.JObject();
                if (System.IO.File.Exists(configPath))
                {
                    try
                    {
                        string existingJson = System.IO.File.ReadAllText(configPath);
                        if (!string.IsNullOrWhiteSpace(existingJson))
                        {
                            existingConfig = Newtonsoft.Json.Linq.JObject.Parse(existingJson);
                        }
                    }
                    catch
                    {
                        // If parsing fails, start with empty config
                        existingConfig = new Newtonsoft.Json.Linq.JObject();
                    }
                }

                // Use ConfigJsonBuilder to apply Unity server configuration
                var updatedConfig = ConfigJsonBuilder.ApplyUnityServerToExistingConfig(existingConfig, uvPath, pythonDir, client);
                
                string json = updatedConfig.ToString(Newtonsoft.Json.Formatting.Indented);
                System.IO.File.WriteAllText(configPath, json);
                
                return "Configured successfully";
            }
            catch (Exception ex)
            {
                return $"Configuration failed: {ex.Message}";
            }
        }
    }
}