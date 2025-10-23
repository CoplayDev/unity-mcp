using System;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Default implementation of platform detection service
    /// </summary>
    public class PlatformService : IPlatformService
    {
        /// <summary>
        /// Checks if the current platform is Windows
        /// </summary>
        /// <returns>True if running on Windows</returns>
        public bool IsWindows()
        {
            return Environment.OSVersion.Platform == PlatformID.Win32NT;
        }

        /// <summary>
        /// Gets the SystemRoot environment variable (Windows-specific)
        /// </summary>
        /// <returns>SystemRoot path, or "C:\\Windows" as fallback on Windows, null on other platforms</returns>
        public string GetSystemRoot()
        {
            if (!IsWindows())
                return null;

            return Environment.GetEnvironmentVariable("SystemRoot") ?? "C:\\Windows";
        }
    }
}
