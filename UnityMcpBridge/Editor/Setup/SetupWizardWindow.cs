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
        private McpClients _mcpClients;
        private int _selectedClientIndex = 0;

        private readonly string[] _stepTitles = {
            "Welcome",
            "Dependency Check",
            "Installation Options",
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
            
            _mcpClients = new McpClients();
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
                case 3: DrawClientConfigurationStep(); break;
                case 4: DrawCompleteStep(); break;
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
                "This wizard will help you set up MCP for Unity to connect AI assistants with your Unity Editor.",
                EditorStyles.wordWrappedLabel
            );
            EditorGUILayout.Space();
            
            EditorGUILayout.LabelField("What is MCP for Unity?", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "MCP for Unity is a bridge that connects AI assistants like Claude Code, Cursor, and VSCode to your Unity Editor, " +
                "allowing them to help you with Unity development tasks directly.",
                EditorStyles.wordWrappedLabel
            );
            EditorGUILayout.Space();
            
            EditorGUILayout.LabelField("Setup Process:", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("1. Check system dependencies (Python & UV)", EditorStyles.label);
            EditorGUILayout.LabelField("2. Get installation guidance if needed", EditorStyles.label);
            EditorGUILayout.LabelField("3. Configure your AI clients", EditorStyles.label);
            EditorGUILayout.LabelField("4. Start using AI assistance in Unity!", EditorStyles.label);
            EditorGUILayout.Space();
            
            EditorGUILayout.HelpBox(
                "This wizard will guide you through each step. You can complete setup at your own pace.",
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
            EditorGUILayout.LabelField("Installation Guide", EditorStyles.boldLabel);
            EditorGUILayout.Space();
            
            var missingDeps = _dependencyResult.GetMissingRequired();
            if (missingDeps.Count == 0)
            {
                EditorGUILayout.HelpBox("All required dependencies are already available!", MessageType.Info);
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("You can proceed to configure your AI clients in the next step.", EditorStyles.wordWrappedLabel);
                return;
            }
            
            EditorGUILayout.LabelField("Missing Dependencies:", EditorStyles.boldLabel);
            foreach (var dep in missingDeps)
            {
                EditorGUILayout.LabelField($"• {dep.Name}", EditorStyles.label);
            }
            EditorGUILayout.Space();
            
            EditorGUILayout.HelpBox(
                "Please install the missing dependencies manually using the instructions below. " +
                "After installation, you can check dependencies again and proceed to client configuration.",
                MessageType.Warning
            );
            EditorGUILayout.Space();
            
            // Manual installation guidance
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Installation Instructions", EditorStyles.boldLabel);
            
            var recommendations = DependencyManager.GetInstallationRecommendations();
            EditorGUILayout.LabelField(recommendations, EditorStyles.wordWrappedLabel);
            
            EditorGUILayout.Space();
            if (GUILayout.Button("Open Installation URLs", GUILayout.Height(30)))
            {
                OpenInstallationUrls();
            }
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.Space();
            if (GUILayout.Button("Check Dependencies Again", GUILayout.Height(30)))
            {
                _dependencyResult = DependencyManager.CheckAllDependencies();
                Repaint();
            }
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
                        _currentStep = 4; // Go to complete step
                    }
                }
            }
        }

        private void DrawCompleteStep()
        {
            EditorGUILayout.LabelField("Setup Complete!", EditorStyles.boldLabel);
            EditorGUILayout.Space();
            
            // Refresh dependency check for final status
            _dependencyResult = DependencyManager.CheckAllDependencies();
            
            if (_dependencyResult.IsSystemReady)
            {
                EditorGUILayout.HelpBox(
                    "🎉 Congratulations! MCP for Unity is now fully set up and ready to use.",
                    MessageType.Info
                );
                
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("What's been configured:", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("✓ Python and UV dependencies verified", EditorStyles.label);
                EditorGUILayout.LabelField("✓ MCP server ready", EditorStyles.label);
                EditorGUILayout.LabelField("✓ AI client configuration completed", EditorStyles.label);
                
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("You can now:", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("• Ask your AI assistant to help with Unity development", EditorStyles.label);
                EditorGUILayout.LabelField("• Use natural language to control Unity Editor", EditorStyles.label);
                EditorGUILayout.LabelField("• Get AI assistance with scripts, scenes, and assets", EditorStyles.label);
                
                EditorGUILayout.Space();
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Open Documentation"))
                {
                    Application.OpenURL("https://github.com/CoplayDev/unity-mcp");
                }
                if (GUILayout.Button("Open Client Configuration"))
                {
                    Windows.MCPForUnityEditorWindow.ShowWindow();
                }
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Setup incomplete. Some dependencies may still be missing. Please review the previous steps.",
                    MessageType.Warning
                );
                
                EditorGUILayout.Space();
                if (GUILayout.Button("Go Back to Dependency Check"))
                {
                    _currentStep = 1;
                }
            }
        }

        private void DrawClientConfigurationStep()
        {
            EditorGUILayout.LabelField("AI Client Configuration", EditorStyles.boldLabel);
            EditorGUILayout.Space();
            
            EditorGUILayout.LabelField(
                "Configure your AI assistants (Claude Desktop, Cursor, VSCode, etc.) to connect with MCP for Unity.",
                EditorStyles.wordWrappedLabel
            );
            EditorGUILayout.Space();
            
            // Check if dependencies are ready first
            if (!_dependencyResult.IsSystemReady)
            {
                EditorGUILayout.HelpBox(
                    "Dependencies are not fully installed yet. Please complete dependency installation before configuring clients.",
                    MessageType.Warning
                );
                
                if (GUILayout.Button("Go Back to Check Dependencies"))
                {
                    _currentStep = 1; // Go back to dependency check
                    _dependencyResult = DependencyManager.CheckAllDependencies();
                }
                return;
            }
            
            // Show available clients
            EditorGUILayout.LabelField("Available AI Clients:", EditorStyles.boldLabel);
            EditorGUILayout.Space();
            
            // Client selector
            if (_mcpClients.clients.Count > 0)
            {
                string[] clientNames = _mcpClients.clients.Select(c => c.name).ToArray();
                _selectedClientIndex = EditorGUILayout.Popup("Select Client", _selectedClientIndex, clientNames);
                _selectedClientIndex = Mathf.Clamp(_selectedClientIndex, 0, _mcpClients.clients.Count - 1);
                
                EditorGUILayout.Space();
                
                var selectedClient = _mcpClients.clients[_selectedClientIndex];
                DrawClientConfigurationPanel(selectedClient);
            }
            
            EditorGUILayout.Space();
            
            // Batch configuration option
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Quick Setup", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Automatically configure all detected AI clients at once.",
                EditorStyles.wordWrappedLabel
            );
            EditorGUILayout.Space();
            
            if (GUILayout.Button("Auto-Configure All Detected Clients", GUILayout.Height(30)))
            {
                ConfigureAllClients();
            }
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "After configuration, restart your AI client for changes to take effect.",
                MessageType.Info
            );
        }

        private void DrawClientConfigurationPanel(McpClient client)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            EditorGUILayout.LabelField($"{client.name} Configuration", EditorStyles.boldLabel);
            EditorGUILayout.Space();
            
            // Show client status
            var statusColor = GetClientStatusColor(client);
            var originalColor = GUI.color;
            GUI.color = statusColor;
            EditorGUILayout.LabelField($"Status: {client.configStatus}", EditorStyles.label);
            GUI.color = originalColor;
            
            EditorGUILayout.Space();
            
            // Configuration button
            if (GUILayout.Button($"Configure {client.name}", GUILayout.Height(25)))
            {
                ConfigureClient(client);
            }
            
            // Manual setup option
            if (client.mcpType != McpTypes.ClaudeCode)
            {
                if (GUILayout.Button("Show Manual Setup Instructions", GUILayout.Height(25)))
                {
                    ShowManualClientSetup(client);
                }
            }
            
            EditorGUILayout.EndVertical();
        }

        private Color GetClientStatusColor(McpClient client)
        {
            return client.status switch
            {
                McpStatus.Configured => Color.green,
                McpStatus.Running => Color.green,
                McpStatus.Connected => Color.green,
                McpStatus.IncorrectPath => Color.yellow,
                McpStatus.CommunicationError => Color.yellow,
                McpStatus.NoResponse => Color.yellow,
                _ => Color.red
            };
        }

        private void ConfigureClient(McpClient client)
        {
            try
            {
                EditorUtility.DisplayDialog(
                    "Client Configuration",
                    $"To configure {client.name}, please:\n\n" +
                    "1. Open the MCP Client Configuration window from Window > MCP for Unity > MCP Client Configuration\n" +
                    "2. Select your client and click 'Auto Configure'\n" +
                    "3. Follow any manual setup instructions if needed",
                    "Open Configuration Window",
                    "OK"
                );
                
                // Open the main MCP window
                Windows.MCPForUnityEditorWindow.ShowWindow();
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog(
                    "Configuration Error",
                    $"Failed to open configuration window: {ex.Message}",
                    "OK"
                );
            }
        }

        private void ConfigureAllClients()
        {
            bool openWindow = EditorUtility.DisplayDialog(
                "Auto-Configure All Clients",
                "This will open the MCP Client Configuration window where you can configure all detected AI clients.\n\n" +
                "Would you like to continue?",
                "Open Configuration Window",
                "Cancel"
            );
            
            if (openWindow)
            {
                // Open the main MCP window
                Windows.MCPForUnityEditorWindow.ShowWindow();
            }
        }

        private void ShowManualClientSetup(McpClient client)
        {
            EditorUtility.DisplayDialog(
                "Manual Setup Instructions",
                $"For manual setup of {client.name}:\n\n" +
                "1. Open Window > MCP for Unity > MCP Client Configuration\n" +
                "2. Select your client and click 'Manual Setup'\n" +
                "3. Follow the detailed configuration instructions",
                "Open Configuration Window",
                "OK"
            );
            
            // Open the main MCP window
            Windows.MCPForUnityEditorWindow.ShowWindow();
        }

        private void DrawFooter()
        {
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            
            // Back button
            GUI.enabled = _currentStep > 0;
            if (GUILayout.Button("Back"))
            {
                _currentStep--;
            }
            
            GUILayout.FlexibleSpace();
            
            // Skip/Dismiss button
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
            string nextButtonText = _currentStep == _stepTitles.Length - 1 ? "Finish" : "Next";
            
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
                    _currentStep++;
                }
            }
            
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
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
    }
}