using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using MCPForUnity.Editor.Dependencies.Models;
using MCPForUnity.Editor.Helpers;

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
            var status = new DependencyStatus("uv Package Manager", isRequired: true)
            {
                InstallationHint = GetUvInstallUrl()
            };

            try
            {
                // Try running uv/uvx directly with augmented PATH
                if (TryValidateUv("uv", out string version, out string fullPath) ||
                    TryValidateUv("uvx", out version, out fullPath))
                {
                    status.IsAvailable = true;
                    status.Version = version;
                    status.Path = fullPath;
                    status.Details = $"Found uv {version} in PATH";
                    return status;
                }

                // Fallback: use which with augmented PATH
                if (TryFindInPath("uv", out string pathResult) ||
                    TryFindInPath("uvx", out pathResult))
                {
                    if (TryValidateUv(pathResult, out version, out fullPath))
                    {
                        status.IsAvailable = true;
                        status.Version = version;
                        status.Path = fullPath;
                        status.Details = $"Found uv {version} in PATH";
                        return status;
                    }
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
                var psi = new ProcessStartInfo
                {
                    FileName = pythonPath,
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                // Set PATH to include common locations
                var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var pathAdditions = new[]
                {
                    "/usr/local/bin",
                    "/usr/bin",
                    "/bin",
                    "/snap/bin",
                    Path.Combine(homeDir, ".local", "bin")
                };

                string currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
                psi.EnvironmentVariables["PATH"] = string.Join(":", pathAdditions) + ":" + currentPath;

                using var process = Process.Start(psi);
                if (process == null) return false;

                string output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit(5000);

                if (process.ExitCode == 0 && output.StartsWith("Python "))
                {
                    version = output.Substring(7); // Remove "Python " prefix
                    fullPath = pythonPath;

                    // Validate minimum version (Python 4+ or Python 3.10+)
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

        private bool TryValidateUv(string uvPath, out string version, out string fullPath)
        {
            version = null;
            fullPath = null;

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

                psi.EnvironmentVariables["PATH"] = BuildAugmentedPath();

                using var process = Process.Start(psi);
                if (process == null) return false;

                string output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit(5000);

                if (process.ExitCode == 0 && output.StartsWith("uv "))
                {
                    version = output.Substring(3).Trim();
                    fullPath = uvPath;
                    return true;
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
                var psi = new ProcessStartInfo
                {
                    FileName = "/usr/bin/which",
                    Arguments = executable,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                // Enhance PATH for Unity's GUI environment
                var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var pathAdditions = new[]
                {
                    "/usr/local/bin",
                    "/usr/bin",
                    "/bin",
                    "/snap/bin",
                    Path.Combine(homeDir, ".local", "bin")
                };

                string currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
                psi.EnvironmentVariables["PATH"] = string.Join(":", pathAdditions) + ":" + currentPath;

                using var process = Process.Start(psi);
                if (process == null) return false;

                string output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit(3000);

                if (process.ExitCode == 0 && !string.IsNullOrEmpty(output) && File.Exists(output))
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
