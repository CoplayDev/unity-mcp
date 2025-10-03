using System;
using System.Diagnostics;
using System.IO;
using MCPForUnity.Editor.Dependencies.Models;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.Dependencies.PlatformDetectors
{
    /// <summary>
    /// Base class for platform-specific dependency detection
    /// </summary>
    public abstract class PlatformDetectorBase : IPlatformDetector
    {
        public abstract string PlatformName { get; }
        public abstract bool CanDetect { get; }

        public abstract DependencyStatus DetectPython();
        public abstract string GetPythonInstallUrl();
        public abstract string GetUVInstallUrl();
        public abstract string GetInstallationRecommendations();

        public virtual DependencyStatus DetectUV()
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

        public virtual DependencyStatus DetectMCPServer()
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

        protected bool TryValidateUV(string uvPath, out string version)
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

        protected bool TryParseVersion(string version, out int major, out int minor)
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
