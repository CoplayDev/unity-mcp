namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Service for platform detection and platform-specific environment access
    /// </summary>
    public interface IPlatformService
    {
        /// <summary>
        /// Checks if the current platform is Windows
        /// </summary>
        /// <returns>True if running on Windows</returns>
        bool IsWindows();

        /// <summary>
        /// Gets the SystemRoot environment variable (Windows-specific)
        /// </summary>
        /// <returns>SystemRoot path, or null if not available</returns>
        string GetSystemRoot();
    }
}
