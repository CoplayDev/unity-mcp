using System;
using MCPForUnity.Editor.Dependencies.Models;
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
        public abstract string GetUvInstallUrl();
        public abstract string GetInstallationRecommendations();

        public virtual DependencyStatus DetectUv()
        {
            var status = new DependencyStatus("uv Package Manager", isRequired: true)
            {
                InstallationHint = GetUvInstallUrl()
            };

            try
            {
                // Get uv path from PathResolverService (respects override)
                string uvxPath = MCPServiceLocator.Paths.GetUvxPath();

                // Verify uv executable and get version
                if (MCPServiceLocator.Paths.TryValidateUvxExecutable(uvxPath, out string version))
                {
                    status.IsAvailable = true;
                    status.Version = version;
                    status.Path = uvxPath;
                    status.Details = MCPServiceLocator.Paths.HasUvxPathOverride
                        ? $"Found uv {version} (override path)"
                        : $"Found uv {version} in system path";
                    return status;
                }

                status.ErrorMessage = "uvx not found";
                status.Details = "Install uv package manager or configure path override in Advanced Settings.";
            }
            catch (Exception ex)
            {
                status.ErrorMessage = $"Error detecting uvx: {ex.Message}";
            }

            return status;
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
