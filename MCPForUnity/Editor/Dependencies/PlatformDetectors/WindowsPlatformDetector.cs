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

        public override DependencyStatus DetectPython(string overridePath = null)
        {
            var status = new DependencyStatus("Python", isRequired: true)
            {
                InstallationHint = GetPythonInstallUrl()
            };

            try
            {
                // 1. Check Override
                if (overridePath == null)
                {
                    try { overridePath = UnityEditor.EditorPrefs.GetString(MCPForUnity.Editor.Constants.EditorPrefKeys.PythonPathOverride, ""); } catch { }
                }
                
                if (!string.IsNullOrEmpty(overridePath) && File.Exists(overridePath))
                {
                    if (TryValidatePython(overridePath, out string version, out string fullPath))
                    {
                        status.IsAvailable = true;
                        status.Version = version;
                        status.Path = fullPath;
                        status.Details = $"Using custom Python path: {fullPath}";
                        return status;
                    }
                }

                // 2. Try running python directly first (works with Windows App Execution Aliases)
                if (TryValidatePython("python3.exe", out string ver, out string path) ||
                    TryValidatePython("python.exe", out ver, out path))
                {
                    status.IsAvailable = true;
                    status.Version = ver;
                    status.Path = path;
                    status.Details = $"Found Python {ver} in PATH";
                    return status;
                }

                // 3. Fallback: try 'where' command
                if (TryFindInPath("python3.exe", out string pathResult) ||
                    TryFindInPath("python.exe", out pathResult))
                {
                    if (TryValidatePython(pathResult, out ver, out path))
                    {
                        status.IsAvailable = true;
                        status.Version = ver;
                        status.Path = path;
                        status.Details = $"Found Python {ver} in PATH";
                        return status;
                    }
                }

                status.ErrorMessage = "Python not found in PATH";
                status.Details = "Install python 3.11+ and ensure it's added to PATH.";
            }
            catch (Exception ex)
            {
                status.ErrorMessage = $"Error detecting Python: {ex.Message}";
            }

            return status;
        }

        public override DependencyStatus DetectUv(string overridePath = null)
        {
            var status = new DependencyStatus("uv Package Manager", isRequired: true)
            {
                InstallationHint = GetUvInstallUrl()
            };

            try
            {
                // 1. Check Override
                if (overridePath == null)
                {
                    try { overridePath = UnityEditor.EditorPrefs.GetString(MCPForUnity.Editor.Constants.EditorPrefKeys.UvPathOverride, ""); } catch { }
                }

                if (!string.IsNullOrEmpty(overridePath) && File.Exists(overridePath))
                {
                    if (TryValidateUv(overridePath, out string version, out string fullPath))
                    {
                        status.IsAvailable = true;
                        status.Version = version;
                        status.Path = fullPath;
                        status.Details = $"Using custom uv path: {fullPath}";
                        return status;
                    }
                }

                // 2. Try running uv directly/PATH
                if (TryValidateUv("uv", out string ver, out string path))
                {
                    status.IsAvailable = true;
                    status.Version = ver;
                    status.Path = path;
                    status.Details = $"Found uv {ver} in PATH";
                    return status;
                }
                
                // 3. Try 'where' command
                if (TryFindInPath("uv.exe", out string pathResult))
                {
                    if (TryValidateUv(pathResult, out ver, out path))
                    {
                        status.IsAvailable = true;
                        status.Version = ver;
                        status.Path = path;
                        status.Details = $"Found uv {ver} in PATH";
                        return status;
                    }
                }

                // 4. Force check common install locations
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                
                var commonPaths = new[]
                {
                    Path.Combine(localAppData, "Programs", "uv", "uv.exe"), // Standard install
                    Path.Combine(localAppData, "uv", "uv.exe"),             // Alternative
                    Path.Combine(userProfile, ".cargo", "bin", "uv.exe"),   // Cargo install
                    Path.Combine(localAppData, "bin", "uv.exe")
                };

                foreach (var candidate in commonPaths)
                {
                    if (File.Exists(candidate))
                    {
                        if (TryValidateUv(candidate, out ver, out path))
                        {
                            status.IsAvailable = true;
                            status.Version = ver;
                            status.Path = path;
                            status.Details = $"Found uv {ver} at {candidate}";
                            return status;
                        }
                    }
                }

                status.ErrorMessage = "uv not found";
                status.Details = "Install uv package manager via PowerShell or from github.com/astral-sh/uv";
            }
            catch (Exception ex)
            {
                status.ErrorMessage = $"Error detecting uv: {ex.Message}";
            }

            return status;
        }

        private bool TryValidateUv(string uvPath, out string version, out string fullPath)
        {
            version = null;
            fullPath = null;
            
            if (TryExecuteProcess(uvPath, "--version", 3000, out string output) && output.StartsWith("uv"))
            {
                var parts = output.Split(' ');
                if (parts.Length > 1) 
                {
                    version = parts[1].Trim();
                    fullPath = uvPath;
                    return true;
                }
            }
            return false;
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
   - Microsoft Store: Search for 'python 3.11' or higher
   - Direct download: https://python.org/downloads/windows/

2. uv Package Manager: Install via PowerShell
   - Run: powershell -ExecutionPolicy ByPass -c ""irm https://astral.sh/uv/install.ps1 | iex""
   - Or download from: https://github.com/astral-sh/uv/releases

3. MCP Server: Will be installed automatically by MCP for Unity Bridge";
        }

        private bool TryValidatePython(string pythonPath, out string version, out string fullPath)
        {
            version = null;
            fullPath = null;
            
            // 5 second timeout for validation
            if (TryExecuteProcess(pythonPath, "--version", 5000, out string output) && output.StartsWith("Python "))
            {
                version = output.Substring(7); // Remove "Python " prefix
                fullPath = pythonPath;

                // Validate minimum version (Python 4+ or python 3.11+)
                if (TryParseVersion(version, out var major, out var minor))
                {
                    return major > 3 || (major == 3 && minor >= 11);
                }
            }
            return false;
        }

        private bool TryFindInPath(string executable, out string fullPath)
        {
            fullPath = null;
            
            // 3 second timeout for 'where'
            if (TryExecuteProcess("where", executable, 3000, out string output) && !string.IsNullOrEmpty(output))
            {
                    // Take the first result
                    var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    if (lines.Length > 0)
                    {
                        fullPath = lines[0].Trim();
                        return File.Exists(fullPath);
                    }
            }
            return false;
        }
    }
}
