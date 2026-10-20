namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// How the MCP for Unity package was installed. Determines which channel can supply a newer
    /// version, and therefore whether an update notification is meaningful at all.
    /// </summary>
    public enum UpdateInstallSource
    {
        /// <summary>Could not be determined; treat as "no update channel known".</summary>
        Unknown,

        /// <summary>Installed from a Git URL (Packages/manifest.json holds a git dependency).</summary>
        Git,

        /// <summary>Imported from the Asset Store into Assets/.</summary>
        AssetStore,

        /// <summary>Resolved from a package registry (scoped registry such as OpenUPM, or Unity's).</summary>
        Registry,

        /// <summary>Local folder or tarball dependency (file: or a tarball).</summary>
        Local,

        /// <summary>Embedded package living directly in Packages/.</summary>
        Embedded,

        /// <summary>Built-in Unity package.</summary>
        BuiltIn
    }

    /// <summary>
    /// Service for checking package updates and version information
    /// </summary>
    public interface IPackageUpdateService
    {
        /// <summary>
        /// Checks if a newer version of the package is available. Only Git and Asset Store installs
        /// have a channel to poll; for any other source the result is marked
        /// <see cref="UpdateCheckResult.CheckSkipped"/> and never reports an update.
        /// </summary>
        /// <param name="currentVersion">The current package version</param>
        /// <returns>Update check result containing availability and latest version info</returns>
        UpdateCheckResult CheckForUpdate(string currentVersion);

        /// <summary>
        /// Returns a cached update result if one exists for today, or null if a network fetch is needed.
        /// Returns a skipped result (never null) when the install source has no update channel.
        /// Main-thread only (reads EditorPrefs).
        /// </summary>
        UpdateCheckResult TryGetCachedResult(string currentVersion);

        /// <summary>
        /// Performs only the network fetch and version comparison (no EditorPrefs access).
        /// Safe to call from a background thread.
        /// </summary>
        UpdateCheckResult FetchAndCompare(string currentVersion);

        /// <summary>
        /// Performs only the network fetch and version comparison using pre-computed installation info.
        /// Use this overload when calling from a background thread to avoid main-thread-only API calls.
        /// </summary>
        UpdateCheckResult FetchAndCompare(string currentVersion, UpdateInstallSource source, string gitBranch);

        /// <summary>
        /// Caches a successful fetch result in EditorPrefs. Must be called from the main thread.
        /// </summary>
        void CacheFetchResult(string currentVersion, string fetchedVersion);

        /// <summary>
        /// Compares two version strings to determine if the first is newer than the second
        /// </summary>
        /// <param name="version1">First version string</param>
        /// <param name="version2">Second version string</param>
        /// <returns>True if version1 is newer than version2</returns>
        bool IsNewerVersion(string version1, string version2);

        /// <summary>
        /// Determines how the package was installed. Must be called from the main thread
        /// (uses Unity PackageManager APIs).
        /// </summary>
        /// <returns>The resolved install source, or <see cref="UpdateInstallSource.Unknown"/>.</returns>
        UpdateInstallSource GetInstallSource();

        /// <summary>
        /// Determines the Git branch to check for updates (e.g. "main" or "beta").
        /// Must be called from the main thread (uses Unity PackageManager APIs).
        /// </summary>
        string GetGitUpdateBranch(string currentVersion);

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
        /// True when there was nothing to check: the install source (registry, local, embedded) is
        /// versioned by the Package Manager, so no remote channel describes what it can install.
        /// </summary>
        public bool CheckSkipped { get; set; }

        /// <summary>
        /// Optional message about the check result
        /// </summary>
        public string Message { get; set; }
    }
}
