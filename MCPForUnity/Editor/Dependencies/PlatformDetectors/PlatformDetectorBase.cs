using System;
using System.Diagnostics;
using System.IO;
using MCPForUnity.Editor.Dependencies.Models;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;

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
                string uvxPath = MCPServiceLocator.Paths.GetUvxPath(verifyPath: false);
                if (!string.IsNullOrEmpty(uvxPath))
                {
                    if (TryValidateUvx(uvxPath, out string version))
                    {
                        status.IsAvailable = true;
                        status.Version = version;
                        status.Path = uvxPath;
                        status.Details = $"Found UV {version} at {uvxPath}";
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

        protected bool TryValidateUvx(string uvxPath, out string version)
        {
            version = null;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = uvxPath,
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
