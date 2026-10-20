namespace MCPForUnity.Editor.Services.Server
{
    /// <summary>
    /// Interface for managing PID files and handshake state for the local HTTP server.
    /// Handshake and tracking state is keyed per project and per port, and the launch
    /// marker is per editor process, so concurrent editors sharing one server (or running
    /// servers on different ports) never act on each other's state.
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
        /// Deletes a PID file.
        /// </summary>
        /// <param name="pidFilePath">Path to the PID file to delete</param>
        void DeletePidFile(string pidFilePath);

        /// <summary>
        /// Stores the handshake for a server this editor process just launched: the PID file path and
        /// instance token go to EditorPrefs (per project, per port; survives editor restarts so the
        /// server can still be stopped deterministically later), and the port is recorded in
        /// SessionState as the launch marker (survives domain reloads, dies with this editor process).
        /// </summary>
        /// <param name="port">Port the server was launched on</param>
        /// <param name="pidFilePath">Path to the PID file</param>
        /// <param name="instanceToken">Unique instance token for the server</param>
        void StoreHandshake(int port, string pidFilePath, string instanceToken);

        /// <summary>
        /// Attempts to retrieve the stored handshake for a port (this project's slot only).
        /// </summary>
        /// <param name="port">Port to look up</param>
        /// <param name="pidFilePath">Output: stored PID file path</param>
        /// <param name="instanceToken">Output: stored instance token</param>
        /// <returns>True if valid handshake information was found</returns>
        bool TryGetHandshake(int port, out string pidFilePath, out string instanceToken);

        /// <summary>
        /// Returns the port of the server this editor process launched, if any. False for servers
        /// launched by other editors, by an earlier run of this editor, or externally.
        /// </summary>
        /// <param name="port">Output: the launched port</param>
        /// <returns>True if this editor process launched a server</returns>
        bool TryGetLaunchedPort(out int port);

        /// <summary>
        /// Stores PID tracking information in EditorPrefs (per project, per port).
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
        /// Gets the stored args hash for the tracked server on a port.
        /// </summary>
        /// <param name="port">The port number</param>
        /// <returns>The stored args hash, or empty string if not found</returns>
        string GetStoredArgsHash(int port);

        /// <summary>
        /// Clears handshake and tracking information for a port, and the launch marker if it
        /// points at that port. Other ports and other projects are untouched.
        /// </summary>
        /// <param name="port">The port number</param>
        void ClearTracking(int port);

        /// <summary>
        /// Computes a short hash of the input string for fingerprinting.
        /// </summary>
        /// <param name="input">The input string</param>
        /// <returns>A short hash string (16 hex characters)</returns>
        string ComputeShortHash(string input);
    }
}
