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


        // Git is not needed to run the bridge, only to add or update the package from a Git URL
        // in the Package Manager, which is the install path most users take (issue #1216). It is
        // reported as optional so a missing git never blocks setup, but the row tells the user why
        // "Error when executing git command" appeared and how to clear it.
        public const string GitInstallUrl = "https://git-scm.com/downloads";

        public virtual DependencyStatus DetectGit()
        {
            var status = new DependencyStatus("Git", isRequired: false)
            {
                InstallationHint = GitInstallUrl
            };

            try
            {
                if (!TryFindInPath("git", out string gitPath))
                {
                    status.ErrorMessage = "git not found";
                    status.Details = "Only needed to add or update MCP for Unity from a Git URL in the Package Manager.";
                    return status;
                }

                if (ExecPath.TryRun(gitPath, "--version", null, out string stdout, out string stderr, 5000)
                    && TryParseGitVersion(string.IsNullOrWhiteSpace(stdout) ? stderr : stdout, out string version))
                {
                    status.IsAvailable = true;
                    status.Version = version;
                    status.Path = gitPath;
                    status.Details = "If the Package Manager still reports 'not in a git directory', git is refusing a folder "
                        + "owned by another user: run  git config --global --add safe.directory <project folder>";
                    return status;
                }

                status.ErrorMessage = "git found but did not report a version";
                status.Path = gitPath;
            }
            catch (Exception ex)
            {
                status.ErrorMessage = $"Error detecting git: {ex.Message}";
            }

            return status;
        }

        /// <summary>Parses "git version 2.45.1.windows.1" or "git version 2.39.5 (Apple Git-154)" into "2.45.1.windows.1" / "2.39.5".</summary>
        internal static bool TryParseGitVersion(string output, out string version)
        {
            version = null;
            string line = (output ?? string.Empty).Trim();
            const string prefix = "git version ";
            if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string rest = line.Substring(prefix.Length).Trim();
            int end = rest.IndexOfAny(new[] { ' ', '\r', '\n' });
            version = end >= 0 ? rest.Substring(0, end) : rest;
            return version.Length > 0 && char.IsDigit(version[0]);
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
