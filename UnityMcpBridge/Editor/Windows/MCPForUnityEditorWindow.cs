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
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using MCPForUnity.Editor.Data;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Models;

namespace MCPForUnity.Editor.Windows
{
    public class MCPForUnityEditorWindow : EditorWindow
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

        [MenuItem("Window/MCP For Unity")]
        public static void ShowWindow()
        {
            GetWindow<MCPForUnityEditorWindow>("MCP For Unity");
        }

        private void OnEnable()
        {
            UpdatePythonServerInstallationStatus();

            // Refresh bridge status
            isUnityBridgeRunning = MCPForUnityBridge.IsRunning;
            autoRegisterEnabled = EditorPrefs.GetBool("MCPForUnity.AutoRegisterEnabled", true);
            debugLogsEnabled = EditorPrefs.GetBool("MCPForUnity.DebugLogs", false);
            if (debugLogsEnabled)
            {
                LogDebugPrefsState();
            }
            foreach (McpClient mcpClient in mcpClients.clients)
            {
                CheckMcpConfiguration(mcpClient);
            }

            // Load validation level setting
            LoadValidationLevelSetting();

            // First-run auto-setup only if Claude CLI is available
            if (autoRegisterEnabled && !string.IsNullOrEmpty(ExecPath.ResolveClaude()))
            {
                AutoFirstRunSetup();
            }
        }

        private void OnFocus()
        {
            // Refresh bridge running state on focus in case initialization completed after domain reload
            isUnityBridgeRunning = MCPForUnityBridge.IsRunning;
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
                "MCP For Unity",
                titleStyle
            );

