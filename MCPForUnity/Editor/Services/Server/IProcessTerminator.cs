namespace MCPForUnity.Editor.Services.Server
{
    /// <summary>
    /// Interface for platform-specific process termination.
    /// Provides methods to terminate processes gracefully or forcefully.
    /// </summary>
    public interface IProcessTerminator
    {
        /// <summary>
        /// Terminates a process using platform-appropriate methods.
        /// On Unix: Tries SIGTERM first with grace period, then SIGKILL.
        /// On Windows: Tries taskkill, then taskkill /F.
        /// </summary>
        /// <param name="pid">The process ID to terminate</param>
        /// <returns>True if the process was terminated successfully</returns>
        bool Terminate(int pid);
    }
}
