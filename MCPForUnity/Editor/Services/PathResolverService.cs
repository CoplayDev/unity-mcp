using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using UnityEditor;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Implementation of path resolver service with override support
    /// </summary>
    public class PathResolverService : IPathResolverService
    {
        public bool HasUvxPathOverride => !string.IsNullOrEmpty(EditorPrefs.GetString(EditorPrefKeys.UvxPathOverride, null));
        public bool HasClaudeCliPathOverride => !string.IsNullOrEmpty(EditorPrefs.GetString(EditorPrefKeys.ClaudeCliPathOverride, null));
        public bool HasPythonPathOverride => !string.IsNullOrEmpty(EditorPrefs.GetString(EditorPrefKeys.PythonPathOverride, null));
        public bool HasNodePathOverride => !string.IsNullOrEmpty(EditorPrefs.GetString(EditorPrefKeys.NodePathOverride, null));

        public string GetUvxPath()
        {
            try
            {
                string overridePath = EditorPrefs.GetString(EditorPrefKeys.UvxPathOverride, string.Empty);
                if (!string.IsNullOrEmpty(overridePath) && File.Exists(overridePath))
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

        public string GetPythonPath()
        {
            try
            {
                string overridePath = EditorPrefs.GetString(EditorPrefKeys.PythonPathOverride, string.Empty);
                if (!string.IsNullOrEmpty(overridePath) && File.Exists(overridePath))
                {
                    return overridePath;
                }
            }
            catch
            {
                McpLog.Debug("No Python path override found, falling back to default command");
            }
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "python" : "python3";
        }

        public string GetNodePath()
        {
            try
            {
                string overridePath = EditorPrefs.GetString(EditorPrefKeys.NodePathOverride, string.Empty);
                if (!string.IsNullOrEmpty(overridePath) && File.Exists(overridePath))
                {
                    return overridePath;
                }
            }
            catch
            {
                McpLog.Debug("No Node path override found, falling back to default command");
            }
            return "node";
        }

        public bool IsPythonDetected()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = GetPythonPath(),
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (!p.WaitForExit(2000))
                {
                    try { p.Kill(); } catch { /* ignore */ }
                    return false;
                }
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

        public void SetPythonPathOverride(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                ClearPythonPathOverride();
                return;
            }

            // Allow commands on PATH, but validate explicit paths
            if ((Path.IsPathRooted(path) || path.Contains("/") || path.Contains("\\")) && !File.Exists(path))
            {
                throw new ArgumentException("The selected Python executable does not exist");
            }

            EditorPrefs.SetString(EditorPrefKeys.PythonPathOverride, path);
        }

        public void ClearPythonPathOverride()
        {
            EditorPrefs.DeleteKey(EditorPrefKeys.PythonPathOverride);
        }

        public void SetNodePathOverride(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                ClearNodePathOverride();
                return;
            }

            // Allow commands on PATH, but validate explicit paths
            if ((Path.IsPathRooted(path) || path.Contains("/") || path.Contains("\\")) && !File.Exists(path))
            {
                throw new ArgumentException("The selected Node executable does not exist");
            }

            EditorPrefs.SetString(EditorPrefKeys.NodePathOverride, path);
        }

        public void ClearNodePathOverride()
        {
            EditorPrefs.DeleteKey(EditorPrefKeys.NodePathOverride);
        }
    }
}
