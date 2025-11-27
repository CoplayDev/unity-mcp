using System;
using UnityEditor;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services.Transport;
using MCPForUnity.Editor.Services.Transport.Transports;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Ensures the legacy stdio bridge resumes after domain reloads, mirroring the HTTP handler.
    /// </summary>
    [InitializeOnLoad]
    internal static class StdioBridgeReloadHandler
    {
        static StdioBridgeReloadHandler()
        {
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
        }

        private static void OnBeforeAssemblyReload()
        {
            try
            {
                // Only persist resume intent when stdio is the active transport and the bridge is running.
                bool useHttp = EditorPrefs.GetBool(EditorPrefKeys.UseHttpTransport, true);
                bool isRunning = MCPServiceLocator.TransportManager.IsRunning(TransportMode.Stdio);
                if (!useHttp && isRunning)
                {
                    EditorPrefs.SetBool(EditorPrefKeys.ResumeStdioAfterReload, true);
                }
                else
                {
                    EditorPrefs.DeleteKey(EditorPrefKeys.ResumeStdioAfterReload);
                }

                if (!useHttp && isRunning)
                {
                    // Stop only the stdio bridge; leave HTTP untouched if it is running concurrently.
                    MCPServiceLocator.TransportManager.StopAsync(TransportMode.Stdio);
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Failed to persist stdio reload flag: {ex.Message}");
            }
        }

        private static void OnAfterAssemblyReload()
        {
            bool resume = false;
            try
            {
                resume = EditorPrefs.GetBool(EditorPrefKeys.ResumeStdioAfterReload, false);
                bool useHttp = EditorPrefs.GetBool(EditorPrefKeys.UseHttpTransport, true);
                resume = resume && !useHttp;
                if (resume)
                {
                    EditorPrefs.DeleteKey(EditorPrefKeys.ResumeStdioAfterReload);
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Failed to read stdio reload flag: {ex.Message}");
            }

            if (!resume)
            {
                return;
            }

            // Restart via TransportManager so state stays in sync; if it fails (port busy), rely on UI to retry.
            TryStartBridgeImmediate();
        }

        private static void TryStartBridgeImmediate()
        {
            try
            {
                MCPServiceLocator.TransportManager.StartAsync(TransportMode.Stdio);
                MCPForUnity.Editor.Windows.MCPForUnityEditorWindow.RequestHealthVerification();
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Failed to resume stdio bridge after reload: {ex.Message}");
            }
        }
    }
}
