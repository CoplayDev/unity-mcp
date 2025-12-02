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
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "python.exe" : "python3",
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                p.WaitForExit(2000);
                return p.ExitCode == 0;
            }
            catch
            {
                return false;
            }
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

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home))
            {
                yield return Path.Combine(home, ".local", "bin", exeName);
                yield return Path.Combine(home, ".cargo", "bin", exeName);
            }

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
                }

                if (!string.IsNullOrEmpty(programFiles))
                {
                    yield return Path.Combine(programFiles, "uv", exeName);
                }
            }

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
    }
}
