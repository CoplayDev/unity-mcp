namespace MCPForUnity.Editor.Services.Server
{
    /// <summary>
    /// Interface for managing PID files and handshake state for the local HTTP server.
    /// Handles persistence of server process information across Unity domain reloads.
    /// </summary>
    public interface IPidFileManager
    {
        /// <summary>
        /// Gets the directory where PID files are stored.
        /// </summary>
        /// <returns>Path to the PID file directory</returns>
        string GetPidDirectory();

        /// <summary>
        /// Gets the path to the PID file for a specific port.
        /// </summary>
        /// <param name="port">The port number</param>
        /// <returns>Full path to the PID file</returns>
        string GetPidFilePath(int port);

        /// <summary>
        /// Attempts to read the PID from a PID file.
        /// </summary>
        /// <param name="pidFilePath">Path to the PID file</param>
        /// <param name="pid">Output: the process ID if found</param>
        /// <returns>True if a valid PID was read</returns>
        bool TryReadPid(string pidFilePath, out int pid);

        /// <summary>
        /// Attempts to extract the port number from a PID file path.
        /// </summary>
        /// <param name="pidFilePath">Path to the PID file</param>
        /// <param name="port">Output: the port number</param>
        /// <returns>True if the port was extracted successfully</returns>
        bool TryGetPortFromPidFilePath(string pidFilePath, out int port);

        /// <summary>
        /// Deletes a PID file.
        /// </summary>
        /// <param name="pidFilePath">Path to the PID file to delete</param>
        void DeletePidFile(string pidFilePath);

        /// <summary>
        /// Stores the handshake information (PID file path and instance token) in EditorPrefs.
        /// </summary>
        /// <param name="pidFilePath">Path to the PID file</param>
        /// <param name="instanceToken">Unique instance token for the server</param>
        void StoreHandshake(string pidFilePath, string instanceToken);

        /// <summary>
        /// Attempts to retrieve stored handshake information from EditorPrefs.
        /// </summary>
        /// <param name="pidFilePath">Output: stored PID file path</param>
        /// <param name="instanceToken">Output: stored instance token</param>
        /// <returns>True if valid handshake information was found</returns>
        bool TryGetHandshake(out string pidFilePath, out string instanceToken);

        /// <summary>
        /// Stores PID tracking information in EditorPrefs.
        /// </summary>
        /// <param name="pid">The process ID</param>
        /// <param name="port">The port number</param>
        /// <param name="argsHash">Optional hash of the command arguments</param>
        void StoreTracking(int pid, int port, string argsHash = null);

        /// <summary>
        /// Attempts to retrieve a stored PID for the expected port.
        /// Validates that the stored information is still valid (within 6-hour window).
        /// </summary>
        /// <param name="expectedPort">The expected port number</param>
        /// <param name="pid">Output: the stored process ID</param>
        /// <returns>True if a valid stored PID was found</returns>
        bool TryGetStoredPid(int expectedPort, out int pid);

        /// <summary>
        /// Gets the stored args hash for the tracked server.
        /// </summary>
        /// <returns>The stored args hash, or empty string if not found</returns>
        string GetStoredArgsHash();

        /// <summary>
        /// Clears all PID tracking information from EditorPrefs.
        /// </summary>
        void ClearTracking();

        /// <summary>
        /// Computes a short hash of the input string for fingerprinting.
        /// </summary>
        /// <param name="input">The input string</param>
        /// <returns>A short hash string (16 hex characters)</returns>
        string ComputeShortHash(string input);
    }
}
