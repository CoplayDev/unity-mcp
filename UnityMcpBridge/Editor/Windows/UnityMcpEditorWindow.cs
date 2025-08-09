using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Net.Sockets;
using System.Net;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityMcpBridge.Editor.Data;
using UnityMcpBridge.Editor.Helpers;
using UnityMcpBridge.Editor.Models;

namespace UnityMcpBridge.Editor.Windows
{
    public class UnityMcpEditorWindow : EditorWindow
    {
        private bool isUnityBridgeRunning = false;
        private Vector2 scrollPosition;
        private string pythonServerInstallationStatus = "Not Installed";
        private Color pythonServerInstallationStatusColor = Color.red;
        private const int mcpPort = 6500; // MCP port (still hardcoded for MCP server)
        private readonly McpClients mcpClients = new();
        private bool autoRegisterEnabled;
        private bool lastClientRegisteredOk;
        private bool lastBridgeVerifiedOk;
        private string pythonDirOverride = null;
        private bool debugLogsEnabled;
        
        // Script validation settings
        private int validationLevelIndex = 1; // Default to Standard
        private readonly string[] validationLevelOptions = new string[]
        {
            "Basic - Only syntax checks",
            "Standard - Syntax + Unity practices", 
            "Comprehensive - All checks + semantic analysis",
            "Strict - Full semantic validation (requires Roslyn)"
        };
        
        // UI state
        private int selectedClientIndex = 0;

        [MenuItem("Window/Unity MCP")]
        public static void ShowWindow()
        {
            GetWindow<UnityMcpEditorWindow>("MCP Editor");
        }

        private void OnEnable()
        {
            UpdatePythonServerInstallationStatus();

            // Refresh bridge status
            isUnityBridgeRunning = UnityMcpBridge.IsRunning;
            autoRegisterEnabled = EditorPrefs.GetBool("UnityMCP.AutoRegisterEnabled", true);
            debugLogsEnabled = EditorPrefs.GetBool("UnityMCP.DebugLogs", false);
            foreach (McpClient mcpClient in mcpClients.clients)
            {
                CheckMcpConfiguration(mcpClient);
            }
            
            // Load validation level setting
            LoadValidationLevelSetting();

            // First-run auto-setup (register client(s) and ensure bridge is listening)
            if (autoRegisterEnabled)
            {
                AutoFirstRunSetup();
            }
        }
        
        private void OnFocus()
        {
            // Refresh bridge running state on focus in case initialization completed after domain reload
            isUnityBridgeRunning = UnityMcpBridge.IsRunning;
            if (mcpClients.clients.Count > 0 && selectedClientIndex < mcpClients.clients.Count)
            {
                McpClient selectedClient = mcpClients.clients[selectedClientIndex];
                CheckMcpConfiguration(selectedClient);
            }
            Repaint();
        }

        private Color GetStatusColor(McpStatus status)
        {
            // Return appropriate color based on the status enum
            return status switch
            {
                McpStatus.Configured => Color.green,
                McpStatus.Running => Color.green,
                McpStatus.Connected => Color.green,
                McpStatus.IncorrectPath => Color.yellow,
                McpStatus.CommunicationError => Color.yellow,
                McpStatus.NoResponse => Color.yellow,
                _ => Color.red, // Default to red for error states or not configured
            };
        }

        private void UpdatePythonServerInstallationStatus()
        {
            try
            {
                string installedPath = ServerInstaller.GetServerPath();
                bool installedOk = !string.IsNullOrEmpty(installedPath) && File.Exists(Path.Combine(installedPath, "server.py"));
                if (installedOk)
                {
                    pythonServerInstallationStatus = "Installed";
                    pythonServerInstallationStatusColor = Color.green;
                    return;
                }

                // Fall back to embedded/dev source via our existing resolution logic
                string embeddedPath = FindPackagePythonDirectory();
                bool embeddedOk = !string.IsNullOrEmpty(embeddedPath) && File.Exists(Path.Combine(embeddedPath, "server.py"));
                if (embeddedOk)
                {
                    pythonServerInstallationStatus = "Installed (Embedded)";
                    pythonServerInstallationStatusColor = Color.green;
                }
                else
                {
                    pythonServerInstallationStatus = "Not Installed";
                    pythonServerInstallationStatusColor = Color.red;
                }
            }
            catch
            {
                pythonServerInstallationStatus = "Not Installed";
                pythonServerInstallationStatusColor = Color.red;
            }
        }


        private void DrawStatusDot(Rect statusRect, Color statusColor, float size = 12)
        {
            float offsetX = (statusRect.width - size) / 2;
            float offsetY = (statusRect.height - size) / 2;
            Rect dotRect = new(statusRect.x + offsetX, statusRect.y + offsetY, size, size);
            Vector3 center = new(
                dotRect.x + (dotRect.width / 2),
                dotRect.y + (dotRect.height / 2),
                0
            );
            float radius = size / 2;

            // Draw the main dot
            Handles.color = statusColor;
            Handles.DrawSolidDisc(center, Vector3.forward, radius);

            // Draw the border
            Color borderColor = new(
                statusColor.r * 0.7f,
                statusColor.g * 0.7f,
                statusColor.b * 0.7f
            );
            Handles.color = borderColor;
            Handles.DrawWireDisc(center, Vector3.forward, radius);
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            // Header
            DrawHeader();
            
            // Compute equal column widths for uniform layout
            float horizontalSpacing = 2f;
            float outerPadding = 20f; // approximate padding
            // Make columns a bit less wide for a tighter layout
            float computed = (position.width - outerPadding - horizontalSpacing) / 2f;
            float colWidth = Mathf.Clamp(computed, 220f, 340f);
            // Use fixed heights per row so paired panels match exactly
            float topPanelHeight = 190f;
            float bottomPanelHeight = 230f;

            // Top row: Server Status (left) and Unity Bridge (right)
            EditorGUILayout.BeginHorizontal();
            {
                EditorGUILayout.BeginVertical(GUILayout.Width(colWidth), GUILayout.Height(topPanelHeight));
                DrawServerStatusSection();
                EditorGUILayout.EndVertical();

                EditorGUILayout.Space(horizontalSpacing);

                EditorGUILayout.BeginVertical(GUILayout.Width(colWidth), GUILayout.Height(topPanelHeight));
                DrawBridgeSection();
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            // Second row: MCP Client Configuration (left) and Script Validation (right)
            EditorGUILayout.BeginHorizontal();
            {
                EditorGUILayout.BeginVertical(GUILayout.Width(colWidth), GUILayout.Height(bottomPanelHeight));
                DrawUnifiedClientConfiguration();
                EditorGUILayout.EndVertical();

                EditorGUILayout.Space(horizontalSpacing);

                EditorGUILayout.BeginVertical(GUILayout.Width(colWidth), GUILayout.Height(bottomPanelHeight));
                DrawValidationSection();
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndHorizontal();

            // Minimal bottom padding
            EditorGUILayout.Space(2);

            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            EditorGUILayout.Space(15);
            Rect titleRect = EditorGUILayout.GetControlRect(false, 40);
            EditorGUI.DrawRect(titleRect, new Color(0.2f, 0.2f, 0.2f, 0.1f));
            
            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleLeft
            };
            
            GUI.Label(
                new Rect(titleRect.x + 15, titleRect.y + 8, titleRect.width - 30, titleRect.height),
                "Unity MCP Editor",
                titleStyle
            );

            // Place the Show Debug Logs toggle on the same header row, right-aligned
            float toggleWidth = 160f;
            Rect toggleRect = new Rect(titleRect.xMax - toggleWidth - 12f, titleRect.y + 10f, toggleWidth, 20f);
            bool newDebug = GUI.Toggle(toggleRect, debugLogsEnabled, "Show Debug Logs");
            if (newDebug != debugLogsEnabled)
            {
                debugLogsEnabled = newDebug;
                EditorPrefs.SetBool("UnityMCP.DebugLogs", debugLogsEnabled);
            }
            EditorGUILayout.Space(15);
        }

        private void DrawServerStatusSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            GUIStyle sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14
            };
            EditorGUILayout.LabelField("Server Status", sectionTitleStyle);
            EditorGUILayout.Space(8);

