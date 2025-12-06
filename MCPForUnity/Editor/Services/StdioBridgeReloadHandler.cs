using System;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using UnityEditor;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Ensures the stdio bridge resumes after domain reloads.
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
                // Persist resume intent when the bridge is running
                bool isRunning = MCPServiceLocator.TransportManager.IsRunning();
                if (isRunning)
                {
                    EditorPrefs.SetBool(EditorPrefKeys.ResumeStdioAfterReload, true);

                    var stopTask = MCPServiceLocator.TransportManager.StopAsync();
                    stopTask.ContinueWith(t =>
                    {
                        if (t.IsFaulted && t.Exception != null)
                        {
                            McpLog.Warn($"Error stopping stdio bridge before reload: {t.Exception.GetBaseException()?.Message}");
                        }
                    }, System.Threading.Tasks.TaskScheduler.Default);
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
                resume = EditorPrefs.GetBool(EditorPrefKeys.ResumeStdioAfterReload, false);
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
            var startTask = MCPServiceLocator.TransportManager.StartAsync();
            startTask.ContinueWith(t =>
            {
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
            }, System.Threading.Tasks.TaskScheduler.Default);
        }
    }
}
