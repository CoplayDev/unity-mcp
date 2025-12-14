using System;
using System.IO;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Constants;
using System.Threading.Tasks;

namespace MCPForUnity.Editor.Setup
{
    /// <summary>
    /// Automates the setup of the Python server environment (venv, dependencies)
    /// </summary>
    public static class ServerEnvironmentSetup
    {
        public static string ServerRoot => Path.Combine(AssetPathUtility.GetPackageAbsolutePath(), "Server~");
        // Corrected path: .venv is at the root of Server~, not inside Server~/Server
        public static string VenvPath => Path.Combine(ServerRoot, ".venv");
        public static string RequirementsPath => Path.Combine(ServerRoot, "Server", "requirements.txt");

        public static bool IsEnvironmentReady(string packageRootPath = null)
        {
            string root = packageRootPath ?? AssetPathUtility.GetPackageAbsolutePath();
            if (string.IsNullOrEmpty(root)) 
            {
                McpLog.Warn("[MCP Setup] Package root is null or empty.");
                return false;
            }
            
            string serverRoot = Path.Combine(root, "Server~");
            string venvPath = Path.Combine(serverRoot, ".venv");
            
            string venvPython = Path.Combine(venvPath, "Scripts", "python.exe");
            if (!File.Exists(venvPython))
            {
                venvPython = Path.Combine(venvPath, "bin", "python");
            }
            
            bool exists = File.Exists(venvPython);
            // Debug Log (Remove later)
            if (!exists) 
            {
                McpLog.Warn($"[MCP Setup] Python not found at: {venvPython}");
                // Also check if serverRoot exists
                if (!Directory.Exists(serverRoot)) McpLog.Warn($"[MCP Setup] Server root directory not found: {serverRoot}");
            }
            
            return exists;
        }

        public static void InstallServerEnvironment()
        {
            // 0. Pre-check prerequisites (Python & Node)
            bool hasPython = CheckPython();
            bool hasNode = CheckNode();

            if (!hasPython || !hasNode)
            {
                SetupWindowService.ShowSetupWindow();
                return;
            }

            try
            {
                // 1. Check/Install uv
                EditorUtility.DisplayProgressBar("MCP Setup", "Checking 'uv' package manager...", 0.3f);
                string uvPath = GetOrInstallUv();
                if (string.IsNullOrEmpty(uvPath))
                {
                   McpLog.Warn("Could not find 'uv'. Falling back to standard 'pip' for installation (slower).");
                }

                // 3. Create venv (Skip if uv sync is used, as it handles venv creation)
                // However, for safety and fallback support, we can still ensure it exists or let uv handle it.
                // If using uv sync, we don't necessarily need to manually create venv, but doing so explicitly doesn't hurt.
                if (string.IsNullOrEmpty(uvPath))
                {
                    EditorUtility.DisplayProgressBar("MCP Setup", "Creating virtual environment...", 0.5f);
                    if (!CreateVenv())
                    {
                        EditorUtility.ClearProgressBar();
                        EditorUtility.DisplayDialog("Error", "Failed to create virtual environment.", "OK");
                        return;
                    }
                }

                // 4. Install Dependencies
                EditorUtility.DisplayProgressBar("MCP Setup", "Syncing dependencies...", 0.7f);
                if (!InstallDependencies(uvPath))
                {
                    EditorUtility.ClearProgressBar();
                    EditorUtility.DisplayDialog("Error", "Failed to install dependencies.", "OK");
                    return;
                }

                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Success", "MCP Server environment setup complete!\n\nYou can now connect using Cursor/Claude.", "OK");
            }
            catch (Exception ex)
            {
                EditorUtility.ClearProgressBar();
                McpLog.Error($"[MCP Setup] Error: {ex}");
                EditorUtility.DisplayDialog("Setup Failed", 
                    $"Setup failed: {ex.Message}\n\nRunning processes might be locking files, or PATH environment variables might need a refresh.\n\nTry restarting Unity (or your computer) and run Setup again.", "OK");
            }
        }

        private static bool CheckPython()
        {
            string pythonCmd = GetPythonCommand();
            if (RunCommand(pythonCmd, "--version", out string output))
            {
                output = output.Trim();
                // Output format: "Python 3.x.x"
                if (output.StartsWith("Python "))
                {
                    string versionStr = output.Substring(7);
                    string[] parts = versionStr.Split('.');
                    if (parts.Length >= 2 && 
                        int.TryParse(parts[0], out int major) && 
                        int.TryParse(parts[1], out int minor))
                    {
                        if (major > 3 || (major == 3 && minor >= 11)) return true;
                        else
                        {
                            McpLog.Error($"[MCP Setup] Python version {versionStr} is too old. Required: 3.11+");
                            return false;
                        }
                    }
                }
                McpLog.Warn($"[MCP Setup] Could not parse Python version: {output}");
                return false;
            }
            return false;
        }

        private static bool CheckNode()
        {
            // Use override if available
            return RunCommand(GetNodeCommand(), "--version", out _);
        }

        public static string InstallUvExplicitly()
        {
            return GetOrInstallUv();
        }