            EditorGUILayout.BeginHorizontal();
            Rect statusRect = GUILayoutUtility.GetRect(0, 28, GUILayout.Width(24));
            DrawStatusDot(statusRect, pythonServerInstallationStatusColor, 16);
            
            GUIStyle statusStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold
            };
            EditorGUILayout.LabelField(pythonServerInstallationStatus, statusStyle, GUILayout.Height(28));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);
            
            EditorGUILayout.BeginHorizontal();
            bool isAutoMode = UnityMcpBridge.IsAutoConnectMode();
            GUIStyle modeStyle = new GUIStyle(EditorStyles.miniLabel) { fontSize = 11 };
            EditorGUILayout.LabelField($"Mode: {(isAutoMode ? "Auto" : "Standard")}", modeStyle);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            
            int currentUnityPort = UnityMcpBridge.GetCurrentPort();
            GUIStyle portStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 11
            };
            EditorGUILayout.LabelField($"Ports: Unity {currentUnityPort}, MCP {mcpPort}", portStyle);
            EditorGUILayout.Space(5);

            /// Auto-Setup button below ports
            string setupButtonText = (lastClientRegisteredOk && lastBridgeVerifiedOk) ? "Connected ✓" : "Auto-Setup";
            if (GUILayout.Button(setupButtonText, GUILayout.Height(24)))
            {
                RunSetupNow();
            }
            EditorGUILayout.Space(4);

            // Repair Python Env button with tooltip tag
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                GUIContent repairLabel = new GUIContent(
                    "Repair Python Env",
                    "Deletes the server's .venv and runs 'uv sync' to rebuild a clean environment. Use this if modules are missing or Python upgraded."
                );
                if (GUILayout.Button(repairLabel, GUILayout.Width(160), GUILayout.Height(22)))
                {
                    bool ok = global::UnityMcpBridge.Editor.Helpers.ServerInstaller.RepairPythonEnvironment();
                    if (ok)
                    {
                        EditorUtility.DisplayDialog("Unity MCP", "Python environment repaired.", "OK");
                        UpdatePythonServerInstallationStatus();
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("Unity MCP", "Repair failed. Please check Console for details.", "OK");
                    }
                }
            }
            // (Removed descriptive tool tag under the Repair button)

            // (Show Debug Logs toggle moved to header)
            EditorGUILayout.Space(2);

            // Python detection warning with link
            if (!IsPythonDetected())
            {
                GUIStyle warnStyle = new GUIStyle(EditorStyles.label) { richText = true, wordWrap = true };
                EditorGUILayout.LabelField("<color=#cc3333><b>Warning:</b></color> No Python installation found.", warnStyle);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Open install instructions", GUILayout.Width(200)))
                    {
                        Application.OpenURL("https://www.python.org/downloads/");
                    }
                }
                EditorGUILayout.Space(4);
            }

            // Troubleshooting helpers
            if (pythonServerInstallationStatusColor != Color.green)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Select server folder…", GUILayout.Width(160)))
                    {
                        string picked = EditorUtility.OpenFolderPanel("Select UnityMcpServer/src", Application.dataPath, "");
                        if (!string.IsNullOrEmpty(picked) && File.Exists(Path.Combine(picked, "server.py")))
                        {
                            pythonDirOverride = picked;
                            EditorPrefs.SetString("UnityMCP.PythonDirOverride", pythonDirOverride);
                            UpdatePythonServerInstallationStatus();
                        }
                        else if (!string.IsNullOrEmpty(picked))
                        {
                            EditorUtility.DisplayDialog("Invalid Selection", "The selected folder does not contain server.py", "OK");
                        }
                    }
                    if (GUILayout.Button("Verify again", GUILayout.Width(120)))
                    {
                        UpdatePythonServerInstallationStatus();
                    }
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawBridgeSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            // Always reflect the live state each repaint to avoid stale UI after recompiles
            isUnityBridgeRunning = UnityMcpBridge.IsRunning;

            GUIStyle sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14
            };
            EditorGUILayout.LabelField("Unity Bridge", sectionTitleStyle);
            EditorGUILayout.Space(8);
            
            EditorGUILayout.BeginHorizontal();
            Color bridgeColor = isUnityBridgeRunning ? Color.green : Color.red;
            Rect bridgeStatusRect = GUILayoutUtility.GetRect(0, 28, GUILayout.Width(24));
            DrawStatusDot(bridgeStatusRect, bridgeColor, 16);
            
            GUIStyle bridgeStatusStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold
            };
            EditorGUILayout.LabelField(isUnityBridgeRunning ? "Running" : "Stopped", bridgeStatusStyle, GUILayout.Height(28));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);
            if (GUILayout.Button(isUnityBridgeRunning ? "Stop Bridge" : "Start Bridge", GUILayout.Height(32)))
            {
                ToggleUnityBridge();
            }
            EditorGUILayout.Space(5);
            EditorGUILayout.EndVertical();
        }

        private void DrawValidationSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            GUIStyle sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14
            };
            EditorGUILayout.LabelField("Script Validation", sectionTitleStyle);
            EditorGUILayout.Space(8);
            
            EditorGUI.BeginChangeCheck();
            validationLevelIndex = EditorGUILayout.Popup("Validation Level", validationLevelIndex, validationLevelOptions, GUILayout.Height(20));
            if (EditorGUI.EndChangeCheck())
            {
                SaveValidationLevelSetting();
            }
            
            EditorGUILayout.Space(8);
            string description = GetValidationLevelDescription(validationLevelIndex);
            EditorGUILayout.HelpBox(description, MessageType.Info);
            EditorGUILayout.Space(4);
            // (Show Debug Logs toggle moved to header)
            EditorGUILayout.Space(2);
            EditorGUILayout.EndVertical();
        }

        private void DrawUnifiedClientConfiguration()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            GUIStyle sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14
            };
            EditorGUILayout.LabelField("MCP Client Configuration", sectionTitleStyle);
            EditorGUILayout.Space(10);
            
            // Auto-connect toggle (moved from Server Status)
            bool newAuto = EditorGUILayout.ToggleLeft("Auto-connect to MCP Clients", autoRegisterEnabled);
            if (newAuto != autoRegisterEnabled)
            {
                autoRegisterEnabled = newAuto;
                EditorPrefs.SetBool("UnityMCP.AutoRegisterEnabled", autoRegisterEnabled);
            }
            EditorGUILayout.Space(6);

            // Client selector
            string[] clientNames = mcpClients.clients.Select(c => c.name).ToArray();
            EditorGUI.BeginChangeCheck();
            selectedClientIndex = EditorGUILayout.Popup("Select Client", selectedClientIndex, clientNames, GUILayout.Height(20));
            if (EditorGUI.EndChangeCheck())
            {
                selectedClientIndex = Mathf.Clamp(selectedClientIndex, 0, mcpClients.clients.Count - 1);
            }
            
            EditorGUILayout.Space(10);
            
            if (mcpClients.clients.Count > 0 && selectedClientIndex < mcpClients.clients.Count)
            {
                McpClient selectedClient = mcpClients.clients[selectedClientIndex];
                DrawClientConfigurationCompact(selectedClient);
            }
            
            EditorGUILayout.Space(5);
            EditorGUILayout.EndVertical();
        }

        private void AutoFirstRunSetup()
        {
            try
            {
                // Project-scoped one-time flag
                string projectPath = Application.dataPath ?? string.Empty;
                string key = $"UnityMCP.AutoRegistered.{ComputeSha1(projectPath)}";
                if (EditorPrefs.GetBool(key, false))
                {
                    return;
                }

                // Attempt client registration using discovered Python server dir
                pythonDirOverride ??= EditorPrefs.GetString("UnityMCP.PythonDirOverride", null);
                string pythonDir = !string.IsNullOrEmpty(pythonDirOverride) ? pythonDirOverride : FindPackagePythonDirectory();
                if (!string.IsNullOrEmpty(pythonDir) && File.Exists(Path.Combine(pythonDir, "server.py")))
                {
                    bool anyRegistered = false;
                    foreach (McpClient client in mcpClients.clients)
                    {
                        try
                        {
                            if (client.mcpType == McpTypes.ClaudeCode)
                            {
                                if (!IsClaudeConfigured())
                                {
                                    RegisterWithClaudeCode(pythonDir);
                                    anyRegistered = true;
                                }
                            }
                            else
                            {
                                // For Cursor/others, skip if already configured
                                if (!IsCursorConfigured(pythonDir))
                                {
                                    ConfigureMcpClient(client);
                                    anyRegistered = true;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            UnityEngine.Debug.LogWarning($"Auto-setup client '{client.name}' failed: {ex.Message}");
                        }
                    }
                    lastClientRegisteredOk = anyRegistered || IsCursorConfigured(pythonDir) || IsClaudeConfigured();
                }

                // Ensure the bridge is listening and has a fresh saved port
                if (!UnityMcpBridge.IsRunning)
                {
                    try
                    {
                        UnityMcpBridge.StartAutoConnect();
                        isUnityBridgeRunning = UnityMcpBridge.IsRunning;
                        Repaint();
                    }
                    catch (Exception ex)
                    {
                        UnityEngine.Debug.LogWarning($"Auto-setup StartAutoConnect failed: {ex.Message}");
                    }
                }

                // Verify bridge with a quick ping
                lastBridgeVerifiedOk = VerifyBridgePing(UnityMcpBridge.GetCurrentPort());

                EditorPrefs.SetBool(key, true);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"Unity MCP auto-setup skipped: {e.Message}");
            }
        }

        private static string ComputeSha1(string input)
        {
            try
            {
                using SHA1 sha1 = SHA1.Create();
                byte[] bytes = Encoding.UTF8.GetBytes(input ?? string.Empty);
                byte[] hash = sha1.ComputeHash(bytes);
                StringBuilder sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString();
            }
            catch
            {
                return "";
            }
        }

        private void RunSetupNow()
        {
            // Force a one-shot setup regardless of first-run flag
            try
            {
                pythonDirOverride ??= EditorPrefs.GetString("UnityMCP.PythonDirOverride", null);
                string pythonDir = !string.IsNullOrEmpty(pythonDirOverride) ? pythonDirOverride : FindPackagePythonDirectory();
                if (string.IsNullOrEmpty(pythonDir) || !File.Exists(Path.Combine(pythonDir, "server.py")))
                {
                    EditorUtility.DisplayDialog("Setup", "Python server not found. Please select UnityMcpServer/src.", "OK");
                    return;
                }

                bool anyRegistered = false;
                foreach (McpClient client in mcpClients.clients)
                {
                    try
                    {
                        if (client.mcpType == McpTypes.ClaudeCode)
                        {
                            if (!IsClaudeConfigured())
                            {
                                RegisterWithClaudeCode(pythonDir);
                                anyRegistered = true;
                            }
                        }
                        else
                        {
                            if (!IsCursorConfigured(pythonDir))
                            {
                                ConfigureMcpClient(client);
                                anyRegistered = true;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        UnityEngine.Debug.LogWarning($"Setup client '{client.name}' failed: {ex.Message}");
                    }
                }
                lastClientRegisteredOk = anyRegistered || IsCursorConfigured(pythonDir) || IsClaudeConfigured();

                // Restart/ensure bridge
                UnityMcpBridge.StartAutoConnect();
                isUnityBridgeRunning = UnityMcpBridge.IsRunning;

                // Verify
                lastBridgeVerifiedOk = VerifyBridgePing(UnityMcpBridge.GetCurrentPort());
                Repaint();
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Setup Failed", e.Message, "OK");
            }
        }

        private static bool IsCursorConfigured(string pythonDir)
        {
            try
            {
                string configPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 
                        ".cursor", "mcp.json")
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 
                        ".cursor", "mcp.json");
                if (!File.Exists(configPath)) return false;
                string json = File.ReadAllText(configPath);
                dynamic cfg = JsonConvert.DeserializeObject(json);
                var servers = cfg?.mcpServers;
                if (servers == null) return false;
                var unity = servers.unityMCP ?? servers.UnityMCP;
                if (unity == null) return false;
                var args = unity.args;
                if (args == null) return false;
                foreach (var a in args)
                {
                    string s = (string)a;
                    if (!string.IsNullOrEmpty(s) && s.Contains(pythonDir, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
                return false;
            }
            catch { return false; }
        }

        private static bool IsClaudeConfigured()
        {
            try
            {
                string command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "claude" : "/usr/local/bin/claude";
                var psi = new ProcessStartInfo { FileName = command, Arguments = "mcp list", UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                using var p = Process.Start(psi);
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(3000);
                if (p.ExitCode != 0) return false;
                return output.IndexOf("UnityMCP", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        private static bool VerifyBridgePing(int port)
        {
            try
            {
                using TcpClient c = new TcpClient();
                var task = c.ConnectAsync(IPAddress.Loopback, port);
                if (!task.Wait(500)) return false;
                using NetworkStream s = c.GetStream();
                byte[] ping = Encoding.UTF8.GetBytes("ping");
                s.Write(ping, 0, ping.Length);
                s.ReadTimeout = 1000;
                byte[] buf = new byte[256];
                int n = s.Read(buf, 0, buf.Length);
                if (n <= 0) return false;
                string resp = Encoding.UTF8.GetString(buf, 0, n);
                return resp.Contains("pong", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private void DrawClientConfigurationCompact(McpClient mcpClient)
        {
            // Status display
            EditorGUILayout.BeginHorizontal();
            Rect statusRect = GUILayoutUtility.GetRect(0, 28, GUILayout.Width(24));
            Color statusColor = GetStatusColor(mcpClient.status);
            DrawStatusDot(statusRect, statusColor, 16);
            
            GUIStyle clientStatusStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold
            };
            EditorGUILayout.LabelField(mcpClient.configStatus, clientStatusStyle, GUILayout.Height(28));
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space(10);
            
            // Action buttons in horizontal layout
            EditorGUILayout.BeginHorizontal();
            
            if (mcpClient.mcpType == McpTypes.VSCode)
            {
                if (GUILayout.Button("Auto Configure", GUILayout.Height(32)))
                {
                    ConfigureMcpClient(mcpClient);
                }
            }
            else if (mcpClient.mcpType == McpTypes.ClaudeCode)
            {
                bool isConfigured = mcpClient.status == McpStatus.Configured;
                string buttonText = isConfigured ? "Unregister UnityMCP with Claude Code" : "Register with Claude Code";
                if (GUILayout.Button(buttonText, GUILayout.Height(32)))
                {
                    if (isConfigured)
                    {
                        UnregisterWithClaudeCode();
                    }
                    else
                    {
                        string pythonDir = FindPackagePythonDirectory();
                        RegisterWithClaudeCode(pythonDir);
                    }
                }
            }
            else
            {
                if (GUILayout.Button($"Auto Configure", GUILayout.Height(32)))
                {
                    ConfigureMcpClient(mcpClient);
                }
            }
            
            if (mcpClient.mcpType != McpTypes.ClaudeCode)
            {
                if (GUILayout.Button("Manual Setup", GUILayout.Height(32)))
                {
                    string configPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                        ? mcpClient.windowsConfigPath
                        : mcpClient.linuxConfigPath;
                        
                    if (mcpClient.mcpType == McpTypes.VSCode)
                    {
                        string pythonDir = FindPackagePythonDirectory();
                        string uvPath = FindUvPath();
                        if (uvPath == null)
                        {
                            UnityEngine.Debug.LogError("UV package manager not found. Cannot configure VSCode.");
                            return;
                        }
                        
                        var vscodeConfig = new
                        {
                            mcp = new
                            {
                                servers = new
                                {
                                    unityMCP = new
                                    {
                                        command = uvPath,
                                        args = new[] { "--directory", pythonDir, "run", "server.py" }
                                    }
                                }
                            }
                        };
                        JsonSerializerSettings jsonSettings = new() { Formatting = Formatting.Indented };
                        string manualConfigJson = JsonConvert.SerializeObject(vscodeConfig, jsonSettings);
                        VSCodeManualSetupWindow.ShowWindow(configPath, manualConfigJson);
                    }
                    else
                    {
                        ShowManualInstructionsWindow(configPath, mcpClient);
                    }
                }
            }
            
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space(8);
            // Quick info
            GUIStyle configInfoStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 10
            };
            EditorGUILayout.LabelField($"Config: {Path.GetFileName(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? mcpClient.windowsConfigPath : mcpClient.linuxConfigPath)}", configInfoStyle);
        }

        private void ToggleUnityBridge()
        {
            if (isUnityBridgeRunning)
            {
                UnityMcpBridge.Stop();
            }
            else
            {
                UnityMcpBridge.Start();
            }
            // Reflect the actual state post-operation (avoid optimistic toggle)
            isUnityBridgeRunning = UnityMcpBridge.IsRunning;
            Repaint();
        }

        private string WriteToConfig(string pythonDir, string configPath, McpClient mcpClient = null)
        {
            string uvPath = FindUvPath();
            if (uvPath == null)
            {
                return "UV package manager not found. Please install UV first.";
            }
            
            // Create configuration object for unityMCP
            McpConfigServer unityMCPConfig = new()
            {
                command = uvPath,
                args = new[] { "--directory", pythonDir, "run", "server.py" },
            };

            JsonSerializerSettings jsonSettings = new() { Formatting = Formatting.Indented };

            // Read existing config if it exists
            string existingJson = "{}";
            if (File.Exists(configPath))
            {
                try
                {
                    existingJson = File.ReadAllText(configPath);
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogWarning($"Error reading existing config: {e.Message}.");
                }
            }

            // Parse the existing JSON while preserving all properties
            dynamic existingConfig = JsonConvert.DeserializeObject(existingJson);
            existingConfig ??= new Newtonsoft.Json.Linq.JObject();

            // Handle different client types with a switch statement
            //Comments: Interestingly, VSCode has mcp.servers.unityMCP while others have mcpServers.unityMCP, which is why we need to prevent this
            switch (mcpClient?.mcpType)
            {
                case McpTypes.VSCode:
                    // VSCode specific configuration
                    // Ensure mcp object exists
                    if (existingConfig.mcp == null)
                    {
                        existingConfig.mcp = new Newtonsoft.Json.Linq.JObject();
                    }

                    // Ensure mcp.servers object exists
                    if (existingConfig.mcp.servers == null)
                    {
                        existingConfig.mcp.servers = new Newtonsoft.Json.Linq.JObject();
                    }

                    // Add/update UnityMCP server in VSCode settings
                    existingConfig.mcp.servers.unityMCP =
                        JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JToken>(
                            JsonConvert.SerializeObject(unityMCPConfig)
                        );
                    break;

                default:
                    // Standard MCP configuration (Claude Desktop, Cursor, etc.)
                    // Ensure mcpServers object exists
                    if (existingConfig.mcpServers == null)
                    {
                        existingConfig.mcpServers = new Newtonsoft.Json.Linq.JObject();
                    }

                    // Add/update UnityMCP server in standard MCP settings
                    existingConfig.mcpServers.unityMCP =
                        JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JToken>(
                            JsonConvert.SerializeObject(unityMCPConfig)
                        );
                    break;
            }

            // Write the merged configuration back to file
            string mergedJson = JsonConvert.SerializeObject(existingConfig, jsonSettings);
            File.WriteAllText(configPath, mergedJson);

            return "Configured successfully";
        }

        private void ShowManualConfigurationInstructions(
            string configPath,
            McpClient mcpClient
        )
        {
            mcpClient.SetStatus(McpStatus.Error, "Manual configuration required");

            ShowManualInstructionsWindow(configPath, mcpClient);
        }

        // New method to show manual instructions without changing status
        private void ShowManualInstructionsWindow(string configPath, McpClient mcpClient)
        {
            // Get the Python directory path using Package Manager API
            string pythonDir = FindPackagePythonDirectory();
            string manualConfigJson;
            
            // Create common JsonSerializerSettings
            JsonSerializerSettings jsonSettings = new() { Formatting = Formatting.Indented };
            
            // Use switch statement to handle different client types
            switch (mcpClient.mcpType)
            {
                case McpTypes.VSCode:
                    // Create VSCode-specific configuration with proper format
                    var vscodeConfig = new
                    {
                        mcp = new
                        {
                            servers = new
                            {
                                unityMCP = new
                                {
                                    command = "uv",
                                    args = new[] { "--directory", pythonDir, "run", "server.py" }
                                }
                            }
                        }
                    };
                    manualConfigJson = JsonConvert.SerializeObject(vscodeConfig, jsonSettings);
                    break;
                    
                default:
                    // Create standard MCP configuration for other clients
                    string uvPath = FindUvPath();
                    if (uvPath == null)
                    {
                        UnityEngine.Debug.LogError("UV package manager not found. Cannot configure manual setup.");
                        return;
                    }
                    
                    McpConfig jsonConfig = new()
                    {
                        mcpServers = new McpConfigServers
                        {
                            unityMCP = new McpConfigServer
                            {
                                command = uvPath,
                                args = new[] { "--directory", pythonDir, "run", "server.py" },
                            },
                        },
                    };
                    manualConfigJson = JsonConvert.SerializeObject(jsonConfig, jsonSettings);
                    break;
            }

            ManualConfigEditorWindow.ShowWindow(configPath, manualConfigJson, mcpClient);
        }

        private string FindPackagePythonDirectory()
        {
            string pythonDir = ServerInstaller.GetServerPath();

            try
            {
                // Only check dev paths if we're using a file-based package (development mode)
                bool isDevelopmentMode = IsDevelopmentMode();
                if (isDevelopmentMode)
                {
                    string currentPackagePath = Path.GetDirectoryName(Application.dataPath);
                    string[] devPaths = {
                        Path.Combine(currentPackagePath, "unity-mcp", "UnityMcpServer", "src"),
                        Path.Combine(Path.GetDirectoryName(currentPackagePath), "unity-mcp", "UnityMcpServer", "src"),
                    };
                    
                    foreach (string devPath in devPaths)
                    {
                        if (Directory.Exists(devPath) && File.Exists(Path.Combine(devPath, "server.py")))
                        {
                            if (debugLogsEnabled)
                            {
                                UnityEngine.Debug.Log($"Currently in development mode. Package: {devPath}");
                            }
                            return devPath;
                        }
                    }
                }

                // Try to find the package using Package Manager API
                UnityEditor.PackageManager.Requests.ListRequest request =
                    UnityEditor.PackageManager.Client.List();
                while (!request.IsCompleted) { } // Wait for the request to complete

                if (request.Status == UnityEditor.PackageManager.StatusCode.Success)
                {
                    foreach (UnityEditor.PackageManager.PackageInfo package in request.Result)
                    {
                        if (package.name == "com.justinpbarnett.unity-mcp")
                        {
                            string packagePath = package.resolvedPath;
                            
                            // Preferred: check for tilde folder inside package
                            string packagedTildeDir = Path.Combine(packagePath, "UnityMcpServer~", "src");
                            if (Directory.Exists(packagedTildeDir) && File.Exists(Path.Combine(packagedTildeDir, "server.py")))
                            {
                                return packagedTildeDir;
                            }
                            
                            // Fallback: legacy local package structure (UnityMcpServer/src)
                            string localPythonDir = Path.Combine(Path.GetDirectoryName(packagePath), "UnityMcpServer", "src");
                            if (Directory.Exists(localPythonDir) && File.Exists(Path.Combine(localPythonDir, "server.py")))
                            {
                                return localPythonDir;
                            }
                            
                            // Check for old structure (Python subdirectory)
                            string potentialPythonDir = Path.Combine(packagePath, "Python");
                            if (Directory.Exists(potentialPythonDir) && File.Exists(Path.Combine(potentialPythonDir, "server.py")))
                            {
                                return potentialPythonDir;
                            }
                        }
                    }
                }
                else if (request.Error != null)
                {
                    UnityEngine.Debug.LogError("Failed to list packages: " + request.Error.message);
                }

                // If not found via Package Manager, try manual approaches
                // Check for local development structure
                string[] possibleDirs =
                {
                    // Check in user's home directory (common installation location)
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "unity-mcp", "UnityMcpServer", "src"),
                    // Check in Applications folder (macOS/Linux common location)
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications", "UnityMCP", "UnityMcpServer", "src"),
                    // Legacy Python folder structure
                    Path.GetFullPath(Path.Combine(Application.dataPath, "unity-mcp", "Python")),
                };

                foreach (string dir in possibleDirs)
                {
                    if (Directory.Exists(dir) && File.Exists(Path.Combine(dir, "server.py")))
                    {
                        return dir;
                    }
                }

                // If still not found, return the placeholder path
                if (debugLogsEnabled)
                {
                    UnityEngine.Debug.LogWarning("Could not find Python directory, using placeholder path");
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"Error finding package path: {e.Message}");
            }

            return pythonDir;
        }

        private bool IsDevelopmentMode()
        {
            try
            {
                // Only treat as development if manifest explicitly references a local file path for the package
                string manifestPath = Path.Combine(Application.dataPath, "..", "Packages", "manifest.json");
                if (!File.Exists(manifestPath)) return false;

                string manifestContent = File.ReadAllText(manifestPath);
                // Look specifically for our package dependency set to a file: URL
                // This avoids auto-enabling dev mode just because a repo exists elsewhere on disk
                if (manifestContent.IndexOf("\"com.justinpbarnett.unity-mcp\"", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    int idx = manifestContent.IndexOf("com.justinpbarnett.unity-mcp", StringComparison.OrdinalIgnoreCase);
                    // Crude but effective: check for "file:" in the same line/value
                    if (manifestContent.IndexOf("file:", idx, StringComparison.OrdinalIgnoreCase) >= 0
                        && manifestContent.IndexOf("\n", idx, StringComparison.OrdinalIgnoreCase) > manifestContent.IndexOf("file:", idx, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private string ConfigureMcpClient(McpClient mcpClient)
        {
            try
            {
                // Determine the config file path based on OS
                string configPath;

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    configPath = mcpClient.windowsConfigPath;
                }
                else if (
                    RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                    || RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                )
                {
                    configPath = mcpClient.linuxConfigPath;
                }
                else
                {
                    return "Unsupported OS";
                }

                // Create directory if it doesn't exist
                Directory.CreateDirectory(Path.GetDirectoryName(configPath));

                // Find the server.py file location using the same logic as FindPackagePythonDirectory
                string pythonDir = FindPackagePythonDirectory();

                if (pythonDir == null || !File.Exists(Path.Combine(pythonDir, "server.py")))
                {
                    ShowManualInstructionsWindow(configPath, mcpClient);
                    return "Manual Configuration Required";
                }

                string result = WriteToConfig(pythonDir, configPath, mcpClient);

                // Update the client status after successful configuration
                if (result == "Configured successfully")
                {
                    mcpClient.SetStatus(McpStatus.Configured);
                }

                return result;
            }
            catch (Exception e)
            {
                // Determine the config file path based on OS for error message
                string configPath = "";
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    configPath = mcpClient.windowsConfigPath;
                }
                else if (
                    RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                    || RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                )
                {
                    configPath = mcpClient.linuxConfigPath;
                }

                ShowManualInstructionsWindow(configPath, mcpClient);
                UnityEngine.Debug.LogError(
                    $"Failed to configure {mcpClient.name}: {e.Message}\n{e.StackTrace}"
                );
                return $"Failed to configure {mcpClient.name}";
            }
        }

        private void ShowCursorManualConfigurationInstructions(
            string configPath,
            McpClient mcpClient
        )
        {
            mcpClient.SetStatus(McpStatus.Error, "Manual configuration required");

            // Get the Python directory path using Package Manager API
            string pythonDir = FindPackagePythonDirectory();

            // Create the manual configuration message
            string uvPath = FindUvPath();
            if (uvPath == null)
            {
                UnityEngine.Debug.LogError("UV package manager not found. Cannot configure manual setup.");
                return;
            }
            
            McpConfig jsonConfig = new()
            {
                mcpServers = new McpConfigServers
                {
                    unityMCP = new McpConfigServer
                    {
                        command = uvPath,
                        args = new[] { "--directory", pythonDir, "run", "server.py" },
                    },
                },
            };

            JsonSerializerSettings jsonSettings = new() { Formatting = Formatting.Indented };
            string manualConfigJson = JsonConvert.SerializeObject(jsonConfig, jsonSettings);

            ManualConfigEditorWindow.ShowWindow(configPath, manualConfigJson, mcpClient);
        }

        private void LoadValidationLevelSetting()
        {
            string savedLevel = EditorPrefs.GetString("UnityMCP_ScriptValidationLevel", "standard");
            validationLevelIndex = savedLevel.ToLower() switch
            {
                "basic" => 0,
                "standard" => 1,
                "comprehensive" => 2,
                "strict" => 3,
                _ => 1 // Default to Standard
            };
        }

        private void SaveValidationLevelSetting()
        {
            string levelString = validationLevelIndex switch
            {
                0 => "basic",
                1 => "standard",
                2 => "comprehensive",
                3 => "strict",
                _ => "standard"
            };
            EditorPrefs.SetString("UnityMCP_ScriptValidationLevel", levelString);
        }

        private string GetValidationLevelDescription(int index)
        {
            return index switch
            {
                0 => "Only basic syntax checks (braces, quotes, comments)",
                1 => "Syntax checks + Unity best practices and warnings",
                2 => "All checks + semantic analysis and performance warnings",
                3 => "Full semantic validation with namespace/type resolution (requires Roslyn)",
                _ => "Standard validation"
            };
        }

        public static string GetCurrentValidationLevel()
        {
            string savedLevel = EditorPrefs.GetString("UnityMCP_ScriptValidationLevel", "standard");
            return savedLevel;
        }

        private void CheckMcpConfiguration(McpClient mcpClient)
        {
            try
            {
                // Special handling for Claude Code
                if (mcpClient.mcpType == McpTypes.ClaudeCode)
                {
                    CheckClaudeCodeConfiguration(mcpClient);
                    return;
                }
                
                string configPath;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    configPath = mcpClient.windowsConfigPath;
                }
                else if (
                    RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                    || RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                )
                {
                    configPath = mcpClient.linuxConfigPath;
                }
                else
                {
                    mcpClient.SetStatus(McpStatus.UnsupportedOS);
                    return;
                }

                if (!File.Exists(configPath))
                {
                    mcpClient.SetStatus(McpStatus.NotConfigured);
                    return;
                }

                string configJson = File.ReadAllText(configPath);
                // Use the same path resolution as configuration to avoid false "Incorrect Path" in dev mode
                string pythonDir = FindPackagePythonDirectory();
                
                // Use switch statement to handle different client types, extracting common logic
                string[] args = null;
                bool configExists = false;
                
                switch (mcpClient.mcpType)
                {
                    case McpTypes.VSCode:
                        dynamic config = JsonConvert.DeserializeObject(configJson);
                        
                        if (config?.mcp?.servers?.unityMCP != null)
                        {
                            // Extract args from VSCode config format
                            args = config.mcp.servers.unityMCP.args.ToObject<string[]>();
                            configExists = true;
                        }
                        break;
                        
                    default:
                        // Standard MCP configuration check for Claude Desktop, Cursor, etc.
                        McpConfig standardConfig = JsonConvert.DeserializeObject<McpConfig>(configJson);
                        
                        if (standardConfig?.mcpServers?.unityMCP != null)
                        {
                            args = standardConfig.mcpServers.unityMCP.args;
                            configExists = true;
                        }
                        break;
                }
                
                // Common logic for checking configuration status
                if (configExists)
                {
                    bool matches = pythonDir != null && Array.Exists(args, arg => arg.Contains(pythonDir, StringComparison.Ordinal));
                    if (matches)
                    {
                        mcpClient.SetStatus(McpStatus.Configured);
                    }
                    else
                    {
                        // Attempt auto-rewrite once if the package path changed
                        try
                        {
                            string rewriteResult = WriteToConfig(pythonDir, configPath, mcpClient);
                            if (rewriteResult == "Configured successfully")
                            {
                                if (debugLogsEnabled)
                                {
                                    UnityEngine.Debug.Log($"UnityMCP: Auto-updated MCP config for '{mcpClient.name}' to new path: {pythonDir}");
                                }
                                mcpClient.SetStatus(McpStatus.Configured);
                            }
                            else
                            {
                                mcpClient.SetStatus(McpStatus.IncorrectPath);
                            }
                        }
                        catch (Exception ex)
                        {
                            mcpClient.SetStatus(McpStatus.IncorrectPath);
                            if (debugLogsEnabled)
                            {
                                UnityEngine.Debug.LogWarning($"UnityMCP: Auto-config rewrite failed for '{mcpClient.name}': {ex.Message}");
                            }
                        }
                    }
                }
                else
                {
                    mcpClient.SetStatus(McpStatus.MissingConfig);
                }
            }
            catch (Exception e)
            {
                mcpClient.SetStatus(McpStatus.Error, e.Message);
            }
        }

        private void RegisterWithClaudeCode(string pythonDir)
        {
            string command;
            string args;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                command = FindClaudeCommand();
                
                if (string.IsNullOrEmpty(command))
                {
                    UnityEngine.Debug.LogError("Claude CLI not found. Please ensure Claude Code is installed and accessible.");
                    return;
                }
                
                // Try to find uv.exe in common locations
                string uvPath = FindUvPath();
                
                if (string.IsNullOrEmpty(uvPath))
                {
                    // Fallback to expecting uv in PATH
                    args = $"mcp add UnityMCP -- uv --directory \"{pythonDir}\" run server.py";
                }
                else
                {
                    args = $"mcp add UnityMCP -- \"{uvPath}\" --directory \"{pythonDir}\" run server.py";
                }
            }
            else
            {
                // Use full path to claude command
                command = "/usr/local/bin/claude";
                args = $"mcp add UnityMCP -- uv --directory \"{pythonDir}\" run server.py";
            }

            try
            {
                // Get the Unity project directory (where the Assets folder is)
                string unityProjectDir = Application.dataPath;
                string projectDir = Path.GetDirectoryName(unityProjectDir);

                var psi = new ProcessStartInfo();
                
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // On Windows, run through PowerShell with explicit PATH setting
                    psi.FileName = "powershell.exe";
                    string nodePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs");
                    psi.Arguments = $"-Command \"$env:PATH += ';{nodePath}'; & '{command}' {args}\"";
                    UnityEngine.Debug.Log($"Executing: powershell.exe {psi.Arguments}");
                }
                else
                {
                    psi.FileName = command;
                    psi.Arguments = args;
                    UnityEngine.Debug.Log($"Executing: {command} {args}");
                }
                
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                psi.WorkingDirectory = projectDir;

                // Set PATH to include common binary locations (OS-specific)
                string currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // Windows: Add common Node.js and npm locations
                    string[] windowsPaths = {
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "nodejs"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "npm")
                    };
                    string additionalPaths = string.Join(";", windowsPaths);
                    psi.EnvironmentVariables["PATH"] = $"{currentPath};{additionalPaths}";
                }
                else
                {
                    // macOS/Linux: Add common binary locations
                    string additionalPaths = "/usr/local/bin:/opt/homebrew/bin:/usr/bin:/bin";
                    psi.EnvironmentVariables["PATH"] = $"{additionalPaths}:{currentPath}";
                }

                using var process = Process.Start(psi);
                string output = process.StandardOutput.ReadToEnd();
                string errors = process.StandardError.ReadToEnd();
                process.WaitForExit();



                // Check for success or already exists
                if (output.Contains("Added stdio MCP server") || errors.Contains("already exists"))
                {
                    // Force refresh the configuration status
                    var claudeClient = mcpClients.clients.FirstOrDefault(c => c.mcpType == McpTypes.ClaudeCode);
                    if (claudeClient != null)
                    {
                        CheckClaudeCodeConfiguration(claudeClient);
                    }
                    Repaint();
                    UnityEngine.Debug.Log("UnityMCP server successfully registered from Claude Code.");
                    

                }
                else if (!string.IsNullOrEmpty(errors))
                {
                    if (debugLogsEnabled)
                    {
                        UnityEngine.Debug.LogWarning($"Claude MCP errors: {errors}");
                    }
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"Claude CLI registration failed: {e.Message}");
            }
        }

        private void UnregisterWithClaudeCode()
        {
            string command;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                command = FindClaudeCommand();
                
                if (string.IsNullOrEmpty(command))
                {
                    UnityEngine.Debug.LogError("Claude CLI not found. Please ensure Claude Code is installed and accessible.");
                    return;
                }
            }
            else
            {
                // Use full path to claude command
                command = "/usr/local/bin/claude";
            }

            try
            {
                // Get the Unity project directory (where the Assets folder is)
                string unityProjectDir = Application.dataPath;
                string projectDir = Path.GetDirectoryName(unityProjectDir);

                var psi = new ProcessStartInfo();
                
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // On Windows, run through PowerShell with explicit PATH setting
                    psi.FileName = "powershell.exe";
                    string nodePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs");
                    psi.Arguments = $"-Command \"$env:PATH += ';{nodePath}'; & '{command}' mcp remove UnityMCP\"";
                }
                else
                {
                    psi.FileName = command;
                    psi.Arguments = "mcp remove UnityMCP";
                }
                
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                psi.WorkingDirectory = projectDir;

                // Set PATH to include common binary locations (OS-specific)
                string currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // Windows: Add common Node.js and npm locations
                    string[] windowsPaths = {
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "nodejs"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "npm")
                    };
                    string additionalPaths = string.Join(";", windowsPaths);
                    psi.EnvironmentVariables["PATH"] = $"{currentPath};{additionalPaths}";
                }
                else
                {
                    // macOS/Linux: Add common binary locations
                    string additionalPaths = "/usr/local/bin:/opt/homebrew/bin:/usr/bin:/bin";
                    psi.EnvironmentVariables["PATH"] = $"{additionalPaths}:{currentPath}";
                }

                using var process = Process.Start(psi);
                string output = process.StandardOutput.ReadToEnd();
                string errors = process.StandardError.ReadToEnd();
                process.WaitForExit();

                // Check for success
                if (output.Contains("Removed MCP server") || process.ExitCode == 0)
                {
                    // Force refresh the configuration status
                    var claudeClient = mcpClients.clients.FirstOrDefault(c => c.mcpType == McpTypes.ClaudeCode);
                    if (claudeClient != null)
                    {
                        CheckClaudeCodeConfiguration(claudeClient);
                    }
                    Repaint();
                    
                    UnityEngine.Debug.Log("UnityMCP server successfully unregistered from Claude Code.");
                }
                else if (!string.IsNullOrEmpty(errors))
                {
                    UnityEngine.Debug.LogWarning($"Claude MCP removal errors: {errors}");
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"Claude CLI unregistration failed: {e.Message}");
            }
        }

        private string FindUvPath()
        {
            string uvPath = null;
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                uvPath = FindWindowsUvPath();
            }
            else
            {
                // macOS/Linux paths
                string[] possiblePaths = {
                    "/Library/Frameworks/Python.framework/Versions/3.13/bin/uv",
                    "/usr/local/bin/uv",
                    "/opt/homebrew/bin/uv",
                    "/usr/bin/uv"
                };
                
                foreach (string path in possiblePaths)
                {
                    if (File.Exists(path) && IsValidUvInstallation(path))
                    {
                        uvPath = path;
                        break;
                    }
                }
                
                // If not found in common locations, try to find via which command
                if (uvPath == null)
                {
                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = "which",
                            Arguments = "uv",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            CreateNoWindow = true
                        };
                        
                        using var process = Process.Start(psi);
                        string output = process.StandardOutput.ReadToEnd().Trim();
                        process.WaitForExit();
                        
                        if (!string.IsNullOrEmpty(output) && File.Exists(output) && IsValidUvInstallation(output))
                        {
                            uvPath = output;
                        }
                    }
                    catch
                    {
                        // Ignore errors
                    }
                }
            }
            
            // If no specific path found, fall back to using 'uv' from PATH
            if (uvPath == null)
            {
                // Test if 'uv' is available in PATH by trying to run it
                string uvCommand = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "uv.exe" : "uv";
                if (IsValidUvInstallation(uvCommand))
                {
                    uvPath = uvCommand;
                }
            }
            
            if (uvPath == null)
            {
                UnityEngine.Debug.LogError("UV package manager not found! Please install UV first:\n" +
                    "• macOS/Linux: curl -LsSf https://astral.sh/uv/install.sh | sh\n" +
                    "• Windows: pip install uv\n" +
                    "• Or visit: https://docs.astral.sh/uv/getting-started/installation");
                return null;
            }
            
            return uvPath;
        }

        private bool IsValidUvInstallation(string uvPath)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = uvPath,
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                
                using var process = Process.Start(psi);
                process.WaitForExit(5000); // 5 second timeout
                
                if (process.ExitCode == 0)
                {
                    string output = process.StandardOutput.ReadToEnd().Trim();
                    // Basic validation - just check if it responds with version info
                    // UV typically outputs "uv 0.x.x" format
                    if (output.StartsWith("uv ") && output.Contains("."))
                    {
                        return true;
                    }
                }
                
                return false;
            }
            catch
            {
                return false;
            }
        }

        private string FindWindowsUvPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            
            // Dynamic Python version detection - check what's actually installed
            List<string> pythonVersions = new List<string>();
            
            // Add common versions but also scan for any Python* directories
            string[] commonVersions = { "Python313", "Python312", "Python311", "Python310", "Python39", "Python38", "Python37" };
            pythonVersions.AddRange(commonVersions);
            
            // Scan for additional Python installations
            string[] pythonBasePaths = {
                Path.Combine(appData, "Python"),
                Path.Combine(localAppData, "Programs", "Python"),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + "\\Python",
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) + "\\Python"
            };
            
            foreach (string basePath in pythonBasePaths)
            {
                if (Directory.Exists(basePath))
                {
                    try
                    {
                        foreach (string dir in Directory.GetDirectories(basePath, "Python*"))
                        {
                            string versionName = Path.GetFileName(dir);
                            if (!pythonVersions.Contains(versionName))
                            {
                                pythonVersions.Add(versionName);
                            }
                        }
                    }
                    catch
                    {
                        // Ignore directory access errors
                    }
                }
            }
            
            // Check Python installations for UV
            foreach (string version in pythonVersions)
            {
                string[] pythonPaths = {
                    Path.Combine(appData, "Python", version, "Scripts", "uv.exe"),
                    Path.Combine(localAppData, "Programs", "Python", version, "Scripts", "uv.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Python", version, "Scripts", "uv.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Python", version, "Scripts", "uv.exe")
                };
                
                foreach (string uvPath in pythonPaths)
                {
                    if (File.Exists(uvPath) && IsValidUvInstallation(uvPath))
                    {
                        return uvPath;
                    }
                }
            }
            
            // Check package manager installations
            string[] packageManagerPaths = {
                // Chocolatey
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "chocolatey", "lib", "uv", "tools", "uv.exe"),
                Path.Combine("C:", "ProgramData", "chocolatey", "lib", "uv", "tools", "uv.exe"),
                
                // Scoop
                Path.Combine(userProfile, "scoop", "apps", "uv", "current", "uv.exe"),
                Path.Combine(userProfile, "scoop", "shims", "uv.exe"),
                
                // Winget/msstore
                Path.Combine(localAppData, "Microsoft", "WinGet", "Packages", "astral-sh.uv_Microsoft.Winget.Source_8wekyb3d8bbwe", "uv.exe"),
                
                // Common standalone installations
                Path.Combine(localAppData, "uv", "uv.exe"),
                Path.Combine(appData, "uv", "uv.exe"),
                Path.Combine(userProfile, ".local", "bin", "uv.exe"),
                Path.Combine(userProfile, "bin", "uv.exe"),
                
                // Cargo/Rust installations
                Path.Combine(userProfile, ".cargo", "bin", "uv.exe"),
                
                // Manual installations in common locations
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "uv", "uv.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "uv", "uv.exe")
            };
            
            foreach (string uvPath in packageManagerPaths)
            {
                if (File.Exists(uvPath) && IsValidUvInstallation(uvPath))
                {
                    return uvPath;
                }
            }
            
            // Try to find uv via where command (Windows equivalent of which)
            // Use where.exe explicitly to avoid PowerShell alias conflicts
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "where.exe",
                    Arguments = "uv",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                
                using var process = Process.Start(psi);
                string output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
                
                if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                {
                    string[] lines = output.Split('\n');
                    foreach (string line in lines)
                    {
                        string cleanPath = line.Trim();
                        if (File.Exists(cleanPath) && IsValidUvInstallation(cleanPath))
                        {
                            return cleanPath;
                        }
                    }
                }
            }
            catch
            {
                // If where.exe fails, try PowerShell's Get-Command as fallback
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = "-Command \"(Get-Command uv -ErrorAction SilentlyContinue).Source\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                    
                    using var process = Process.Start(psi);
                    string output = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit();
                    
                    if (process.ExitCode == 0 && !string.IsNullOrEmpty(output) && File.Exists(output))
                    {
                        if (IsValidUvInstallation(output))
                        {
                            return output;
                        }
                    }
                }
                catch
                {
                    // Ignore PowerShell errors too
                }
            }
            
            return null; // Will fallback to using 'uv' from PATH
        }

        private string FindClaudeCommand()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Common locations for Claude CLI on Windows
                string[] possiblePaths = {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "claude.cmd"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "npm", "claude.cmd"),
                    "claude.cmd", // Fallback to PATH
                    "claude" // Final fallback
                };
                
                foreach (string path in possiblePaths)
                {
                    if (path.Contains("\\") && File.Exists(path))
                    {
                        return path;
                    }
                }
                
                // Try to find via where command (PowerShell compatible)
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "where.exe",
                        Arguments = "claude",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                    
                    using var process = Process.Start(psi);
                    string output = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit();
                    
                    if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                    {
                        string[] lines = output.Split('\n');
                        foreach (string line in lines)
                        {
                            string cleanPath = line.Trim();
                            if (File.Exists(cleanPath))
                            {
                                return cleanPath;
                            }
                        }
                    }
                }
                catch
                {
                    // If where.exe fails, try PowerShell's Get-Command as fallback
                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = "powershell.exe",
                            Arguments = "-Command \"(Get-Command claude -ErrorAction SilentlyContinue).Source\"",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        };
                        
                        using var process = Process.Start(psi);
                        string output = process.StandardOutput.ReadToEnd().Trim();
                        process.WaitForExit();
                        
                        if (process.ExitCode == 0 && !string.IsNullOrEmpty(output) && File.Exists(output))
                        {
                            return output;
                        }
                    }
                    catch
                    {
                        // Ignore PowerShell errors too
                    }
                }
                
                return "claude"; // Final fallback to PATH
            }
            else
            {
                return "/usr/local/bin/claude";
            }
        }

        private void CheckClaudeCodeConfiguration(McpClient mcpClient)
        {
            try
            {
                // Get the Unity project directory to check project-specific config
                string unityProjectDir = Application.dataPath;
                string projectDir = Path.GetDirectoryName(unityProjectDir);
                
                // Read the global Claude config file
                string configPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) 
                    ? mcpClient.windowsConfigPath 
                    : mcpClient.linuxConfigPath;
                
                if (debugLogsEnabled)
                {
                    UnityEngine.Debug.Log($"Checking Claude config at: {configPath}");
                }
                
                if (!File.Exists(configPath))
                {
                    UnityEngine.Debug.LogWarning($"Claude config file not found at: {configPath}");
                    mcpClient.SetStatus(McpStatus.NotConfigured);
                    return;
                }
                
                string configJson = File.ReadAllText(configPath);
                dynamic claudeConfig = JsonConvert.DeserializeObject(configJson);
                
                // Check for UnityMCP server in the mcpServers section (current format)
                if (claudeConfig?.mcpServers != null)
                {
                    var servers = claudeConfig.mcpServers;
                    if (servers.UnityMCP != null || servers.unityMCP != null)
                    {
                        // Found UnityMCP configured
                        mcpClient.SetStatus(McpStatus.Configured);
                        return;
                    }
                }
                
                // Also check if there's a project-specific configuration for this Unity project (legacy format)
                if (claudeConfig?.projects != null)
                {
                    // Look for the project path in the config
                    foreach (var project in claudeConfig.projects)
                    {
                        string projectPath = project.Name;
                        
                        // Normalize paths for comparison (handle forward/back slash differences)
                        string normalizedProjectPath = Path.GetFullPath(projectPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        string normalizedProjectDir = Path.GetFullPath(projectDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        
                        if (string.Equals(normalizedProjectPath, normalizedProjectDir, StringComparison.OrdinalIgnoreCase) && project.Value?.mcpServers != null)
                        {
                            // Check for UnityMCP (case variations)
                            var servers = project.Value.mcpServers;
                            if (servers.UnityMCP != null || servers.unityMCP != null)
                            {
                                // Found UnityMCP configured for this project
                                mcpClient.SetStatus(McpStatus.Configured);
                                return;
                            }
                        }
                    }
                }
                
                // No configuration found for this project
                mcpClient.SetStatus(McpStatus.NotConfigured);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"Error checking Claude Code config: {e.Message}");
                mcpClient.SetStatus(McpStatus.Error, e.Message);
            }
        }

        private bool IsPythonDetected()
        {
            try
            {
                // Windows-specific Python detection
                if (Application.platform == RuntimePlatform.WindowsEditor)
                {
                    // Common Windows Python installation paths
                    string[] windowsCandidates =
                    {
                        @"C:\Python313\python.exe",
                        @"C:\Python312\python.exe",
                        @"C:\Python311\python.exe",
                        @"C:\Python310\python.exe",
                        @"C:\Python39\python.exe",
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Python\Python313\python.exe"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Python\Python312\python.exe"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Python\Python311\python.exe"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Python\Python310\python.exe"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Python\Python39\python.exe"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Python313\python.exe"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Python312\python.exe"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Python311\python.exe"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Python310\python.exe"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Python39\python.exe"),
                    };
                    
                    foreach (string c in windowsCandidates)
                    {
                        if (File.Exists(c)) return true;
                    }

                    // Try 'where python' command (Windows equivalent of 'which')
                    var psi = new ProcessStartInfo
                    {
                        FileName = "where",
                        Arguments = "python",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                    using var p = Process.Start(psi);
                    string outp = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit(2000);
                    if (p.ExitCode == 0 && !string.IsNullOrEmpty(outp))
                    {
                        string[] lines = outp.Split('\n');
                        foreach (string line in lines)
                        {
                            string trimmed = line.Trim();
                            if (File.Exists(trimmed)) return true;
                        }
                    }
                }
                else
                {
                    // macOS/Linux detection (existing code)
                    string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) ?? string.Empty;
                    string[] candidates =
                    {
                        "/opt/homebrew/bin/python3",
                        "/usr/local/bin/python3",
                        "/usr/bin/python3",
                        "/opt/local/bin/python3",
                        Path.Combine(home, ".local", "bin", "python3"),
                        "/Library/Frameworks/Python.framework/Versions/3.13/bin/python3",
                        "/Library/Frameworks/Python.framework/Versions/3.12/bin/python3",
                    };
                    foreach (string c in candidates)
                    {
                        if (File.Exists(c)) return true;
                    }

                    // Try 'which python3'
                    var psi = new ProcessStartInfo
                    {
                        FileName = "/usr/bin/which",
                        Arguments = "python3",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                    using var p = Process.Start(psi);
                    string outp = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit(2000);
                    if (p.ExitCode == 0 && !string.IsNullOrEmpty(outp) && File.Exists(outp)) return true;
                }
            }
            catch { }
            return false;
        }
    }
}
