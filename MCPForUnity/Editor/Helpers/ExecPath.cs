using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using MCPForUnity.Editor.Constants;
using UnityEditor;

namespace MCPForUnity.Editor.Helpers
{
    internal static class ExecPath
    {
        private const string PrefClaude = EditorPrefKeys.ClaudeCliPathOverride;

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
                    Path.Combine(home, ".claude", "local", "claude"), // `claude migrate-installer` location
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
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) ?? string.Empty;
                string[] candidates =
                {
                    // Native installer locations
                    Path.Combine(localAppData, "Programs", "claude", "claude.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "claude", "claude.exe"),
                    Path.Combine(home, ".local", "bin", "claude.exe"),
                    Path.Combine(home, ".claude", "local", "claude.exe"), // `claude migrate-installer` location
                    // npm global locations (.cmd preferred for non-interactive processes)
                    Path.Combine(appData, "npm", "claude.cmd"),
                    Path.Combine(localAppData, "npm", "claude.cmd"),
                    // Fall back to PowerShell shim if only .ps1 is present
                    Path.Combine(appData, "npm", "claude.ps1"),
                    Path.Combine(localAppData, "npm", "claude.ps1"),
                };
                foreach (string c in candidates) { if (File.Exists(c)) return c; }
                string fromWhere = FindInPathWindows("claude.exe") ?? FindInPathWindows("claude.cmd") ?? FindInPathWindows("claude.ps1") ?? FindInPathWindows("claude");
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
                    Path.Combine(home, ".claude", "local", "claude"), // `claude migrate-installer` location
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

        /// <summary>
        /// Assigns PATH on a ProcessStartInfo using the spelling the inherited environment already
        /// uses. Mono - the runtime the Editor runs on - backs EnvironmentVariables with a
        /// case-SENSITIVE dictionary, so writing "PATH" when Windows handed us "Path" adds a second,
        /// separate entry and the child process keeps reading the original one. That silently made
        /// every extraPathPrepend on Windows a no-op.
        /// </summary>
        private static void SetPathVariable(ProcessStartInfo psi, string value)
        {
            string key = "PATH";
            foreach (string existing in psi.EnvironmentVariables.Keys)
            {
                if (string.Equals(existing, "PATH", StringComparison.OrdinalIgnoreCase))
                {
                    key = existing;
                    break;
                }
            }
            psi.EnvironmentVariables[key] = value;
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
                bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

                // Handle PowerShell scripts on Windows by invoking through powershell.exe
                bool isPs1 = isWindows && file.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase);

                // Handle batch shims (pyenv-win, npm .cmd wrappers, ...) on Windows: CreateProcess
                // cannot launch .bat/.cmd directly while UseShellExecute is false, so route them
                // through cmd.exe instead of failing with a Win32Exception.
                bool isBatch = isWindows &&
                               (file.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) ||
                                file.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase));

                string fileName;
                string arguments;
                if (isPs1)
                {
                    fileName = "powershell.exe";
                    arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{file}\" {args}".Trim();
                }
                else if (isBatch)
                {
                    fileName = "cmd.exe";
                    // /s /c plus an outer pair of quotes keeps paths containing spaces intact
                    arguments = $"/s /c \"\"{file}\" {args}\"";
                }
                else
                {
                    fileName = file;
                    arguments = args;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    WorkingDirectory = string.IsNullOrEmpty(workingDir) ? Environment.CurrentDirectory : workingDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                if (!string.IsNullOrEmpty(extraPathPrepend))
                {
                    string currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                    SetPathVariable(psi, string.IsNullOrEmpty(currentPath)
                        ? extraPathPrepend
                        : (extraPathPrepend + System.IO.Path.PathSeparator + currentPath));
                }

                using var process = new Process { StartInfo = psi, EnableRaisingEvents = false };

                var sb = new StringBuilder();
                var se = new StringBuilder();
                process.OutputDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
                process.ErrorDataReceived += (_, e) => { if (e.Data != null) se.AppendLine(e.Data); };

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

                stdout = sb.ToString();
                stderr = se.ToString();
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Cross-platform path lookup. Uses 'where' on Windows, 'which' on macOS/Linux.
        /// Returns the full path if found, null otherwise.
        /// </summary>
        internal static string FindInPath(string executable, string extraPathPrepend = null)
        {
#if UNITY_EDITOR_WIN
            return FindInPathWindows(executable, extraPathPrepend);
#elif UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
            return Which(executable, extraPathPrepend ?? string.Empty);
#else
            return null;
#endif
        }

        /// <summary>
        /// Like <see cref="FindInPath"/>, but returns every match in PATH order instead of only the
        /// first one. On Windows a single name can resolve to several entries (App Execution Alias
        /// stubs, pyenv-win .bat shims, real interpreters); callers that validate the candidate need
        /// to be able to skip the ones that turn out to be non-functional.
        /// </summary>
        internal static string[] FindAllInPath(string executable, string extraPathPrepend = null)
        {
#if UNITY_EDITOR_WIN
            return FindAllInPathWindows(executable, extraPathPrepend);
#else
            string single = FindInPath(executable, extraPathPrepend);
            return string.IsNullOrEmpty(single) ? Array.Empty<string>() : new[] { single };
#endif
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
                SetPathVariable(psi, string.IsNullOrEmpty(path) ? prependPath : (prependPath + Path.PathSeparator + path));

                using var p = Process.Start(psi);
                if (p == null) return null;

                var so = new StringBuilder();
                p.OutputDataReceived += (_, e) => { if (e.Data != null) so.AppendLine(e.Data); };
                p.BeginOutputReadLine();

                if (!p.WaitForExit(1500))
                {
                    try { p.Kill(); } catch { }
                    return null;
                }

                p.WaitForExit();
                string output = so.ToString().Trim();
                return (!string.IsNullOrEmpty(output) && File.Exists(output)) ? output : null;
            }
            catch { return null; }
        }
#endif

#if UNITY_EDITOR_WIN
        internal static string FindInPathWindows(string exe, string extraPathPrepend = null)
        {
            return FindAllInPathWindows(exe, extraPathPrepend).FirstOrDefault();
        }

        private static string[] FindAllInPathWindows(string exe, string extraPathPrepend = null)
        {
            try
            {
                string currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                string effectivePath = string.IsNullOrEmpty(extraPathPrepend)
                    ? currentPath
                    : (string.IsNullOrEmpty(currentPath) ? extraPathPrepend : extraPathPrepend + Path.PathSeparator + currentPath);

                var psi = new ProcessStartInfo("where", exe)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                if (!string.IsNullOrEmpty(effectivePath))
                {
                    SetPathVariable(psi, effectivePath);
                }

                using var p = Process.Start(psi);
                if (p == null) return Array.Empty<string>();

                var so = new StringBuilder();
                p.OutputDataReceived += (_, e) => { if (e.Data != null) so.AppendLine(e.Data); };
                p.BeginOutputReadLine();

                if (!p.WaitForExit(1500))
                {
                    try { p.Kill(); } catch { }
                    return Array.Empty<string>();
                }

                p.WaitForExit();
                return so.ToString()
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Trim())
                    .Where(line => line.Length > 0 && File.Exists(line))
                    .ToArray();
            }
            catch { return Array.Empty<string>(); }
        }
#endif
    }
}
