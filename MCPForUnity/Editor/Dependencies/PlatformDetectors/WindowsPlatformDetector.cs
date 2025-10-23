using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using MCPForUnity.Editor.Dependencies.Models;
using MCPForUnity.Editor.Helpers;

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
                // Check common Python installation paths
                var candidates = new[]
                {
                    "python.exe",
                    "python3.exe",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Programs", "Python", "Python313", "python.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Programs", "Python", "Python312", "python.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Programs", "Python", "Python311", "python.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                        "Python313", "python.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                        "Python312", "python.exe")
                };

                foreach (var candidate in candidates)
                {
                    if (TryValidatePython(candidate, out string version, out string fullPath))
                    {
                        status.IsAvailable = true;
                        status.Version = version;
                        status.Path = fullPath;
                        status.Details = $"Found Python {version} at {fullPath}";
                        return status;
                    }
                }

                // Try PATH resolution using 'where' command
                if (TryFindInPath("python.exe", out string pathResult) ||
                    TryFindInPath("python3.exe", out pathResult))
                {
                    if (TryValidatePython(pathResult, out string version, out string fullPath))
                    {
                        status.IsAvailable = true;
                        status.Version = version;
                        status.Path = fullPath;
                        status.Details = $"Found Python {version} in PATH at {fullPath}";
                        return status;
                    }
                }

                status.ErrorMessage = "Python not found. Please install Python 3.11 or later.";
                status.Details = "Checked common installation paths and PATH environment variable.";
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

        public override string GetUVInstallUrl()
        {
            return "https://docs.astral.sh/uv/getting-started/installation/#windows";
        }

        public override string GetInstallationRecommendations()
        {
            return @"Windows Installation Recommendations:

1. Python: Install from Microsoft Store or python.org
   - Microsoft Store: Search for 'Python 3.12' or 'Python 3.13'
   - Direct download: https://python.org/downloads/windows/

2. UV Package Manager: Install via PowerShell
   - Run: powershell -ExecutionPolicy ByPass -c ""irm https://astral.sh/uv/install.ps1 | iex""
   - Or download from: https://github.com/astral-sh/uv/releases

3. MCP Server: Will be installed automatically by MCP for Unity Bridge";
        }

        private bool TryValidatePython(string pythonPath, out string version, out string fullPath)
        {
            version = null;
            fullPath = null;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = pythonPath,
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return false;

                string output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit(5000);

                if (process.ExitCode == 0 && output.StartsWith("Python "))
                {
                    version = output.Substring(7); // Remove "Python " prefix
                    fullPath = pythonPath;

                    // Validate minimum version (Python 4+ or Python 3.11+)
                    if (TryParseVersion(version, out var major, out var minor))
                    {
                        return major > 3 || (major >= 3 && minor >= 11);
                    }
                }
            }
            catch
            {
                // Ignore validation errors
            }

            return false;
        }

        private bool TryFindInPath(string executable, out string fullPath)
        {
            fullPath = null;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "where",
                    Arguments = executable,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return false;

                string output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit(3000);

                if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                {
                    // Take the first result
                    var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    if (lines.Length > 0)
                    {
                        fullPath = lines[0].Trim();
                        return File.Exists(fullPath);
                    }
                }
            }
            catch
            {
                // Ignore errors
            }

            return false;
        }
    }
}