            // Place the Show Debug Logs toggle on the same header row, right-aligned
            float toggleWidth = 160f;
            Rect toggleRect = new Rect(titleRect.xMax - toggleWidth - 12f, titleRect.y + 10f, toggleWidth, 20f);
            bool newDebug = GUI.Toggle(toggleRect, debugLogsEnabled, "Show Debug Logs");
            if (newDebug != debugLogsEnabled)
            {
                debugLogsEnabled = newDebug;
                EditorPrefs.SetBool("MCPForUnity.DebugLogs", debugLogsEnabled);
                if (debugLogsEnabled)
                {
                    LogDebugPrefsState();
                }
            }
            EditorGUILayout.Space(15);
        }

        private void LogDebugPrefsState()
        {
            try
            {
                string pythonDirOverridePref = SafeGetPrefString("MCPForUnity.PythonDirOverride");
                string uvPathPref = SafeGetPrefString("MCPForUnity.UvPath");
                string serverSrcPref = SafeGetPrefString("MCPForUnity.ServerSrc");
                bool useEmbedded = SafeGetPrefBool("MCPForUnity.UseEmbeddedServer");

                // Version-scoped detection key
                string embeddedVer = ReadEmbeddedVersionOrFallback();
                string detectKey = $"MCPForUnity.LegacyDetectLogged:{embeddedVer}";
                bool detectLogged = SafeGetPrefBool(detectKey);

                // Project-scoped auto-register key
                string projectPath = Application.dataPath ?? string.Empty;
                string autoKey = $"MCPForUnity.AutoRegistered.{ComputeSha1(projectPath)}";
                bool autoRegistered = SafeGetPrefBool(autoKey);

                MCPForUnity.Editor.Helpers.McpLog.Info(
                    "MCP Debug Prefs:\n" +
                    $"  DebugLogs: {debugLogsEnabled}\n" +
                    $"  PythonDirOverride: '{pythonDirOverridePref}'\n" +
                    $"  UvPath: '{uvPathPref}'\n" +
                    $"  ServerSrc: '{serverSrcPref}'\n" +
                    $"  UseEmbeddedServer: {useEmbedded}\n" +
                    $"  DetectOnceKey: '{detectKey}' => {detectLogged}\n" +
                    $"  AutoRegisteredKey: '{autoKey}' => {autoRegistered}",
                    always: false
                );
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"MCP Debug Prefs logging failed: {ex.Message}");
            }
        }

        private static string SafeGetPrefString(string key)
        {
            try { return EditorPrefs.GetString(key, string.Empty) ?? string.Empty; } catch { return string.Empty; }
        }

        private static bool SafeGetPrefBool(string key)
        {
            try { return EditorPrefs.GetBool(key, false); } catch { return false; }
        }

        private static string ReadEmbeddedVersionOrFallback()
        {
            try
            {
                if (ServerPathResolver.TryFindEmbeddedServerSource(out var embeddedSrc))
                {
                    var p = Path.Combine(embeddedSrc, "server_version.txt");
                    if (File.Exists(p))
                    {
                        var s = File.ReadAllText(p)?.Trim();
                        if (!string.IsNullOrEmpty(s)) return s;
                    }
                }
            }
            catch { }
            return "unknown";
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
            bool isAutoMode = MCPForUnityBridge.IsAutoConnectMode();
            GUIStyle modeStyle = new GUIStyle(EditorStyles.miniLabel) { fontSize = 11 };
            EditorGUILayout.LabelField($"Mode: {(isAutoMode ? "Auto" : "Standard")}", modeStyle);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            int currentUnityPort = MCPForUnityBridge.GetCurrentPort();
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

            // Rebuild MCP Server button with tooltip tag
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                GUIContent repairLabel = new GUIContent(
                    "Rebuild MCP Server",
                    "Deletes the installed server and re-copies it from the package. Use this to update the server after making source code changes or if the installation is corrupted."
                );
                if (GUILayout.Button(repairLabel, GUILayout.Width(160), GUILayout.Height(22)))
                {
                    bool ok = global::MCPForUnity.Editor.Helpers.ServerInstaller.RebuildMcpServer();
                    if (ok)
                    {
                        EditorUtility.DisplayDialog("MCP For Unity", "Server rebuilt successfully.", "OK");
                        UpdatePythonServerInstallationStatus();
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("MCP For Unity", "Rebuild failed. Please check Console for details.", "OK");
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
                    if (GUILayout.Button("Open Install Instructions", GUILayout.Width(200)))
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
                            EditorPrefs.SetString("MCPForUnity.PythonDirOverride", pythonDirOverride);
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
            isUnityBridgeRunning = MCPForUnityBridge.IsRunning;

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

            // (Auto-connect toggle removed per design)

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
                string key = $"MCPForUnity.AutoRegistered.{ComputeSha1(projectPath)}";
                if (EditorPrefs.GetBool(key, false))
                {
                    return;
                }

                // Attempt client registration using discovered Python server dir
                pythonDirOverride ??= EditorPrefs.GetString("MCPForUnity.PythonDirOverride", null);
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
                                // Only attempt if Claude CLI is present
                                if (!IsClaudeConfigured() && !string.IsNullOrEmpty(ExecPath.ResolveClaude()))
                                {
                                    RegisterWithClaudeCode(pythonDir);
                                    anyRegistered = true;
                                }
                            }
                            else
                            {
                                CheckMcpConfiguration(client);
                                bool alreadyConfigured = client.status == McpStatus.Configured;
                                if (!alreadyConfigured)
                                {
                                    ConfigureMcpClient(client);
                                    anyRegistered = true;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            MCPForUnity.Editor.Helpers.McpLog.Warn($"Auto-setup client '{client.name}' failed: {ex.Message}");
                        }
                    }
                    lastClientRegisteredOk = anyRegistered
                        || IsCursorConfigured(pythonDir)
                        || CodexConfigHelper.IsCodexConfigured(pythonDir)
                        || IsClaudeConfigured();
                }

                // Ensure the bridge is listening and has a fresh saved port
                if (!MCPForUnityBridge.IsRunning)
                {
                    try
                    {
                        MCPForUnityBridge.StartAutoConnect();
                        isUnityBridgeRunning = MCPForUnityBridge.IsRunning;
                        Repaint();
                    }
                    catch (Exception ex)
                    {
                        MCPForUnity.Editor.Helpers.McpLog.Warn($"Auto-setup StartAutoConnect failed: {ex.Message}");
                    }
                }

                // Verify bridge with a quick ping
                lastBridgeVerifiedOk = VerifyBridgePing(MCPForUnityBridge.GetCurrentPort());

                EditorPrefs.SetBool(key, true);
            }
            catch (Exception e)
            {
                MCPForUnity.Editor.Helpers.McpLog.Warn($"MCP for Unity auto-setup skipped: {e.Message}");
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
                pythonDirOverride ??= EditorPrefs.GetString("MCPForUnity.PythonDirOverride", null);
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
                            CheckMcpConfiguration(client);
                            bool alreadyConfigured = client.status == McpStatus.Configured;
                            if (!alreadyConfigured)
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
                lastClientRegisteredOk = anyRegistered
                    || IsCursorConfigured(pythonDir)
                    || CodexConfigHelper.IsCodexConfigured(pythonDir)
                    || IsClaudeConfigured();

                // Restart/ensure bridge
                MCPForUnityBridge.StartAutoConnect();
                isUnityBridgeRunning = MCPForUnityBridge.IsRunning;

                // Verify
                lastBridgeVerifiedOk = VerifyBridgePing(MCPForUnityBridge.GetCurrentPort());
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
                // Prefer exact extraction of the --directory value and compare normalized paths
                string[] strArgs = ((System.Collections.Generic.IEnumerable<object>)args)
                    .Select(x => x?.ToString() ?? string.Empty)
                    .ToArray();
                string dir = McpConfigFileHelper.ExtractDirectoryArg(strArgs);
                if (string.IsNullOrEmpty(dir)) return false;
                return McpConfigFileHelper.PathsEqual(dir, pythonDir);
            }
            catch { return false; }
        }

        private static bool IsClaudeConfigured()
        {
            try
            {
                string claudePath = ExecPath.ResolveClaude();
                if (string.IsNullOrEmpty(claudePath)) return false;

                // Only prepend PATH on Unix
                string pathPrepend = null;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    pathPrepend = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                        ? "/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin"
                        : "/usr/local/bin:/usr/bin:/bin";
                }

                if (!ExecPath.TryRun(claudePath, "mcp list", workingDir: null, out var stdout, out var stderr, 5000, pathPrepend))
                {
                    return false;
                }
                return (stdout ?? string.Empty).IndexOf("UnityMCP", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        private static bool VerifyBridgePing(int port)
        {
            // Use strict framed protocol to match bridge (FRAMING=1)
            const int ConnectTimeoutMs = 1000;
            const int FrameTimeoutMs = 30000; // match bridge frame I/O timeout

            try
            {
                using TcpClient client = new TcpClient();
                var connectTask = client.ConnectAsync(IPAddress.Loopback, port);
                if (!connectTask.Wait(ConnectTimeoutMs)) return false;

                using NetworkStream stream = client.GetStream();
                try { client.NoDelay = true; } catch { }

                // 1) Read handshake line (ASCII, newline-terminated)
                string handshake = ReadLineAscii(stream, 2000);
                if (string.IsNullOrEmpty(handshake) || handshake.IndexOf("FRAMING=1", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    UnityEngine.Debug.LogWarning("MCP for Unity: Bridge handshake missing FRAMING=1");
                    return false;
                }

                // 2) Send framed "ping"
                byte[] payload = Encoding.UTF8.GetBytes("ping");
                WriteFrame(stream, payload, FrameTimeoutMs);

                // 3) Read framed response and check for pong
                string response = ReadFrameUtf8(stream, FrameTimeoutMs);
                bool ok = !string.IsNullOrEmpty(response) && response.IndexOf("pong", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!ok)
                {
                    UnityEngine.Debug.LogWarning($"MCP for Unity: Framed ping failed; response='{response}'");
                }
                return ok;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"MCP for Unity: VerifyBridgePing error: {ex.Message}");
                return false;
            }
        }

        // Minimal framing helpers (8-byte big-endian length prefix), blocking with timeouts
        private static void WriteFrame(NetworkStream stream, byte[] payload, int timeoutMs)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (payload.LongLength < 1) throw new IOException("Zero-length frames are not allowed");
            byte[] header = new byte[8];
            ulong len = (ulong)payload.LongLength;
            header[0] = (byte)(len >> 56);
            header[1] = (byte)(len >> 48);
            header[2] = (byte)(len >> 40);
            header[3] = (byte)(len >> 32);
            header[4] = (byte)(len >> 24);
            header[5] = (byte)(len >> 16);
            header[6] = (byte)(len >> 8);
            header[7] = (byte)(len);

            stream.WriteTimeout = timeoutMs;
            stream.Write(header, 0, header.Length);
            stream.Write(payload, 0, payload.Length);
        }

        private static string ReadFrameUtf8(NetworkStream stream, int timeoutMs)
        {
            byte[] header = ReadExact(stream, 8, timeoutMs);
            ulong len = ((ulong)header[0] << 56)
                      | ((ulong)header[1] << 48)
                      | ((ulong)header[2] << 40)
                      | ((ulong)header[3] << 32)
                      | ((ulong)header[4] << 24)
                      | ((ulong)header[5] << 16)
                      | ((ulong)header[6] << 8)
                      | header[7];
            if (len == 0UL) throw new IOException("Zero-length frames are not allowed");
            if (len > int.MaxValue) throw new IOException("Frame too large");
            byte[] payload = ReadExact(stream, (int)len, timeoutMs);
            return Encoding.UTF8.GetString(payload);
        }

        private static byte[] ReadExact(NetworkStream stream, int count, int timeoutMs)
        {
            byte[] buffer = new byte[count];
            int offset = 0;
            stream.ReadTimeout = timeoutMs;
            while (offset < count)
            {
                int read = stream.Read(buffer, offset, count - offset);
                if (read <= 0) throw new IOException("Connection closed before reading expected bytes");
                offset += read;
            }
            return buffer;
        }

        private static string ReadLineAscii(NetworkStream stream, int timeoutMs, int maxLen = 512)
        {
            stream.ReadTimeout = timeoutMs;
            using var ms = new MemoryStream();
            byte[] one = new byte[1];
            while (ms.Length < maxLen)
            {
                int n = stream.Read(one, 0, 1);
                if (n <= 0) break;
                if (one[0] == (byte)'\n') break;
                ms.WriteByte(one[0]);
            }
            return Encoding.ASCII.GetString(ms.ToArray());
        }

        private void DrawClientConfigurationCompact(McpClient mcpClient)
        {
            // Special pre-check for Claude Code: if CLI missing, reflect in status UI
            if (mcpClient.mcpType == McpTypes.ClaudeCode)
            {
                string claudeCheck = ExecPath.ResolveClaude();
                if (string.IsNullOrEmpty(claudeCheck))
                {
                    mcpClient.configStatus = "Claude Not Found";
                    mcpClient.status = McpStatus.NotConfigured;
                }
            }

            // Pre-check for clients that require uv (all except Claude Code)
            bool uvRequired = mcpClient.mcpType != McpTypes.ClaudeCode;
            bool uvMissingEarly = false;
            if (uvRequired)
            {
                string uvPathEarly = FindUvPath();
                if (string.IsNullOrEmpty(uvPathEarly))
                {
                    uvMissingEarly = true;
                    mcpClient.configStatus = "uv Not Found";
                    mcpClient.status = McpStatus.NotConfigured;
                }
            }

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
            // When Claude CLI is missing, show a clear install hint directly below status
            if (mcpClient.mcpType == McpTypes.ClaudeCode && string.IsNullOrEmpty(ExecPath.ResolveClaude()))
            {
                GUIStyle installHintStyle = new GUIStyle(clientStatusStyle);
                installHintStyle.normal.textColor = new Color(1f, 0.5f, 0f); // orange
                EditorGUILayout.BeginHorizontal();
                GUIContent installText = new GUIContent("Make sure Claude Code is installed!");
                Vector2 textSize = installHintStyle.CalcSize(installText);
                EditorGUILayout.LabelField(installText, installHintStyle, GUILayout.Height(22), GUILayout.Width(textSize.x + 2), GUILayout.ExpandWidth(false));
                GUIStyle helpLinkStyle = new GUIStyle(EditorStyles.linkLabel) { fontStyle = FontStyle.Bold };
                GUILayout.Space(6);
                if (GUILayout.Button("[HELP]", helpLinkStyle, GUILayout.Height(22), GUILayout.ExpandWidth(false)))
                {
                    Application.OpenURL("https://github.com/CoplayDev/unity-mcp/wiki/Troubleshooting-Unity-MCP-and-Claude-Code");
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(10);

            // If uv is missing for required clients, show hint and picker then exit early to avoid showing other controls
            if (uvRequired && uvMissingEarly)
            {
                GUIStyle installHintStyle2 = new GUIStyle(EditorStyles.label)
                {
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    wordWrap = false
                };
                installHintStyle2.normal.textColor = new Color(1f, 0.5f, 0f);
                EditorGUILayout.BeginHorizontal();
                GUIContent installText2 = new GUIContent("Make sure uv is installed!");
                Vector2 sz = installHintStyle2.CalcSize(installText2);
                EditorGUILayout.LabelField(installText2, installHintStyle2, GUILayout.Height(22), GUILayout.Width(sz.x + 2), GUILayout.ExpandWidth(false));
                GUIStyle helpLinkStyle2 = new GUIStyle(EditorStyles.linkLabel) { fontStyle = FontStyle.Bold };
                GUILayout.Space(6);
                if (GUILayout.Button("[HELP]", helpLinkStyle2, GUILayout.Height(22), GUILayout.ExpandWidth(false)))
                {
                    Application.OpenURL("https://github.com/CoplayDev/unity-mcp/wiki/Troubleshooting-Unity-MCP-and-Cursor,-VSCode-&-Windsurf");
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(8);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Choose uv Install Location", GUILayout.Width(260), GUILayout.Height(22)))
                {
                    string suggested = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "/opt/homebrew/bin" : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                    string picked = EditorUtility.OpenFilePanel("Select 'uv' binary", suggested, "");
                    if (!string.IsNullOrEmpty(picked))
                    {
                        EditorPrefs.SetString("MCPForUnity.UvPath", picked);
                        ConfigureMcpClient(mcpClient);
                        Repaint();
                    }
                }
                EditorGUILayout.EndHorizontal();
                return;
            }

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
                bool claudeAvailable = !string.IsNullOrEmpty(ExecPath.ResolveClaude());
                if (claudeAvailable)
                {
                    bool isConfigured = mcpClient.status == McpStatus.Configured;
                    string buttonText = isConfigured ? "Unregister MCP for Unity with Claude Code" : "Register with Claude Code";
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
                    // Hide the picker once a valid binary is available
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                    GUIStyle pathLabelStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
                    string resolvedClaude = ExecPath.ResolveClaude();
                    EditorGUILayout.LabelField($"Claude CLI: {resolvedClaude}", pathLabelStyle);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                }
                // CLI picker row (only when not found)
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                if (!claudeAvailable)
                {
                    // Only show the picker button in not-found state (no redundant "not found" label)
                    if (GUILayout.Button("Choose Claude Install Location", GUILayout.Width(260), GUILayout.Height(22)))
                    {
                        string suggested = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "/opt/homebrew/bin" : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                        string picked = EditorUtility.OpenFilePanel("Select 'claude' CLI", suggested, "");
                        if (!string.IsNullOrEmpty(picked))
                        {
                            ExecPath.SetClaudeCliPath(picked);
                            // Auto-register after setting a valid path
                            string pythonDir = FindPackagePythonDirectory();
                            RegisterWithClaudeCode(pythonDir);
                            Repaint();
                        }
                    }
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
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
                        // VSCode now reads from mcp.json with a top-level "servers" block
                        var vscodeConfig = new
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
            // Quick info (hide when Claude is not found to avoid confusion)
            bool hideConfigInfo =
                (mcpClient.mcpType == McpTypes.ClaudeCode && string.IsNullOrEmpty(ExecPath.ResolveClaude()))
                || ((mcpClient.mcpType != McpTypes.ClaudeCode) && string.IsNullOrEmpty(FindUvPath()));
            if (!hideConfigInfo)
            {
                GUIStyle configInfoStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    fontSize = 10
                };
                EditorGUILayout.LabelField($"Config: {Path.GetFileName(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? mcpClient.windowsConfigPath : mcpClient.linuxConfigPath)}", configInfoStyle);
            }
        }

        private void ToggleUnityBridge()
        {
            if (isUnityBridgeRunning)
            {
                MCPForUnityBridge.Stop();
            }
            else
            {
                MCPForUnityBridge.Start();
            }
            // Reflect the actual state post-operation (avoid optimistic toggle)
            isUnityBridgeRunning = MCPForUnityBridge.IsRunning;
            Repaint();
        }

        // New method to show manual instructions without changing status
        private void ShowManualInstructionsWindow(string configPath, McpClient mcpClient)
        {
            // Get the Python directory path using Package Manager API
            string pythonDir = FindPackagePythonDirectory();
            // Build manual JSON centrally using the shared builder
            string uvPathForManual = FindUvPath();
            if (uvPathForManual == null)
            {
                UnityEngine.Debug.LogError("UV package manager not found. Cannot generate manual configuration.");
                return;
            }

            string manualConfig = mcpClient?.mcpType == McpTypes.Codex
                ? CodexConfigHelper.BuildCodexServerBlock(uvPathForManual, McpConfigFileHelper.ResolveServerDirectory(pythonDir, null)).TrimEnd() + Environment.NewLine
                : ConfigJsonBuilder.BuildManualConfigJson(uvPathForManual, pythonDir, mcpClient);
            ManualConfigEditorWindow.ShowWindow(configPath, manualConfig, mcpClient);
        }

        private string FindPackagePythonDirectory()
        {
            // Use shared helper for consistent path resolution across both windows
            return McpPathResolver.FindPackagePythonDirectory(debugLogsEnabled);
        }

        private string ConfigureMcpClient(McpClient mcpClient)
        {
            try
            {
                // Use shared helper for consistent config path resolution
                string configPath = McpConfigurationHelper.GetClientConfigPath(mcpClient);

                // Create directory if it doesn't exist
                McpConfigurationHelper.EnsureConfigDirectoryExists(configPath);

                // Find the server.py file location using shared helper
                string pythonDir = FindPackagePythonDirectory();

                if (pythonDir == null || !File.Exists(Path.Combine(pythonDir, "server.py")))
                {
                    ShowManualInstructionsWindow(configPath, mcpClient);
                    return "Manual Configuration Required";
                }

                string result = mcpClient.mcpType == McpTypes.Codex
                    ? McpConfigurationHelper.ConfigureCodexClient(pythonDir, configPath, mcpClient)
                    : McpConfigurationHelper.WriteMcpConfiguration(pythonDir, configPath, mcpClient);

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
                )
                {
                    configPath = string.IsNullOrEmpty(mcpClient.macConfigPath)
                        ? mcpClient.linuxConfigPath
                        : mcpClient.macConfigPath;
                }
                else if (
                    RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
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

        private void LoadValidationLevelSetting()
        {
            string savedLevel = EditorPrefs.GetString("MCPForUnity_ScriptValidationLevel", "standard");
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
            EditorPrefs.SetString("MCPForUnity_ScriptValidationLevel", levelString);
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

                // Use shared helper for consistent config path resolution
                string configPath = McpConfigurationHelper.GetClientConfigPath(mcpClient);

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

                        // New schema: top-level servers
                        if (config?.servers?.unityMCP != null)
                        {
                            args = config.servers.unityMCP.args.ToObject<string[]>();
                            configExists = true;
                        }
                        // Back-compat: legacy mcp.servers
                        else if (config?.mcp?.servers?.unityMCP != null)
                        {
                            args = config.mcp.servers.unityMCP.args.ToObject<string[]>();
                            configExists = true;
                        }
                        break;

                    case McpTypes.Codex:
                        if (CodexConfigHelper.TryParseCodexServer(configJson, out _, out var codexArgs))
                        {
                            args = codexArgs;
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
                    string configuredDir = McpConfigFileHelper.ExtractDirectoryArg(args);
                    bool matches = !string.IsNullOrEmpty(configuredDir) && McpConfigFileHelper.PathsEqual(configuredDir, pythonDir);
                    if (matches)
                    {
                        mcpClient.SetStatus(McpStatus.Configured);
                    }
                    else
                    {
                        // Attempt auto-rewrite once if the package path changed
                        try
                        {
                            string rewriteResult = mcpClient.mcpType == McpTypes.Codex
                                ? McpConfigurationHelper.ConfigureCodexClient(pythonDir, configPath, mcpClient)
                                : McpConfigurationHelper.WriteMcpConfiguration(pythonDir, configPath, mcpClient);
                            if (rewriteResult == "Configured successfully")
                            {
                                if (debugLogsEnabled)
                                {
                                    MCPForUnity.Editor.Helpers.McpLog.Info($"Auto-updated MCP config for '{mcpClient.name}' to new path: {pythonDir}", always: false);
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
                                UnityEngine.Debug.LogWarning($"MCP for Unity: Auto-config rewrite failed for '{mcpClient.name}': {ex.Message}");
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
            // Resolve claude and uv; then run register command
            string claudePath = ExecPath.ResolveClaude();
            if (string.IsNullOrEmpty(claudePath))
            {
                UnityEngine.Debug.LogError("MCP for Unity: Claude CLI not found. Set a path in this window or install the CLI, then try again.");
                return;
            }
            string uvPath = ExecPath.ResolveUv() ?? "uv";

            // Prefer embedded/dev path when available
            string srcDir = !string.IsNullOrEmpty(pythonDirOverride) ? pythonDirOverride : FindPackagePythonDirectory();
            if (string.IsNullOrEmpty(srcDir)) srcDir = pythonDir;

            string args = $"mcp add UnityMCP -- \"{uvPath}\" run --directory \"{srcDir}\" server.py";

            string projectDir = Path.GetDirectoryName(Application.dataPath);
            // Ensure PATH includes common locations on Unix; on Windows leave PATH as-is
            string pathPrepend = null;
            if (Application.platform == RuntimePlatform.OSXEditor || Application.platform == RuntimePlatform.LinuxEditor)
            {
                pathPrepend = Application.platform == RuntimePlatform.OSXEditor
                    ? "/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin"
                    : "/usr/local/bin:/usr/bin:/bin";
            }
            if (!ExecPath.TryRun(claudePath, args, projectDir, out var stdout, out var stderr, 15000, pathPrepend))
            {
                string combined = ($"{stdout}\n{stderr}") ?? string.Empty;
                if (combined.IndexOf("already exists", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // Treat as success if Claude reports existing registration
                    var existingClient = mcpClients.clients.FirstOrDefault(c => c.mcpType == McpTypes.ClaudeCode);
                    if (existingClient != null) CheckClaudeCodeConfiguration(existingClient);
                    Repaint();
                    UnityEngine.Debug.Log("<b><color=#2EA3FF>MCP-FOR-UNITY</color></b>: MCP for Unity already registered with Claude Code.");
                }
                else
                {
                    UnityEngine.Debug.LogError($"MCP for Unity: Failed to start Claude CLI.\n{stderr}\n{stdout}");
                }
                return;
            }

            // Update status
            var claudeClient = mcpClients.clients.FirstOrDefault(c => c.mcpType == McpTypes.ClaudeCode);
            if (claudeClient != null) CheckClaudeCodeConfiguration(claudeClient);
            Repaint();
            UnityEngine.Debug.Log("<b><color=#2EA3FF>MCP-FOR-UNITY</color></b>: Registered with Claude Code.");
        }

        private void UnregisterWithClaudeCode()
        {
            string claudePath = ExecPath.ResolveClaude();
            if (string.IsNullOrEmpty(claudePath))
            {
                UnityEngine.Debug.LogError("MCP for Unity: Claude CLI not found. Set a path in this window or install the CLI, then try again.");
                return;
            }

            string projectDir = Path.GetDirectoryName(Application.dataPath);
            string pathPrepend = Application.platform == RuntimePlatform.OSXEditor
                ? "/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin"
                : null; // On Windows, don't modify PATH - use system PATH as-is

            // Determine if Claude has a "UnityMCP" server registered by using exit codes from `claude mcp get <name>`
            string[] candidateNamesForGet = { "UnityMCP", "unityMCP", "unity-mcp", "UnityMcpServer" };
            List<string> existingNames = new List<string>();
            foreach (var candidate in candidateNamesForGet)
            {
                if (ExecPath.TryRun(claudePath, $"mcp get {candidate}", projectDir, out var getStdout, out var getStderr, 7000, pathPrepend))
                {
                    // Success exit code indicates the server exists
                    existingNames.Add(candidate);
                }
            }

            if (existingNames.Count == 0)
            {
                // Nothing to unregister – set status and bail early
                var claudeClient = mcpClients.clients.FirstOrDefault(c => c.mcpType == McpTypes.ClaudeCode);
                if (claudeClient != null)
                {
                    claudeClient.SetStatus(McpStatus.NotConfigured);
                    UnityEngine.Debug.Log("Claude CLI reports no MCP for Unity server via 'mcp get' - setting status to NotConfigured and aborting unregister.");
                    Repaint();
                }
                return;
            }

            // Try different possible server names
            string[] possibleNames = { "UnityMCP", "unityMCP", "unity-mcp", "UnityMcpServer" };
            bool success = false;

            foreach (string serverName in possibleNames)
            {
                if (ExecPath.TryRun(claudePath, $"mcp remove {serverName}", projectDir, out var stdout, out var stderr, 10000, pathPrepend))
                {
                    success = true;
                    UnityEngine.Debug.Log($"MCP for Unity: Successfully removed MCP server: {serverName}");
                    break;
                }
                else if (!string.IsNullOrEmpty(stderr) &&
                         !stderr.Contains("No MCP server found", StringComparison.OrdinalIgnoreCase))
                {
                    // If it's not a "not found" error, log it and stop trying
                    UnityEngine.Debug.LogWarning($"Error removing {serverName}: {stderr}");
                    break;
                }
            }

            if (success)
            {
                var claudeClient = mcpClients.clients.FirstOrDefault(c => c.mcpType == McpTypes.ClaudeCode);
                if (claudeClient != null)
                {
                    // Optimistically flip to NotConfigured; then verify
                    claudeClient.SetStatus(McpStatus.NotConfigured);
                    CheckClaudeCodeConfiguration(claudeClient);
                }
                Repaint();
                UnityEngine.Debug.Log("MCP for Unity: MCP server successfully unregistered from Claude Code.");
            }
            else
            {
                // If no servers were found to remove, they're already unregistered
                // Force status to NotConfigured and update the UI
                UnityEngine.Debug.Log("No MCP servers found to unregister - already unregistered.");
                var claudeClient = mcpClients.clients.FirstOrDefault(c => c.mcpType == McpTypes.ClaudeCode);
                if (claudeClient != null)
                {
                    claudeClient.SetStatus(McpStatus.NotConfigured);
                    CheckClaudeCodeConfiguration(claudeClient);
                }
                Repaint();
            }
        }

        // Removed unused ParseTextOutput

        private string FindUvPath()
        {
            try { return MCPForUnity.Editor.Helpers.ServerInstaller.FindUvPath(); } catch { return null; }
        }

        // Validation and platform-specific scanning are handled by ServerInstaller.FindUvPath()

        // Windows-specific discovery removed; use ServerInstaller.FindUvPath() instead

        // Removed unused FindClaudeCommand

        private void CheckClaudeCodeConfiguration(McpClient mcpClient)
        {
            try
            {
                // Get the Unity project directory to check project-specific config
                string unityProjectDir = Application.dataPath;
                string projectDir = Path.GetDirectoryName(unityProjectDir);

                // Read the global Claude config file (honor macConfigPath on macOS)
                string configPath;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    configPath = mcpClient.windowsConfigPath;
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    configPath = string.IsNullOrEmpty(mcpClient.macConfigPath) ? mcpClient.linuxConfigPath : mcpClient.macConfigPath;
                else
                    configPath = mcpClient.linuxConfigPath;

                if (debugLogsEnabled)
                {
                    MCPForUnity.Editor.Helpers.McpLog.Info($"Checking Claude config at: {configPath}", always: false);
                }

                if (!File.Exists(configPath))
                {
                    UnityEngine.Debug.LogWarning($"Claude config file not found at: {configPath}");
                    mcpClient.SetStatus(McpStatus.NotConfigured);
                    return;
                }

                string configJson = File.ReadAllText(configPath);
                dynamic claudeConfig = JsonConvert.DeserializeObject(configJson);

                // Check for "UnityMCP" server in the mcpServers section (current format)
                if (claudeConfig?.mcpServers != null)
                {
                    var servers = claudeConfig.mcpServers;
                    if (servers.UnityMCP != null || servers.unityMCP != null)
                    {
                        // Found MCP for Unity configured
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
                            // Check for "UnityMCP" (case variations)
                            var servers = project.Value.mcpServers;
                            if (servers.UnityMCP != null || servers.unityMCP != null)
                            {
                                // Found MCP for Unity configured for this project
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
