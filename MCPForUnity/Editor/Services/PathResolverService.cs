using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Implementation of path resolver service with override support
    /// </summary>
    public class PathResolverService : IPathResolverService
    {
        private bool _hasUvxPathFallback;

        public bool HasUvxPathOverride => !string.IsNullOrEmpty(EditorPrefs.GetString(EditorPrefKeys.UvxPathOverride, null));
        public bool HasClaudeCliPathOverride => !string.IsNullOrEmpty(EditorPrefs.GetString(EditorPrefKeys.ClaudeCliPathOverride, null));
        public bool HasUvxPathFallback => _hasUvxPathFallback;

        public string GetUvxPath()
        {
            // Reset fallback flag at the start of each resolution
            _hasUvxPathFallback = false;

            // Check override first - only validate if explicitly set
            if (HasUvxPathOverride)
            {
                string overridePath = EditorPrefs.GetString(EditorPrefKeys.UvxPathOverride, string.Empty);
                // Validate the override - if invalid, fall back to system discovery
                if (TryValidateUvxExecutable(overridePath, out string version))
                {
                    return overridePath;
                }
                // Override is set but invalid - fall back to system discovery
                string fallbackPath = ResolveUvxFromSystem();
                if (!string.IsNullOrEmpty(fallbackPath))
                {
                    _hasUvxPathFallback = true;
                    return fallbackPath;
                }
                // Return null to indicate override is invalid and no system fallback found
                return null;
            }

            // No override set - try discovery (uvx first, then uv)
            string discovered = ResolveUvxFromSystem();
            if (!string.IsNullOrEmpty(discovered))
            {
                return discovered;
            }

            // Fallback to bare command
            return "uvx";
        }

        /// <summary>
        /// Resolves uv/uvx from system by trying both commands.
        /// Returns the full path if found, null otherwise.
        /// </summary>
        private static string ResolveUvxFromSystem()
        {
            try
            {
                // Try uvx first, then uv
                string[] commandNames = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? new[] { "uvx.exe", "uv.exe" }
                    : new[] { "uvx", "uv" };

                foreach (string commandName in commandNames)
                {
                    foreach (string candidate in EnumerateCommandCandidates(commandName))
                    {
                        if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
                        {
                            return candidate;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                McpLog.Debug($"PathResolver error: {ex.Message}");
            }

            return null;
        }



        public string GetClaudeCliPath()
        {
            // Check override first - only validate if explicitly set
            if (HasClaudeCliPathOverride)
            {
                string overridePath = EditorPrefs.GetString(EditorPrefKeys.ClaudeCliPathOverride, string.Empty);
                // Validate the override - if invalid, don't fall back to discovery
                if (File.Exists(overridePath))
                {
                    return overridePath;
                }
                // Override is set but invalid - return null (no fallback)
                return null;
            }

            // No override - use platform-specific discovery
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                string[] candidates = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "claude", "claude.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "claude", "claude.exe"),
                    "claude.exe"
                };

                foreach (var c in candidates)
                {
                    if (File.Exists(c)) return c;
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                string[] candidates = new[]
                {
                    "/opt/homebrew/bin/claude",
                    "/usr/local/bin/claude",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude")
                };

                foreach (var c in candidates)
                {
                    if (File.Exists(c)) return c;
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                string[] candidates = new[]
                {
                    "/usr/bin/claude",
                    "/usr/local/bin/claude",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude")
                };

                foreach (var c in candidates)
                {
                    if (File.Exists(c)) return c;
                }
            }

            return null;
        }

        public bool IsPythonDetected()
        {
            return ExecPath.TryRun(
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "python.exe" : "python3",
                "--version",
                null,
                out _,
                out _,
                2000);
        }

        public bool IsClaudeCliDetected()
        {
            return !string.IsNullOrEmpty(GetClaudeCliPath());
        }

        public void SetUvxPathOverride(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                ClearUvxPathOverride();
                return;
            }

            if (!File.Exists(path))
            {
                throw new ArgumentException("The selected uvx executable does not exist");
            }

            EditorPrefs.SetString(EditorPrefKeys.UvxPathOverride, path);
        }

        public void SetClaudeCliPathOverride(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                ClearClaudeCliPathOverride();
                return;
            }

            if (!File.Exists(path))
            {
                throw new ArgumentException("The selected Claude CLI executable does not exist");
            }

            EditorPrefs.SetString(EditorPrefKeys.ClaudeCliPathOverride, path);
        }

        public void ClearUvxPathOverride()
        {
            EditorPrefs.DeleteKey(EditorPrefKeys.UvxPathOverride);
        }

        public void ClearClaudeCliPathOverride()
        {
            EditorPrefs.DeleteKey(EditorPrefKeys.ClaudeCliPathOverride);
        }

        /// <summary>
        /// Validates the provided uv executable by running "--version" and parsing the output.
        /// </summary>
        /// <param name="uvxPath">Absolute or relative path to the uv/uvx executable.</param>
        /// <param name="version">Parsed version string if successful.</param>
        /// <returns>True when the executable runs and returns a uvx version string.</returns>
        public bool TryValidateUvxExecutable(string uvxPath, out string version)
        {
            version = null;

            if (string.IsNullOrEmpty(uvxPath))
                return false;

            try
            {
                // Check if the path is just a command name (no directory separator)
                bool isBareCommand = !uvxPath.Contains('/') && !uvxPath.Contains('\\');

                if (isBareCommand)
                {
                    // For bare commands like "uvx" or "uv", use EnumerateCommandCandidates to find full path first
                    string fullPath = FindUvxExecutableInPath(uvxPath);
                    if (string.IsNullOrEmpty(fullPath))
                        return false;
                    uvxPath = fullPath;
                }

                // Use ExecPath.TryRun which properly handles async output reading and timeouts
                if (!ExecPath.TryRun(uvxPath, "--version", null, out string stdout, out string stderr, 5000))
                    return false;

                // Check stdout first, then stderr (some tools output to stderr)
                string versionOutput = !string.IsNullOrWhiteSpace(stdout) ? stdout.Trim() : stderr.Trim();

                // uv/uvx outputs "uv x.y.z" or "uvx x.y.z", extract version number
                if (versionOutput.StartsWith("uvx ") || versionOutput.StartsWith("uv "))
                {
                    // Extract version: "uv 0.9.18 (hash date)" -> "0.9.18"
                    int spaceIndex = versionOutput.IndexOf(' ');
                    if (spaceIndex >= 0)
                    {
                        string afterCommand = versionOutput.Substring(spaceIndex + 1).Trim();
                        // Version is up to the first space or parenthesis
                        int nextSpace = afterCommand.IndexOf(' ');
                        int parenIndex = afterCommand.IndexOf('(');
                        int endIndex = Math.Min(
                            nextSpace >= 0 ? nextSpace : int.MaxValue,
                            parenIndex >= 0 ? parenIndex : int.MaxValue
                        );
                        version = endIndex < int.MaxValue ? afterCommand.Substring(0, endIndex).Trim() : afterCommand;
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

        private string FindUvxExecutableInPath(string commandName)
        {
            try
            {
                // Generic search for any command in PATH and common locations
                foreach (string candidate in EnumerateCommandCandidates(commandName))
                {
                    if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
            catch
            {
                // Ignore errors
            }

            return null;
        }

        /// <summary>
        /// Enumerates candidate paths for a generic command name.
        /// Searches PATH and common locations.
        /// </summary>
        private static IEnumerable<string> EnumerateCommandCandidates(string commandName)
        {
            string exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !commandName.EndsWith(".exe")
                ? commandName + ".exe"
                : commandName;

            // Search PATH first
            string pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                foreach (string rawDir in pathEnv.Split(Path.PathSeparator))
                {
                    if (string.IsNullOrWhiteSpace(rawDir)) continue;
                    string dir = rawDir.Trim();
                    yield return Path.Combine(dir, exeName);
                }
            }

            // User-local binary directories
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home))
            {
                yield return Path.Combine(home, ".local", "bin", exeName);
                yield return Path.Combine(home, ".cargo", "bin", exeName);
            }

            // System directories (platform-specific)
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                yield return "/opt/homebrew/bin/" + exeName;
                yield return "/usr/local/bin/" + exeName;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                yield return "/usr/local/bin/" + exeName;
                yield return "/usr/bin/" + exeName;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

                if (!string.IsNullOrEmpty(localAppData))
                {
                    yield return Path.Combine(localAppData, "Programs", "uv", exeName);
                    // WinGet creates shim files in this location
                    yield return Path.Combine(localAppData, "Microsoft", "WinGet", "Links", exeName);
                }

                if (!string.IsNullOrEmpty(programFiles))
                {
                    yield return Path.Combine(programFiles, "uv", exeName);
                }
            }
        }
    }
}
