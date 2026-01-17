using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Dependencies.Models;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;

namespace MCPForUnity.Editor.Dependencies.PlatformDetectors
{
    /// <summary>
    /// Windows-specific dependency detection
    /// </summary>
    public class WindowsPlatformDetector : PlatformDetectorBase
    {
        public override string PlatformName => "Windows";

        public override bool CanDetect => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        public override DependencyStatus DetectPython()
        {
            var status = new DependencyStatus("Python", isRequired: true)
            {
                InstallationHint = GetPythonInstallUrl()
            };

            try
            {
                // Try running python directly first (works with Windows App Execution Aliases)
                if (TryValidatePython("python3.exe", out string version, out string fullPath) ||
                    TryValidatePython("python.exe", out version, out fullPath))
                {
                    status.IsAvailable = true;
                    status.Version = version;
                    status.Path = fullPath;
                    status.Details = $"Found Python {version} in PATH";
                    return status;
                }

                // Fallback: try 'where' command
                if (TryFindInPath("python3.exe", out string pathResult) ||
                    TryFindInPath("python.exe", out pathResult))
                {
                    if (TryValidatePython(pathResult, out version, out fullPath))
                    {
                        status.IsAvailable = true;
                        status.Version = version;
                        status.Path = fullPath;
                        status.Details = $"Found Python {version} in PATH";
                        return status;
                    }
                }

                // Fallback: try to find python via uv
                if (TryFindPythonViaUv(out version, out fullPath))
                {
                    status.IsAvailable = true;
                    status.Version = version;
                    status.Path = fullPath;
                    status.Details = $"Found Python {version} via uv";
                    return status;
                }

                status.ErrorMessage = "Python not found in PATH";
                status.Details = "Install Python 3.10+ and ensure it's added to PATH.";
            }
            catch (Exception ex)
            {
                status.ErrorMessage = $"Error detecting Python: {ex.Message}";
            }

            return status;
        }

        public override string GetPythonInstallUrl()
        {
            return "https://apps.microsoft.com/store/detail/python-313/9NCVDN91XZQP";
        }

        public override string GetUvInstallUrl()
        {
            return "https://docs.astral.sh/uv/getting-started/installation/#windows";
        }

        public override string GetInstallationRecommendations()
        {
            return @"Windows Installation Recommendations:

1. Python: Install from Microsoft Store or python.org
   - Microsoft Store: Search for 'Python 3.10' or higher
   - Direct download: https://python.org/downloads/windows/

2. uv Package Manager: Install via PowerShell
   - Run: powershell -ExecutionPolicy ByPass -c ""irm https://astral.sh/uv/install.ps1 | iex""
   - Or download from: https://github.com/astral-sh/uv/releases

3. MCP Server: Will be installed automatically by MCP for Unity Bridge";
        }

        public override DependencyStatus DetectUv()
        {
            // First, honor overrides and cross-platform resolution via the base implementation
            var status = base.DetectUv();
            if (status.IsAvailable)
            {
                return status;
            }

            // If the user configured an override path but fallback was not used, keep the base result
            // (failure typically means the override path is invalid and no system fallback found)
            if (MCPServiceLocator.Paths.HasUvxPathOverride && !MCPServiceLocator.Paths.HasUvxPathFallback)
            {
                return status;
            }

            try
            {
                string augmentedPath = BuildAugmentedPath();

                // try to find uv
                if (TryValidateUvWithPath("uv.exe", augmentedPath, out string uvVersion, out string uvPath))
                {
                    status.IsAvailable = true;
                    status.Version = uvVersion;
                    status.Path = uvPath;
                    status.Details = $"Found uv {uvVersion} at {uvPath}";
                    return status;
                }

                // try to find uvx
                if (TryValidateUvWithPath("uvx.exe", augmentedPath, out string uvxVersion, out string uvxPath))
                {
                    status.IsAvailable = true;
                    status.Version = uvxVersion;
                    status.Path = uvxPath;
                    status.Details = $"Found uvx {uvxVersion} at {uvxPath} (fallback)";
                    return status;
                }

                status.ErrorMessage = "uv not found in PATH";
                status.Details = "Install uv package manager and ensure it's added to PATH.";
            }
            catch (Exception ex)
            {
                status.ErrorMessage = $"Error detecting uv: {ex.Message}";
            }

            return status;
        }


