using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
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
                    // Asset Store install - no embedded server
                    // Check if server was already downloaded
                    if (File.Exists(Path.Combine(destSrc, "server.py")))
                    {
                        McpLog.Info("Using previously downloaded MCP server.", always: false);
                    }
                    else
                    {
                        McpLog.Info("MCP server not found. Download via Window > MCP For Unity > Open MCP Window.", always: false);
                    }
                    return; // Graceful exit - no exception
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
                            if (DeleteDirectoryWithRetry(legacyRoot))
                            {
                                McpLog.Info($"Removed legacy server at '{legacyRoot}'.");
                            }
                            else
                            {
                                McpLog.Warn($"Failed to remove legacy server at '{legacyRoot}' (files may be in use)");
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
            throw new Exception("Unsupported operating system");
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
                var psi = new ProcessStartInfo
                {
                    FileName = "/bin/ln",
                    Arguments = $"-s \"{canonical}\" \"{symlink}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
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
            var roots = new List<string>();
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
                // Windows legacy: early installers/dev scripts used %LOCALAPPDATA%\Programs\UnityMCP\UnityMcpServer
                // Detect this location so we can clean up older copies during install/update.
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) ?? string.Empty;
                if (!string.IsNullOrEmpty(localAppData))
                    roots.Add(Path.Combine(localAppData, "Programs", "UnityMCP", "UnityMcpServer"));
            }
            catch { }
            return roots;
        }

        /// <summary>
        /// Attempts to kill UV and Python processes associated with a specific server path.
        /// This is necessary on Windows because the OS blocks file deletion when processes
        /// have open file handles, unlike macOS/Linux which allow unlinking open files.
        /// </summary>
        private static void TryKillUvForPath(string serverSrcPath)
        {
            try
            {
                if (string.IsNullOrEmpty(serverSrcPath)) return;

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    KillWindowsUvProcesses(serverSrcPath);
                    return;
                }

                // Unix: use pgrep to find processes by command line
                var psi = new ProcessStartInfo
                {
                    FileName = "/usr/bin/pgrep",
                    Arguments = $"-f \"uv .*--directory {serverSrcPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p == null) return;
                string outp = p.StandardOutput.ReadToEnd();
                p.WaitForExit(1500);
                if (p.ExitCode == 0 && !string.IsNullOrEmpty(outp))
                {
                    foreach (var line in outp.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (int.TryParse(line.Trim(), out int pid))
                        {
                            try { Process.GetProcessById(pid).Kill(); } catch { }
                        }
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Kills Windows processes running from the virtual environment directory.
        /// Uses WMIC (Windows Management Instrumentation) to safely query only processes
        /// with executables in the .venv path, avoiding the need to iterate all system processes.
        /// This prevents accidentally killing IDE processes or other critical system processes.
        /// 
        /// Why this is needed on Windows:
        /// - Windows blocks file/directory deletion when ANY process has an open file handle
        /// - UV creates a virtual environment with python.exe and other executables
        /// - These processes may hold locks on DLLs, .pyd files, or the executables themselves
        /// - macOS/Linux allow deletion of open files (unlink), but Windows does not
        /// </summary>
        private static void KillWindowsUvProcesses(string serverSrcPath)
        {
            try
            {
                if (string.IsNullOrEmpty(serverSrcPath)) return;

                string venvPath = Path.Combine(serverSrcPath, ".venv");
                if (!Directory.Exists(venvPath)) return;

                string normalizedVenvPath = Path.GetFullPath(venvPath).ToLowerInvariant();

                // Use WMIC to find processes with executables in the .venv directory
                // This is much safer than iterating all processes
                var psi = new ProcessStartInfo
                {
                    FileName = "wmic",
                    Arguments = $"process where \"ExecutablePath like '%{normalizedVenvPath.Replace("\\", "\\\\")}%'\" get ProcessId",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return;

                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(5000);

                if (proc.ExitCode != 0) return;

                // Parse PIDs from WMIC output
                var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    string trimmed = line.Trim();
                    if (trimmed.Equals("ProcessId", StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.IsNullOrWhiteSpace(trimmed)) continue;

                    if (int.TryParse(trimmed, out int pid))
                    {
                        try
                        {
                            using var p = Process.GetProcessById(pid);
                            // Double-check it's not a critical process
                            string name = p.ProcessName.ToLowerInvariant();
                            if (name == "unity" || name == "code" || name == "devenv" || name == "rider64")
                            {
                                continue; // Skip IDE processes
                            }
                            p.Kill();
                            p.WaitForExit(2000);
                        }
                        catch { }
                    }
                }

                // Give processes time to fully exit
                System.Threading.Thread.Sleep(500);
            }
            catch { }
        }

        /// <summary>
        /// Attempts to delete a directory with retry logic to handle Windows file locking issues.
        /// 
        /// Why retries are necessary on Windows:
        /// - Even after killing processes, Windows may take time to release file handles
        /// - Antivirus, Windows Defender, or indexing services may temporarily lock files
        /// - File Explorer previews can hold locks on certain file types
        /// - Readonly attributes on files (common in .venv) block deletion
        /// 
        /// This method handles these cases by:
        /// - Retrying deletion after a delay to allow handle release
        /// - Clearing readonly attributes that block deletion
        /// - Distinguishing between temporary locks (retry) and permanent failures
        /// </summary>
        private static bool DeleteDirectoryWithRetry(string path, int maxRetries = 3, int delayMs = 500)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    if (!Directory.Exists(path)) return true;
                    
                    Directory.Delete(path, recursive: true);
                    return true;
                }
                catch (UnauthorizedAccessException)
                {
                    if (i < maxRetries - 1)
                    {
                        // Wait for file handles to be released
                        System.Threading.Thread.Sleep(delayMs);
                        
                        // Try to clear readonly attributes
                        try
                        {
                            foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                            {
                                try
                                {
                                    var attrs = File.GetAttributes(file);
                                    if ((attrs & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                                    {
                                        File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
                                    }
                                }
                                catch { }
                            }
                        }
                        catch { }
                    }
                }
                catch (IOException)
                {
                    if (i < maxRetries - 1)
                    {
                        // File in use, wait and retry
                        System.Threading.Thread.Sleep(delayMs);
                    }
                }
                catch
                {
                    return false;
                }
            }
            return false;
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
            NextDir:;
            }
        }

        public static bool RebuildMcpServer()
        {
            try
            {
                // Find embedded source
                if (!TryGetEmbeddedServerSource(out string embeddedSrc))
                {
                    McpLog.Error("RebuildMcpServer: Could not find embedded server source.");
                    return false;
                }

                string saveLocation = GetSaveLocation();
                string destRoot = Path.Combine(saveLocation, ServerFolder);
                string destSrc = Path.Combine(destRoot, "src");

                // Kill any running uv processes for this server
                TryKillUvForPath(destSrc);

                // Delete the entire installed server directory
                if (Directory.Exists(destRoot))
                {
                    if (!DeleteDirectoryWithRetry(destRoot, maxRetries: 5, delayMs: 1000))
                    {
                        McpLog.Error($"Failed to delete existing server at {destRoot}. Please close any applications using the Python virtual environment and try again.");
                        return false;
                    }
                    McpLog.Info($"Deleted existing server at {destRoot}");
                }

                // Re-copy from embedded source
                string embeddedRoot = Path.GetDirectoryName(embeddedSrc) ?? embeddedSrc;
                Directory.CreateDirectory(destRoot);
                CopyDirectoryRecursive(embeddedRoot, destRoot);

                // Write version file
                string embeddedVer = ReadVersionFile(Path.Combine(embeddedSrc, VersionFileName)) ?? "unknown";
                try
                {
                    File.WriteAllText(Path.Combine(destSrc, VersionFileName), embeddedVer);
                }
                catch (Exception ex)
                {
                    McpLog.Warn($"Failed to write version file: {ex.Message}");
                }

                McpLog.Info($"Server rebuilt successfully at {destRoot} (version {embeddedVer})");

                // Clear any previous installation error

                PackageLifecycleManager.ClearLastInstallError();


                return true;
            }
            catch (Exception ex)
            {
                McpLog.Error($"RebuildMcpServer failed: {ex.Message}");
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
                    var wherePsi = new ProcessStartInfo
                    {
                        FileName = "where",
                        Arguments = "uv.exe",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                    using var wp = Process.Start(wherePsi);
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

                // Windows Store (PythonSoftwareFoundation) install location probe
                // Example: %LOCALAPPDATA%\Packages\PythonSoftwareFoundation.Python.3.13_*\LocalCache\local-packages\Python313\Scripts\uv.exe
                try
                {
                    string pkgsRoot = Path.Combine(localAppData, "Packages");
                    if (Directory.Exists(pkgsRoot))
                    {
                        var pythonPkgs = Directory.GetDirectories(pkgsRoot, "PythonSoftwareFoundation.Python.*", SearchOption.TopDirectoryOnly)
                                                 .OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase);
                        foreach (var pkg in pythonPkgs)
                        {
                            string localCache = Path.Combine(pkg, "LocalCache", "local-packages");
                            if (!Directory.Exists(localCache)) continue;
                            var pyRoots = Directory.GetDirectories(localCache, "Python*", SearchOption.TopDirectoryOnly)
                                                   .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase);
                            foreach (var pyRoot in pyRoots)
                            {
                                string uvExe = Path.Combine(pyRoot, "Scripts", "uv.exe");
                                if (File.Exists(uvExe) && ValidateUvBinary(uvExe)) return uvExe;
                            }
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
                    var whichPsi = new ProcessStartInfo
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
                            Path.Combine(homeDir, ".local", "bin"),
                            "/opt/homebrew/bin",
                            "/usr/local/bin",
                            "/usr/bin",
                            "/bin"
                        });
                        string currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                        whichPsi.EnvironmentVariables["PATH"] = string.IsNullOrEmpty(currentPath) ? prepend : (prepend + ":" + currentPath);
                    }
                    catch { }
                    using var wp = Process.Start(whichPsi);
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
                var psi = new ProcessStartInfo
                {
                    FileName = uvPath,
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
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

        /// <summary>
        /// Download and install server from GitHub release (Asset Store workflow)
        /// </summary>
        public static bool DownloadAndInstallServer()
        {
            string packageVersion = AssetPathUtility.GetPackageVersion();
            if (packageVersion == "unknown")
            {
                McpLog.Error("Cannot determine package version for download.");
                return false;
            }

            string downloadUrl = $"https://github.com/CoplayDev/unity-mcp/releases/download/v{packageVersion}/mcp-for-unity-server-v{packageVersion}.zip";
            string tempZip = Path.Combine(Path.GetTempPath(), $"mcp-server-v{packageVersion}.zip");
            string destRoot = Path.Combine(GetSaveLocation(), ServerFolder);

            try
            {
                EditorUtility.DisplayProgressBar("MCP for Unity", "Downloading server...", 0.3f);

                // Download
                using (var client = new WebClient())
                {
                    client.DownloadFile(downloadUrl, tempZip);
                }

                EditorUtility.DisplayProgressBar("MCP for Unity", "Extracting server...", 0.7f);

                // Kill any running UV processes
                string destSrc = Path.Combine(destRoot, "src");
                TryKillUvForPath(destSrc);

                // Delete old installation
                if (Directory.Exists(destRoot))
                {
                    if (!DeleteDirectoryWithRetry(destRoot, maxRetries: 5, delayMs: 1000))
                    {
                        McpLog.Warn($"Could not fully delete old server (files may be in use)");
                    }
                }

                // Extract to temp location first
                string tempExtractDir = Path.Combine(Path.GetTempPath(), $"mcp-server-extract-{Guid.NewGuid()}");
                Directory.CreateDirectory(tempExtractDir);

                try
                {
                    ZipFile.ExtractToDirectory(tempZip, tempExtractDir);

                    // The ZIP contains UnityMcpServer~ folder, find it and move its contents
                    string extractedServerFolder = Path.Combine(tempExtractDir, "UnityMcpServer~");
                    Directory.CreateDirectory(destRoot);
                    CopyDirectoryRecursive(extractedServerFolder, destRoot);
                }
                finally
                {
                    // Cleanup temp extraction directory
                    try
                    {
                        if (Directory.Exists(tempExtractDir))
                        {
                            Directory.Delete(tempExtractDir, recursive: true);
                        }
                    }
                    catch (Exception ex)
                    {
                        McpLog.Warn($"Could not fully delete temp extraction directory: {ex.Message}");
                    }
                }

                EditorUtility.ClearProgressBar();
                McpLog.Info($"Server v{packageVersion} downloaded and installed successfully!");
                return true;
            }
            catch (Exception ex)
            {
                EditorUtility.ClearProgressBar();
                McpLog.Error($"Failed to download server: {ex.Message}");
                EditorUtility.DisplayDialog(
                    "Download Failed",
                    $"Could not download server from GitHub.\n\n{ex.Message}\n\nPlease check your internet connection or try again later.",
                    "OK"
                );
                return false;
            }
            finally
            {
                try
                {
                    if (File.Exists(tempZip)) File.Delete(tempZip);
                }
                catch (Exception ex)
                {
                    McpLog.Warn($"Could not delete temp zip file: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Check if the package has an embedded server (Git install vs Asset Store)
        /// </summary>
        public static bool HasEmbeddedServer()
        {
            return TryGetEmbeddedServerSource(out _);
        }

        /// <summary>
        /// Get the installed server version from the local installation
        /// </summary>
        public static string GetInstalledServerVersion()
        {
            try
            {
                string destRoot = Path.Combine(GetSaveLocation(), ServerFolder);
                string versionPath = Path.Combine(destRoot, "src", VersionFileName);
                if (File.Exists(versionPath))
                {
                    return File.ReadAllText(versionPath)?.Trim() ?? string.Empty;
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Could not read version file: {ex.Message}");
            }
            return string.Empty;
        }
    }
}
