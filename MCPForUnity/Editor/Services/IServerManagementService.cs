namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Interface for server management operations
    /// </summary>
    public interface IServerManagementService
    {
        /// <summary>
        /// Clear the local uvx cache for the MCP server package
        /// </summary>
        /// <returns>True if successful, false otherwise</returns>
        bool ClearUvxCache();

        /// <summary>
        /// Start the local HTTP server headless (no terminal window), redirecting its output to a
        /// per-port launch log. Stops any existing server on the port and clears stale artifacts first.
        /// </summary>
        /// <param name="quiet">When true, skip confirmation dialogs (used by auto-start).</param>
        /// <returns>True if server was started successfully, false otherwise</returns>
        bool StartLocalHttpServer(bool quiet = false);

        /// <summary>
        /// Gets the launch-log path for the configured local HTTP server port, or null if unavailable.
        /// </summary>
        string GetLocalHttpServerLaunchLogPath();

        /// <summary>
        /// Returns true while the most-recently launched headless server process is still alive.
        /// Used by callers to keep waiting for reachability instead of declaring failure prematurely.
        /// </summary>
        bool IsManagedServerLaunchProcessAlive();

        /// <summary>
        /// Writes a launch-failure report to the Console (Error): the tail of the launch log,
        /// the log path, and a copy-command hint pointing at the Manual Server Launch foldout.
        /// </summary>
        void LogLocalHttpServerLaunchFailure();

        /// <summary>
        /// Stop the local HTTP server by finding the process listening on the configured port
        /// </summary>
        bool StopLocalHttpServer();

        /// <summary>
        /// Stop the Unity-managed local HTTP server if a handshake/pidfile exists,
        /// even if the current transport selection has changed.
        /// </summary>
        bool StopManagedLocalHttpServer();

        /// <summary>
        /// Best-effort detection: returns true if a local MCP HTTP server appears to be running
        /// on the configured local URL/port (used to drive UI state even if the session is not active).
        /// </summary>
        bool IsLocalHttpServerRunning();

        /// <summary>
        /// Fast reachability check: returns true if a local TCP listener is accepting connections
        /// for the configured local URL/port (used for UI state without process inspection).
        /// </summary>
        bool IsLocalHttpServerReachable();

        /// <summary>
        /// Attempts to get the command that will be executed when starting the local HTTP server
        /// </summary>
        /// <param name="command">The command that will be executed when available</param>
        /// <param name="error">Reason why a command could not be produced</param>
        /// <returns>True if a command is available, false otherwise</returns>
        bool TryGetLocalHttpServerCommand(out string command, out string error);

        /// <summary>
        /// Check if the configured HTTP URL is a local address
        /// </summary>
        /// <returns>True if URL is local (localhost, 127.0.0.1, etc.)</returns>
        bool IsLocalUrl();

        /// <summary>
        /// Check if the local HTTP server can be started
        /// </summary>
        /// <returns>True if HTTP transport is enabled and URL satisfies local launch security policy</returns>
        bool CanStartLocalServer();
    }
}
