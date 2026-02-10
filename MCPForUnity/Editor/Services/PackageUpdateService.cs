using System;
using System.Net;
using System.Text.RegularExpressions;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Service for checking package updates from GitHub or Asset Store metadata
    /// </summary>
    public class PackageUpdateService : IPackageUpdateService
    {
        private const string LastCheckDateKey = EditorPrefKeys.LastUpdateCheck;
        private const string CachedVersionKey = EditorPrefKeys.LatestKnownVersion;
        private const string LastBetaCheckDateKey = EditorPrefKeys.LastUpdateCheck + ".beta";
        private const string CachedBetaVersionKey = EditorPrefKeys.LatestKnownVersion + ".beta";
        private const string LastAssetStoreCheckDateKey = EditorPrefKeys.LastAssetStoreUpdateCheck;
        private const string CachedAssetStoreVersionKey = EditorPrefKeys.LatestKnownAssetStoreVersion;
        private const string MainPackageJsonUrl = "https://raw.githubusercontent.com/CoplayDev/unity-mcp/main/MCPForUnity/package.json";
        private const string BetaPackageJsonUrl = "https://raw.githubusercontent.com/CoplayDev/unity-mcp/beta/MCPForUnity/package.json";
        private const string AssetStoreVersionUrl = "https://gqoqjkkptwfbkwyssmnj.supabase.co/storage/v1/object/public/coplay-images/assetstoreversion.json";

        /// <inheritdoc/>
        public UpdateCheckResult CheckForUpdate(string currentVersion)
        {
            bool isGitInstallation = IsGitInstallation();
            string gitBranch = isGitInstallation ? GetGitUpdateBranch(currentVersion) : "main";
            bool useBetaChannel = isGitInstallation && string.Equals(gitBranch, "beta", StringComparison.OrdinalIgnoreCase);

            string lastCheckKey = isGitInstallation
                ? (useBetaChannel ? LastBetaCheckDateKey : LastCheckDateKey)
                : LastAssetStoreCheckDateKey;
            string cachedVersionKey = isGitInstallation
                ? (useBetaChannel ? CachedBetaVersionKey : CachedVersionKey)
                : CachedAssetStoreVersionKey;

            string lastCheckDate = EditorPrefs.GetString(lastCheckKey, "");
            string cachedLatestVersion = EditorPrefs.GetString(cachedVersionKey, "");

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

            string latestVersion = isGitInstallation
                ? FetchLatestVersionFromGitHub(gitBranch)
                : FetchLatestVersionFromAssetStoreJson();

            if (!string.IsNullOrEmpty(latestVersion))
            {
                // Cache the result
                EditorPrefs.SetString(lastCheckKey, DateTime.Now.ToString("yyyy-MM-dd"));
                EditorPrefs.SetString(cachedVersionKey, latestVersion);

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
                Message = isGitInstallation
                    ? "Failed to check for updates (network issue or offline)"
                    : "Failed to check for Asset Store updates (network issue or offline)"
            };
        }

        /// <inheritdoc/>
        public bool IsNewerVersion(string version1, string version2)
        {
            if (!TryParseVersion(version1, out var left) || !TryParseVersion(version2, out var right))
            {
                return false;
            }

            return CompareVersions(left, right) > 0;
        }

        private static int CompareVersions(ParsedVersion left, ParsedVersion right)
        {
            int cmp = left.Major.CompareTo(right.Major);
            if (cmp != 0) return cmp;

            cmp = left.Minor.CompareTo(right.Minor);
            if (cmp != 0) return cmp;

            cmp = left.Patch.CompareTo(right.Patch);
            if (cmp != 0) return cmp;

            // Stable is newer than prerelease when core version matches.
            if (!left.IsPrerelease && right.IsPrerelease) return 1;
            if (left.IsPrerelease && !right.IsPrerelease) return -1;
            if (!left.IsPrerelease && !right.IsPrerelease) return 0;

            cmp = GetPrereleaseRank(left.PrereleaseLabel).CompareTo(GetPrereleaseRank(right.PrereleaseLabel));
            if (cmp != 0) return cmp;

            cmp = left.PrereleaseNumber.CompareTo(right.PrereleaseNumber);
            if (cmp != 0) return cmp;

            return string.Compare(left.PrereleaseLabel, right.PrereleaseLabel, StringComparison.OrdinalIgnoreCase);
        }

        private static int GetPrereleaseRank(string label)
        {
            if (string.IsNullOrEmpty(label))
            {
                return 0;
            }

            switch (label.ToLowerInvariant())
            {
                case "a":
                case "alpha":
                    return 1;
                case "b":
                case "beta":
                    return 2;
                case "rc":
                    return 3;
                case "preview":
                case "pre":
                    return 4;
                default:
                    return 5;
            }
        }

        private static bool TryParseVersion(string version, out ParsedVersion parsed)
        {
            parsed = default;
            if (string.IsNullOrWhiteSpace(version))
            {
                return false;
            }

            string normalized = version.Trim().TrimStart('v', 'V');
            var match = Regex.Match(
                normalized,
                @"^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-(?<label>[A-Za-z]+)(?:\.(?<number>\d+))?)?$");

            if (!match.Success)
            {
                return false;
            }

            if (!int.TryParse(match.Groups["major"].Value, out int major) ||
                !int.TryParse(match.Groups["minor"].Value, out int minor) ||
                !int.TryParse(match.Groups["patch"].Value, out int patch))
            {
                return false;
            }

            string prereleaseLabel = match.Groups["label"].Success ? match.Groups["label"].Value : string.Empty;
            int prereleaseNumber = 0;
            if (match.Groups["number"].Success)
            {
                int.TryParse(match.Groups["number"].Value, out prereleaseNumber);
            }

            parsed = new ParsedVersion
            {
                Major = major,
                Minor = minor,
                Patch = patch,
                PrereleaseLabel = prereleaseLabel,
                PrereleaseNumber = prereleaseNumber,
                IsPrerelease = !string.IsNullOrEmpty(prereleaseLabel)
            };
            return true;
        }

        private static bool IsPreReleaseVersion(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                return AssetPathUtility.IsPreReleaseVersion();
            }

            return version.IndexOf('-', StringComparison.Ordinal) >= 0;
        }

        private static string GetGitUpdateBranch(string currentVersion)
        {
            try
            {
                var packageInfo = PackageInfo.FindForAssembly(typeof(PackageUpdateService).Assembly);
                string packageId = packageInfo?.packageId ?? string.Empty;

                if (packageId.IndexOf("#beta", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "beta";
                }

                if (packageId.IndexOf("#main", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "main";
                }
            }
            catch
            {
                // Fall back to version-based inference below.
            }

            return IsPreReleaseVersion(currentVersion) ? "beta" : "main";
        }

        /// <inheritdoc/>
        public virtual bool IsGitInstallation()
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
            EditorPrefs.DeleteKey(LastBetaCheckDateKey);
            EditorPrefs.DeleteKey(CachedBetaVersionKey);
            EditorPrefs.DeleteKey(LastAssetStoreCheckDateKey);
            EditorPrefs.DeleteKey(CachedAssetStoreVersionKey);
        }

        /// <summary>
        /// Fetches the latest version from GitHub package.json for the requested branch.
        /// </summary>
        protected virtual string FetchLatestVersionFromGitHub(string branch)
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
                    string packageJsonUrl = string.Equals(branch, "beta", StringComparison.OrdinalIgnoreCase)
                        ? BetaPackageJsonUrl
                        : MainPackageJsonUrl;
                    string jsonContent = client.DownloadString(packageJsonUrl);

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

        private struct ParsedVersion
        {
            public int Major;
            public int Minor;
            public int Patch;
            public string PrereleaseLabel;
            public int PrereleaseNumber;
            public bool IsPrerelease;
        }

        /// <summary>
        /// Fetches the latest Asset Store version from a hosted JSON file.
        /// </summary>
        protected virtual string FetchLatestVersionFromAssetStoreJson()
        {
            try
            {
                using (var client = new WebClient())
                {
                    client.Headers.Add("User-Agent", "Unity-MCPForUnity-AssetStoreUpdateChecker");
                    string jsonContent = client.DownloadString(AssetStoreVersionUrl);

                    var versionJson = JObject.Parse(jsonContent);
                    string version = versionJson["version"]?.ToString();

                    return string.IsNullOrEmpty(version) ? null : version;
                }
            }
            catch (Exception ex)
            {
                // Silent fail - don't interrupt the user if network is unavailable
                McpLog.Info($"Asset Store update check failed (this is normal if offline): {ex.Message}");
                return null;
            }
        }
    }
}
