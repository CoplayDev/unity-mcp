using System;
using System.Diagnostics;
using System.IO;
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
        private const string PythonDirOverrideKey = "MCPForUnity.PythonDirOverride";
        private const string UvPathOverrideKey = "MCPForUnity.UvPath";
        private const string ClaudeCliPathOverrideKey = "MCPForUnity.ClaudeCliPath";

        public bool HasMcpServerOverride => !string.IsNullOrEmpty(EditorPrefs.GetString(PythonDirOverrideKey, null));
        public bool HasUvPathOverride => !string.IsNullOrEmpty(EditorPrefs.GetString(UvPathOverrideKey, null));
        public bool HasClaudeCliPathOverride => !string.IsNullOrEmpty(EditorPrefs.GetString(ClaudeCliPathOverrideKey, null));

        public string GetMcpServerPath()
        {
            // Check for override first
            string overridePath = EditorPrefs.GetString(PythonDirOverrideKey, null);
            if (!string.IsNullOrEmpty(overridePath) && File.Exists(Path.Combine(overridePath, "server.py")))
            {
                return overridePath;
            }

            // Fall back to automatic detection
            return McpPathResolver.FindPackagePythonDirectory(false);
        }

        public string GetUvPath()
        {
            // Check for override first
            string overridePath = EditorPrefs.GetString(UvPathOverrideKey, null);
            if (!string.IsNullOrEmpty(overridePath) && File.Exists(overridePath))
            {
                return overridePath;
            }

            // Fall back to automatic detection
            try
            {
                return ServerInstaller.FindUvPath();
            }
            catch
            {
                return null;
            }
        }

        public string GetClaudeCliPath()
        {
            // Check for override first
            string overridePath = EditorPrefs.GetString(ClaudeCliPathOverrideKey, null);
            if (!string.IsNullOrEmpty(overridePath) && File.Exists(overridePath))
            {
                return overridePath;
            }

            // Fall back to automatic detection
            return ExecPath.ResolveClaude();
        }

        public bool IsPythonDetected()
        {
            try
            {
                // Windows-specific Python detection
                if (Application.platform == RuntimePlatform.WindowsEditor)
                {
                    // Common Windows Python installation paths
                    string[] windowsCandidates =
                    {
                        @"C:\Python314\python.exe",
                        @"C:\Python313\python.exe",
                        @"C:\Python312\python.exe",
                        @"C:\Python311\python.exe",
                        @"C:\Python310\python.exe",
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Python\Python314\python.exe"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Python\Python313\python.exe"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Python\Python312\python.exe"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Python\Python311\python.exe"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Python\Python310\python.exe"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Python314\python.exe"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Python313\python.exe"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Python312\python.exe"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Python311\python.exe"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Python310\python.exe"),
                    };

                    foreach (string c in windowsCandidates)
                    {
                        if (File.Exists(c)) return true;
                    }

                    // Try 'where python' command (Windows equivalent of 'which')
                    var psi = new ProcessStartInfo
                    {
                        FileName = "where",
                        Arguments = "python",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                    using (var p = Process.Start(psi))
                    {
                        string outp = p.StandardOutput.ReadToEnd().Trim();
                        p.WaitForExit(2000);
                        if (p.ExitCode == 0 && !string.IsNullOrEmpty(outp))
                        {
                            string[] lines = outp.Split('\n');
                            foreach (string line in lines)
                            {
                                string trimmed = line.Trim();
                                if (File.Exists(trimmed)) return true;
                            }
                        }
                    }
                }
                else
                {
                    // macOS/Linux detection
                    string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) ?? string.Empty;
                    string[] candidates =
                    {
                        "/opt/homebrew/bin/python3",
                        "/usr/local/bin/python3",
                        "/usr/bin/python3",
                        "/opt/local/bin/python3",
                        Path.Combine(home, ".local", "bin", "python3"),
                        "/Library/Frameworks/Python.framework/Versions/3.14/bin/python3",
                        "/Library/Frameworks/Python.framework/Versions/3.13/bin/python3",
                        "/Library/Frameworks/Python.framework/Versions/3.12/bin/python3",
                        "/Library/Frameworks/Python.framework/Versions/3.11/bin/python3",
                        "/Library/Frameworks/Python.framework/Versions/3.10/bin/python3",
                    };
                    foreach (string c in candidates)
                    {
                        if (File.Exists(c)) return true;
                    }

                    // Try 'which python3'
                    var psi = new ProcessStartInfo
                    {
                        FileName = "/usr/bin/which",
                        Arguments = "python3",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                    using (var p = Process.Start(psi))
                    {
                        string outp = p.StandardOutput.ReadToEnd().Trim();
                        p.WaitForExit(2000);
                        if (p.ExitCode == 0 && !string.IsNullOrEmpty(outp) && File.Exists(outp)) return true;
                    }
                }
            }
            catch { }
            return false;
        }

        public bool IsUvDetected()
        {
            return !string.IsNullOrEmpty(GetUvPath());
        }

        public bool IsClaudeCliDetected()
        {
            return !string.IsNullOrEmpty(GetClaudeCliPath());
        }

        public void SetMcpServerOverride(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                ClearMcpServerOverride();
                return;
            }

            if (!File.Exists(Path.Combine(path, "server.py")))
            {
                throw new ArgumentException("The selected folder does not contain server.py");
            }

            EditorPrefs.SetString(PythonDirOverrideKey, path);
        }

        public void SetUvPathOverride(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                ClearUvPathOverride();
                return;
            }

            if (!File.Exists(path))
            {
                throw new ArgumentException("The selected UV executable does not exist");
            }

            EditorPrefs.SetString(UvPathOverrideKey, path);
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

            EditorPrefs.SetString(ClaudeCliPathOverrideKey, path);
            // Also update the ExecPath helper for backwards compatibility
            ExecPath.SetClaudeCliPath(path);
        }

        public void ClearMcpServerOverride()
        {
            EditorPrefs.DeleteKey(PythonDirOverrideKey);
        }

        public void ClearUvPathOverride()
        {
            EditorPrefs.DeleteKey(UvPathOverrideKey);
        }

        public void ClearClaudeCliPathOverride()
        {
            EditorPrefs.DeleteKey(ClaudeCliPathOverrideKey);
        }
    }
}
