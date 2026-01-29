using System.Collections.Generic;

namespace MCPForUnity.Editor.Services.Server
{
    /// <summary>
    /// Interface for platform-specific process inspection operations.
    /// Provides methods to detect MCP server processes, query process command lines,
    /// and find processes listening on specific ports.
    /// </summary>
    public interface IProcessDetector
    {
        /// <summary>
        /// Determines if a process looks like an MCP server process based on its command line.
        /// Checks for indicators like uvx, python, mcp-for-unity, uvicorn, etc.
        /// </summary>
        /// <param name="pid">The process ID to check</param>
        /// <returns>True if the process appears to be an MCP server</returns>
        bool LooksLikeMcpServerProcess(int pid);

        /// <summary>
        /// Attempts to get the command line arguments for a Unix process.
        /// </summary>
        /// <param name="pid">The process ID</param>
        /// <param name="argsLower">Output: normalized (lowercase, whitespace removed) command line args</param>
        /// <returns>True if the command line was retrieved successfully</returns>
        bool TryGetProcessCommandLine(int pid, out string argsLower);

        /// <summary>
        /// Gets the process IDs of all processes listening on a specific TCP port.
        /// </summary>
        /// <param name="port">The port number to check</param>
        /// <returns>List of process IDs listening on the port</returns>
        List<int> GetListeningProcessIdsForPort(int port);

        /// <summary>
        /// Gets the current Unity Editor process ID safely.
        /// </summary>
        /// <returns>The current process ID, or -1 if it cannot be determined</returns>
        int GetCurrentProcessId();

        /// <summary>
        /// Checks if a process exists on Unix systems.
        /// </summary>
        /// <param name="pid">The process ID to check</param>
        /// <returns>True if the process exists</returns>
        bool ProcessExists(int pid);

        /// <summary>
        /// Normalizes a string for matching by removing whitespace and converting to lowercase.
        /// </summary>
        /// <param name="input">The input string</param>
        /// <returns>Normalized string for matching</returns>
        string NormalizeForMatch(string input);
    }
}
