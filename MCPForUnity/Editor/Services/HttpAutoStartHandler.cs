using System;
using System.Threading.Tasks;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services.Transport;
using MCPForUnity.Editor.Windows;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Automatically starts the HTTP MCP bridge on editor load when the user has opted in
    /// via the "Auto-Start on Editor Load" toggle in Advanced Settings.
    /// This complements HttpBridgeReloadHandler (which only resumes after domain reloads).
    /// </summary>
    [InitializeOnLoad]
    internal static class HttpAutoStartHandler
    {
        private const string SessionInitKey = "HttpAutoStartHandler.SessionInitialized";

        static HttpAutoStartHandler()
        {
            // SessionState resets on editor process start but persists across domain reloads.
            // Only run once per session — let HttpBridgeReloadHandler handle reload-resume cases.
            if (SessionState.GetBool(SessionInitKey, false)) return;

            if (Application.isBatchMode &&
                string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("UNITY_MCP_ALLOW_BATCH")))
            {
                return;
            }

            // Only check lightweight EditorPrefs here — services like EditorConfigurationCache
            // and MCPServiceLocator may not be initialized yet on fresh editor launch.
            bool autoStartEnabled = EditorPrefs.GetBool(EditorPrefKeys.AutoStartOnLoad, false);
            if (!autoStartEnabled) return;

            SessionState.SetBool(SessionInitKey, true);

            // Delay to let the editor and services finish initialization.
            EditorApplication.delayCall += OnEditorReady;
        }

        private static void OnEditorReady()
        {
            try
            {
                bool autoStartEnabled = EditorPrefs.GetBool(EditorPrefKeys.AutoStartOnLoad, false);
                if (!autoStartEnabled) return;

                bool useHttp = EditorConfigurationCache.Instance.UseHttpTransport;
                if (!useHttp) return;

                // Don't auto-start if bridge is already running.
                if (MCPServiceLocator.TransportManager.IsRunning(TransportMode.Http)) return;

                _ = AutoStartAsync();
            }
            catch (Exception ex)
            {
                McpLog.Debug($"[HTTP Auto-Start] Deferred check failed: {ex.Message}");
            }
        }

        private static async Task AutoStartAsync()
        {
            try
            {
                bool isLocal = !HttpEndpointUtility.IsRemoteScope();

                if (isLocal)
                {
                    // For HTTP Local: launch the server process first, then connect the bridge.
                    // This mirrors what the UI "Start Server" button does.
                    if (!HttpEndpointUtility.IsHttpLocalUrlAllowedForLaunch(
                            HttpEndpointUtility.GetLocalBaseUrl(), out string policyError))
                    {
                        McpLog.Debug($"[HTTP Auto-Start] Local URL blocked by security policy: {policyError}");
                        return;
                    }

                    // Check if server is already reachable (e.g. user started it externally).
                    if (!MCPServiceLocator.Server.IsLocalHttpServerReachable())
                    {
                        bool serverStarted = MCPServiceLocator.Server.StartLocalHttpServer(quiet: true);
                        if (!serverStarted)
                        {
                            McpLog.Warn("[HTTP Auto-Start] Failed to start local HTTP server");
                            return;
                        }
                    }

                    // Wait for the server to become reachable, then connect.
                    await WaitForServerAndConnectAsync();
                }
                else
                {
                    // For HTTP Remote: server is external, just connect the bridge.
                    await ConnectBridgeAsync();
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"[HTTP Auto-Start] Failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Waits for the local HTTP server to accept connections, then connects the bridge.
        /// Mirrors TryAutoStartSessionAsync in McpConnectionSection: keep polling reachability while
        /// the launched process is alive; declare failure only when it exits without the port coming
        /// up, or a generous hard cap is reached.
        /// </summary>
        private static async Task WaitForServerAndConnectAsync()
        {
            var server = MCPServiceLocator.Server;
            string url = HttpEndpointUtility.GetLocalBaseUrl();
            var pollDelay = TimeSpan.FromMilliseconds(500);
            var hardCap = TimeSpan.FromMinutes(5);
            double startTime = EditorApplication.timeSinceStartup;

            while (true)
            {
                // Abort if user changed settings while we were waiting.
                if (!EditorPrefs.GetBool(EditorPrefKeys.AutoStartOnLoad, false)) return;
                if (!EditorConfigurationCache.Instance.UseHttpTransport) return;
                if (MCPServiceLocator.TransportManager.IsRunning(TransportMode.Http)) return;

                if (server.IsLocalHttpServerReachable())
                {
                    McpLog.Info($"Server ready on {url}");
                    bool started = await MCPServiceLocator.Bridge.StartAsync();
                    if (started)
                    {
                        McpLog.Info("Session connected");
                        MCPForUnityEditorWindow.RequestHealthVerification();
                        return;
                    }
                }

                bool processAlive = server.IsManagedServerLaunchProcessAlive();
                double elapsed = EditorApplication.timeSinceStartup - startTime;

                if ((!processAlive && elapsed > 1.0) || elapsed > hardCap.TotalSeconds)
                {
                    // Last-resort connect attempt in case reachability detection missed a live server.
                    if (await MCPServiceLocator.Bridge.StartAsync())
                    {
                        McpLog.Info("Session connected");
                        MCPForUnityEditorWindow.RequestHealthVerification();
                        return;
                    }

                    server.LogLocalHttpServerLaunchFailure();
                    return;
                }

                try { await Task.Delay(pollDelay); }
                catch { return; }
            }
        }

        /// <summary>
        /// Connects the bridge directly (for remote HTTP where the server is already running).
        /// </summary>
        private static async Task ConnectBridgeAsync()
        {
            string url = HttpEndpointUtility.GetRemoteBaseUrl();
            McpLog.Info($"Connecting to {url}…");
            bool started = await MCPServiceLocator.Bridge.StartAsync();
            if (started)
            {
                McpLog.Info("Connected");
                MCPForUnityEditorWindow.RequestHealthVerification();
            }
            else
            {
                McpLog.Warn("Connection failed: could not connect to remote HTTP server");
            }
        }
    }
}
