using System;
using System.IO;
using MCPForUnity.Editor.Helpers;
using UnityEngine;

namespace MCPForUnity.Editor.Services.Server
{
    /// <summary>
    /// Platform-specific process termination for stopping MCP server processes.
    /// </summary>
    public class ProcessTerminator : IProcessTerminator
    {
        private readonly IProcessDetector _processDetector;

        /// <summary>
        /// Creates a new ProcessTerminator with the specified process detector.
        /// </summary>
        /// <param name="processDetector">Process detector for checking process existence</param>
        public ProcessTerminator(IProcessDetector processDetector)
        {
            _processDetector = processDetector ?? throw new ArgumentNullException(nameof(processDetector));
        }

        /// <inheritdoc/>
        public bool Terminate(int pid)
        {
            // CRITICAL: Validate PID before any kill operation.
            // On Unix, kill(-1) kills ALL processes the user can signal!
            // On Unix, kill(0) signals all processes in the process group.
            // PID 1 is init/launchd and must never be killed.
            // Only positive PIDs > 1 are valid for targeted termination.
            if (pid <= 1)
            {
                return false;
            }

            // Never kill the current Unity process
            int currentPid = _processDetector.GetCurrentProcessId();
            if (currentPid > 0 && pid == currentPid)
            {
                return false;
            }

            try
            {
                string stdout, stderr;
                if (Application.platform == RuntimePlatform.WindowsEditor)
                {
                    // taskkill without /F first; fall back to /F if needed.
                    bool ok = ExecPath.TryRun("taskkill", $"/PID {pid} /T", Application.dataPath, out stdout, out stderr);
                    if (!ok)
                    {
                        ok = ExecPath.TryRun("taskkill", $"/F /PID {pid} /T", Application.dataPath, out stdout, out stderr);
                    }
                    return ok;
                }
                else
                {
                    // Try a graceful termination first, then escalate if the process is still alive.
                    // Note: `kill -15` can succeed (exit 0) even if the process takes time to exit,
                    // so we verify and only escalate when needed.
                    string killPath = "/bin/kill";
                    if (!File.Exists(killPath)) killPath = "kill";
                    ExecPath.TryRun(killPath, $"-15 {pid}", Application.dataPath, out stdout, out stderr);

                    // Wait briefly for graceful shutdown.
                    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(8);
                    while (DateTime.UtcNow < deadline)
                    {
                        if (!_processDetector.ProcessExists(pid))
                        {
                            return true;
                        }
                        System.Threading.Thread.Sleep(100);
                    }

                    // Escalate.
                    ExecPath.TryRun(killPath, $"-9 {pid}", Application.dataPath, out stdout, out stderr);
                    return !_processDetector.ProcessExists(pid);
                }
            }
            catch (Exception ex)
            {
                McpLog.Error($"Error killing process {pid}: {ex.Message}");
                return false;
            }
        }
    }
}
