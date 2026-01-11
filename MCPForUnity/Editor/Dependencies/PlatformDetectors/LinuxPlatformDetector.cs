using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Dependencies.Models;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;

namespace MCPForUnity.Editor.Dependencies.PlatformDetectors
{
    /// <summary>
    /// Linux-specific dependency detection
    /// </summary>
    public class LinuxPlatformDetector : PlatformDetectorBase
    {
        public override string PlatformName => "Linux";

        public override bool CanDetect => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

        public override DependencyStatus DetectPython()
        {
            var status = new DependencyStatus("Python", isRequired: true)
            {
                InstallationHint = GetPythonInstallUrl()
            };

            try
            {
                // Try running python directly first
                if (TryValidatePython("python3", out string version, out string fullPath) ||
                    TryValidatePython("python", out version, out fullPath))
                {
                    status.IsAvailable = true;
                    status.Version = version;
                    status.Path = fullPath;
                    status.Details = $"Found Python {version} in PATH";
                    return status;
                }

                // Fallback: try 'which' command
                if (TryFindInPath("python3", out string pathResult) ||
                    TryFindInPath("python", out pathResult))
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
            return "https://www.python.org/downloads/source/";
        }

        public override string GetUvInstallUrl()
        {
            return "https://docs.astral.sh/uv/getting-started/installation/#linux";
        }

        public override string GetInstallationRecommendations()
        {
            return @"Linux Installation Recommendations:

1. Python: Install via package manager or pyenv
   - Ubuntu/Debian: sudo apt install python3 python3-pip
   - Fedora/RHEL: sudo dnf install python3 python3-pip
   - Arch: sudo pacman -S python python-pip
   - Or use pyenv: https://github.com/pyenv/pyenv

2. uv Package Manager: Install via curl
   - Run: curl -LsSf https://astral.sh/uv/install.sh | sh
   - Or download from: https://github.com/astral-sh/uv/releases

3. MCP Server: Will be installed automatically by MCP for Unity

Note: Make sure ~/.local/bin is in your PATH for user-local installations.";
        }

        public override DependencyStatus DetectUv()
        {
            // First, honor overrides and cross-platform resolution via the base implementation
            var status = base.DetectUv();
            if (status.IsAvailable)
            {
                return status;
            }

            // If the user configured an override path, keep the base result (failure typically means the override path is invalid)
            if (MCPServiceLocator.Paths.HasUvxPathOverride)
            {
                return status;
            }

            try
            {
                string augmentedPath = BuildAugmentedPath();

                // Try uv first, then uvx, using ExecPath.TryRun for proper timeout handling
                if (TryValidateUvWithPath("uv", augmentedPath, out string version, out string fullPath) ||
                    TryValidateUvWithPath("uvx", augmentedPath, out version, out fullPath))
                {
                    status.IsAvailable = true;
                    status.Version = version;
                    status.Path = fullPath;
                    status.Details = $"Found uv {version} in PATH";
                    status.ErrorMessage = null;
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

        private bool TryValidatePython(string pythonPath, out string version, out string fullPath)
        {
            version = null;
            fullPath = null;

            try
            {
                var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var pathAdditions = new[]
                {
                    "/usr/local/bin",
                    "/usr/bin",
                    "/bin",
                    "/snap/bin",
                    Path.Combine(homeDir, ".local", "bin")
                };
                string augmentedPath = string.Join(":", pathAdditions) + ":" + (Environment.GetEnvironmentVariable("PATH") ?? "");

                if (!ExecPath.TryRun(pythonPath, "--version", null, out string stdout, out string stderr,
                    5000, augmentedPath))
                    return false;

                string output = stdout.Trim();
                if (output.StartsWith("Python "))
                {
                    version = output.Substring(7);
                    fullPath = pythonPath;

                    if (TryParseVersion(version, out var major, out var minor))
                    {
                        return major > 3 || (major >= 3 && minor >= 10);
                    }
                }
            }
            catch
            {
                // Ignore validation errors
            }

            return false;
        }

        private bool TryValidateUvWithPath(string command, string augmentedPath, out string version, out string fullPath)
        {
            version = null;
            fullPath = null;

            try
            {
                // Use ExecPath.TryRun which properly handles async output reading and timeouts
                if (!ExecPath.TryRun(command, "--version", null, out string stdout, out string stderr,
                    5000, augmentedPath))
                    return false;

                string output = string.IsNullOrWhiteSpace(stdout) ? stderr.Trim() : stdout.Trim();

                if (output.StartsWith("uv ") || output.StartsWith("uvx "))
                {
                    // Extract version: "uvx 0.9.18" -> "0.9.18"
                    int spaceIndex = output.IndexOf(' ');
                    if (spaceIndex >= 0)
                    {
                        var remainder = output.Substring(spaceIndex + 1).Trim();
                        int nextSpace = remainder.IndexOf(' ');
                        int parenIndex = remainder.IndexOf('(');
                        int endIndex = Math.Min(
                            nextSpace >= 0 ? nextSpace : int.MaxValue,
                            parenIndex >= 0 ? parenIndex : int.MaxValue
                        );
                        version = endIndex < int.MaxValue ? remainder.Substring(0, endIndex).Trim() : remainder;
                        fullPath = command;
                        return true;
                    }
                }
            }
            catch
            {
                // Ignore validation errors
            }

            return false;
        }

        private string BuildAugmentedPath()
        {
            string currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
            return string.Join(":", GetPathAdditions()) + ":" + currentPath;
        }

        private string[] GetPathAdditions()
        {
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return new[]
            {
                "/usr/local/bin",
                "/usr/bin",
                "/bin",
                "/snap/bin",
                Path.Combine(homeDir, ".local", "bin")
            };
        }

        private bool TryFindInPath(string executable, out string fullPath)
        {
            fullPath = null;

            try
            {
                var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var pathAdditions = new[]
                {
                    "/usr/local/bin",
                    "/usr/bin",
                    "/bin",
                    "/snap/bin",
                    Path.Combine(homeDir, ".local", "bin")
                };
                string augmentedPath = string.Join(":", pathAdditions) + ":" + (Environment.GetEnvironmentVariable("PATH") ?? "");

                if (!ExecPath.TryRun("/usr/bin/which", executable, null, out string stdout, out _, 3000, augmentedPath))
                    return false;

                string output = stdout.Trim();
                if (!string.IsNullOrEmpty(output) && File.Exists(output))
                {
                    fullPath = output;
                    return true;
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
