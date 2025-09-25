using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using UnityEditor;

namespace MCPForUnity.Editor.Helpers
{
    internal static class ExecPath
    {
        private const string PrefClaude = "MCPForUnity.ClaudeCliPath";

        // Resolve Claude CLI absolute path. Pref → env → common locations → PATH.
        internal static string ResolveClaude()
        {
            try
            {
                string pref = EditorPrefs.GetString(PrefClaude, string.Empty);
                if (!string.IsNullOrEmpty(pref) && File.Exists(pref)) return pref;
            }
            catch { }

            string env = Environment.GetEnvironmentVariable("CLAUDE_CLI");
            if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) ?? string.Empty;
                string[] candidates =
                {
                    "/opt/homebrew/bin/claude",
                    "/usr/local/bin/claude",
                    Path.Combine(home, ".local", "bin", "claude"),
                };
                foreach (string c in candidates) { if (File.Exists(c)) return c; }
                // Try NVM-installed claude under ~/.nvm/versions/node/*/bin/claude
                string nvmClaude = ResolveClaudeFromNvm(home);
                if (!string.IsNullOrEmpty(nvmClaude)) return nvmClaude;
#if UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
                return Which("claude", "/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin");
#else
                return null;
#endif
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
#if UNITY_EDITOR_WIN
                // Common npm global locations
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) ?? string.Empty;
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) ?? string.Empty;
                string[] candidates =
                {
                    // Prefer .cmd (most reliable from non-interactive processes)
                    Path.Combine(appData, "npm", "claude.cmd"),
                    Path.Combine(localAppData, "npm", "claude.cmd"),
                    // Fall back to PowerShell shim if only .ps1 is present
                    Path.Combine(appData, "npm", "claude.ps1"),
                    Path.Combine(localAppData, "npm", "claude.ps1"),
                };
                foreach (string c in candidates) { if (File.Exists(c)) return c; }
                string fromWhere = Where("claude.exe") ?? Where("claude.cmd") ?? Where("claude.ps1") ?? Where("claude");
                if (!string.IsNullOrEmpty(fromWhere)) return fromWhere;
#endif
                return null;
            }

            // Linux
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) ?? string.Empty;
                string[] candidates =
                {
                    "/usr/local/bin/claude",
                    "/usr/bin/claude",
                    Path.Combine(home, ".local", "bin", "claude"),
                };
                foreach (string c in candidates) { if (File.Exists(c)) return c; }
                // Try NVM-installed claude under ~/.nvm/versions/node/*/bin/claude
                string nvmClaude = ResolveClaudeFromNvm(home);
                if (!string.IsNullOrEmpty(nvmClaude)) return nvmClaude;
#if UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
                return Which("claude", "/usr/local/bin:/usr/bin:/bin");
#else
                return null;
