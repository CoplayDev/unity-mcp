using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Auto-runs legacy/older install detection on package load/update (log-only).
    /// Runs once per embedded server version using an EditorPrefs version-scoped key.
    /// </summary>
    [InitializeOnLoad]
    public static class PackageDetector
    {
        private const string DetectOnceFlagKeyPrefix = "MCPForUnity.LegacyDetectLogged:";

        static PackageDetector()
        {
            try
            {
                string pkgVer = ReadPackageVersionOrFallback();
                string key = DetectOnceFlagKeyPrefix + pkgVer;

                // Always force-run if legacy roots exist or canonical install is missing
                bool legacyPresent = LegacyRootsExist();
                bool canonicalMissing = !System.IO.File.Exists(System.IO.Path.Combine(ServerInstaller.GetServerPath(), "server.py"));

                // Check if any MCPForUnityTools have updated versions
                bool toolsNeedUpdate = ToolsVersionsChanged();

                if (!EditorPrefs.GetBool(key, false) || legacyPresent || canonicalMissing || toolsNeedUpdate)
                {
                    // Marshal the entire flow to the main thread. EnsureServerInstalled may touch Unity APIs.
                    EditorApplication.delayCall += () =>
                    {
                        string error = null;
                        System.Exception capturedEx = null;
                        try
                        {
                            // Ensure any UnityEditor API usage inside runs on the main thread
                            ServerInstaller.EnsureServerInstalled();
                        }
                        catch (System.Exception ex)
                        {
                            error = ex.Message;
                            capturedEx = ex;
                        }

                        // Unity APIs must stay on main thread
                        try { EditorPrefs.SetBool(key, true); } catch { }
                        // Ensure prefs cleanup happens on main thread
                        try { EditorPrefs.DeleteKey("MCPForUnity.ServerSrc"); } catch { }
                        try { EditorPrefs.DeleteKey("MCPForUnity.PythonDirOverride"); } catch { }

                        if (!string.IsNullOrEmpty(error))
                        {
                            Debug.LogWarning($"MCP for Unity: Auto-detect on load failed: {capturedEx}");
                            // Alternatively: Debug.LogException(capturedEx);
                        }
                    };
                }
            }
            catch { /* ignore */ }
        }

        private static string ReadEmbeddedVersionOrFallback()
        {
            try
            {
                if (ServerPathResolver.TryFindEmbeddedServerSource(out var embeddedSrc))
                {
                    var p = System.IO.Path.Combine(embeddedSrc, "server_version.txt");
                    if (System.IO.File.Exists(p))
                        return (System.IO.File.ReadAllText(p)?.Trim() ?? "unknown");
                }
            }
            catch { }
            return "unknown";
        }

        private static string ReadPackageVersionOrFallback()
        {
            try
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(PackageDetector).Assembly);
                if (info != null && !string.IsNullOrEmpty(info.version)) return info.version;
            }
            catch { }
            // Fallback to embedded server version if package info unavailable
            return ReadEmbeddedVersionOrFallback();
        }

        private static bool LegacyRootsExist()
        {
            try
            {
                string home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile) ?? string.Empty;
                string[] roots =
                {
                    System.IO.Path.Combine(home, ".config", "UnityMCP", "UnityMcpServer", "src"),
                    System.IO.Path.Combine(home, ".local", "share", "UnityMCP", "UnityMcpServer", "src")
                };
                foreach (var r in roots)
                {
                    try { if (System.IO.File.Exists(System.IO.Path.Combine(r, "server.py"))) return true; } catch { }
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Checks if any MCPForUnityTools folders have version.txt files that differ from installed versions.
        /// Returns true if any tool needs updating.
        /// </summary>
        private static bool ToolsVersionsChanged()
        {
            try
            {
                // Get Unity project root
                string projectRoot = System.IO.Directory.GetParent(UnityEngine.Application.dataPath)?.FullName;
                if (string.IsNullOrEmpty(projectRoot))
                {
                    return false;
                }

                // Get server tools directory
                string serverPath = ServerInstaller.GetServerPath();
                string toolsDir = System.IO.Path.Combine(serverPath, "tools");

                if (!System.IO.Directory.Exists(toolsDir))
                {
                    // Tools directory doesn't exist yet, needs initial setup
                    return true;
                }

                // Find all MCPForUnityTools folders in project
                var toolsFolders = System.IO.Directory.GetDirectories(projectRoot, "MCPForUnityTools", System.IO.SearchOption.AllDirectories);

                foreach (var folder in toolsFolders)
                {
                    // Check if version.txt exists in this folder
                    string versionFile = System.IO.Path.Combine(folder, "version.txt");
                    if (!System.IO.File.Exists(versionFile))
                    {
                        continue; // No version tracking for this folder
                    }

                    // Read source version
                    string sourceVersion = System.IO.File.ReadAllText(versionFile)?.Trim();
                    if (string.IsNullOrEmpty(sourceVersion))
                    {
                        continue;
                    }

                    // Get folder identifier (same logic as ServerInstaller.GetToolsFolderIdentifier)
                    string folderIdentifier = GetToolsFolderIdentifier(folder);
                    string trackingFile = System.IO.Path.Combine(toolsDir, $"{folderIdentifier}_version.txt");

                    // Read installed version
                    string installedVersion = null;
                    if (System.IO.File.Exists(trackingFile))
                    {
                        installedVersion = System.IO.File.ReadAllText(trackingFile)?.Trim();
                    }

                    // Check if versions differ
                    if (string.IsNullOrEmpty(installedVersion) || sourceVersion != installedVersion)
                    {
                        return true; // Version changed, needs update
                    }
                }

                return false; // All versions match
            }
            catch
            {
                // On error, assume update needed to be safe
                return true;
            }
        }

        /// <summary>
        /// Generates a unique identifier for a MCPForUnityTools folder (duplicates ServerInstaller logic).
        /// </summary>
        private static string GetToolsFolderIdentifier(string toolsFolderPath)
        {
            try
            {
                System.IO.DirectoryInfo parent = System.IO.Directory.GetParent(toolsFolderPath);
                if (parent == null) return "MCPForUnityTools";

                System.IO.DirectoryInfo current = parent;
                while (current != null)
                {
                    string name = current.Name;
                    System.IO.DirectoryInfo grandparent = current.Parent;

                    if (grandparent != null &&
                        (grandparent.Name.Equals("Assets", System.StringComparison.OrdinalIgnoreCase) ||
                         grandparent.Name.Equals("Packages", System.StringComparison.OrdinalIgnoreCase)))
                    {
                        return $"{name}_MCPForUnityTools";
                    }

                    current = grandparent;
                }

                return $"{parent.Name}_MCPForUnityTools";
            }
            catch
            {
                return "MCPForUnityTools";
            }
        }
    }
}
