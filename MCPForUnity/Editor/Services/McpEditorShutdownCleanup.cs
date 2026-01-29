using System;
using System.Threading.Tasks;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services.Transport;
using UnityEditor;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Best-effort cleanup when the Unity Editor is quitting.
    /// - Stops active transports so clients don't see a "hung" session longer than necessary.
    /// - If HTTP Local is selected, attempts to stop the local HTTP server (guarded by PID heuristics).
    /// </summary>
    [InitializeOnLoad]
    internal static class McpEditorShutdownCleanup
    {
        static McpEditorShutdownCleanup()
        {
            // Guard against duplicate subscriptions across domain reloads.
            try { EditorApplication.quitting -= OnEditorQuitting; } catch { }
            EditorApplication.quitting += OnEditorQuitting;
        }

        private static void OnEditorQuitting()
        {
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

            // 2) Stop local HTTP server if it was Unity-managed (best-effort).
            try
            {
                bool useHttp = EditorConfigurationCache.Instance.UseHttpTransport;
                string scope = string.Empty;
                try { scope = EditorPrefs.GetString(EditorPrefKeys.HttpTransportScope, string.Empty); } catch { }

                bool stopped = false;
                bool httpLocalSelected =
                    useHttp &&
                    (string.Equals(scope, "local", StringComparison.OrdinalIgnoreCase)
                     || (string.IsNullOrEmpty(scope) && MCPServiceLocator.Server.IsLocalUrl()));

                if (httpLocalSelected)
                {
                    // StopLocalHttpServer is already guarded to only terminate processes that look like mcp-for-unity.
                    // If it refuses to stop (e.g. URL was edited away from local), fall back to the Unity-managed stop.
                    stopped = MCPServiceLocator.Server.StopLocalHttpServer();
                }

                // Always attempt to stop a Unity-managed server if one exists.
                // This covers cases where the user switched transports (e.g. to stdio) or StopLocalHttpServer refused.
                if (!stopped)
                {
                    MCPServiceLocator.Server.StopManagedLocalHttpServer();
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Shutdown cleanup: failed to stop local HTTP server: {ex.Message}");
            }
        }
    }
}

