using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Constants;
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

        public string GetUvxPath(bool verifyPath = true)
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

        public bool IsUvxDetected()
        {
            return !string.IsNullOrEmpty(GetUvxPath());
        }

        public string GetUvxPackageSourcePath()
        {
            try
            {
                // Get the uv cache directory
                var cacheDirProcess = new ProcessStartInfo
                {
                    FileName = "uv",
                    Arguments = "cache dir",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(cacheDirProcess);
                if (process == null) return null;

                string cacheDir = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit(5000);

                if (process.ExitCode != 0 || string.IsNullOrEmpty(cacheDir))
                {
                    McpLog.Error("Failed to get uv cache directory");
                    return null;
                }

                // Normalize path for cross-platform compatibility
                cacheDir = Path.GetFullPath(cacheDir);

                // Look for git checkouts directory
                string gitCheckoutsDir = Path.Combine(cacheDir, "git-v0", "checkouts");
                if (!Directory.Exists(gitCheckoutsDir))
                {
                    McpLog.Error($"Git checkouts directory not found: {gitCheckoutsDir}");
                    return null;
                }

                // Find the unity-mcp checkout directory
                // The pattern is: git-v0/checkouts/{hash}/{commit-hash}/
                foreach (var hashDir in Directory.GetDirectories(gitCheckoutsDir))
                {
                    foreach (var commitDir in Directory.GetDirectories(hashDir))
                    {
                        // Check for the new Server structure
                        string serverPath = Path.Combine(commitDir, "Server");
                        if (Directory.Exists(serverPath))
                        {
                            // Verify it has the expected pyproject.toml
                            string pyprojectPath = Path.Combine(serverPath, "pyproject.toml");
                            if (File.Exists(pyprojectPath))
                            {
                                McpLog.Info($"Found uvx package source at: {serverPath}");
                                return serverPath;
                            }
                        }
                    }
                }

                var packageVersion = AssetPathUtility.GetPackageVersion();
                McpLog.Error($"Server package source not found in uv cache. Make sure to run: uvx --from git+https://github.com/CoplayDev/unity-mcp@{packageVersion}#subdirectory=Server mcp-for-unity");
                return null;
            }
            catch (System.Exception ex)
            {
                McpLog.Error($"Failed to find uvx package source: {ex.Message}");
                return null;
            }
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
    }
}
