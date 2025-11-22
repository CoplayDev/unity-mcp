using System;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Data;
using MCPForUnity.Editor.Helpers;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Service for managing MCP server lifecycle
    /// </summary>
    public class ServerManagementService : IServerManagementService
    {
        /// <summary>
        /// Start the local HTTP server in a new terminal window.
        /// Stops any existing server on the port and clears the uvx cache first.
        /// </summary>
        public bool StartLocalHttpServer()
        {
            if (!TryGetLocalHttpServerCommand(out var command, out var error))
            {
                EditorUtility.DisplayDialog(
                    "Cannot Start HTTP Server",
                    error ?? "The server command could not be constructed with the current settings.",
                    "OK");
                return false;
            }

            // First, try to stop any existing server
            StopLocalHttpServer();

            // Clear the cache to ensure we get a fresh version
            try
            {
                MCPServiceLocator.Cache.ClearUvxCache();
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Failed to clear cache before starting server: {ex.Message}");
            }

            if (EditorUtility.DisplayDialog(
                "Start Local HTTP Server",
                $"This will start the MCP server in HTTP mode:\n\n{command}\n\n" +
                "The server will run in a separate terminal window. " +
                "Close the terminal to stop the server.\n\n" +
                "Continue?",
                "Start Server",
                "Cancel"))
            {
                try
                {
                    // Start the server in a new terminal window (cross-platform)
                    var startInfo = CreateTerminalProcessStartInfo(command);

                    System.Diagnostics.Process.Start(startInfo);

                    McpLog.Info($"Started local HTTP server: {command}");
                    return true;
                }
                catch (Exception ex)
                {
                    McpLog.Error($"Failed to start server: {ex.Message}");
                    EditorUtility.DisplayDialog(
                        "Error",
                        $"Failed to start server: {ex.Message}",
                        "OK");
                    return false;
                }
            }

            return false;
        }

        /// <summary>
        /// Stop the local HTTP server by finding the process listening on the configured port
        /// </summary>
        public bool StopLocalHttpServer()
        {
            string httpUrl = HttpEndpointUtility.GetBaseUrl();
            if (!IsLocalUrl(httpUrl))
            {
                McpLog.Warn("Cannot stop server: URL is not local.");
                return false;
            }

            try
            {
                var uri = new Uri(httpUrl);
                int port = uri.Port;

                if (port <= 0)
                {
                    McpLog.Warn("Cannot stop server: Invalid port.");
                    return false;
                }

                int pid = GetProcessIdForPort(port);
                if (pid > 0)
                {
                    KillProcess(pid);
                    McpLog.Info($"Stopped local HTTP server on port {port} (PID: {pid})");
                    return true;
                }
                else
                {
                    McpLog.Info($"No process found listening on port {port}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                McpLog.Error($"Failed to stop server: {ex.Message}");
                return false;
            }
        }

        private int GetProcessIdForPort(int port)
        {
            try
            {
                string stdout, stderr;
                bool success;

                if (Application.platform == RuntimePlatform.WindowsEditor)
                {
                    // netstat -ano | findstr :<port>
                    success = ExecPath.TryRun("cmd.exe", $"/c netstat -ano | findstr :{port}", Application.dataPath, out stdout, out stderr);
                    if (success && !string.IsNullOrEmpty(stdout))
                    {
                        var lines = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var line in lines)
                        {
                            if (line.Contains("LISTENING"))
                            {
                                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                                if (parts.Length > 0 && int.TryParse(parts[parts.Length - 1], out int pid))
                                {
                                    return pid;
                                }
                            }
                        }
                    }
                }
                else
                {
                    // lsof -i :<port> -t
                    // Use /usr/sbin/lsof directly as it might not be in PATH for Unity
                    string lsofPath = "/usr/sbin/lsof";
                    if (!System.IO.File.Exists(lsofPath)) lsofPath = "lsof"; // Fallback

                    success = ExecPath.TryRun(lsofPath, $"-i :{port} -t", Application.dataPath, out stdout, out stderr);
                    if (success && !string.IsNullOrEmpty(stdout))
                    {
                        if (int.TryParse(stdout.Trim(), out int pid))
                        {
                            return pid;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Error checking port {port}: {ex.Message}");
            }
            return -1;
        }

        private void KillProcess(int pid)
        {
            try
            {
                string stdout, stderr;
                if (Application.platform == RuntimePlatform.WindowsEditor)
                {
                    ExecPath.TryRun("taskkill", $"/F /PID {pid}", Application.dataPath, out stdout, out stderr);
                }
                else
                {
                    ExecPath.TryRun("kill", $"-9 {pid}", Application.dataPath, out stdout, out stderr);
                }
            }
            catch (Exception ex)
            {
                McpLog.Error($"Error killing process {pid}: {ex.Message}");
            }
        }

        /// <summary>
        /// Attempts to build the command used for starting the local HTTP server
        /// </summary>
        public bool TryGetLocalHttpServerCommand(out string command, out string error)
        {
            command = null;
            error = null;

            bool useHttpTransport = EditorPrefs.GetBool(EditorPrefKeys.UseHttpTransport, true);
            if (!useHttpTransport)
            {
                error = "HTTP transport is disabled. Enable it in the MCP For Unity window first.";
                return false;
            }

            string httpUrl = HttpEndpointUtility.GetBaseUrl();
            if (!IsLocalUrl())
            {
                error = $"The configured URL ({httpUrl}) is not a local address. Local server launch only works for localhost.";
                return false;
            }

            var (uvxPath, fromUrl, packageName) = AssetPathUtility.GetUvxCommandParts();
            if (string.IsNullOrEmpty(uvxPath))
            {
                error = "UV/UVX is not installed or found in PATH. Install it or set an override in Advanced Settings.";
                return false;
            }

            string args = string.IsNullOrEmpty(fromUrl)
                ? $"{packageName} --transport http --http-url {httpUrl}"
                : $"--from {fromUrl} {packageName} --transport http --http-url {httpUrl}";

            command = $"{uvxPath} {args}";
            return true;
        }

        /// <summary>
        /// Check if the configured HTTP URL is a local address
        /// </summary>
        public bool IsLocalUrl()
        {
            string httpUrl = HttpEndpointUtility.GetBaseUrl();
            return IsLocalUrl(httpUrl);
        }

        /// <summary>
        /// Check if a URL is local (localhost, 127.0.0.1, 0.0.0.0)
        /// </summary>
        private static bool IsLocalUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;

            try
            {
                var uri = new Uri(url);
                string host = uri.Host.ToLower();
                return host == "localhost" || host == "127.0.0.1" || host == "0.0.0.0" || host == "::1";
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Check if the local HTTP server can be started
        /// </summary>
        public bool CanStartLocalServer()
        {
            bool useHttpTransport = EditorPrefs.GetBool(EditorPrefKeys.UseHttpTransport, true);
            return useHttpTransport && IsLocalUrl();
        }

        /// <summary>
        /// Creates a ProcessStartInfo for opening a terminal window with the given command
        /// Works cross-platform: macOS, Windows, and Linux
        /// </summary>
        private System.Diagnostics.ProcessStartInfo CreateTerminalProcessStartInfo(string command)
        {
#if UNITY_EDITOR_OSX
            // macOS: Use osascript to open Terminal.app
            return new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"osascript -e 'tell app \\\"Terminal\\\" to do script \\\"{command}\\\"'\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
#elif UNITY_EDITOR_WIN
            // Windows: Use cmd.exe with start command to open new window
            return new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c start cmd.exe /k \"{command}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
#else
            // Linux: Try common terminal emulators
            // Priority: gnome-terminal, xterm, konsole, xfce4-terminal
            string[] terminals = { "gnome-terminal", "xterm", "konsole", "xfce4-terminal" };
            string terminalCmd = null;
            
            foreach (var term in terminals)
            {
                try
                {
                    var which = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "which",
                        Arguments = term,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    });
                    which.WaitForExit();
                    if (which.ExitCode == 0)
                    {
                        terminalCmd = term;
                        break;
                    }
                }
                catch { }
            }
            
            if (terminalCmd == null)
            {
                terminalCmd = "xterm"; // Fallback
            }
            
            // Different terminals have different argument formats
            string args;
            if (terminalCmd == "gnome-terminal")
            {
                args = $"-- bash -c \"{command}; exec bash\"";
            }
            else if (terminalCmd == "konsole")
            {
                args = $"-e bash -c \"{command}; exec bash\"";
            }
            else if (terminalCmd == "xfce4-terminal")
            {
                args = $"--hold -e \"bash -c '{command}'\"";
            }
            else // xterm and others
            {
                args = $"-hold -e bash -c \"{command}\"";
            }
            
            return new System.Diagnostics.ProcessStartInfo
            {
                FileName = terminalCmd,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true
            };
#endif
        }
    }
}
