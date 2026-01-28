using System.Diagnostics;

namespace MCPForUnity.Editor.Services.Server
{
    /// <summary>
    /// Interface for launching commands in platform-specific terminal windows.
    /// Supports macOS Terminal, Windows cmd, and Linux terminal emulators.
    /// </summary>
    public interface ITerminalLauncher
    {
        /// <summary>
        /// Creates a ProcessStartInfo for opening a terminal window with the given command.
        /// Works cross-platform: macOS, Windows, and Linux.
        /// </summary>
        /// <param name="command">The command to execute in the terminal</param>
        /// <returns>A configured ProcessStartInfo for launching the terminal</returns>
        ProcessStartInfo CreateTerminalProcessStartInfo(string command);

        /// <summary>
        /// Gets the project root path for storing terminal scripts.
        /// </summary>
        /// <returns>Path to the project root directory</returns>
        string GetProjectRootPath();
    }
}
