using MCPForUnity.Editor.Dependencies.Models;

namespace MCPForUnity.Editor.Dependencies.PlatformDetectors
{
    /// <summary>
    /// Interface for platform-specific dependency detection
    /// </summary>
    public interface IPlatformDetector
    {
        /// <summary>
        /// Platform name this detector handles
        /// </summary>
        string PlatformName { get; }

        /// <summary>
        /// Whether this detector can run on the current platform
        /// </summary>
        bool CanDetect { get; }

        /// <summary>
        /// Detect Python installation on this platform
        /// </summary>
        DependencyStatus DetectPython();

        /// <summary>
        /// Detect uv package manager on this platform
        /// </summary>
        DependencyStatus DetectUv();

        /// <summary>
        /// Detect git on this platform. Optional: only the Package Manager's Git-URL install path needs it.
        /// </summary>
        DependencyStatus DetectGit();

        /// <summary>
        /// Get platform-specific installation recommendations
        /// </summary>
        string GetInstallationRecommendations();

        /// <summary>
        /// Get platform-specific Python installation URL
        /// </summary>
        string GetPythonInstallUrl();

        /// <summary>
        /// Get platform-specific uv installation URL
        /// </summary>
        string GetUvInstallUrl();
    }
}
