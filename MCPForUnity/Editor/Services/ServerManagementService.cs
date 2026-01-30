using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services.Server;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Service for managing MCP server lifecycle
    /// </summary>
    public class ServerManagementService : IServerManagementService
    {
        private readonly IProcessDetector _processDetector;
        private readonly IPidFileManager _pidFileManager;
        private readonly IProcessTerminator _processTerminator;
        private readonly IServerCommandBuilder _commandBuilder;
        private readonly ITerminalLauncher _terminalLauncher;

        /// <summary>
        /// Creates a new ServerManagementService with default dependencies.
        /// </summary>
        public ServerManagementService() : this(null, null, null, null, null) { }

        /// <summary>
        /// Creates a new ServerManagementService with injected dependencies (for testing).
        /// </summary>
        /// <param name="processDetector">Process detector implementation (null for default)</param>
        /// <param name="pidFileManager">PID file manager implementation (null for default)</param>
        /// <param name="processTerminator">Process terminator implementation (null for default)</param>
        /// <param name="commandBuilder">Server command builder implementation (null for default)</param>
        /// <param name="terminalLauncher">Terminal launcher implementation (null for default)</param>
        public ServerManagementService(
            IProcessDetector processDetector,
            IPidFileManager pidFileManager = null,
            IProcessTerminator processTerminator = null,
            IServerCommandBuilder commandBuilder = null,
            ITerminalLauncher terminalLauncher = null)
        {
            _processDetector = processDetector ?? new ProcessDetector();
            _pidFileManager = pidFileManager ?? new PidFileManager();
            _processTerminator = processTerminator ?? new ProcessTerminator(_processDetector);
            _commandBuilder = commandBuilder ?? new ServerCommandBuilder();
            _terminalLauncher = terminalLauncher ?? new TerminalLauncher();
        }

        private string QuoteIfNeeded(string s)
        {
            return _commandBuilder.QuoteIfNeeded(s);
        }

        private string NormalizeForMatch(string s)
        {
            return _processDetector.NormalizeForMatch(s);
        }

        private void ClearLocalServerPidTracking()
        {
            _pidFileManager.ClearTracking();
        }

        private void StoreLocalHttpServerHandshake(string pidFilePath, string instanceToken)
        {
            _pidFileManager.StoreHandshake(pidFilePath, instanceToken);
        }

        private bool TryGetLocalHttpServerHandshake(out string pidFilePath, out string instanceToken)
        {
            return _pidFileManager.TryGetHandshake(out pidFilePath, out instanceToken);
        }

        private string GetLocalHttpServerPidFilePath(int port)
        {
            return _pidFileManager.GetPidFilePath(port);
        }

        private bool TryReadPidFromPidFile(string pidFilePath, out int pid)
        {
            return _pidFileManager.TryReadPid(pidFilePath, out pid);
        }

        private bool TryProcessCommandLineContainsInstanceToken(int pid, string instanceToken, out bool containsToken)
        {
            containsToken = false;
            if (pid <= 0 || string.IsNullOrEmpty(instanceToken))
            {
                return false;
            }

            try
            {
                string tokenNeedle = instanceToken.ToLowerInvariant();

                if (Application.platform == RuntimePlatform.WindowsEditor)
                {
                    // Query full command line so we can validate token (reduces PID reuse risk).
                    // Use CIM via PowerShell (wmic is deprecated).
                    string ps = $"(Get-CimInstance Win32_Process -Filter \\\"ProcessId={pid}\\\").CommandLine";
                    bool ok = ExecPath.TryRun("powershell", $"-NoProfile -Command \"{ps}\"", Application.dataPath, out var stdout, out var stderr, 5000);
                    string combined = ((stdout ?? string.Empty) + "\n" + (stderr ?? string.Empty)).ToLowerInvariant();
                    containsToken = combined.Contains(tokenNeedle);
                    return ok;
                }

                if (TryGetUnixProcessArgs(pid, out var argsLowerNow))
                {
                    containsToken = argsLowerNow.Contains(NormalizeForMatch(tokenNeedle));
                    return true;
                }
            }
            catch { }

            return false;
        }

        private string ComputeShortHash(string input)
        {
            return _pidFileManager.ComputeShortHash(input);
        }

        private bool TryGetStoredLocalServerPid(int expectedPort, out int pid)
        {
            return _pidFileManager.TryGetStoredPid(expectedPort, out pid);
        }

        private string GetStoredArgsHash()
        {
            return _pidFileManager.GetStoredArgsHash();
        }

        /// <summary>
        /// Clear the local uvx cache for the MCP server package
        /// </summary>
        /// <returns>True if successful, false otherwise</returns>
        public bool ClearUvxCache()
        {
            try
            {
                string uvxPath = MCPServiceLocator.Paths.GetUvxPath();
                string uvCommand = BuildUvPathFromUvx(uvxPath);

                // Get the package name
                string packageName = "mcp-for-unity";

                // Run uvx cache clean command
                string args = $"cache clean {packageName}";

                bool success;
                string stdout;
                string stderr;

                success = ExecuteUvCommand(uvCommand, args, out stdout, out stderr);

                if (success)
                {
                    McpLog.Info($"uv cache cleared successfully: {stdout}");
                    return true;
                }
                string combinedOutput = string.Join(
                    Environment.NewLine,
                    new[] { stderr, stdout }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()));

                string lockHint = (!string.IsNullOrEmpty(combinedOutput) &&
                                   combinedOutput.IndexOf("currently in-use", StringComparison.OrdinalIgnoreCase) >= 0)
                    ? "Another uv process may be holding the cache lock; wait a moment and try again or clear with '--force' from a terminal."
                    : string.Empty;

                if (string.IsNullOrEmpty(combinedOutput))
                {
                    combinedOutput = "Command failed with no output. Ensure uv is installed, on PATH, or set an override in Advanced Settings.";
                }

                McpLog.Error(
                    $"Failed to clear uv cache using '{uvCommand} {args}'. " +
                    $"Details: {combinedOutput}{(string.IsNullOrEmpty(lockHint) ? string.Empty : " Hint: " + lockHint)}");
                return false;
            }
            catch (Exception ex)
            {
                McpLog.Error($"Error clearing uv cache: {ex.Message}");
                return false;
            }
        }

        private bool ExecuteUvCommand(string uvCommand, string args, out string stdout, out string stderr)
        {
            stdout = null;
            stderr = null;

            string uvxPath = MCPServiceLocator.Paths.GetUvxPath();
            string uvPath = BuildUvPathFromUvx(uvxPath);

            if (!string.Equals(uvCommand, uvPath, StringComparison.OrdinalIgnoreCase))
            {
                return ExecPath.TryRun(uvCommand, args, Application.dataPath, out stdout, out stderr, 30000);
            }

            string command = $"{uvPath} {args}";
            string extraPathPrepend = GetPlatformSpecificPathPrepend();

            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                return ExecPath.TryRun("cmd.exe", $"/c {command}", Application.dataPath, out stdout, out stderr, 30000, extraPathPrepend);
            }

            string shell = File.Exists("/bin/bash") ? "/bin/bash" : "/bin/sh";

            if (!string.IsNullOrEmpty(shell) && File.Exists(shell))
            {
                string escaped = command.Replace("\"", "\\\"");
                return ExecPath.TryRun(shell, $"-lc \"{escaped}\"", Application.dataPath, out stdout, out stderr, 30000, extraPathPrepend);
            }

            return ExecPath.TryRun(uvPath, args, Application.dataPath, out stdout, out stderr, 30000, extraPathPrepend);
        }

        private string BuildUvPathFromUvx(string uvxPath)
        {
            return _commandBuilder.BuildUvPathFromUvx(uvxPath);
        }

        private string GetPlatformSpecificPathPrepend()
        {
            return _commandBuilder.GetPlatformSpecificPathPrepend();
        }

        /// <summary>
        /// Start the local HTTP server in a separate terminal window.
        /// Stops any existing server on the port and clears the uvx cache first.
        /// </summary>
        public bool StartLocalHttpServer()
        {
            /// Clean stale Python build artifacts when using a local dev server path
            AssetPathUtility.CleanLocalServerBuildArtifacts();

            if (!TryGetLocalHttpServerCommandParts(out _, out _, out var displayCommand, out var error))
            {
                EditorUtility.DisplayDialog(
                    "Cannot Start HTTP Server",
                    error ?? "The server command could not be constructed with the current settings.",
                    "OK");
                return false;
            }

            // First, try to stop any existing server (quietly; we'll only warn if the port remains occupied).
            StopLocalHttpServerInternal(quiet: true);

            // If the port is still occupied, don't start and explain why (avoid confusing "refusing to stop" warnings).
            try
            {
                string httpUrl = HttpEndpointUtility.GetLocalBaseUrl();
                if (Uri.TryCreate(httpUrl, UriKind.Absolute, out var uri) && uri.Port > 0)
                {
                    var remaining = GetListeningProcessIdsForPort(uri.Port);
                    if (remaining.Count > 0)
                    {
                        EditorUtility.DisplayDialog(
                            "Port In Use",
                            $"Cannot start the local HTTP server because port {uri.Port} is already in use by PID(s): " +
                            $"{string.Join(", ", remaining)}\n\n" +
                            "MCP For Unity will not terminate unrelated processes. Stop the owning process manually or change the HTTP URL.",
                            "OK");
                        return false;
                    }
                }
            }
            catch { }

            // Note: Dev mode cache-busting is handled by `uvx --no-cache --refresh` in the generated command.

            // Create a per-launch token + pidfile path so Stop can be deterministic without relying on port/PID heuristics.
            string baseUrlForPid = HttpEndpointUtility.GetLocalBaseUrl();
            Uri.TryCreate(baseUrlForPid, UriKind.Absolute, out var uriForPid);
            int portForPid = uriForPid?.Port ?? 0;
            string instanceToken = Guid.NewGuid().ToString("N");
            string pidFilePath = portForPid > 0 ? GetLocalHttpServerPidFilePath(portForPid) : null;

            string launchCommand = displayCommand;
            if (!string.IsNullOrEmpty(pidFilePath))
            {
                launchCommand = $"{displayCommand} --pidfile {QuoteIfNeeded(pidFilePath)} --unity-instance-token {instanceToken}";
            }

            if (EditorUtility.DisplayDialog(
                "Start Local HTTP Server",
                $"This will start the MCP server in HTTP mode in a new terminal window:\n\n{launchCommand}\n\n" +
                "Continue?",
                "Start Server",
                "Cancel"))
            {
                try
                {
                    // Clear any stale handshake state from prior launches.
                    ClearLocalServerPidTracking();

                    // Best-effort: delete stale pidfile if it exists.
                    try
                    {
                        if (!string.IsNullOrEmpty(pidFilePath) && File.Exists(pidFilePath))
                        {
                            DeletePidFile(pidFilePath);
                        }
                    }
                    catch { }

                    // Launch the server in a new terminal window (keeps user-visible logs).
                    var startInfo = CreateTerminalProcessStartInfo(launchCommand);
                    System.Diagnostics.Process.Start(startInfo);
                    if (!string.IsNullOrEmpty(pidFilePath))
                    {
                        StoreLocalHttpServerHandshake(pidFilePath, instanceToken);
                    }
                    McpLog.Info($"Started local HTTP server in terminal: {launchCommand}");
                    return true;
                }
                catch (Exception ex)
                {
                    McpLog.Error($"Failed to start server: {ex.Message}");
                    EditorUtility.DisplayDialog(
                        "Error",
                        $"Failed to start server: {ex.Message}",
                        "OK");
                    return false;
                }
            }

            return false;
        }

        /// <summary>
        /// Stop the local HTTP server by finding the process listening on the configured port
        /// </summary>
        public bool StopLocalHttpServer()
        {
            return StopLocalHttpServerInternal(quiet: false);
        }

        public bool StopManagedLocalHttpServer()
        {
            if (!TryGetLocalHttpServerHandshake(out var pidFilePath, out _))
            {
                return false;
            }

            int port = 0;
            if (!TryGetPortFromPidFilePath(pidFilePath, out port) || port <= 0)
            {
                string baseUrl = HttpEndpointUtility.GetLocalBaseUrl();
                if (IsLocalUrl(baseUrl)
                    && Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
                    && uri.Port > 0)
                {
                    port = uri.Port;
                }
            }

            if (port <= 0)
            {
                return false;
            }

            return StopLocalHttpServerInternal(quiet: true, portOverride: port, allowNonLocalUrl: true);
        }

        public bool IsLocalHttpServerRunning()
        {
            try
            {
                string httpUrl = HttpEndpointUtility.GetLocalBaseUrl();
                if (!IsLocalUrl(httpUrl))
                {
                    return false;
                }

                if (!Uri.TryCreate(httpUrl, UriKind.Absolute, out var uri) || uri.Port <= 0)
                {
                    return false;
                }

                int port = uri.Port;

                // Handshake path: if we have a pidfile+token and the PID is still the listener, treat as running.
                if (TryGetLocalHttpServerHandshake(out var pidFilePath, out var instanceToken)
                    && TryReadPidFromPidFile(pidFilePath, out var pidFromFile)
                    && pidFromFile > 0)
                {
                    var pidsNow = GetListeningProcessIdsForPort(port);
                    if (pidsNow.Contains(pidFromFile))
                    {
                        return true;
                    }
                }

                var pids = GetListeningProcessIdsForPort(port);
                if (pids.Count == 0)
                {
                    return false;
                }

                // Strong signal: stored PID is still the listener.
                if (TryGetStoredLocalServerPid(port, out int storedPid) && storedPid > 0)
                {
                    if (pids.Contains(storedPid))
                    {
                        return true;
                    }
                }

                // Best-effort: if anything listening looks like our server, treat as running.
                foreach (var pid in pids)
                {
                    if (pid <= 0) continue;
                    if (LooksLikeMcpServerProcess(pid))
                    {
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public bool IsLocalHttpServerReachable()
        {
            try
            {
                string httpUrl = HttpEndpointUtility.GetLocalBaseUrl();
                if (!IsLocalUrl(httpUrl))
                {
                    return false;
                }

                if (!Uri.TryCreate(httpUrl, UriKind.Absolute, out var uri) || uri.Port <= 0)
                {
                    return false;
                }

                return TryConnectToLocalPort(uri.Host, uri.Port, timeoutMs: 50);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryConnectToLocalPort(string host, int port, int timeoutMs)
        {
            try
            {
                if (string.IsNullOrEmpty(host))
                {
                    host = "127.0.0.1";
                }

                var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { host };
                if (host == "localhost" || host == "0.0.0.0")
                {
                    hosts.Add("127.0.0.1");
                }
                if (host == "::" || host == "0:0:0:0:0:0:0:0")
                {
                    hosts.Add("::1");
                }

                foreach (var target in hosts)
                {
                    try
                    {
                        using (var client = new TcpClient())
                        {
                            var connectTask = client.ConnectAsync(target, port);
                            if (connectTask.Wait(timeoutMs) && client.Connected)
                            {
                                return true;
                            }
                        }
                    }
                    catch
                    {
                        // Ignore per-host failures.
                    }
                }
            }
            catch
            {
                // Ignore probe failures and treat as unreachable.
            }

            return false;
        }

        private bool StopLocalHttpServerInternal(bool quiet, int? portOverride = null, bool allowNonLocalUrl = false)
        {
            string httpUrl = HttpEndpointUtility.GetLocalBaseUrl();
            if (!allowNonLocalUrl && !IsLocalUrl(httpUrl))
            {
                if (!quiet)
                {
                    McpLog.Warn("Cannot stop server: URL is not local.");
                }
                return false;
            }

            try
            {
                int port = 0;
                if (portOverride.HasValue)
                {
                    port = portOverride.Value;
                }
                else
                {
                    var uri = new Uri(httpUrl);
                    port = uri.Port;
                }

                if (port <= 0)
                {
                    if (!quiet)
                    {
                        McpLog.Warn("Cannot stop server: Invalid port.");
                    }
                    return false;
                }

                // Guardrails:
                // - Never terminate the Unity Editor process.
                // - Only terminate processes that look like the MCP server (uv/uvx/python running mcp-for-unity).
                // This prevents accidental termination of unrelated services (including Unity itself).
                int unityPid = GetCurrentProcessIdSafe();
                bool stoppedAny = false;

                // Preferred deterministic stop path: if we have a pidfile+token from a Unity-managed launch,
                // validate and terminate exactly that PID.
                if (TryGetLocalHttpServerHandshake(out var pidFilePath, out var instanceToken))
                {
                    // Prefer deterministic stop when Unity started the server (pidfile+token).
                    // If the pidfile isn't available yet (fast quit after start), we can optionally fall back
                    // to port-based heuristics when a port override was supplied (managed-stop path).
                    if (!TryReadPidFromPidFile(pidFilePath, out var pidFromFile) || pidFromFile <= 0)
                    {
                        if (!portOverride.HasValue)
                        {
                            if (!quiet)
                            {
                                McpLog.Warn(
                                    $"Cannot stop local HTTP server on port {port}: pidfile not available yet at '{pidFilePath}'. " +
                                    "If you just started the server, wait a moment and try again.");
                            }
                            return false;
                        }

                        // Managed-stop fallback: proceed with port-based heuristics below.
                        // We intentionally do NOT clear handshake state here; it will be cleared if we successfully
                        // stop a server process and/or the port is freed.
                    }
                    else
                    {
                        // Never kill Unity/Hub.
                        if (unityPid > 0 && pidFromFile == unityPid)
                        {
                            if (!quiet)
                            {
                                McpLog.Warn($"Refusing to stop port {port}: pidfile PID {pidFromFile} is the Unity Editor process.");
                            }
                        }
                        else
                        {
                            var listeners = GetListeningProcessIdsForPort(port);
                            if (listeners.Count == 0)
                            {
                                // Nothing is listening anymore; clear stale handshake state.
                                try { DeletePidFile(pidFilePath); } catch { }
                                ClearLocalServerPidTracking();
                                if (!quiet)
                                {
                                    McpLog.Info($"No process found listening on port {port}");
                                }
                                return false;
                            }
                            bool pidIsListener = listeners.Contains(pidFromFile);
                            bool tokenQueryOk = TryProcessCommandLineContainsInstanceToken(pidFromFile, instanceToken, out bool tokenMatches);
                            bool allowKill;
                            if (tokenQueryOk)
                            {
                                allowKill = tokenMatches;
                            }
                            else
                            {
                                // If token validation is unavailable (e.g. Windows CIM permission issues),
                                // fall back to a stricter heuristic: only allow stop if the PID still looks like our server.
                                allowKill = LooksLikeMcpServerProcess(pidFromFile);
                            }

                            if (pidIsListener && allowKill)
                            {
                                if (TerminateProcess(pidFromFile))
                                {
                                    stoppedAny = true;
                                    try { DeletePidFile(pidFilePath); } catch { }
                                    ClearLocalServerPidTracking();
                                    if (!quiet)
                                    {
                                        McpLog.Info($"Stopped local HTTP server on port {port} (PID: {pidFromFile})");
                                    }
                                    return true;
                                }
                                if (!quiet)
                                {
                                    McpLog.Warn($"Failed to terminate local HTTP server on port {port} (PID: {pidFromFile}).");
                                }
                                return false;
                            }
                            if (!quiet)
                            {
                                McpLog.Warn(
                                    $"Refusing to stop port {port}: pidfile PID {pidFromFile} failed validation " +
                                    $"(listener={pidIsListener}, tokenMatch={tokenMatches}, tokenQueryOk={tokenQueryOk}).");
                            }
                            return false;
                        }
                    }
                }

                var pids = GetListeningProcessIdsForPort(port);
                if (pids.Count == 0)
                {
                    if (stoppedAny)
                    {
                        // We stopped what Unity started; the port is now free.
                        if (!quiet)
                        {
                            McpLog.Info($"Stopped local HTTP server on port {port}");
                        }
                        ClearLocalServerPidTracking();
                        return true;
                    }

                    if (!quiet)
                    {
                        McpLog.Info($"No process found listening on port {port}");
                    }
                    ClearLocalServerPidTracking();
                    return false;
                }

                // Prefer killing the PID that we previously observed binding this port (if still valid).
                if (TryGetStoredLocalServerPid(port, out int storedPid))
                {
                    if (pids.Contains(storedPid))
                    {
                        string expectedHash = string.Empty;
                        expectedHash = GetStoredArgsHash();

                        // Prefer a fingerprint match (reduces PID reuse risk). If missing (older installs),
                        // fall back to a looser check to avoid leaving orphaned servers after domain reload.
                        if (TryGetUnixProcessArgs(storedPid, out var storedArgsLowerNow))
                        {
                            // Never kill Unity/Hub.
                            // Note: "mcp-for-unity" includes "unity", so detect MCP indicators first.
                            bool storedMentionsMcp = storedArgsLowerNow.Contains("mcp-for-unity")
                                                     || storedArgsLowerNow.Contains("mcp_for_unity")
                                                     || storedArgsLowerNow.Contains("mcpforunity");
                            if (storedArgsLowerNow.Contains("unityhub")
                                || storedArgsLowerNow.Contains("unity hub")
                                || (storedArgsLowerNow.Contains("unity") && !storedMentionsMcp))
                            {
                                if (!quiet)
                                {
                                    McpLog.Warn($"Refusing to stop port {port}: stored PID {storedPid} appears to be a Unity process.");
                                }
                            }
                            else
                            {
                                bool allowKill = false;
                                if (!string.IsNullOrEmpty(expectedHash))
                                {
                                    allowKill = string.Equals(expectedHash, ComputeShortHash(storedArgsLowerNow), StringComparison.OrdinalIgnoreCase);
                                }
                                else
                                {
                                    // Older versions didn't store a fingerprint; accept common server indicators.
                                    allowKill = storedArgsLowerNow.Contains("uvicorn")
                                                || storedArgsLowerNow.Contains("fastmcp")
                                                || storedArgsLowerNow.Contains("mcpforunity")
                                                || storedArgsLowerNow.Contains("mcp-for-unity")
                                                || storedArgsLowerNow.Contains("mcp_for_unity")
                                                || storedArgsLowerNow.Contains("uvx")
                                                || storedArgsLowerNow.Contains("python");
                                }

                                if (allowKill && TerminateProcess(storedPid))
                                {
                                    if (!quiet)
                                    {
                                        McpLog.Info($"Stopped local HTTP server on port {port} (PID: {storedPid})");
                                    }
                                    stoppedAny = true;
                                    ClearLocalServerPidTracking();
                                    // Refresh the PID list to avoid double-work.
                                    pids = GetListeningProcessIdsForPort(port);
                                }
                                else if (!allowKill && !quiet)
                                {
                                    McpLog.Warn($"Refusing to stop port {port}: stored PID {storedPid} did not match expected server fingerprint.");
                                }
                            }
                        }
                    }
                    else
                    {
                        // Stale PID (no longer listening). Clear.
                        ClearLocalServerPidTracking();
                    }
                }

                foreach (var pid in pids)
                {
                    if (pid <= 0) continue;
                    if (unityPid > 0 && pid == unityPid)
                    {
                        if (!quiet)
                        {
                            McpLog.Warn($"Refusing to stop port {port}: owning PID appears to be the Unity Editor process (PID {pid}).");
                        }
                        continue;
                    }

                    if (!LooksLikeMcpServerProcess(pid))
                    {
                        if (!quiet)
                        {
                            McpLog.Warn($"Refusing to stop port {port}: owning PID {pid} does not look like mcp-for-unity.");
                        }
                        continue;
                    }

                    if (TerminateProcess(pid))
                    {
                        McpLog.Info($"Stopped local HTTP server on port {port} (PID: {pid})");
                        stoppedAny = true;
                    }
                    else
                    {
                        if (!quiet)
                        {
                            McpLog.Warn($"Failed to stop process PID {pid} on port {port}");
                        }
                    }
                }

                if (stoppedAny)
                {
                    ClearLocalServerPidTracking();
                }
                return stoppedAny;
            }
            catch (Exception ex)
            {
                if (!quiet)
                {
                    McpLog.Error($"Failed to stop server: {ex.Message}");
                }
                return false;
            }
        }

        private bool TryGetUnixProcessArgs(int pid, out string argsLower)
        {
            return _processDetector.TryGetProcessCommandLine(pid, out argsLower);
        }

        private bool TryGetPortFromPidFilePath(string pidFilePath, out int port)
        {
            return _pidFileManager.TryGetPortFromPidFilePath(pidFilePath, out port);
        }

        private void DeletePidFile(string pidFilePath)
        {
            _pidFileManager.DeletePidFile(pidFilePath);
        }

        private List<int> GetListeningProcessIdsForPort(int port)
        {
            return _processDetector.GetListeningProcessIdsForPort(port);
        }

        private int GetCurrentProcessIdSafe()
        {
            return _processDetector.GetCurrentProcessId();
        }

        private bool LooksLikeMcpServerProcess(int pid)
        {
            return _processDetector.LooksLikeMcpServerProcess(pid);
        }

        private bool TerminateProcess(int pid)
        {
            return _processTerminator.Terminate(pid);
        }

        /// <summary>
        /// Attempts to build the command used for starting the local HTTP server
        /// </summary>
        public bool TryGetLocalHttpServerCommand(out string command, out string error)
        {
            command = null;
            error = null;
            if (!TryGetLocalHttpServerCommandParts(out var fileName, out var args, out var displayCommand, out error))
            {
                return false;
            }

            // Maintain existing behavior: return a single command string suitable for display/copy.
            command = displayCommand;
            return true;
        }

        private bool TryGetLocalHttpServerCommandParts(out string fileName, out string arguments, out string displayCommand, out string error)
        {
            return _commandBuilder.TryBuildCommand(out fileName, out arguments, out displayCommand, out error);
        }

        /// <summary>
        /// Check if the configured HTTP URL is a local address
        /// </summary>
        public bool IsLocalUrl()
        {
            string httpUrl = HttpEndpointUtility.GetLocalBaseUrl();
            return IsLocalUrl(httpUrl);
        }

        /// <summary>
        /// Check if a URL is local (localhost, 127.0.0.1, 0.0.0.0)
        /// </summary>
        private static bool IsLocalUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;

            try
            {
                var uri = new Uri(url);
                string host = uri.Host.ToLower();
                return host == "localhost" || host == "127.0.0.1" || host == "0.0.0.0" || host == "::1";
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Check if the local HTTP server can be started
        /// </summary>
        public bool CanStartLocalServer()
        {
            bool useHttpTransport = EditorConfigurationCache.Instance.UseHttpTransport;
            return useHttpTransport && IsLocalUrl();
        }

        private System.Diagnostics.ProcessStartInfo CreateTerminalProcessStartInfo(string command)
        {
            return _terminalLauncher.CreateTerminalProcessStartInfo(command);
        }
    }
}
