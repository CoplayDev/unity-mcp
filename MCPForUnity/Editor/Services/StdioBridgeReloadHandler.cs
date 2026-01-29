using System;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services.Transport;
using MCPForUnity.Editor.Services.Transport.Transports;
using UnityEditor;

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
                bool useHttp = EditorConfigurationCache.Instance.UseHttpTransport;
                // Check both TransportManager AND StdioBridgeHost directly, because CI starts via StdioBridgeHost
                // bypassing TransportManager state.
                bool tmRunning = MCPServiceLocator.TransportManager.IsRunning(TransportMode.Stdio);
                bool hostRunning = StdioBridgeHost.IsRunning;
                bool isRunning = tmRunning || hostRunning;
                bool shouldResume = !useHttp && isRunning;

                if (shouldResume)
                {
                    EditorPrefs.SetBool(EditorPrefKeys.ResumeStdioAfterReload, true);

                    // Stop only the stdio bridge; leave HTTP untouched if it is running concurrently.
                    var stopTask = MCPServiceLocator.TransportManager.StopAsync(TransportMode.Stdio);
                    
                    // Wait for stop to complete (which deletes the status file)
                    try { stopTask.Wait(500); } catch { }

                    // Write reloading status so clients don't think we vanished
                    StdioBridgeHost.WriteHeartbeat(true, "reloading");
                }
                else
                {
                    EditorPrefs.DeleteKey(EditorPrefKeys.ResumeStdioAfterReload);
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
                bool resumeFlag = EditorPrefs.GetBool(EditorPrefKeys.ResumeStdioAfterReload, false);
                bool useHttp = EditorConfigurationCache.Instance.UseHttpTransport;
                resume = resumeFlag && !useHttp;

                // If we're not going to resume, clear the flag immediately to avoid stuck "Resuming..." state
                if (!resume)
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
            var startTask = MCPServiceLocator.TransportManager.StartAsync(TransportMode.Stdio);
            startTask.ContinueWith(t =>
            {
                // Clear the flag after attempting to start (success or failure).
                // This prevents getting stuck in "Resuming..." state.
                // We do this synchronously on the continuation thread - it's safe because
                // EditorPrefs operations are thread-safe and any new reload will set the flag
                // fresh in OnBeforeAssemblyReload before we get here.
                try { EditorPrefs.DeleteKey(EditorPrefKeys.ResumeStdioAfterReload); } catch { }

                if (t.IsFaulted)
                {
                    var baseEx = t.Exception?.GetBaseException();
                    McpLog.Warn($"Failed to resume stdio bridge after reload: {baseEx?.Message}");
                    return;
                }
                if (!t.Result)
                {
                    McpLog.Warn("Failed to resume stdio bridge after domain reload");
                    return;
                }

                MCPForUnity.Editor.Windows.MCPForUnityEditorWindow.RequestHealthVerification();
            }, System.Threading.Tasks.TaskScheduler.Default);
        }
    }
}
