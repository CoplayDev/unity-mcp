using System;
using System.Threading;
using System.Threading.Tasks;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services.Transport;
using MCPForUnity.Editor.Services.Transport.Transports;
using MCPForUnity.Editor.Windows;
using UnityEditor;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Ensures the legacy stdio bridge resumes after domain reloads, mirroring the HTTP handler.
    /// </summary>
    [InitializeOnLoad]
    internal static class StdioBridgeReloadHandler
    {
        private static readonly TimeSpan[] ResumeRetrySchedule =
        {
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(3),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(30)
        };

        private static CancellationTokenSource _retryCts;

        static StdioBridgeReloadHandler()
        {
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
            EditorApplication.quitting += CancelRetries;
        }

        private static void CancelRetries()
        {
            try { _retryCts?.Cancel(); } catch { }
        }

        private static void OnBeforeAssemblyReload()
        {
            // Cancel any in-flight retry loop before the next reload.
            CancelRetries();

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
                }
                else
                {
                    EditorPrefs.DeleteKey(EditorPrefKeys.ResumeStdioAfterReload);
                }

                if (isRunning)
                {
                    // Stop only stdio before reload. This is centralized here so resume-flag updates
                    // and teardown cannot race each other via separate beforeAssemblyReload handlers.
                    var stopTask = MCPServiceLocator.TransportManager.StopAsync(TransportMode.Stdio);
                    try { stopTask.Wait(500); } catch { }

                    // Legacy safety: stdio may have been started outside TransportManager state.
                    try { StdioBridgeHost.Stop(); } catch { }
                }

                if (shouldResume)
                {
                    // Write reloading status so clients don't think we vanished.
                    StdioBridgeHost.WriteHeartbeat(true, "reloading");
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

            // If the editor is not compiling, attempt an immediate restart without relying on editor focus.
            bool isCompiling = EditorApplication.isCompiling;
            try
            {
                var pipeline = Type.GetType("UnityEditor.Compilation.CompilationPipeline, UnityEditor");
                var prop = pipeline?.GetProperty("isCompiling", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (prop != null) isCompiling |= (bool)prop.GetValue(null);
            }
            catch { }

            if (!isCompiling)
            {
                _ = ResumeStdioWithRetriesAsync();
                return;
            }

            // Fallback when compiling: schedule on the editor loop
            EditorApplication.delayCall += () =>
            {
                _ = ResumeStdioWithRetriesAsync();
            };
        }

        private static async Task ResumeStdioWithRetriesAsync()
        {
            // Cancel any previous retry loop and create a fresh token.
            CancelRetries();
            var cts = _retryCts = new CancellationTokenSource();
            var token = cts.Token;

            Exception lastException = null;

            for (int i = 0; i < ResumeRetrySchedule.Length; i++)
            {
                if (token.IsCancellationRequested) return;

                int attempt = i + 1;
                McpLog.Debug($"[Stdio Reload] Resume attempt {attempt}/{ResumeRetrySchedule.Length}");

                TimeSpan delay = ResumeRetrySchedule[i];
                if (delay > TimeSpan.Zero)
                {
                    McpLog.Debug($"[Stdio Reload] Waiting {delay.TotalSeconds:0.#}s before resume attempt {attempt}");
                    try { await Task.Delay(delay, token); }
                    catch (OperationCanceledException) { return; }
                }

                // Abort retries if the user switched transports while we were waiting.
                if (EditorConfigurationCache.Instance.UseHttpTransport)
                {
                    try { EditorPrefs.DeleteKey(EditorPrefKeys.ResumeStdioAfterReload); } catch { }
                    return;
                }

                try
                {
                    bool started = await MCPServiceLocator.TransportManager.StartAsync(TransportMode.Stdio);
                    if (started)
                    {
                        McpLog.Debug($"[Stdio Reload] Resume succeeded on attempt {attempt}");
                        try { EditorPrefs.DeleteKey(EditorPrefKeys.ResumeStdioAfterReload); } catch { }
                        MCPForUnityEditorWindow.RequestHealthVerification();
                        return;
                    }

                    var state = MCPServiceLocator.TransportManager.GetState(TransportMode.Stdio);
                    string reason = string.IsNullOrWhiteSpace(state?.Error) ? "no error detail" : state.Error;
                    McpLog.Debug($"[Stdio Reload] Resume attempt {attempt} failed: {reason}");
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    McpLog.Debug($"[Stdio Reload] Resume attempt {attempt} threw: {ex.Message}");
                }
            }

            try { EditorPrefs.DeleteKey(EditorPrefKeys.ResumeStdioAfterReload); } catch { }

            if (lastException != null)
            {
                McpLog.Warn($"Failed to resume stdio bridge after domain reload: {lastException.Message}");
            }
            else
            {
                McpLog.Warn("Failed to resume stdio bridge after domain reload");
            }
        }
    }
}
