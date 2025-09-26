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
    public class LinuxPlatformDetector : IPlatformDetector
    {
        public string PlatformName => "Linux";

        public bool CanDetect => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

        public DependencyStatus DetectPython()
        {
            var status = new DependencyStatus("Python", isRequired: true)
            {
                InstallationHint = GetPythonInstallUrl()
            };

            try
            {
                // Check common Python installation paths on Linux
                var candidates = new[]
                {
                    "python3",
                    "python",
                    "/usr/bin/python3",
                    "/usr/local/bin/python3",
                    "/opt/python/bin/python3",
                    "/snap/bin/python3"
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

                // Try PATH resolution using 'which' command
                if (TryFindInPath("python3", out string pathResult) || 
                    TryFindInPath("python", out pathResult))
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

                status.ErrorMessage = "Python not found. Please install Python 3.10 or later.";
                status.Details = "Checked common installation paths including system, snap, and user-local locations.";
            }
            catch (Exception ex)
            {
                status.ErrorMessage = $"Error detecting Python: {ex.Message}";
            }

            return status;
        }

        public DependencyStatus DetectUV()
        {
            var status = new DependencyStatus("UV Package Manager", isRequired: true)
            {
                InstallationHint = GetUVInstallUrl()
            };

            try
            {
                // Use existing UV detection from ServerInstaller
                string uvPath = ServerInstaller.FindUvPath();
                if (!string.IsNullOrEmpty(uvPath))
                {
                    if (TryValidateUV(uvPath, out string version))
                    {
                        status.IsAvailable = true;
                        status.Version = version;
                        status.Path = uvPath;
                        status.Details = $"Found UV {version} at {uvPath}";
                        return status;
                    }
                }

                status.ErrorMessage = "UV package manager not found. Please install UV.";
                status.Details = "UV is required for managing Python dependencies.";
            }
            catch (Exception ex)
            {
                status.ErrorMessage = $"Error detecting UV: {ex.Message}";
            }

            return status;
        }

        public DependencyStatus DetectMCPServer()
        {
            var status = new DependencyStatus("MCP Server", isRequired: false);

            try
            {
                // Check if server is installed
                string serverPath = ServerInstaller.GetServerPath();
                string serverPy = Path.Combine(serverPath, "server.py");

                if (File.Exists(serverPy))
                {
                    status.IsAvailable = true;
                    status.Path = serverPath;
                    
                    // Try to get version
                    string versionFile = Path.Combine(serverPath, "server_version.txt");
                    if (File.Exists(versionFile))
                    {
                        status.Version = File.ReadAllText(versionFile).Trim();
                    }
                    
                    status.Details = $"MCP Server found at {serverPath}";
                }
                else
                {
                    // Check for embedded server
                    if (ServerPathResolver.TryFindEmbeddedServerSource(out string embeddedPath))
                    {
                        status.IsAvailable = true;
                        status.Path = embeddedPath;
                        status.Details = "MCP Server available (embedded in package)";
                    }
                    else
                    {
                        status.ErrorMessage = "MCP Server not found";
                        status.Details = "Server will be installed automatically when needed";
                    }
                }
            }
            catch (Exception ex)
            {
                status.ErrorMessage = $"Error detecting MCP Server: {ex.Message}";
            }

            return status;
        }

        public string GetInstallationRecommendations()
        {
            return @"Linux Installation Recommendations:

1. Python: Install via package manager or pyenv
   - Ubuntu/Debian: sudo apt install python3 python3-pip
   - Fedora/RHEL: sudo dnf install python3 python3-pip
   - Arch: sudo pacman -S python python-pip
   - Or use pyenv: https://github.com/pyenv/pyenv

2. UV Package Manager: Install via curl
   - Run: curl -LsSf https://astral.sh/uv/install.sh | sh
   - Or download from: https://github.com/astral-sh/uv/releases

3. MCP Server: Will be installed automatically by Unity MCP Bridge

Note: Make sure ~/.local/bin is in your PATH for user-local installations.";
        }

        public string GetPythonInstallUrl()
        {
            return "https://www.python.org/downloads/source/";
        }

        public string GetUVInstallUrl()
        {
            return "https://docs.astral.sh/uv/getting-started/installation/#linux";
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
                    
                    // Validate minimum version (3.10+)
                    if (TryParseVersion(version, out var major, out var minor))
                    {
                        return major >= 3 && minor >= 10;
                    }
                }
            }
            catch
            {
                // Ignore validation errors
            }

            return false;
        }

        private bool TryValidateUV(string uvPath, out string version)
        {
            version = null;

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
                if (process == null) return false;

                string output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit(5000);

                if (process.ExitCode == 0 && output.StartsWith("uv "))
                {
                    version = output.Substring(3); // Remove "uv " prefix
                    return true;
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

        private bool TryParseVersion(string version, out int major, out int minor)
        {
            major = 0;
            minor = 0;

            try
            {
                var parts = version.Split('.');
                if (parts.Length >= 2)
                {
                    return int.TryParse(parts[0], out major) && int.TryParse(parts[1], out minor);
                }
            }
            catch
            {
                // Ignore parsing errors
            }

            return false;
        }
    }
}