#endif
            }
        }

        // Attempt to resolve claude from NVM-managed Node installations, choosing the newest version
        private static string ResolveClaudeFromNvm(string home)
        {
            try
            {
                if (string.IsNullOrEmpty(home)) return null;
                string nvmNodeDir = Path.Combine(home, ".nvm", "versions", "node");
                if (!Directory.Exists(nvmNodeDir)) return null;

                string bestPath = null;
                Version bestVersion = null;
                foreach (string versionDir in Directory.EnumerateDirectories(nvmNodeDir))
                {
                    string name = Path.GetFileName(versionDir);
                    if (string.IsNullOrEmpty(name)) continue;
                    if (name.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                    {
                        // Extract numeric portion: e.g., v18.19.0-nightly -> 18.19.0
                        string versionStr = name.Substring(1);
                        int dashIndex = versionStr.IndexOf('-');
                        if (dashIndex > 0)
                        {
                            versionStr = versionStr.Substring(0, dashIndex);
                        }
                        if (Version.TryParse(versionStr, out Version parsed))
                        {
                            string candidate = Path.Combine(versionDir, "bin", "claude");
                            if (File.Exists(candidate))
                            {
                                if (bestVersion == null || parsed > bestVersion)
                                {
                                    bestVersion = parsed;
                                    bestPath = candidate;
                                }
                            }
                        }
                    }
                }
                return bestPath;
            }
            catch { return null; }
        }

        // Explicitly set the Claude CLI absolute path override in EditorPrefs
        internal static void SetClaudeCliPath(string absolutePath)
        {
            try
            {
                if (!string.IsNullOrEmpty(absolutePath) && File.Exists(absolutePath))
                {
                    EditorPrefs.SetString(PrefClaude, absolutePath);
                }
            }
            catch { }
        }

        // Clear any previously set Claude CLI override path
        internal static void ClearClaudeCliPath()
        {
            try
            {
                if (EditorPrefs.HasKey(PrefClaude))
                {
                    EditorPrefs.DeleteKey(PrefClaude);
                }
            }
            catch { }
        }

        // Use existing UV resolver; returns absolute path or null.
        internal static string ResolveUv()
        {
            return ServerInstaller.FindUvPath();
        }

        internal static bool TryRun(
            string file,
            string args,
            string workingDir,
            out string stdout,
            out string stderr,
            int timeoutMs = 15000,
            string extraPathPrepend = null)
        {
            stdout = string.Empty;
            stderr = string.Empty;
            try
            {
                // Handle PowerShell scripts on Windows by invoking through powershell.exe
                bool isPs1 = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
                             file.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase);

                var psi = new ProcessStartInfo
                {
                    FileName = isPs1 ? "powershell.exe" : file,
                    Arguments = isPs1
                        ? $"-NoProfile -ExecutionPolicy Bypass -File \"{file}\" {args}".Trim()
                        : args,
                    WorkingDirectory = string.IsNullOrEmpty(workingDir) ? Environment.CurrentDirectory : workingDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                if (!string.IsNullOrEmpty(extraPathPrepend))
                {
                    string currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                    psi.EnvironmentVariables["PATH"] = string.IsNullOrEmpty(currentPath)
                        ? extraPathPrepend
                        : (extraPathPrepend + System.IO.Path.PathSeparator + currentPath);
                }

                using var process = new Process { StartInfo = psi, EnableRaisingEvents = false };

                var so = new StringBuilder();
                var se = new StringBuilder();
                process.OutputDataReceived += (_, e) => { if (e.Data != null) so.AppendLine(e.Data); };
                process.ErrorDataReceived  += (_, e) => { if (e.Data != null) se.AppendLine(e.Data); };

                if (!process.Start()) return false;

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                if (!process.WaitForExit(timeoutMs))
                {
                    try { process.Kill(); } catch { }
                    return false;
                }

                // Ensure async buffers are flushed
                process.WaitForExit();

                stdout = so.ToString();
                stderr = se.ToString();
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

#if UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
        private static string Which(string exe, string prependPath)
        {
            try
            {
                var psi = new ProcessStartInfo("/usr/bin/which", exe)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                };
                string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                psi.EnvironmentVariables["PATH"] = string.IsNullOrEmpty(path) ? prependPath : (prependPath + Path.PathSeparator + path);
                using var p = Process.Start(psi);
                string output = p?.StandardOutput.ReadToEnd().Trim();
                p?.WaitForExit(1500);
                return (!string.IsNullOrEmpty(output) && File.Exists(output)) ? output : null;
            }
            catch { return null; }
        }
#endif

#if UNITY_EDITOR_WIN
        private static string Where(string exe)
        {
            try
            {
                var psi = new ProcessStartInfo("where", exe)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                string first = p?.StandardOutput.ReadToEnd()
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault();
                p?.WaitForExit(1500);
                return (!string.IsNullOrEmpty(first) && File.Exists(first)) ? first : null;
            }
            catch { return null; }
        }
#endif
    }
}


