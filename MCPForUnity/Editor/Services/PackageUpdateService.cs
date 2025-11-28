using System;
using System.Net;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Service for checking package updates from GitHub
    /// </summary>
    public class PackageUpdateService : IPackageUpdateService
    {
        private const string LastCheckDateKey = EditorPrefKeys.LastUpdateCheck;
        private const string CachedVersionKey = EditorPrefKeys.LatestKnownVersion;
        private const string PackageJsonUrl = "https://raw.githubusercontent.com/CoplayDev/unity-mcp/main/MCPForUnity/package.json";

        /// <inheritdoc/>
        public UpdateCheckResult CheckForUpdate(string currentVersion)
        {
            // Check cache first - only check once per day
            string lastCheckDate = EditorPrefs.GetString(LastCheckDateKey, "");
            string cachedLatestVersion = EditorPrefs.GetString(CachedVersionKey, "");

            if (lastCheckDate == DateTime.Now.ToString("yyyy-MM-dd") && !string.IsNullOrEmpty(cachedLatestVersion))
            {
                return new UpdateCheckResult
                {
                    CheckSucceeded = true,
                    LatestVersion = cachedLatestVersion,
                    UpdateAvailable = IsNewerVersion(cachedLatestVersion, currentVersion),
                    Message = "Using cached version check"
                };
            }

            // Don't check for Asset Store installations
            if (!IsGitInstallation())
            {
                return new UpdateCheckResult
                {
                    CheckSucceeded = false,
                    UpdateAvailable = false,
                    Message = "Asset Store installations are updated via Unity Asset Store"
                };
            }

            // Fetch latest version from GitHub
            string latestVersion = FetchLatestVersionFromGitHub();

            if (!string.IsNullOrEmpty(latestVersion))
            {
                // Cache the result
                EditorPrefs.SetString(LastCheckDateKey, DateTime.Now.ToString("yyyy-MM-dd"));
                EditorPrefs.SetString(CachedVersionKey, latestVersion);

                return new UpdateCheckResult
                {
                    CheckSucceeded = true,
                    LatestVersion = latestVersion,
                    UpdateAvailable = IsNewerVersion(latestVersion, currentVersion),
                    Message = "Successfully checked for updates"
                };
            }

            return new UpdateCheckResult
            {
                CheckSucceeded = false,
                UpdateAvailable = false,
                Message = "Failed to check for updates (network issue or offline)"
            };
        }

        /// <inheritdoc/>
        public bool IsNewerVersion(string version1, string version2)
        {
            try
            {
                // Remove any "v" prefix
                version1 = version1.TrimStart('v', 'V');
                version2 = version2.TrimStart('v', 'V');

                var version1Parts = version1.Split('.');
                var version2Parts = version2.Split('.');

                for (int i = 0; i < Math.Min(version1Parts.Length, version2Parts.Length); i++)
                {
                    if (int.TryParse(version1Parts[i], out int v1Num) &&
                        int.TryParse(version2Parts[i], out int v2Num))
                    {
                        if (v1Num > v2Num) return true;
                        if (v1Num < v2Num) return false;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <inheritdoc/>
        public bool IsGitInstallation()
        {
            // Git packages are installed via Package Manager and have a package.json in Packages/
            // Asset Store packages are in Assets/
            string packageRoot = AssetPathUtility.GetMcpPackageRootPath();

            if (string.IsNullOrEmpty(packageRoot))
            {
                return false;
            }

            // If the package is in Packages/ it's a PM install (likely Git)
            // If it's in Assets/ it's an Asset Store install
            return packageRoot.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc/>
        public void ClearCache()
        {
            EditorPrefs.DeleteKey(LastCheckDateKey);
            EditorPrefs.DeleteKey(CachedVersionKey);
        }

        /// <summary>
        /// Fetches the latest version from GitHub's main branch package.json
        /// </summary>
        private string FetchLatestVersionFromGitHub()
        {
            try
            {
                // GitHub API endpoint (Option 1 - has rate limits):
                // https://api.github.com/repos/CoplayDev/unity-mcp/releases/latest
                //
                // We use Option 2 (package.json directly) because:
                // - No API rate limits (GitHub serves raw files freely)
                // - Simpler - just parse JSON for version field
                // - More reliable - doesn't require releases to be published
                // - Direct source of truth from the main branch

                using (var client = new WebClient())
                {
                    client.Headers.Add("User-Agent", "Unity-MCPForUnity-UpdateChecker");
                    string jsonContent = client.DownloadString(PackageJsonUrl);

                    var packageJson = JObject.Parse(jsonContent);
                    string version = packageJson["version"]?.ToString();

                    return string.IsNullOrEmpty(version) ? null : version;
                }
            }
            catch (Exception ex)
            {
                // Silent fail - don't interrupt the user if network is unavailable
                McpLog.Info($"Update check failed (this is normal if offline): {ex.Message}");
                return null;
            }
        }
    }
}
