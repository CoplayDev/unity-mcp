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
    /// macOS-specific dependency detection
    /// </summary>
    public class MacOSPlatformDetector : PlatformDetectorBase
    {
        public override string PlatformName => "macOS";

        public override bool CanDetect => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        public override DependencyStatus DetectPython()
        {
            var status = new DependencyStatus("Python", isRequired: true)
            {
                InstallationHint = GetPythonInstallUrl()
            };

            try
            {
                // 1. Try 'which' command with augmented PATH (prioritizing Homebrew)
                if (TryFindInPath("python3", out string pathResult) ||
                    TryFindInPath("python", out pathResult))
                {
                    if (TryValidatePython(pathResult, out string version, out string fullPath))
                    {
                        status.IsAvailable = true;
                        status.Version = version;
                        status.Path = fullPath;
                        status.Details = $"Found Python {version} at {fullPath}";
                        return status;
                    }
                }

                // 2. Fallback: Try running python directly from PATH
                if (TryValidatePython("python3", out string v, out string p) ||
                    TryValidatePython("python", out v, out p))
                {
                    status.IsAvailable = true;
                    status.Version = v;
                    status.Path = p;
                    status.Details = $"Found Python {v} in PATH";
                    return status;
                }

                status.ErrorMessage = "Python not found in PATH or standard locations";
                status.Details = "Install Python 3.10+ via Homebrew ('brew install python3') and ensure it's in your PATH.";
            }
            catch (Exception ex)
            {
                status.ErrorMessage = $"Error detecting Python: {ex.Message}";
            }

            return status;
        }

        public override string GetPythonInstallUrl()
        {
            return "https://www.python.org/downloads/macos/";
        }

        public override string GetUvInstallUrl()
        {
            return "https://docs.astral.sh/uv/getting-started/installation/#macos";
        }

        public override string GetInstallationRecommendations()
        {
            return @"macOS Installation Recommendations:

1. Python: Install via Homebrew (recommended) or python.org
   - Homebrew: brew install python3
   - Direct download: https://python.org/downloads/macos/

2. uv Package Manager: Install via curl or Homebrew
   - Curl: curl -LsSf https://astral.sh/uv/install.sh | sh
   - Homebrew: brew install uv

3. MCP Server: Will be installed automatically by MCP for Unity Bridge

Note: If using Homebrew, make sure /opt/homebrew/bin is in your PATH.";
        }

        public override DependencyStatus DetectUv()
        {
            // First, honor overrides and cross-platform resolution via the base implementation
            var status = base.DetectUv();
            if (status.IsAvailable)
            {
                return status;
            }

            // If the user configured an override path but fallback was not used, keep the base result
            // (failure typically means the override path is invalid and no system fallback found)
            if (MCPServiceLocator.Paths.HasUvxPathOverride && !MCPServiceLocator.Paths.HasUvxPathFallback)
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
                string augmentedPath = BuildAugmentedPath();

                // First, try to resolve the absolute path for better UI/logging display
                string commandToRun = pythonPath;
                if (TryFindInPath(pythonPath, out string resolvedPath))
                {
                    commandToRun = resolvedPath;
                }

                if (!ExecPath.TryRun(commandToRun, "--version", null, out string stdout, out string stderr,
                    5000, augmentedPath))
                    return false;

                // Check stdout first, then stderr (some Python distributions output to stderr)
                string output = !string.IsNullOrWhiteSpace(stdout) ? stdout.Trim() : stderr.Trim();
                if (output.StartsWith("Python "))
                {
                    version = output.Substring(7);
                    fullPath = commandToRun;

                    if (TryParseVersion(version, out var major, out var minor))
                    {
                        return major > 3 || (major == 3 && minor >= 10);
                    }
                }
            }
            catch
            {
                // Ignore validation errors
            }

            return false;
        }

        protected string BuildAugmentedPath()
        {
            var additions = GetPathAdditions();
            if (additions.Length == 0) return null;

            // Only return the additions - ExecPath.TryRun will prepend to existing PATH
            return string.Join(Path.PathSeparator, additions);
        }

        private string[] GetPathAdditions()
        {
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return new[]
            {
                "/opt/homebrew/bin",
                "/usr/local/bin",
                "/usr/bin",
                "/bin",
                Path.Combine(homeDir, ".local", "bin")
            };
        }

        protected override bool TryFindInPath(string executable, out string fullPath)
        {
            fullPath = ExecPath.FindInPath(executable, BuildAugmentedPath());
            return !string.IsNullOrEmpty(fullPath);
        }
    }
}
