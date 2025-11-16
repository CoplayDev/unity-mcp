namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Service for checking package updates and version information
    /// </summary>
    public interface IPackageUpdateService
    {
        /// <summary>
        /// Checks if a newer version of the package is available
        /// </summary>
        /// <param name="currentVersion">The current package version</param>
        /// <returns>Update check result containing availability and latest version info</returns>
        UpdateCheckResult CheckForUpdate(string currentVersion);

        /// <summary>
        /// Compares two version strings to determine if the first is newer than the second
        /// </summary>
        /// <param name="version1">First version string</param>
        /// <param name="version2">Second version string</param>
        /// <returns>True if version1 is newer than version2</returns>
        bool IsNewerVersion(string version1, string version2);

        /// <summary>
        /// Determines if the package was installed via Git or Asset Store
        /// </summary>
        /// <returns>True if installed via Git, false if Asset Store or unknown</returns>
        bool IsGitInstallation();

        /// <summary>
        /// Clears the cached update check data, forcing a fresh check on next request
        /// </summary>
        void ClearCache();
    }

    /// <summary>
    /// Result of an update check operation
    /// </summary>
    public class UpdateCheckResult
    {
        /// <summary>
        /// Whether an update is available
        /// </summary>
        public bool UpdateAvailable { get; set; }

        /// <summary>
        /// The latest version available (null if check failed or no update)
        /// </summary>
        public string LatestVersion { get; set; }

        /// <summary>
        /// Whether the check was successful (false if network error, etc.)
        /// </summary>
        public bool CheckSucceeded { get; set; }

        /// <summary>
        /// Optional message about the check result
        /// </summary>
        public string Message { get; set; }
    }
}
