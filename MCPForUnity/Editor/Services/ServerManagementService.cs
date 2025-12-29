using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using MCPForUnity.Editor.Constants;
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
        /// Clear the local uvx cache for the MCP server package
        /// </summary>
        /// <returns>True if successful, false otherwise</returns>
        public bool ClearUvxCache()
        {
            try
            {
                string uvxPath = MCPServiceLocator.Paths.GetUvxPath();
                string uvCommand = BuildUvPathFromUvx(uvxPath);

                // Get the package name
                string packageName = "mcp-for-unity";

                // Run uvx cache clean command
                string args = $"cache clean {packageName}";

                bool success;
                string stdout;
                string stderr;

                success = ExecuteUvCommand(uvCommand, args, out stdout, out stderr);

                if (success)
                {
                    McpLog.Debug($"uv cache cleared successfully: {stdout}");
                    return true;
                }
                string combinedOutput = string.Join(
                    Environment.NewLine,
                    new[] { stderr, stdout }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()));

                string lockHint = (!string.IsNullOrEmpty(combinedOutput) &&
                                   combinedOutput.IndexOf("currently in-use", StringComparison.OrdinalIgnoreCase) >= 0)
                    ? "Another uv process may be holding the cache lock; wait a moment and try again or clear with '--force' from a terminal."
                    : string.Empty;

                if (string.IsNullOrEmpty(combinedOutput))
                {
                    combinedOutput = "Command failed with no output. Ensure uv is installed, on PATH, or set an override in Advanced Settings.";
                }

                McpLog.Error(
                    $"Failed to clear uv cache using '{uvCommand} {args}'. " +
                    $"Details: {combinedOutput}{(string.IsNullOrEmpty(lockHint) ? string.Empty : " Hint: " + lockHint)}");
                return false;
            }
            catch (Exception ex)
            {
                McpLog.Error($"Error clearing uv cache: {ex.Message}");
                return false;
            }
        }

        private bool ExecuteUvCommand(string uvCommand, string args, out string stdout, out string stderr)
        {
            stdout = null;
            stderr = null;

            string uvxPath = MCPServiceLocator.Paths.GetUvxPath();
            string uvPath = BuildUvPathFromUvx(uvxPath);

            if (!string.Equals(uvCommand, uvPath, StringComparison.OrdinalIgnoreCase))
            {
                return ExecPath.TryRun(uvCommand, args, Application.dataPath, out stdout, out stderr, 30000);
            }

            string command = $"{uvPath} {args}";
            string extraPathPrepend = GetPlatformSpecificPathPrepend();

            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                return ExecPath.TryRun("cmd.exe", $"/c {command}", Application.dataPath, out stdout, out stderr, 30000, extraPathPrepend);
            }

            string shell = File.Exists("/bin/bash") ? "/bin/bash" : "/bin/sh";

            if (!string.IsNullOrEmpty(shell) && File.Exists(shell))
            {
                string escaped = command.Replace("\"", "\\\"");
                return ExecPath.TryRun(shell, $"-lc \"{escaped}\"", Application.dataPath, out stdout, out stderr, 30000, extraPathPrepend);
            }

            return ExecPath.TryRun(uvPath, args, Application.dataPath, out stdout, out stderr, 30000, extraPathPrepend);
        }

        private static string BuildUvPathFromUvx(string uvxPath)
        {
            if (string.IsNullOrWhiteSpace(uvxPath))
            {
                return uvxPath;
            }

            string directory = Path.GetDirectoryName(uvxPath);
            string extension = Path.GetExtension(uvxPath);
            string uvFileName = "uv" + extension;

            return string.IsNullOrEmpty(directory)
                ? uvFileName
                : Path.Combine(directory, uvFileName);
        }

        private string GetPlatformSpecificPathPrepend()
        {
            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                return string.Join(Path.PathSeparator.ToString(), new[]
                {
                    "/opt/homebrew/bin",
                    "/usr/local/bin",
                    "/usr/bin",
                    "/bin"
                });
            }

            if (Application.platform == RuntimePlatform.LinuxEditor)
            {
                return string.Join(Path.PathSeparator.ToString(), new[]
                {
                    "/usr/local/bin",
                    "/usr/bin",
                    "/bin"
                });
            }

            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

                return string.Join(Path.PathSeparator.ToString(), new[]
                {
                    !string.IsNullOrEmpty(localAppData) ? Path.Combine(localAppData, "Programs", "uv") : null,
                    !string.IsNullOrEmpty(programFiles) ? Path.Combine(programFiles, "uv") : null
                }.Where(p => !string.IsNullOrEmpty(p)).ToArray());
            }

            return null;
        }

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

            // Note: Dev mode cache-busting is handled by `uvx --no-cache --refresh` in the generated command.

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

                // Guardrails:
                // - Never terminate the Unity Editor process.
                // - Only terminate processes that look like the MCP server (uv/uvx/python running mcp-for-unity).
                // This prevents accidental termination of unrelated services (including Unity itself).
                int unityPid = GetCurrentProcessIdSafe();

                var pids = GetListeningProcessIdsForPort(port);
                if (pids.Count == 0)
                {
                    McpLog.Info($"No process found listening on port {port}");
                    return false;
                }

                bool stoppedAny = false;
                foreach (var pid in pids)
                {
                    if (pid <= 0) continue;
                    if (unityPid > 0 && pid == unityPid)
                    {
                        McpLog.Warn($"Refusing to stop port {port}: owning PID appears to be the Unity Editor process (PID {pid}).");
                        continue;
                    }

                    if (!LooksLikeMcpServerProcess(pid))
                    {
                        McpLog.Warn($"Refusing to stop port {port}: owning PID {pid} does not look like mcp-for-unity (uvx/uv/python).");
                        continue;
                    }

                    if (TerminateProcess(pid))
                    {
                        McpLog.Info($"Stopped local HTTP server on port {port} (PID: {pid})");
                        stoppedAny = true;
                    }
                    else
                    {
                        McpLog.Warn($"Failed to stop process PID {pid} on port {port}");
                    }
                }

                return stoppedAny;
            }
            catch (Exception ex)
            {
                McpLog.Error($"Failed to stop server: {ex.Message}");
                return false;
            }
        }

        private List<int> GetListeningProcessIdsForPort(int port)
        {
            var results = new List<int>();
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
                                    results.Add(pid);
                                }
                            }
                        }
                    }
                }
                else
                {
                    // lsof: only return LISTENers (avoids capturing random clients)
                    // Use /usr/sbin/lsof directly as it might not be in PATH for Unity
                    string lsofPath = "/usr/sbin/lsof";
                    if (!System.IO.File.Exists(lsofPath)) lsofPath = "lsof"; // Fallback

                    // -nP: avoid DNS/service name lookups; faster and less error-prone
                    success = ExecPath.TryRun(lsofPath, $"-nP -iTCP:{port} -sTCP:LISTEN -t", Application.dataPath, out stdout, out stderr);
                    if (success && !string.IsNullOrWhiteSpace(stdout))
                    {
                        var pidStrings = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var pidString in pidStrings)
                        {
                            if (int.TryParse(pidString.Trim(), out int pid))
                            {
                                results.Add(pid);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Error checking port {port}: {ex.Message}");
            }
            return results.Distinct().ToList();
        }

        private static int GetCurrentProcessIdSafe()
        {
            try { return System.Diagnostics.Process.GetCurrentProcess().Id; }
            catch { return -1; }
        }

        private bool LooksLikeMcpServerProcess(int pid)
        {
            try
            {
                // Windows best-effort: tasklist /FI "PID eq X"
                if (Application.platform == RuntimePlatform.WindowsEditor)
                {
                    if (ExecPath.TryRun("cmd.exe", $"/c tasklist /FI \"PID eq {pid}\"", Application.dataPath, out var stdout, out var stderr, 5000))
                    {
                        string combined = (stdout ?? string.Empty) + "\n" + (stderr ?? string.Empty);
                        combined = combined.ToLowerInvariant();
                        // Common process names: python.exe, uv.exe, uvx.exe
                        return combined.Contains("python") || combined.Contains("uvx") || combined.Contains("uv.exe") || combined.Contains("uvx.exe");
                    }
                    return false;
                }

                // macOS/Linux: ps -p pid -o comm= -o args=
                if (ExecPath.TryRun("ps", $"-p {pid} -o comm= -o args=", Application.dataPath, out var psOut, out var psErr, 5000))
                {
                    string s = (psOut ?? string.Empty).Trim().ToLowerInvariant();
                    if (string.IsNullOrEmpty(s))
                    {
                        s = (psErr ?? string.Empty).Trim().ToLowerInvariant();
                    }

                    // Explicitly never kill Unity / Unity Hub processes
                    if (s.Contains("unity") || s.Contains("unityhub") || s.Contains("unity hub"))
                    {
                        return false;
                    }

                    // Positive indicators
                    bool mentionsUvx = s.Contains("uvx") || s.Contains(" uvx ");
                    bool mentionsUv = s.Contains("uv ") || s.Contains("/uv");
                    bool mentionsPython = s.Contains("python");
                    bool mentionsMcp = s.Contains("mcp-for-unity") || s.Contains("mcp_for_unity") || s.Contains("mcp for unity");
                    bool mentionsTransport = s.Contains("--transport") && s.Contains("http");

                    // Accept if it looks like uv/uvx/python launching our server package/entrypoint
                    if ((mentionsUvx || mentionsUv || mentionsPython) && (mentionsMcp || mentionsTransport))
                    {
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        private bool TerminateProcess(int pid)
        {
            try
            {
                string stdout, stderr;
                if (Application.platform == RuntimePlatform.WindowsEditor)
                {
                    // taskkill without /F first; fall back to /F if needed.
                    bool ok = ExecPath.TryRun("taskkill", $"/PID {pid}", Application.dataPath, out stdout, out stderr);
                    if (!ok)
                    {
                        ok = ExecPath.TryRun("taskkill", $"/F /PID {pid}", Application.dataPath, out stdout, out stderr);
                    }
                    return ok;
                }
                else
                {
                    // Try a graceful termination first, then escalate.
                    bool ok = ExecPath.TryRun("kill", $"-15 {pid}", Application.dataPath, out stdout, out stderr);
                    if (!ok)
                    {
                        ok = ExecPath.TryRun("kill", $"-9 {pid}", Application.dataPath, out stdout, out stderr);
                    }
                    return ok;
                }
            }
            catch (Exception ex)
            {
                McpLog.Error($"Error killing process {pid}: {ex.Message}");
                return false;
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
                error = "uv is not installed or found in PATH. Install it or set an override in Advanced Settings.";
                return false;
            }

            bool devForceRefresh = false;
            try { devForceRefresh = EditorPrefs.GetBool(EditorPrefKeys.DevModeForceServerRefresh, false); } catch { }

            string devFlags = devForceRefresh ? "--no-cache --refresh " : string.Empty;
            string args = string.IsNullOrEmpty(fromUrl)
                ? $"{devFlags}{packageName} --transport http --http-url {httpUrl}"
                : $"{devFlags}--from {fromUrl} {packageName} --transport http --http-url {httpUrl}";

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
            if (string.IsNullOrWhiteSpace(command))
                throw new ArgumentException("Command cannot be empty", nameof(command));

            command = command.Replace("\r", "").Replace("\n", "");

#if UNITY_EDITOR_OSX
            // macOS: Use osascript directly to avoid shell metacharacter injection via bash
            // Escape for AppleScript: backslash and double quotes
            string escapedCommand = command.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/usr/bin/osascript",
                Arguments = $"-e \"tell application \\\"Terminal\\\" to do script \\\"{escapedCommand}\\\" activate\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
#elif UNITY_EDITOR_WIN
            // Windows: Use cmd.exe with start command to open new window
            // Wrap in quotes for /k and escape internal quotes
            string escapedCommandWin = command.Replace("\"", "\\\"");
            return new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c start \"MCP Server\" cmd.exe /k \"{escapedCommandWin}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
#else
            // Linux: Try common terminal emulators
            // We use bash -c to execute the command, so we must properly quote/escape for bash
            // Escape single quotes for the inner bash string
            string escapedCommandLinux = command.Replace("'", "'\\''");
            // Wrap the command in single quotes for bash -c
            string script = $"'{escapedCommandLinux}; exec bash'";
            // Escape double quotes for the outer Process argument string
            string escapedScriptForArg = script.Replace("\"", "\\\"");
            string bashCmdArgs = $"bash -c \"{escapedScriptForArg}\"";
            
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
                    which.WaitForExit(5000); // Wait for up to 5 seconds, the command is typically instantaneous
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
                args = $"-- {bashCmdArgs}";
            }
            else if (terminalCmd == "konsole")
            {
                args = $"-e {bashCmdArgs}";
            }
            else if (terminalCmd == "xfce4-terminal")
            {
                // xfce4-terminal expects -e "command string" or -e command arg
                args = $"--hold -e \"{bashCmdArgs.Replace("\"", "\\\"")}\"";
            }
            else // xterm and others
            {
                args = $"-hold -e {bashCmdArgs}";
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
