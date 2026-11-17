using System;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services.Transport;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Best-effort cleanup when the Unity Editor is quitting.
    /// - Stops active transports so clients don't see a "hung" session longer than necessary.
    /// - Stops the local HTTP server this editor process launched, but only when no other Unity
    ///   instance is still connected to it (last one out turns the lights off). A headless server
    ///   has no terminal window, so an unstopped one would be an invisible orphan; a stopped one
    ///   that other editors still use disconnects all of them. This runs on quit only, never on
    ///   domain reload.
    /// </summary>
    [InitializeOnLoad]
    internal static class McpEditorShutdownCleanup
    {
        // Upper bound for asking the server how many Unity instances are still connected. The quit
        // handler already waits up to 750 ms on transport stops; keep the whole thing near a second.
        internal const int InstanceProbeTimeoutMs = 500;

        static McpEditorShutdownCleanup()
        {
            // Guard against duplicate subscriptions across domain reloads.
            try { EditorApplication.quitting -= OnEditorQuitting; } catch { }
            EditorApplication.quitting += OnEditorQuitting;
        }

        // A -batchmode/CI instance never auto-starts the server (HttpAutoStartHandler has the same
        // guard), so it has nothing of its own to stop. Mirror the sibling guards (HttpAutoStartHandler,
        // StdioBridgeHost): skip in batch unless opted in.
        internal static bool ShouldRunCleanup() =>
            ShouldRunCleanup(Application.isBatchMode, Environment.GetEnvironmentVariable("UNITY_MCP_ALLOW_BATCH"));

        internal static bool ShouldRunCleanup(bool isBatchMode, string allowBatchEnv) =>
            !isBatchMode || !string.IsNullOrWhiteSpace(allowBatchEnv);

        /// <summary>
        /// Last-one-out decision for a server this editor process launched. <paramref name="otherConnectedInstances"/>
        /// is null when the server did not answer the instance probe in time; that fails toward leaving
        /// it running, because killing a server other editors depend on is worse than a stray process.
        /// </summary>
        internal static bool ShouldStopManagedServer(int? otherConnectedInstances) =>
            otherConnectedInstances == 0;

        private static void OnEditorQuitting()
        {
            if (!ShouldRunCleanup()) return;

            // 1) Stop transports (best-effort, bounded wait).
            try
            {
                var transport = MCPServiceLocator.TransportManager;

                Task stopHttp = transport.StopAsync(TransportMode.Http);
                Task stopStdio = transport.StopAsync(TransportMode.Stdio);

                try { Task.WaitAll(new[] { stopHttp, stopStdio }, 750); } catch { }
            }
            catch (Exception ex)
            {
                // Avoid hard failures on quit.
                McpLog.Warn($"Shutdown cleanup: failed to stop transports: {ex.Message}");
            }

            // 2) Stop the local HTTP server this editor process launched (best-effort).
            // The launch marker lives in SessionState, which is per editor process, so a server launched
            // by another editor on this machine (or started externally) is never resolved here. Even for
            // our own launch, other editors may share it: ask the server first and only stop it when we
            // are the last Unity instance connected.
            try
            {
                var server = MCPServiceLocator.Server;
                if (!server.TryGetLaunchedLocalHttpServerPort(out int port))
                {
                    return;
                }

                int? otherInstances = server.TryCountOtherConnectedUnityInstances(port, InstanceProbeTimeoutMs, out int count)
                    ? count
                    : (int?)null;

                if (!ShouldStopManagedServer(otherInstances))
                {
                    McpLog.Debug(otherInstances.HasValue
                        ? $"Shutdown cleanup: leaving local HTTP server on port {port} running; {otherInstances.Value} other Unity instance(s) still connected."
                        : $"Shutdown cleanup: leaving local HTTP server on port {port} running; it did not report connected instances within {InstanceProbeTimeoutMs} ms.");
                    return;
                }

                server.StopManagedLocalHttpServer();
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Shutdown cleanup: failed to stop local HTTP server: {ex.Message}");
            }
        }
    }
}
