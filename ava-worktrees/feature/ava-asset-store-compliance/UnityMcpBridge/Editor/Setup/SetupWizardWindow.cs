using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Dependencies;
using MCPForUnity.Editor.Dependencies.Models;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Installation;
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

        private readonly string[] _stepTitles = {
            "Welcome",
            "Dependency Check",
            "Installation Options",
            "Installation Progress",
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
            
            EditorGUILayout.LabelField("Required Dependencies:", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("• Python 3.10 or later", EditorStyles.label);
            EditorGUILayout.LabelField("• UV package manager", EditorStyles.label);
            EditorGUILayout.Space();
            
            EditorGUILayout.HelpBox(
                "This wizard will check for these dependencies and guide you through installation if needed.",
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
                        _currentStep = 4; // Go to complete step
                    }
                }
            }
        }

        private void DrawCompleteStep()
        {
            EditorGUILayout.LabelField("Setup Complete!", EditorStyles.boldLabel);
            EditorGUILayout.Space();
            
            if (_dependencyResult.IsSystemReady)
            {
                EditorGUILayout.HelpBox(
                    "✓ All dependencies are now available! MCP for Unity is ready to use.",
                    MessageType.Info
                );
                
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Next Steps:", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("1. Configure your AI assistant (Claude Desktop, Cursor, etc.)", EditorStyles.label);
                EditorGUILayout.LabelField("2. Add MCP for Unity to your AI assistant's configuration", EditorStyles.label);
                EditorGUILayout.LabelField("3. Start using AI assistance in Unity!", EditorStyles.label);
                
                EditorGUILayout.Space();
                if (GUILayout.Button("Open Documentation"))
                {
                    Application.OpenURL("https://github.com/CoplayDev/unity-mcp");
                }
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Some dependencies are still missing. Please install them manually or try the automatic installation again.",
                    MessageType.Warning
                );
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
            GUI.enabled = !_isInstalling;
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
                    _currentStep = 4; // Go to complete step
                }
            }
            
            Repaint();
        }
    }
}