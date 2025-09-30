using System;
using System.Linq;
using MCPForUnity.Editor.Dependencies;
using MCPForUnity.Editor.Dependencies.Models;
using MCPForUnity.Editor.Helpers;
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
            "Setup",
            "Configure",
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

            // Check client configurations on startup
            foreach (var client in _mcpClients.clients)
            {
                CheckClientConfiguration(client);
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
                case 1: DrawConfigureStep(); break;
                case 2: DrawCompleteStep(); break;
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
                    "⚠️ Missing Dependencies: MCP for Unity requires Python 3.10+ and UV package manager to function properly.",
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

        private void DrawConfigureStep()
        {
            DrawSectionTitle("AI Client Configuration");

            // Check dependencies first (with caching to avoid heavy operations on every repaint)
            if (_dependencyResult == null || (DateTime.UtcNow - _dependencyResult.CheckedAt).TotalSeconds > 2)
            {
                _dependencyResult = DependencyManager.CheckAllDependencies();
            }
            if (!_dependencyResult.IsSystemReady)
            {
                DrawErrorStatus("Cannot Configure - System Requirements Not Met");

                EditorGUILayout.HelpBox(
                    "Client configuration requires system dependencies to be installed first. Please complete setup before proceeding.",
                    MessageType.Warning
                );

                if (GUILayout.Button("Go Back to Setup", GUILayout.Height(30)))
                {
                    _currentStep = 0;
                }
                return;
            }

            EditorGUILayout.LabelField(
                "Configure your AI assistants to work with Unity. Select a client below to set it up:",
                EditorStyles.wordWrappedLabel
            );
            EditorGUILayout.Space();

            // Client selection and configuration
            if (_mcpClients.clients.Count > 0)
            {
                // Client selector dropdown
                string[] clientNames = _mcpClients.clients.Select(c => c.name).ToArray();
                EditorGUI.BeginChangeCheck();
                _selectedClientIndex = EditorGUILayout.Popup("Select AI Client:", _selectedClientIndex, clientNames);
                if (EditorGUI.EndChangeCheck())
                {
                    _selectedClientIndex = Mathf.Clamp(_selectedClientIndex, 0, _mcpClients.clients.Count - 1);
                    // Refresh client status when selection changes
                    CheckClientConfiguration(_mcpClients.clients[_selectedClientIndex]);
                }

                EditorGUILayout.Space();

                var selectedClient = _mcpClients.clients[_selectedClientIndex];
                DrawClientConfigurationInWizard(selectedClient);

                EditorGUILayout.Space();

                // Batch configuration option
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("Quick Setup", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "Automatically configure all detected AI clients at once:",
                    EditorStyles.wordWrappedLabel
                );
                EditorGUILayout.Space();

                if (GUILayout.Button("Configure All Detected Clients", GUILayout.Height(30)))
                {
                    ConfigureAllClientsInWizard();
                }
                EditorGUILayout.EndVertical();
            }
            else
            {
                EditorGUILayout.HelpBox("No AI clients detected. Make sure you have Claude Code, Cursor, or VSCode installed.", MessageType.Info);
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "💡 You might need to restart your AI client after configuring.",
                MessageType.Info
            );
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
                    "⚠️ Skipping setup will leave MCP for Unity non-functional!\n\n" +
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

        private void DrawClientConfigurationInWizard(McpClient client)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField($"{client.name} Configuration", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // Show current status
            var statusColor = GetClientStatusColor(client);
            var originalColor = GUI.color;
            GUI.color = statusColor;
            EditorGUILayout.LabelField($"Status: {client.configStatus}", EditorStyles.label);
            GUI.color = originalColor;

            EditorGUILayout.Space();

            // Configuration buttons
            EditorGUILayout.BeginHorizontal();

            if (client.mcpType == McpTypes.ClaudeCode)
            {
                // Special handling for Claude Code
                bool claudeAvailable = !string.IsNullOrEmpty(ExecPath.ResolveClaude());
                if (claudeAvailable)
                {
                    bool isConfigured = client.status == McpStatus.Configured;
                    string buttonText = isConfigured ? "Unregister" : "Register";
                    if (GUILayout.Button($"{buttonText} with Claude Code"))
                    {
                        if (isConfigured)
                        {
                            UnregisterFromClaudeCode(client);
                        }
                        else
                        {
                            RegisterWithClaudeCode(client);
                        }
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("Claude Code not found. Please install Claude Code first.", MessageType.Warning);
                    if (GUILayout.Button("Open Claude Code Website"))
                    {
                        Application.OpenURL("https://claude.ai/download");
                    }
                }
            }
            else
            {
                // Standard client configuration
                if (GUILayout.Button($"Configure {client.name}"))
                {
                    ConfigureClientInWizard(client);
                }

                if (GUILayout.Button("Manual Setup"))
                {
                    ShowManualSetupInWizard(client);
                }
            }

            EditorGUILayout.EndHorizontal();
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

        private void ConfigureClientInWizard(McpClient client)
        {
            try
            {
                string result = PerformClientConfiguration(client);

                EditorUtility.DisplayDialog(
                    $"{client.name} Configuration",
                    result,
                    "OK"
                );

                // Refresh client status
                CheckClientConfiguration(client);
                Repaint();
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog(
                    "Configuration Error",
                    $"Failed to configure {client.name}: {ex.Message}",
                    "OK"
                );
            }
        }

        private void ConfigureAllClientsInWizard()
        {
            int successCount = 0;
            int totalCount = _mcpClients.clients.Count;

            foreach (var client in _mcpClients.clients)
            {
                try
                {
                    if (client.mcpType == McpTypes.ClaudeCode)
                    {
                        if (!string.IsNullOrEmpty(ExecPath.ResolveClaude()) && client.status != McpStatus.Configured)
                        {
                            RegisterWithClaudeCode(client);
                            successCount++;
                        }
                        else if (client.status == McpStatus.Configured)
                        {
                            successCount++; // Already configured
                        }
                    }
                    else
                    {
                        string result = PerformClientConfiguration(client);
                        if (result.Contains("success", System.StringComparison.OrdinalIgnoreCase))
                        {
                            successCount++;
                        }
                    }

                    CheckClientConfiguration(client);
                }
                catch (System.Exception ex)
                {
                    McpLog.Error($"Failed to configure {client.name}: {ex.Message}");
                }
            }

            EditorUtility.DisplayDialog(
                "Batch Configuration Complete",
                $"Successfully configured {successCount} out of {totalCount} clients.\n\n" +
                "Restart your AI clients for changes to take effect.",
                "OK"
            );

            Repaint();
        }

        private void RegisterWithClaudeCode(McpClient client)
        {
            try
            {
                string pythonDir = FindPackagePythonDirectory();
                string claudePath = ExecPath.ResolveClaude();
                string uvPath = ExecPath.ResolveUv() ?? "uv";

                string args = $"mcp add UnityMCP -- \"{uvPath}\" run --directory \"{pythonDir}\" server.py";

                if (!ExecPath.TryRun(claudePath, args, null, out var stdout, out var stderr, 15000, GetPathPrepend()))
                {
                    if ((stdout + stderr).Contains("already exists", System.StringComparison.OrdinalIgnoreCase))
                    {
                        CheckClientConfiguration(client);
                        EditorUtility.DisplayDialog("Claude Code", "MCP for Unity is already registered with Claude Code.", "OK");
                    }
                    else
                    {
                        throw new System.Exception($"Registration failed: {stderr}");
                    }
                }
                else
                {
                    CheckClientConfiguration(client);
                    EditorUtility.DisplayDialog("Claude Code", "Successfully registered MCP for Unity with Claude Code!", "OK");
                }
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("Registration Error", $"Failed to register with Claude Code: {ex.Message}", "OK");
            }
        }

        private void UnregisterFromClaudeCode(McpClient client)
        {
            try
            {
                string claudePath = ExecPath.ResolveClaude();
                if (ExecPath.TryRun(claudePath, "mcp remove UnityMCP", null, out var stdout, out var stderr, 10000, GetPathPrepend()))
                {
                    CheckClientConfiguration(client);
                    EditorUtility.DisplayDialog("Claude Code", "Successfully unregistered MCP for Unity from Claude Code.", "OK");
                }
                else
                {
                    throw new System.Exception($"Unregistration failed: {stderr}");
                }
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("Unregistration Error", $"Failed to unregister from Claude Code: {ex.Message}", "OK");
            }
        }

        private string PerformClientConfiguration(McpClient client)
        {
            // This mirrors the logic from MCPForUnityEditorWindow.ConfigureMcpClient
            string configPath = GetConfigPath(client);
            string pythonDir = FindPackagePythonDirectory();

            if (string.IsNullOrEmpty(pythonDir))
            {
                return "Manual configuration required - Python server directory not found.";
            }

            return WriteToConfig(pythonDir, configPath, client);
        }

        private void ShowManualSetupInWizard(McpClient client)
        {
            string configPath = GetConfigPath(client);
            string pythonDir = FindPackagePythonDirectory();
            string uvPath = ServerInstaller.FindUvPath();

            if (string.IsNullOrEmpty(uvPath))
            {
                EditorUtility.DisplayDialog("Manual Setup", "UV package manager not found. Please install UV first.", "OK");
                return;
            }

            string manualConfig = BuildManualConfig(client, uvPath, pythonDir);

            EditorUtility.DisplayDialog(
                $"Manual Setup - {client.name}",
                $"Configuration file location:\n{configPath}\n\n" +
                $"Add this configuration:\n\n{manualConfig}\n\n" +
                "After adding the configuration, restart your AI client.",
                "OK"
            );
        }

        private string GetConfigPath(McpClient client)
        {
            if (Application.platform == RuntimePlatform.WindowsEditor)
                return client.windowsConfigPath;
            else if (Application.platform == RuntimePlatform.OSXEditor)
                return string.IsNullOrEmpty(client.macConfigPath) ? client.linuxConfigPath : client.macConfigPath;
            else
                return client.linuxConfigPath;
        }

        private string FindPackagePythonDirectory()
        {
            // This should use the same logic as the main MCP window
            try
            {
                ServerInstaller.EnsureServerInstalled();
                return ServerInstaller.GetServerPath();
            }
            catch
            {
                return null;
            }
        }

        private string GetPathPrepend()
        {
            if (Application.platform == RuntimePlatform.OSXEditor)
                return "/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin";
            else if (Application.platform == RuntimePlatform.LinuxEditor)
                return "/usr/local/bin:/usr/bin:/bin";
            return null;
        }

        private void CheckClientConfiguration(McpClient client)
        {
            // Basic status check - could be enhanced to mirror MCPForUnityEditorWindow logic
            try
            {
                string configPath = GetConfigPath(client);
                if (System.IO.File.Exists(configPath))
                {
                    client.configStatus = "Configured";
                    client.status = McpStatus.Configured;
                }
                else
                {
                    client.configStatus = "Not Configured";
                    client.status = McpStatus.NotConfigured;
                }
            }
            catch
            {
                client.configStatus = "Error";
                client.status = McpStatus.Error;
            }
        }

        private string WriteToConfig(string pythonDir, string configPath, McpClient client)
        {
            // Simplified version of the config writing logic
            try
            {
                string uvPath = ServerInstaller.FindUvPath();
                if (string.IsNullOrEmpty(uvPath))
                {
                    return "UV package manager not found. Please install UV first.";
                }

                // Create directory if it doesn't exist
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(configPath));

                // Build configuration JSON
                string configJson = BuildClientConfig(client, uvPath, pythonDir);

                // Write configuration
                System.IO.File.WriteAllText(configPath, configJson);

                return "Configuration successful!";
            }
            catch (System.Exception ex)
            {
                return $"Configuration failed: {ex.Message}";
            }
        }

        private string BuildClientConfig(McpClient client, string uvPath, string pythonDir)
        {
            // Build appropriate JSON configuration based on client type
            if (client.mcpType == McpTypes.VSCode)
            {
                var config = new
                {
                    servers = new
                    {
                        unityMCP = new
                        {
                            command = uvPath,
                            args = new[] { "run", "--directory", pythonDir, "server.py" }
                        }
                    }
                };
                return Newtonsoft.Json.JsonConvert.SerializeObject(config, Newtonsoft.Json.Formatting.Indented);
            }
            else
            {
                // Default configuration for other clients
                var config = new
                {
                    mcpServers = new
                    {
                        unityMCP = new
                        {
                            command = uvPath,
                            args = new[] { "run", "--directory", pythonDir, "server.py" }
                        }
                    }
                };
                return Newtonsoft.Json.JsonConvert.SerializeObject(config, Newtonsoft.Json.Formatting.Indented);
            }
        }

        private string BuildManualConfig(McpClient client, string uvPath, string pythonDir)
        {
            return BuildClientConfig(client, uvPath, pythonDir);
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
