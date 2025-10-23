using System.IO;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Manages package lifecycle events including first-time installation,
    /// version updates, and legacy installation detection.
    /// Consolidates the functionality of PackageInstaller and PackageDetector.
    /// </summary>
    [InitializeOnLoad]
    public static class PackageLifecycleManager
    {
        private const string VersionKeyPrefix = "MCPForUnity.InstalledVersion:";
        private const string LegacyInstallFlagKey = "MCPForUnity.ServerInstalled"; // For migration
        private const string InstallErrorKeyPrefix = "MCPForUnity.InstallError:"; // Stores last installation error

        static PackageLifecycleManager()
        {
            // Schedule the check for after Unity is fully loaded
            EditorApplication.delayCall += CheckAndInstallServer;
        }

        private static void CheckAndInstallServer()
        {
            try
            {
                string currentVersion = GetPackageVersion();
                string versionKey = VersionKeyPrefix + currentVersion;
                bool hasRunForThisVersion = EditorPrefs.GetBool(versionKey, false);

                // Check for conditions that require installation/verification
                bool isFirstTimeInstall = !EditorPrefs.HasKey(LegacyInstallFlagKey) && !hasRunForThisVersion;
                bool legacyPresent = LegacyRootsExist();
                bool canonicalMissing = !File.Exists(
                    Path.Combine(ServerInstaller.GetServerPath(), "server.py")
                );

                // Run if: first install, version update, legacy detected, or canonical missing
                if (isFirstTimeInstall || !hasRunForThisVersion || legacyPresent || canonicalMissing)
                {
                    PerformInstallation(currentVersion, versionKey, isFirstTimeInstall);
                }
            }
            catch (System.Exception ex)
            {
                McpLog.Info($"Package lifecycle check failed: {ex.Message}. Open Window > MCP For Unity if needed.", always: false);
            }
        }

        private static void PerformInstallation(string version, string versionKey, bool isFirstTimeInstall)
        {
            string error = null;

            try
            {
                ServerInstaller.EnsureServerInstalled();

                // Mark as installed for this version
                EditorPrefs.SetBool(versionKey, true);

                // Migrate legacy flag if this is first time
                if (isFirstTimeInstall)
                {
                    EditorPrefs.SetBool(LegacyInstallFlagKey, true);
                }

                // Clean up old version keys (keep only current version)
                CleanupOldVersionKeys(version);

                // Clean up legacy preference keys
                CleanupLegacyPrefs();

                // Only log success if server was actually embedded and copied
                if (ServerInstaller.HasEmbeddedServer() && isFirstTimeInstall)
                {
                    McpLog.Info("MCP server installation completed successfully.");
                }
            }
            catch (System.Exception ex)
            {
                error = ex.Message;

                // Store the error for display in the UI, but don't mark as handled
                // This allows the user to manually rebuild via the "Rebuild Server" button
                string errorKey = InstallErrorKeyPrefix + version;
                EditorPrefs.SetString(errorKey, ex.Message ?? "Unknown error");
                
                // Don't mark as installed - user needs to manually rebuild
            }

            if (!string.IsNullOrEmpty(error))
            {
                McpLog.Info($"Server installation failed: {error}. Use Window > MCP For Unity > Rebuild Server to retry.", always: false);
            }
        }

        private static string GetPackageVersion()
        {
            try
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                    typeof(PackageLifecycleManager).Assembly
                );
                if (info != null && !string.IsNullOrEmpty(info.version))
                {
                    return info.version;
                }
            }
            catch { }

            // Fallback to embedded server version
            return GetEmbeddedServerVersion();
        }

        private static string GetEmbeddedServerVersion()
        {
            try
            {
                if (ServerPathResolver.TryFindEmbeddedServerSource(out var embeddedSrc))
                {
                    var versionPath = Path.Combine(embeddedSrc, "server_version.txt");
                    if (File.Exists(versionPath))
                    {
                        return File.ReadAllText(versionPath)?.Trim() ?? "unknown";
                    }
                }
            }
            catch { }
            return "unknown";
        }

        private static bool LegacyRootsExist()
        {
            try
            {
                string home = System.Environment.GetFolderPath(
                    System.Environment.SpecialFolder.UserProfile
                ) ?? string.Empty;

                string[] legacyRoots =
                {
                    Path.Combine(home, ".config", "UnityMCP", "UnityMcpServer", "src"),
                    Path.Combine(home, ".local", "share", "UnityMCP", "UnityMcpServer", "src")
                };

                foreach (var root in legacyRoots)
                {
                    try
                    {
                        if (File.Exists(Path.Combine(root, "server.py")))
                        {
                            return true;
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return false;
        }

        private static void CleanupOldVersionKeys(string currentVersion)
        {
            try
            {
                // Get all EditorPrefs keys that start with our version prefix
                // Note: Unity doesn't provide a way to enumerate all keys, so we can only
                // clean up known legacy keys. Future versions will be cleaned up when
                // a newer version runs.
                // This is a best-effort cleanup.
            }
            catch { }
        }

        private static void CleanupLegacyPrefs()
        {
            try
            {
                // Clean up old preference keys that are no longer used
                string[] legacyKeys =
                {
                    "MCPForUnity.ServerSrc",
                    "MCPForUnity.PythonDirOverride",
                    "MCPForUnity.LegacyDetectLogged" // Old prefix without version
                };

                foreach (var key in legacyKeys)
                {
                    try
                    {
                        if (EditorPrefs.HasKey(key))
                        {
                            EditorPrefs.DeleteKey(key);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        /// <summary>
        /// Gets the last installation error for the current package version, if any.
        /// Returns null if there was no error or the error has been cleared.
        /// </summary>
        public static string GetLastInstallError()
        {
            try
            {
                string currentVersion = GetPackageVersion();
                string errorKey = InstallErrorKeyPrefix + currentVersion;
                if (EditorPrefs.HasKey(errorKey))
                {
                    return EditorPrefs.GetString(errorKey, null);
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Clears the last installation error. Should be called after a successful manual rebuild.
        /// </summary>
        public static void ClearLastInstallError()
        {
            try
            {
                string currentVersion = GetPackageVersion();
                string errorKey = InstallErrorKeyPrefix + currentVersion;
                if (EditorPrefs.HasKey(errorKey))
                {
                    EditorPrefs.DeleteKey(errorKey);
                }
            }
            catch { }
        }
    }
}
