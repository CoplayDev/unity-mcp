using System;
using System.Linq;
using MCPForUnity.Editor.Data;
using MCPForUnity.Editor.Dependencies;
using MCPForUnity.Editor.Dependencies.Models;
using MCPForUnity.Editor.Helpers;
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

        private readonly string[] _stepTitles = {
            "Setup",
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
        }

        private void OnGUI()
        {
            DrawHeader();
            DrawProgressBar();

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            switch (_currentStep)
            {
                case 0: DrawSetupStep(); break;
                case 1: DrawCompleteStep(); break;
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

        private void DrawSetupStep()
        {
            // Welcome section
            DrawSectionTitle("MCP for Unity Setup");

            EditorGUILayout.LabelField(
                "This wizard will help you set up MCP for Unity to connect AI assistants with your Unity Editor.",
                EditorStyles.wordWrappedLabel
            );
            EditorGUILayout.Space();

            // Dependency check section
            EditorGUILayout.BeginHorizontal();
            DrawSectionTitle("System Check", 14);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Refresh", GUILayout.Width(60), GUILayout.Height(20)))
            {
                _dependencyResult = DependencyManager.CheckAllDependencies();
            }
            EditorGUILayout.EndHorizontal();

            // Show simplified dependency status
            foreach (var dep in _dependencyResult.Dependencies)
            {
                DrawSimpleDependencyStatus(dep);
            }

            // Overall status and installation guidance
            EditorGUILayout.Space();
            if (!_dependencyResult.IsSystemReady)
            {
                // Only show critical warnings when dependencies are actually missing
                EditorGUILayout.HelpBox(
                    "\u26A0 Missing Dependencies: MCP for Unity requires Python 3.10+ and UV package manager to function properly.",
                    MessageType.Warning
                );

                EditorGUILayout.Space();
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                DrawErrorStatus("Installation Required");

                var recommendations = DependencyManager.GetInstallationRecommendations();
                EditorGUILayout.LabelField(recommendations, EditorStyles.wordWrappedLabel);

                EditorGUILayout.Space();
                if (GUILayout.Button("Open Installation Links", GUILayout.Height(25)))
                {
                    OpenInstallationUrls();
                }
                EditorGUILayout.EndVertical();
            }
            else
            {
                DrawSuccessStatus("System Ready");
                EditorGUILayout.LabelField("All requirements are met. You can proceed to configure your AI clients.", EditorStyles.wordWrappedLabel);
            }
        }

        private void DrawCompleteStep()
        {
            DrawSectionTitle("Setup Complete");

            // Refresh dependency check with caching to avoid heavy operations on every repaint
            if (_dependencyResult == null || (DateTime.UtcNow - _dependencyResult.CheckedAt).TotalSeconds > 2)
            {
                _dependencyResult = DependencyManager.CheckAllDependencies();
            }

            if (_dependencyResult.IsSystemReady)
            {
                DrawSuccessStatus("MCP for Unity Ready!");

                EditorGUILayout.HelpBox(
                    "🎉 MCP for Unity is now set up and ready to use!\n\n" +
                    "• Dependencies verified\n" +
                    "• MCP server ready\n" +
                    "• Client configuration accessible",
                    MessageType.Info
                );

                EditorGUILayout.Space();
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Documentation", GUILayout.Height(30)))
                {
                    Application.OpenURL("https://github.com/CoplayDev/unity-mcp");
                }
                if (GUILayout.Button("Client Settings", GUILayout.Height(30)))
                {
                    Windows.MCPForUnityEditorWindow.ShowWindow();
                }
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                DrawErrorStatus("Setup Incomplete - Package Non-Functional");

                EditorGUILayout.HelpBox(
                    "🚨 MCP for Unity CANNOT work - dependencies still missing!\n\n" +
                    "Install ALL required dependencies before the package will function.",
                    MessageType.Error
                );

                var missingDeps = _dependencyResult.GetMissingRequired();
                if (missingDeps.Count > 0)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Still Missing:", EditorStyles.boldLabel);
                    foreach (var dep in missingDeps)
                    {
                        EditorGUILayout.LabelField($"✗ {dep.Name}", EditorStyles.label);
                    }
                }

                EditorGUILayout.Space();
                if (GUILayout.Button("Go Back to Setup", GUILayout.Height(30)))
                {
                    _currentStep = 0;
                }
            }
        }

        // Helper methods for consistent UI components
        private void DrawSectionTitle(string title, int fontSize = 16)
        {
            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = fontSize,
                fontStyle = FontStyle.Bold
            };
            EditorGUILayout.LabelField(title, titleStyle);
            EditorGUILayout.Space();
        }

        private void DrawSuccessStatus(string message)
        {
            var originalColor = GUI.color;
            GUI.color = Color.green;
            EditorGUILayout.LabelField($"✓ {message}", EditorStyles.boldLabel);
            GUI.color = originalColor;
            EditorGUILayout.Space();
        }

        private void DrawErrorStatus(string message)
        {
            var originalColor = GUI.color;
            GUI.color = Color.red;
            EditorGUILayout.LabelField($"✗ {message}", EditorStyles.boldLabel);
            GUI.color = originalColor;
            EditorGUILayout.Space();
        }

        private void DrawSimpleDependencyStatus(DependencyStatus dep)
        {
            EditorGUILayout.BeginHorizontal();

            var statusIcon = dep.IsAvailable ? "✓" : "✗";
            var statusColor = dep.IsAvailable ? Color.green : Color.red;

            var originalColor = GUI.color;
            GUI.color = statusColor;
            GUILayout.Label(statusIcon, GUILayout.Width(20));
            EditorGUILayout.LabelField(dep.Name, EditorStyles.boldLabel);
            GUI.color = originalColor;

            if (!dep.IsAvailable && !string.IsNullOrEmpty(dep.ErrorMessage))
            {
                EditorGUILayout.LabelField($"({dep.ErrorMessage})", EditorStyles.miniLabel);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawFooter()
        {
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();

            // Back button
            GUI.enabled = _currentStep > 0;
            if (GUILayout.Button("Back", GUILayout.Width(60)))
            {
                _currentStep--;
            }

            GUILayout.FlexibleSpace();

            // Skip button
            if (GUILayout.Button("Skip", GUILayout.Width(60)))
            {
                bool dismiss = EditorUtility.DisplayDialog(
                    "Skip Setup",
                    "\u26A0 Skipping setup will leave MCP for Unity non-functional!\n\n" +
                    "You can restart setup from: Window > MCP for Unity > Setup Wizard (Required)",
                    "Skip Anyway",
                    "Cancel"
                );

                if (dismiss)
                {
                    SetupWizard.MarkSetupDismissed();
                    Close();
                }
            }

            // Next/Done button
            GUI.enabled = true;
            string buttonText = _currentStep == _stepTitles.Length - 1 ? "Done" : "Next";

            if (GUILayout.Button(buttonText, GUILayout.Width(80)))
            {
                if (_currentStep == _stepTitles.Length - 1)
                {
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
