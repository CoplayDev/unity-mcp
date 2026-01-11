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
        public bool HasUvxPathOverride => !string.IsNullOrEmpty(EditorPrefs.GetString(EditorPrefKeys.UvxPathOverride, null));
        public bool HasClaudeCliPathOverride => !string.IsNullOrEmpty(EditorPrefs.GetString(EditorPrefKeys.ClaudeCliPathOverride, null));

        public string GetUvxPath()
        {
            try
            {
                string overridePath = EditorPrefs.GetString(EditorPrefKeys.UvxPathOverride, string.Empty);
                if (!string.IsNullOrEmpty(overridePath))
                {
                    return overridePath;
                }
            }
            catch
            {
                // ignore EditorPrefs read errors and fall back to default command
                McpLog.Debug("No uvx path override found, falling back to default command");
            }

            string discovered = ResolveUvxFromSystem();
            if (!string.IsNullOrEmpty(discovered))
            {
                return discovered;
            }

            return "uvx";
        }

        public string GetClaudeCliPath()
        {
            try
            {
                string overridePath = EditorPrefs.GetString(EditorPrefKeys.ClaudeCliPathOverride, string.Empty);
                if (!string.IsNullOrEmpty(overridePath) && File.Exists(overridePath))
                {
                    return overridePath;
                }
            }
            catch { /* ignore */ }

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
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), ".local", "bin", "claude")
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
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), ".local", "bin", "claude")
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

        private static string ResolveUvxFromSystem()
        {
            try
            {
                foreach (string candidate in EnumerateUvxCandidates())
                {
                    if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
            catch
            {
                // fall back to bare command
            }

            return null;
        }

        private static IEnumerable<string> EnumerateUvxCandidates()
        {
            string exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "uvx.exe" : "uvx";

            // Priority 1: User-configured PATH (most common scenario from official install scripts)
            string pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                foreach (string rawDir in pathEnv.Split(Path.PathSeparator))
                {
                    if (string.IsNullOrWhiteSpace(rawDir)) continue;
                    string dir = rawDir.Trim();
                    yield return Path.Combine(dir, exeName);

                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        // Some PATH entries may already contain the file without extension
                        yield return Path.Combine(dir, "uvx");
                    }
                }
            }

            // Priority 2: User directories
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home))
            {
                yield return Path.Combine(home, ".local", "bin", exeName);
                yield return Path.Combine(home, ".cargo", "bin", exeName);
            }

            // Priority 3: System directories (platform-specific)
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
                // Priority 4: Windows-specific program directories
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

                if (!string.IsNullOrEmpty(localAppData))
                {
                    yield return Path.Combine(localAppData, "Programs", "uv", exeName);
                }

                if (!string.IsNullOrEmpty(programFiles))
                {
                    yield return Path.Combine(programFiles, "uv", exeName);
                }
            }
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
        /// <param name="uvPath">Absolute or relative path to the uv/uvx executable.</param>
        /// <param name="version">Parsed version string if successful.</param>
        /// <returns>True when the executable runs and returns a uv version string.</returns>
        public bool TryValidateUvExecutable(string uvPath, out string version)
        {
            version = null;

            if (string.IsNullOrEmpty(uvPath))
                return false;

            try
            {
                // Check if the path is just a command name (no directory separator)
                bool isBareCommand = !uvPath.Contains('/') && !uvPath.Contains('\\');

                if (isBareCommand)
                {
                    // For bare commands like "uvx", use where/which to find full path first
                    string fullPath = FindUvxExecutableInPath(uvPath);
                    if (string.IsNullOrEmpty(fullPath))
                        return false;
                    uvPath = fullPath;
                }

                // Use ExecPath.TryRun which properly handles async output reading and timeouts
                if (!ExecPath.TryRun(uvPath, "--version", null, out string stdout, out string stderr, 5000))
                    return false;

                // Check stdout first, then stderr (some tools output to stderr)
                string versionOutput = !string.IsNullOrWhiteSpace(stdout) ? stdout.Trim() : stderr.Trim();

                // uvx outputs "uvx x.y.z" or "uv x.y.z", extract version number
                if (versionOutput.StartsWith("uv ") || versionOutput.StartsWith("uvx "))
                {
                    // Extract version: "uvx 0.9.18 (hash date)" -> "0.9.18"
                    int spaceIndex = versionOutput.IndexOf(' ');
                    if (spaceIndex >= 0)
                    {
                        string afterCommand = versionOutput.Substring(spaceIndex + 1).Trim();
                        // Version is up to the first space or parenthesis
                        int parenIndex = afterCommand.IndexOf('(');
                        version = parenIndex > 0
                            ? afterCommand.Substring(0, parenIndex).Trim()
                            : afterCommand.Split(' ')[0];
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
                string exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !commandName.EndsWith(".exe")
                    ? commandName + ".exe"
                    : commandName;

                // First try EnumerateUvxCandidates which checks File.Exists
                foreach (string candidate in EnumerateUvxCandidates())
                {
                    if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
                    {
                        // Check if this candidate matches our command name
                        string candidateName = Path.GetFileName(candidate);
                        if (candidateName.Equals(exeName, StringComparison.OrdinalIgnoreCase) ||
                            candidateName.Equals(commandName, StringComparison.OrdinalIgnoreCase))
                        {
                            return candidate;
                        }
                    }
                }
            }
            catch
            {
                // Ignore errors
            }

            return null;
        }
    }
}