        private bool TryFindPythonViaUv(out string version, out string fullPath)
        {
            version = null;
            fullPath = null;

            try
            {
                string augmentedPath = BuildAugmentedPath();
                // Try to list installed python versions via uvx
                if (!ExecPath.TryRun("uv", "python list", null, out string stdout, out string stderr, 5000, augmentedPath))
                    return false;

                var lines = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (line.Contains("<download available>")) continue;

                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        string potentialPath = parts[parts.Length - 1];
                        if (File.Exists(potentialPath) &&
                            (potentialPath.EndsWith("python.exe") || potentialPath.EndsWith("python3.exe")))
                        {
                            if (TryValidatePython(potentialPath, out version, out fullPath))
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore errors if uv is not installed or fails
            }

            return false;
        }

        private bool TryValidatePython(string pythonPath, out string version, out string fullPath)
        {
            version = null;
            fullPath = null;

            try
            {
                string augmentedPath = BuildAugmentedPath();

                // First, try to resolve the absolute path for better UI/logging display
                string commandToRun = pythonPath;
                if (TryFindInPath(pythonPath, out string resolvedPath))
                {
                    commandToRun = resolvedPath;
                }

                // Run 'python --version' to get the version
                if (!ExecPath.TryRun(commandToRun, "--version", null, out string stdout, out string stderr, 5000, augmentedPath))
                    return false;

                // Check stdout first, then stderr (some Python distributions output to stderr)
                string output = !string.IsNullOrWhiteSpace(stdout) ? stdout.Trim() : stderr.Trim();
                if (output.StartsWith("Python "))
                {
                    version = output.Substring(7);
                    fullPath = commandToRun;

                    if (TryParseVersion(version, out var major, out var minor))
                    {
                        return major > 3 || (major == 3 && minor >= 10);
                    }
                }
            }
            catch
            {
                // Ignore validation errors
            }

            return false;
        }

        protected override bool TryFindInPath(string executable, out string fullPath)
        {
            fullPath = ExecPath.FindInPath(executable, BuildAugmentedPath());
            return !string.IsNullOrEmpty(fullPath);
        }

        protected string BuildAugmentedPath()
        {
            var additions = GetPathAdditions();
            if (additions.Length == 0) return null;

            // Only return the additions - ExecPath.TryRun will prepend to existing PATH
            return string.Join(Path.PathSeparator, additions);
        }

        private string[] GetPathAdditions()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            var additions = new List<string>();

            // uv common installation paths
            if (!string.IsNullOrEmpty(localAppData))
                additions.Add(Path.Combine(localAppData, "Programs", "uv"));
            if (!string.IsNullOrEmpty(programFiles))
                additions.Add(Path.Combine(programFiles, "uv"));

            // npm global paths
            if (!string.IsNullOrEmpty(appData))
                additions.Add(Path.Combine(appData, "npm"));
            if (!string.IsNullOrEmpty(localAppData))
                additions.Add(Path.Combine(localAppData, "npm"));

            // Python common paths
            if (!string.IsNullOrEmpty(localAppData))
                additions.Add(Path.Combine(localAppData, "Programs", "Python"));
            // Instead of hardcoded versions, enumerate existing directories
            if (!string.IsNullOrEmpty(programFiles))
            {
                try
                {
                    var pythonDirs = Directory.GetDirectories(programFiles, "Python3*")
                        .OrderByDescending(d => d); // Newest first
                    foreach (var dir in pythonDirs)
                    {
                        additions.Add(dir);
                    }
                }
                catch { /* Ignore if directory doesn't exist */ }
            }

            // User scripts
            if (!string.IsNullOrEmpty(homeDir))
                additions.Add(Path.Combine(homeDir, ".local", "bin"));

            return additions.ToArray();
        }
    }
}
