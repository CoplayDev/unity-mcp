using System;
using System.IO;
using System.Linq;
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
        private static bool _cleanupRegistered = false;
        
        /// <summary>
        /// Register cleanup handler for Unity exit
        /// </summary>
        private static void EnsureCleanupRegistered()
        {
            if (_cleanupRegistered) return;
            
            EditorApplication.quitting += () =>
            {
                // Try to stop the HTTP server when Unity exits
                try
                {
                    var service = new ServerManagementService();
                    service.StopLocalHttpServer();
                }
                catch (Exception ex)
                {
                    McpLog.Debug($"Cleanup on exit: {ex.Message}");
                }
            };
            
            _cleanupRegistered = true;
        }
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

                McpLog.Warn(
                    $"Failed to clear uv cache using '{uvCommand} {args}'. " +
                    $"Details: {combinedOutput}{(string.IsNullOrEmpty(lockHint) ? string.Empty : " Hint: " + lockHint)}");
                
                // Cache clearing failure is not critical, so we can return true to proceed
                return true; 
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Error clearing uv cache: {ex.Message}");
                return true; // Proceed anyway
            }
        }

        private bool ExecuteUvCommand(string uvCommand, string args, out string stdout, out string stderr)
        {
            stdout = null;
            stderr = null;

            string uvxPath = MCPServiceLocator.Paths.GetUvxPath();
            string uvPath = BuildUvPathFromUvx(uvxPath);

            string extraPathPrepend = GetPlatformSpecificPathPrepend();

            if (!string.Equals(uvCommand, uvPath, StringComparison.OrdinalIgnoreCase))
            {
                // Timeout reduced to 2 seconds to prevent UI freezing
                return ExecPath.TryRun(uvCommand, args, Application.dataPath, out stdout, out stderr, 2000, extraPathPrepend);
            }

            string command = $"{uvPath} {args}";

            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                return ExecPath.TryRun("cmd.exe", $"/c {command}", Application.dataPath, out stdout, out stderr, 2000, extraPathPrepend);
            }

            string shell = File.Exists("/bin/bash") ? "/bin/bash" : "/bin/sh";

            if (!string.IsNullOrEmpty(shell) && File.Exists(shell))
            {
                string escaped = command.Replace("\"", "\\\"");
                return ExecPath.TryRun(shell, $"-lc \"{escaped}\"", Application.dataPath, out stdout, out stderr, 2000, extraPathPrepend);
            }

            return ExecPath.TryRun(uvPath, args, Application.dataPath, out stdout, out stderr, 2000, extraPathPrepend);
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

            // Clear the cache to ensure we get a fresh version
            try
            {
                ClearUvxCache();
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Failed to clear cache before starting server: {ex.Message}");
            }

            if (EditorUtility.DisplayDialog(
                "Start Local HTTP Server",
                $"This will start the MCP server in HTTP mode:\n\n{command}\n\n" +
                "The server will run in a separate terminal window. " +
                "Use 'Stop Server' button or close Unity to stop the server.\n\n" +
                "Continue?",
                "Start Server",
                "Cancel"))
            {
                try
                {
                    // Register cleanup handler for Unity exit
                    EnsureCleanupRegistered();

                    System.Diagnostics.ProcessStartInfo startInfo;

                    if (Application.platform == RuntimePlatform.WindowsEditor)
                    {
                        // Use a batch file to avoid complex escaping issues on Windows (OS error 123)
                        string tempBatPath = Path.Combine(Path.GetTempPath(), "start_mcp_server.bat");
                        
                        // Add title and pause for better UX
                        string batContent = "@echo off\r\n";
                        batContent += "set PYTHONUNBUFFERED=1\r\n"; // Fix: Prevent stdout buffering which can block SSE/logging
                        batContent += $"title MCP Server (Port {HttpEndpointUtility.GetBaseUrl()})\r\n";
                        batContent += "echo Starting MCP Server...\r\n";
                        batContent += command + "\r\n";
                        batContent += "if %errorlevel% neq 0 pause\r\n"; // Pause only on error
                        
                        File.WriteAllText(tempBatPath, batContent);
                        McpLog.Debug($"Created batch file at: {tempBatPath}");

                        // Execute the batch file in a new window
                        startInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "cmd.exe",
                            Arguments = $"/c start \"MCP Server\" \"{tempBatPath}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                    }
                    else
                    {
                        // Start the server in a new terminal window (cross-platform for Mac/Linux)
                         startInfo = CreateTerminalProcessStartInfo(command);
                    }

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

                McpLog.Info($"Attempting to stop any process listening on local port {port}. This will terminate the owning process even if it is not the MCP server.");

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
                    if (success && !string.IsNullOrWhiteSpace(stdout))
                    {
                        var pidStrings = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var pidString in pidStrings)
                        {
                            if (int.TryParse(pidString.Trim(), out int pid))
                            {
                                if (pidStrings.Length > 1)
                                {
                                    McpLog.Debug($"Multiple processes found on port {port}; attempting to stop PID {pid} returned by lsof -t.");
                                }

                                return pid;
                            }
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
            
            // NOTE: When running from local source (Server~), we should execute via python directly
            // rather than uvx, because 'mcp-for-unity' package name won't be valid for uvx
            // unless we install it. So we detect if we are running from a local path.
            
            string localServerPath = AssetPathUtility.GetWrapperJsPath(); // Wrapper implies local path? No, dedicated method better.
            string packageRoot = AssetPathUtility.GetPackageAbsolutePath();
            bool isLocalSource = false;
            string serverSrcPath = null;
            
            if (!string.IsNullOrEmpty(packageRoot))
            {
                string checkPath = Path.Combine(packageRoot, "Server~");
                if (Directory.Exists(checkPath))
                {
                     isLocalSource = true;
                     serverSrcPath = checkPath;
                }
                else
                {
                    checkPath = Path.Combine(packageRoot, "Server");
                     if (Directory.Exists(checkPath))
                    {
                         isLocalSource = true;
                         serverSrcPath = checkPath;
                    }
                }
            }

            if (isLocalSource && !string.IsNullOrEmpty(serverSrcPath))
            {
                 // Local execution mode:
                 // uv run --directory "{serverSrcPath}" python -m src.server --transport http --http-url {httpUrl}
                 // This ensures dependencies in pyproject.toml are respected and src module is found.
                 
                 string uvPath = BuildUvPathFromUvx(uvxPath);
                 if (string.IsNullOrEmpty(uvPath)) uvPath = "uv"; // Fallback to PATH if empty

                 // Fix: Ensure proper quoting for executable path if it has spaces
                 string safeUvPath = uvPath.Contains(" ") && !uvPath.StartsWith("\"") ? $"\"{uvPath}\"" : uvPath;
                 
                 // Fix: Ensure directory path is clean and safe for quoting
                 // Trim trailing directory separators to prevent escaping the closing quote
                 string safeSrcPath = serverSrcPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                 
                 // Add -u flag to force unbuffered stdout/stderr, critical for SSE stability on Windows
                 command = $"{safeUvPath} run --directory \"{safeSrcPath}\" python -u -m src.main --transport http --http-url {httpUrl}";
                 return true;
            }

            // Fallback to uvx (remote execution)
            if (string.IsNullOrEmpty(uvxPath))
            {
                error = "uv is not installed or found in PATH. Install it or set an override in Advanced Settings.";
                return false;
            }

            // Quote the path if it contains spaces and isn't already quoted
            string finalUvxPath = uvxPath;
            if (uvxPath.Contains(" ") && !uvxPath.StartsWith("\""))
            {
                finalUvxPath = $"\"{uvxPath}\"";
            }

            string args = string.IsNullOrEmpty(fromUrl)
                ? $"{packageName} --transport http --http-url {httpUrl}"
                : $"--from {fromUrl} {packageName} --transport http --http-url {httpUrl}";

            command = $"{finalUvxPath} {args}";
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
                string host = uri.Host.ToLowerInvariant();
                return host == "localhost" || host == "127.0.0.1" || host == "0.0.0.0" || host == "::1";
            }
            catch
            {
                // Fallback for simple localhost strings without scheme
                if (url.StartsWith("localhost") || url.StartsWith("127.0.0.1")) return true;
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
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                // We need to inject PATH into the new cmd window.
                // Since 'start' launches a separate process, we'll try to set PATH before running the command.
                // Note: 'start' inherits environment variables, so setting them on this ProcessStartInfo should work.
                Arguments = $"/c start \"MCP Server\" cmd.exe /k \"{escapedCommandWin}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // Inject PATH
            string pathPrepend = GetPlatformSpecificPathPrepend();
            if (!string.IsNullOrEmpty(pathPrepend))
            {
                string currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
                psi.EnvironmentVariables["PATH"] = pathPrepend + Path.PathSeparator + currentPath;
            }
            return psi;
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
                    using var which = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "which",
                        Arguments = term,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    });
                    
                    if (which != null)
                    {
                        if (!which.WaitForExit(5000))
                        {
                            // Timeout - kill the process
                            try 
                            { 
                                if (!which.HasExited)
                                {
                                    which.Kill();
                                }
                            } 
                            catch { }
                        }
                        else if (which.ExitCode == 0)
                        {
                            terminalCmd = term;
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    McpLog.Debug($"Terminal check failed for {term}: {ex.Message}");
                }
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
