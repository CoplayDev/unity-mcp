using System;
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

                    // Check if we used fallback from override to system path
                    if (MCPServiceLocator.Paths.HasUvxPathFallback)
                    {
                        status.Details = $"Found uv {version} (fallback to system path)";
                        status.ErrorMessage = "Override path not found, using system path";
                    }
                    else
                    {
                        status.Details = MCPServiceLocator.Paths.HasUvxPathOverride
                            ? $"Found uv {version} (override path)"
                            : $"Found uv {version} in system path";
                    }
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
        // In PlatformDetectorBase.cs
        protected bool TryValidateUvWithPath(string command, string augmentedPath, out string version, out string fullPath)
        {
            version = null;
            fullPath = null;

            try
            {
                string commandToRun = command;
                if (TryFindInPath(command, out string resolvedPath))
                {
                    commandToRun = resolvedPath;
                }

                if (!ExecPath.TryRun(commandToRun, "--version", null, out string stdout, out string stderr,
                    5000, augmentedPath))
                    return false;

                string output = string.IsNullOrWhiteSpace(stdout) ? stderr.Trim() : stdout.Trim();

                if (output.StartsWith("uvx ") || output.StartsWith("uv "))
                {
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
                        fullPath = commandToRun;
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
        

        // Add abstract method for subclasses to implement
        protected abstract bool TryFindInPath(string executable, out string fullPath);
    }
}