        private static string GetOrInstallUv()
        {
            // Check if uv is already in PATH
            if (RunCommand("uv", "--version", out _)) return "uv";

            // Check if uv is in the known install locations (override or default)
            string overridePath = EditorPrefs.GetString(EditorPrefKeys.UvPathOverride, "");
            if (!string.IsNullOrEmpty(overridePath) && File.Exists(overridePath)) return overridePath;

            McpLog.Info("[MCP Setup] 'uv' not found in PATH. Installing via pip...");
            
            string pythonCmd = GetPythonCommand();

            // Install via pip
            if (!RunCommand(pythonCmd, "-m pip install uv", out string pipOutput))
            {
                McpLog.Warn($"Failed to install uv via pip: {pipOutput}");
                return null;
            }

            // Check if uv is now in PATH
            if (RunCommand("uv", "--version", out _)) return "uv";

            // Try to find uv in likely locations (Fallback logic)
            string findUvScript = "import site, os, sys; " +
                "candidates = [" +
                "os.path.join(sys.prefix, 'Scripts', 'uv.exe'), " +
                "os.path.join(sys.prefix, 'bin', 'uv'), " +
                "os.path.join(site.getuserbase(), 'Scripts', 'uv.exe'), " +
                "os.path.join(site.getuserbase(), 'bin', 'uv')" +
                "]; " +
                "found = next((p for p in candidates if os.path.exists(p)), ''); " +
                "print(found)";

            if (RunCommand(pythonCmd, $"-c \"{findUvScript}\"", out string foundPath))
            {
                foundPath = foundPath.Trim();
                if (!string.IsNullOrEmpty(foundPath) && File.Exists(foundPath)) return foundPath;
            }

            return null;
        }

        /// <summary>
        /// Creates a Python virtual environment manually. No-op if UV is available (UV handles venv creation).
        /// </summary>
        private static bool CreateVenv()
        {
            if (Directory.Exists(VenvPath))
            {
                McpLog.Info("[MCP Setup] Cleaning existing .venv...");
                try { Directory.Delete(VenvPath, true); } catch { /* ignore */ }
            }

            McpLog.Info($"[MCP Setup] Creating virtual environment at: {VenvPath}");
            return RunCommand(GetPythonCommand(), $"-m venv \"{VenvPath}\"", out string output, workingDirectory: ServerRoot);
        }

        private static bool InstallDependencies(string uvPath)
        {
            string workingDir = Path.GetFullPath(ServerRoot);

            if (!string.IsNullOrEmpty(uvPath))
            {
                McpLog.Info("[MCP Setup] Using 'uv sync' to install dependencies...");
                // uv sync will create .venv if needed and install everything defined in pyproject.toml
                // Removed --frozen to allow lock file generation if missing
                return RunCommand(uvPath, "sync", out string output, workingDirectory: workingDir);
            }
            else
            {
                // Fallback for standard pip
                string venvPython = Path.Combine(VenvPath, "Scripts", "python.exe");
                if (!File.Exists(venvPython)) venvPython = Path.Combine(VenvPath, "bin", "python");
                venvPython = Path.GetFullPath(venvPython);

                if (!File.Exists(venvPython))
                {
                    McpLog.Error($"[MCP Setup] Virtual environment python not found at: {venvPython}");
                    return false;
                }

                McpLog.Info("[MCP Setup] Using standard pip to install dependencies...");
                return RunCommand(venvPython, "-m pip install -e .", out string output, workingDirectory: workingDir);
            }
        }

        private static bool RunCommand(string fileName, string arguments, out string output, string workingDirectory = null)
        {
            output = "";
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = fileName;
                psi.Arguments = arguments;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                
                if (!string.IsNullOrEmpty(workingDirectory))
                {
                    psi.WorkingDirectory = workingDirectory;
                }

                using (Process p = Process.Start(psi))
                {
                    if (p == null)
                    {
                        McpLog.Error($"[MCP Setup] Failed to start process: {fileName}");
                        return false;
                    }

                    // Read streams asynchronously to prevent deadlock on buffer fill
                    // Note: ReadToEndAsync is not available in .NET Standard 2.0 (Unity's default profile), using wrapper or async delegate
                    var outputTask = Task.Run(() => p.StandardOutput.ReadToEnd());
                    var errorTask = Task.Run(() => p.StandardError.ReadToEnd());
                    
                    // 2 minute timeout to prevent hanging indefinitely
                    if (!p.WaitForExit(120000)) 
                    {
                        try { p.Kill(); } catch {}
                        McpLog.Error($"[MCP Setup] Command timed out: {fileName} {arguments}");
                        return false;
                    }

                    output = outputTask.Result;
                    string error = errorTask.Result;
                    
                    // 2 minute timeout to prevent hanging indefinitely
                    if (!p.WaitForExit(120000)) 
                    {
                        try { p.Kill(); } catch {}
                        McpLog.Error($"[MCP Setup] Command timed out: {fileName} {arguments}");
                        return false;
                    }

                    if (p.ExitCode != 0)
                    {
                        McpLog.Error($"[MCP Setup] Command failed: {fileName} {arguments}\nOutput: {output}\nError: {error}");
                        return false;
                    }
                    return true;
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // File not found (common when checking if a tool exists in PATH).
                // Do not log as Error to avoid confusing the user.
                return false;
            }
            catch (Exception ex)
            {
                McpLog.Error($"[MCP Setup] Exception running command '{fileName} {arguments}': {ex.Message}");
                return false;
            }
        }

        private static string GetPythonCommand()
        {
            string overridePath = EditorPrefs.GetString(EditorPrefKeys.PythonPathOverride, "");
            if (!string.IsNullOrEmpty(overridePath) && File.Exists(overridePath))
            {
                return overridePath;
            }
            return "python";
        }

        private static string GetNodeCommand()
        {
            string overridePath = EditorPrefs.GetString(EditorPrefKeys.NodePathOverride, "");
            if (!string.IsNullOrEmpty(overridePath) && File.Exists(overridePath))
            {
                return overridePath;
            }
            return "node";
        }
    }
}
