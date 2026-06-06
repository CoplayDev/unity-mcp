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
                    string launchBaseUrl = HttpEndpointUtility.GetLocalServerLaunchBaseUrl();
                    string policyError;
                    bool launchAllowed = HttpEndpointUtility.IsLanScope()
                        ? HttpEndpointUtility.IsHttpLanUrlAllowedForLaunch(launchBaseUrl, out policyError)
                        : HttpEndpointUtility.IsHttpLocalUrlAllowedForLaunch(launchBaseUrl, out policyError);
                    if (!launchAllowed)
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
        /// Mirrors TryAutoStartSessionAsync in McpConnectionSection.
        /// </summary>
        private static async Task WaitForServerAndConnectAsync()
        {
            const int maxAttempts = 30;
            var shortDelay = TimeSpan.FromMilliseconds(500);
            var longDelay = TimeSpan.FromSeconds(3);

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                // Abort if user changed settings while we were waiting.
                if (!EditorPrefs.GetBool(EditorPrefKeys.AutoStartOnLoad, false)) return;
                if (!EditorConfigurationCache.Instance.UseHttpTransport) return;
                if (MCPServiceLocator.TransportManager.IsRunning(TransportMode.Http)) return;

                bool reachable = MCPServiceLocator.Server.IsLocalHttpServerReachable();

                if (reachable)
                {
                    bool started = await MCPServiceLocator.Bridge.StartAsync();
                    if (started)
                    {
                        McpLog.Info("[HTTP Auto-Start] Bridge started successfully");
                        MCPForUnityEditorWindow.RequestHealthVerification();
                        return;
                    }
                }
                else if (attempt >= 20 && (attempt - 20) % 3 == 0)
                {
                    // Last-resort: try connecting even if not detected (process detection may fail).
                    bool started = await MCPServiceLocator.Bridge.StartAsync();
                    if (started)
                    {
                        McpLog.Info("[HTTP Auto-Start] Bridge started successfully (late connect)");
                        MCPForUnityEditorWindow.RequestHealthVerification();
                        return;
                    }
                }

                var delay = attempt < 6 ? shortDelay : longDelay;
                try { await Task.Delay(delay); }
                catch { return; }
            }

            McpLog.Warn("[HTTP Auto-Start] Server did not become reachable after launch");
        }

        /// <summary>
        /// Connects the bridge directly (for remote HTTP where the server is already running).
        /// </summary>
        private static async Task ConnectBridgeAsync()
        {
            bool started = await MCPServiceLocator.Bridge.StartAsync();
            if (started)
            {
                McpLog.Info("[HTTP Auto-Start] Bridge started successfully (remote)");
                MCPForUnityEditorWindow.RequestHealthVerification();
            }
            else
            {
                McpLog.Warn("[HTTP Auto-Start] Failed to connect to remote HTTP server");
            }
        }
    }
}
