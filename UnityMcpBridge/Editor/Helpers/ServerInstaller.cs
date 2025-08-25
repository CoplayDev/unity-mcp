using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Helpers
{
    public static class ServerInstaller
    {
        private const string RootFolder = "UnityMCP";
        private const string ServerFolder = "UnityMcpServer";
        private const string VersionFileName = "server_version.txt";

        /// <summary>
        /// Ensures the mcp-for-unity-server is installed locally by copying from the embedded package source.
        /// No network calls or Git operations are performed.
        /// </summary>
        public static void EnsureServerInstalled()
        {
            try
            {
                string saveLocation = GetSaveLocation();
                TryCreateMacSymlinkForAppSupport();
                string destRoot = Path.Combine(saveLocation, ServerFolder);
                string destSrc = Path.Combine(destRoot, "src");

                // Detect legacy installs and version state (logs)
                DetectAndLogLegacyInstallStates(destRoot);

                // Resolve embedded source and versions
                if (!TryGetEmbeddedServerSource(out string embeddedSrc))
                {
                    throw new Exception("Could not find embedded UnityMcpServer/src in the package.");
                }
                string embeddedVer = ReadVersionFile(Path.Combine(embeddedSrc, VersionFileName)) ?? "unknown";
                string installedVer = ReadVersionFile(Path.Combine(destSrc, VersionFileName));

                bool destHasServer = File.Exists(Path.Combine(destSrc, "server.py"));
                bool needOverwrite = !destHasServer
                                     || string.IsNullOrEmpty(installedVer)
                                     || (!string.IsNullOrEmpty(embeddedVer) && CompareSemverSafe(installedVer, embeddedVer) < 0);

                // Ensure destination exists
                Directory.CreateDirectory(destRoot);

                if (needOverwrite)
                {
                    // Copy the entire UnityMcpServer folder (parent of src)
                    string embeddedRoot = Path.GetDirectoryName(embeddedSrc) ?? embeddedSrc; // go up from src to UnityMcpServer
                    CopyDirectoryRecursive(embeddedRoot, destRoot);
                    // Write/refresh version file
                    try { File.WriteAllText(Path.Combine(destSrc, VersionFileName), embeddedVer ?? "unknown"); } catch { }
                    McpLog.Info($"Installed/updated server to {destRoot} (version {embeddedVer}).");
                }

                // Cleanup legacy installs that are missing version or older than embedded
                foreach (var legacyRoot in GetLegacyRootsForDetection())
                {
                    try
                    {
                        string legacySrc = Path.Combine(legacyRoot, "src");
                        if (!File.Exists(Path.Combine(legacySrc, "server.py"))) continue;
                        string legacyVer = ReadVersionFile(Path.Combine(legacySrc, VersionFileName));
                        bool legacyOlder = string.IsNullOrEmpty(legacyVer)
                                           || (!string.IsNullOrEmpty(embeddedVer) && CompareSemverSafe(legacyVer, embeddedVer) < 0);
                        if (legacyOlder)
                        {
                            TryKillUvForPath(legacySrc);
                            try
                            {
                                Directory.Delete(legacyRoot, recursive: true);
                                McpLog.Info($"Removed legacy server at '{legacyRoot}'.");
                            }
                            catch (Exception ex)
                            {
                                McpLog.Warn($"Failed to remove legacy server at '{legacyRoot}': {ex.Message}");
                            }
                        }
                    }
                    catch { }
                }

                // Clear overrides that might point at legacy locations
                try
                {
                    EditorPrefs.DeleteKey("MCPForUnity.ServerSrc");
                    EditorPrefs.DeleteKey("MCPForUnity.PythonDirOverride");
                }
                catch { }
                return;
            }
            catch (Exception ex)
            {
                // If a usable server is already present (installed or embedded), don't fail hard—just warn.
                bool hasInstalled = false;
                try { hasInstalled = File.Exists(Path.Combine(GetServerPath(), "server.py")); } catch { }

                if (hasInstalled || TryGetEmbeddedServerSource(out _))
                {
                    McpLog.Warn($"Using existing server; skipped install. Details: {ex.Message}");
                    return;
                }

                McpLog.Error($"Failed to ensure server installation: {ex.Message}");
            }
        }

        public static string GetServerPath()
        {
            return Path.Combine(GetSaveLocation(), ServerFolder, "src");
        }

        /// <summary>
        /// Gets the platform-specific save location for the server.
        /// </summary>
        private static string GetSaveLocation()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Use per-user LocalApplicationData for canonical install location
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                                   ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) ?? string.Empty, "AppData", "Local");
                return Path.Combine(localAppData, RootFolder);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
                if (string.IsNullOrEmpty(xdg))
                {
                    xdg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) ?? string.Empty,
                                       ".local", "share");
                }
                return Path.Combine(xdg, RootFolder);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // On macOS, use LocalApplicationData (~/Library/Application Support)
                var localAppSupport = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                // Unity/Mono may map LocalApplicationData to ~/.local/share on macOS; normalize to Application Support
                bool looksLikeXdg = !string.IsNullOrEmpty(localAppSupport) && localAppSupport.Replace('\\', '/').Contains("/.local/share");
                if (string.IsNullOrEmpty(localAppSupport) || looksLikeXdg)
                {
                    // Fallback: construct from $HOME
                    var home = Environment.GetFolderPath(Environment.SpecialFolder.Personal) ?? string.Empty;
                    localAppSupport = Path.Combine(home, "Library", "Application Support");
                }
                TryCreateMacSymlinkForAppSupport();
                return Path.Combine(localAppSupport, RootFolder);
            }
            throw new Exception("Unsupported operating system.");
        }

        /// <summary>
        /// On macOS, create a no-spaces symlink ~/Library/AppSupport -> ~/Library/Application Support
        /// to mitigate arg parsing and quoting issues in some MCP clients.
        /// Safe to call repeatedly.
        /// </summary>
        private static void TryCreateMacSymlinkForAppSupport()
        {
            try
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return;
                string home = Environment.GetFolderPath(Environment.SpecialFolder.Personal) ?? string.Empty;
                if (string.IsNullOrEmpty(home)) return;

                string canonical = Path.Combine(home, "Library", "Application Support");
                string symlink = Path.Combine(home, "Library", "AppSupport");

                // If symlink exists already, nothing to do
                if (Directory.Exists(symlink) || File.Exists(symlink)) return;

                // Create symlink only if canonical exists
                if (!Directory.Exists(canonical)) return;

                // Use 'ln -s' to create a directory symlink (macOS)
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "/bin/ln",
                    Arguments = $"-s \"{canonical}\" \"{symlink}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var p = System.Diagnostics.Process.Start(psi);
                p?.WaitForExit(2000);
            }
            catch { /* best-effort */ }
        }

        private static bool IsDirectoryWritable(string path)
        {
            try
            {
                File.Create(Path.Combine(path, "test.txt")).Dispose();
                File.Delete(Path.Combine(path, "test.txt"));
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if the server is installed at the specified location.
        /// </summary>
        private static bool IsServerInstalled(string location)
        {
            return Directory.Exists(location)
                && File.Exists(Path.Combine(location, ServerFolder, "src", "server.py"));
        }

        /// <summary>
        /// Detects legacy installs or older versions and logs findings (no deletion yet).
        /// </summary>
        private static void DetectAndLogLegacyInstallStates(string canonicalRoot)
        {
            try
            {
                string canonicalSrc = Path.Combine(canonicalRoot, "src");
                // Normalize canonical root for comparisons
                string normCanonicalRoot = NormalizePathSafe(canonicalRoot);
                string embeddedSrc = null;
                TryGetEmbeddedServerSource(out embeddedSrc);

                string embeddedVer = ReadVersionFile(Path.Combine(embeddedSrc ?? string.Empty, VersionFileName));
                string installedVer = ReadVersionFile(Path.Combine(canonicalSrc, VersionFileName));

                // Legacy paths (macOS/Linux .config; Windows roaming as example)
                foreach (var legacyRoot in GetLegacyRootsForDetection())
                {
                    // Skip logging for the canonical root itself
                    if (PathsEqualSafe(legacyRoot, normCanonicalRoot))
                        continue;
                    string legacySrc = Path.Combine(legacyRoot, "src");
                    bool hasServer = File.Exists(Path.Combine(legacySrc, "server.py"));
                    string legacyVer = ReadVersionFile(Path.Combine(legacySrc, VersionFileName));

                    if (hasServer)
                    {
                        // Case 1: No version file
                        if (string.IsNullOrEmpty(legacyVer))
                        {
                            McpLog.Info("Detected legacy install without version file at: " + legacyRoot, always: false);
                        }

                        // Case 2: Lives in legacy path
                        McpLog.Info("Detected legacy install path: " + legacyRoot, always: false);

                        // Case 3: Has version but appears older than embedded
                        if (!string.IsNullOrEmpty(embeddedVer) && !string.IsNullOrEmpty(legacyVer) && CompareSemverSafe(legacyVer, embeddedVer) < 0)
                        {
                            McpLog.Info($"Legacy install version {legacyVer} is older than embedded {embeddedVer}", always: false);
                        }
                    }
                }

                // Also log if canonical is missing version (treated as older)
                if (Directory.Exists(canonicalRoot))
                {
                    if (string.IsNullOrEmpty(installedVer))
                    {
                        McpLog.Info("Canonical install missing version file (treat as older). Path: " + canonicalRoot, always: false);
                    }
                    else if (!string.IsNullOrEmpty(embeddedVer) && CompareSemverSafe(installedVer, embeddedVer) < 0)
                    {
                        McpLog.Info($"Canonical install version {installedVer} is older than embedded {embeddedVer}", always: false);
                    }
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn("Detect legacy/version state failed: " + ex.Message);
            }
        }

        private static string NormalizePathSafe(string path)
        {
            try { return string.IsNullOrEmpty(path) ? path : Path.GetFullPath(path.Trim()); }
            catch { return path; }
        }

        private static bool PathsEqualSafe(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            string na = NormalizePathSafe(a);
            string nb = NormalizePathSafe(b);
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
                }
                return string.Equals(na, nb, StringComparison.Ordinal);
            }
            catch { return false; }
        }

        private static IEnumerable<string> GetLegacyRootsForDetection()
        {
            var roots = new System.Collections.Generic.List<string>();
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) ?? string.Empty;
            // macOS/Linux legacy
            roots.Add(Path.Combine(home, ".config", "UnityMCP", "UnityMcpServer"));
            roots.Add(Path.Combine(home, ".local", "share", "UnityMCP", "UnityMcpServer"));
            // Windows roaming example
            try
            {
                string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) ?? string.Empty;
                if (!string.IsNullOrEmpty(roaming))
                    roots.Add(Path.Combine(roaming, "UnityMCP", "UnityMcpServer"));
            }
            catch { }
            return roots;
        }

        private static void TryKillUvForPath(string serverSrcPath)
        {
            try
            {
                if (string.IsNullOrEmpty(serverSrcPath)) return;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "/usr/bin/pgrep",
                    Arguments = $"-f \"uv .*--directory {serverSrcPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var p = System.Diagnostics.Process.Start(psi);
                if (p == null) return;
                string outp = p.StandardOutput.ReadToEnd();
                p.WaitForExit(1500);
                if (p.ExitCode == 0 && !string.IsNullOrEmpty(outp))
                {
                    foreach (var line in outp.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (int.TryParse(line.Trim(), out int pid))
                        {
                            try { System.Diagnostics.Process.GetProcessById(pid).Kill(); } catch { }
                        }
                    }
                }
            }
            catch { }
        }

        private static string ReadVersionFile(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                string v = File.ReadAllText(path).Trim();
                return string.IsNullOrEmpty(v) ? null : v;
            }
            catch { return null; }
        }

        private static int CompareSemverSafe(string a, string b)
        {
            try
            {
                if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0;
                var ap = a.Split('.');
                var bp = b.Split('.');
                for (int i = 0; i < Math.Max(ap.Length, bp.Length); i++)
                {
                    int ai = (i < ap.Length && int.TryParse(ap[i], out var t1)) ? t1 : 0;
                    int bi = (i < bp.Length && int.TryParse(bp[i], out var t2)) ? t2 : 0;
                    if (ai != bi) return ai.CompareTo(bi);
                }
                return 0;
            }
            catch { return 0; }
        }

        /// <summary>
        /// Attempts to locate the embedded UnityMcpServer/src directory inside the installed package
        /// or common development locations.
        /// </summary>
        private static bool TryGetEmbeddedServerSource(out string srcPath)
        {
            return ServerPathResolver.TryFindEmbeddedServerSource(out srcPath);
        }

        private static readonly string[] _skipDirs = { ".venv", "__pycache__", ".pytest_cache", ".mypy_cache", ".git" };
        private static void CopyDirectoryRecursive(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);

            foreach (string filePath in Directory.GetFiles(sourceDir))
            {
                string fileName = Path.GetFileName(filePath);
                string destFile = Path.Combine(destinationDir, fileName);
                File.Copy(filePath, destFile, overwrite: true);
            }

            foreach (string dirPath in Directory.GetDirectories(sourceDir))
            {
                string dirName = Path.GetFileName(dirPath);
                foreach (var skip in _skipDirs)
                {
                    if (dirName.Equals(skip, StringComparison.OrdinalIgnoreCase))
                        goto NextDir;
                }
                try { if ((File.GetAttributes(dirPath) & FileAttributes.ReparsePoint) != 0) continue; } catch { }
                string destSubDir = Path.Combine(destinationDir, dirName);
                CopyDirectoryRecursive(dirPath, destSubDir);
            NextDir: ;
            }
        }

        public static bool RepairPythonEnvironment()
        {
            try
            {
                string serverSrc = GetServerPath();
                bool hasServer = File.Exists(Path.Combine(serverSrc, "server.py"));
                if (!hasServer)
                {
                    // In dev mode or if not installed yet, try the embedded/dev source
                    if (TryGetEmbeddedServerSource(out string embeddedSrc) && File.Exists(Path.Combine(embeddedSrc, "server.py")))
                    {
                        serverSrc = embeddedSrc;
                        hasServer = true;
                    }
                    else
                    {
                        // Attempt to install then retry
                        EnsureServerInstalled();
                        serverSrc = GetServerPath();
                        hasServer = File.Exists(Path.Combine(serverSrc, "server.py"));
                    }
                }

                if (!hasServer)
                {
                    Debug.LogWarning("RepairPythonEnvironment: server.py not found; ensure server is installed first.");
                    return false;
                }

                // Remove stale venv and pinned version file if present
                string venvPath = Path.Combine(serverSrc, ".venv");
                if (Directory.Exists(venvPath))
                {
                    try { Directory.Delete(venvPath, recursive: true); } catch (Exception ex) { Debug.LogWarning($"Failed to delete .venv: {ex.Message}"); }
                }
                string pyPin = Path.Combine(serverSrc, ".python-version");
                if (File.Exists(pyPin))
                {
                    try { File.Delete(pyPin); } catch (Exception ex) { Debug.LogWarning($"Failed to delete .python-version: {ex.Message}"); }
                }

                string uvPath = FindUvPath();
                if (uvPath == null)
                {
                    Debug.LogError("UV not found. Please install uv (https://docs.astral.sh/uv/)." );
                    return false;
                }

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = uvPath,
                    Arguments = "sync",
                    WorkingDirectory = serverSrc,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var proc = new System.Diagnostics.Process { StartInfo = psi };
                var sbOut = new StringBuilder();
                var sbErr = new StringBuilder();
                proc.OutputDataReceived += (_, e) => { if (e.Data != null) sbOut.AppendLine(e.Data); };
                proc.ErrorDataReceived  += (_, e) => { if (e.Data != null) sbErr.AppendLine(e.Data); };

                if (!proc.Start())
                {
                    Debug.LogError("Failed to start uv process.");
                    return false;
                }

                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                if (!proc.WaitForExit(60000))
                {
                    try { proc.Kill(); } catch { }
                    Debug.LogError("uv sync timed out.");
                    return false;
                }

                // Ensure async buffers flushed
                proc.WaitForExit();

                string stdout = sbOut.ToString();
                string stderr = sbErr.ToString();

                if (proc.ExitCode != 0)
                {
                    Debug.LogError($"uv sync failed: {stderr}\n{stdout}");
                    return false;
                }

                Debug.Log("<b><color=#2EA3FF>MCP-FOR-UNITY</color></b>: Python environment repaired successfully.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"RepairPythonEnvironment failed: {ex.Message}");
                return false;
            }
        }

        internal static string FindUvPath()
        {
            // Allow user override via EditorPrefs
            try
            {
                string overridePath = EditorPrefs.GetString("MCPForUnity.UvPath", string.Empty);
                if (!string.IsNullOrEmpty(overridePath) && File.Exists(overridePath))
                {
                    if (ValidateUvBinary(overridePath)) return overridePath;
                }
            }
            catch { }

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) ?? string.Empty;

            // Platform-specific candidate lists
            string[] candidates;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) ?? string.Empty;
                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) ?? string.Empty;
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) ?? string.Empty;

                // Fast path: resolve from PATH first
                try
                {
                    var wherePsi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "where",
                        Arguments = "uv.exe",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                    using var wp = System.Diagnostics.Process.Start(wherePsi);
                    string output = wp.StandardOutput.ReadToEnd().Trim();
                    wp.WaitForExit(1500);
                    if (wp.ExitCode == 0 && !string.IsNullOrEmpty(output))
                    {
                        foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            string path = line.Trim();
                            if (File.Exists(path) && ValidateUvBinary(path)) return path;
                        }
                    }
                }
                catch { }

                candidates = new[]
                {
                    // Preferred: WinGet Links shims (stable entrypoints)
                    // Per-user shim (LOCALAPPDATA) → machine-wide shim (Program Files\WinGet\Links)
                    Path.Combine(localAppData, "Microsoft", "WinGet", "Links", "uv.exe"),
                    Path.Combine(programFiles, "WinGet", "Links", "uv.exe"),

                    // Common per-user installs
                    Path.Combine(localAppData, @"Programs\Python\Python313\Scripts\uv.exe"),
                    Path.Combine(localAppData, @"Programs\Python\Python312\Scripts\uv.exe"),
                    Path.Combine(localAppData, @"Programs\Python\Python311\Scripts\uv.exe"),
                    Path.Combine(localAppData, @"Programs\Python\Python310\Scripts\uv.exe"),
                    Path.Combine(appData, @"Python\Python313\Scripts\uv.exe"),
                    Path.Combine(appData, @"Python\Python312\Scripts\uv.exe"),
                    Path.Combine(appData, @"Python\Python311\Scripts\uv.exe"),
                    Path.Combine(appData, @"Python\Python310\Scripts\uv.exe"),

                    // Program Files style installs (if a native installer was used)
                    Path.Combine(programFiles, @"uv\uv.exe"),

                    // Try simple name resolution later via PATH
                    "uv.exe",
                    "uv"
                };
            }
            else
            {
                candidates = new[]
                {
                    "/opt/homebrew/bin/uv",
                    "/usr/local/bin/uv",
                    "/usr/bin/uv",
                    "/opt/local/bin/uv",
                    Path.Combine(home, ".local", "bin", "uv"),
                    "/opt/homebrew/opt/uv/bin/uv",
                    // Framework Python installs
                    "/Library/Frameworks/Python.framework/Versions/3.13/bin/uv",
                    "/Library/Frameworks/Python.framework/Versions/3.12/bin/uv",
                    // Fallback to PATH resolution by name
                    "uv"
                };
            }

            foreach (string c in candidates)
            {
                try
                {
                    if (File.Exists(c) && ValidateUvBinary(c)) return c;
                }
                catch { /* ignore */ }
            }

            // Use platform-appropriate which/where to resolve from PATH (non-Windows handled here; Windows tried earlier)
            try
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var whichPsi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "/usr/bin/which",
                        Arguments = "uv",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                    try
                    {
                        // Prepend common user-local and package manager locations so 'which' can see them in Unity's GUI env
                        string homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) ?? string.Empty;
                        string prepend = string.Join(":", new[]
                        {
                            System.IO.Path.Combine(homeDir, ".local", "bin"),
                            "/opt/homebrew/bin",
                            "/usr/local/bin",
                            "/usr/bin",
                            "/bin"
                        });
                        string currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                        whichPsi.EnvironmentVariables["PATH"] = string.IsNullOrEmpty(currentPath) ? prepend : (prepend + ":" + currentPath);
                    }
                    catch { }
                    using var wp = System.Diagnostics.Process.Start(whichPsi);
                    string output = wp.StandardOutput.ReadToEnd().Trim();
                    wp.WaitForExit(3000);
                    if (wp.ExitCode == 0 && !string.IsNullOrEmpty(output) && File.Exists(output))
                    {
                        if (ValidateUvBinary(output)) return output;
                    }
                }
            }
            catch { }

            // Manual PATH scan
            try
            {
                string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                string[] parts = pathEnv.Split(Path.PathSeparator);
                foreach (string part in parts)
                {
                    try
                    {
                        // Check both uv and uv.exe
                        string candidateUv = Path.Combine(part, "uv");
                        string candidateUvExe = Path.Combine(part, "uv.exe");
                        if (File.Exists(candidateUv) && ValidateUvBinary(candidateUv)) return candidateUv;
                        if (File.Exists(candidateUvExe) && ValidateUvBinary(candidateUvExe)) return candidateUvExe;
                    }
                    catch { }
                }
            }
            catch { }

            return null;
        }

        private static bool ValidateUvBinary(string uvPath)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = uvPath,
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var p = System.Diagnostics.Process.Start(psi);
                if (!p.WaitForExit(5000)) { try { p.Kill(); } catch { } return false; }
                if (p.ExitCode == 0)
                {
                    string output = p.StandardOutput.ReadToEnd().Trim();
                    return output.StartsWith("uv ");
                }
            }
            catch { }
            return false;
        }
    }
}
