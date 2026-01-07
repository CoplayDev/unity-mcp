using System;
using System.Threading;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Compilation;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Explicitly refreshes Unity's asset database and optionally requests a script compilation.
    /// This is side-effectful and should be treated as a tool.
    /// </summary>
    [McpForUnityTool("refresh_unity", AutoRegister = false)]
    public static class RefreshUnity
    {
        private const int DefaultWaitTimeoutSeconds = 60;

        public static async Task<object> HandleCommand(JObject @params)
        {
            string mode = @params?["mode"]?.ToString() ?? "if_dirty";
            string scope = @params?["scope"]?.ToString() ?? "all";
            string compile = @params?["compile"]?.ToString() ?? "none";
            bool waitForReady = false;

            try
            {
                var waitToken = @params?["wait_for_ready"];
                if (waitToken != null && bool.TryParse(waitToken.ToString(), out var parsed))
                {
                    waitForReady = parsed;
                }
            }
            catch
            {
                // ignore parse failures
            }

            if (TestRunStatus.IsRunning)
            {
                return new ErrorResponse("tests_running", new
                {
                    reason = "tests_running",
                    retry_after_ms = 5000
                });
            }

            bool refreshTriggered = false;
            bool compileRequested = false;

            try
            {
                // Best-effort semantics: if_dirty currently behaves like force unless future dirty signals are added.
                bool shouldRefresh = string.Equals(mode, "force", StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(mode, "if_dirty", StringComparison.OrdinalIgnoreCase);

                if (shouldRefresh)
                {
                    if (string.Equals(scope, "scripts", StringComparison.OrdinalIgnoreCase))
                    {
                        // For scripts, requesting compilation is usually the meaningful action.
                        // We avoid a heavyweight full refresh by default.
                    }
                    else
                    {
                        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                        refreshTriggered = true;
                    }
                }

                if (string.Equals(compile, "request", StringComparison.OrdinalIgnoreCase))
                {
                    CompilationPipeline.RequestScriptCompilation();
                    compileRequested = true;
                }

                if (string.Equals(scope, "all", StringComparison.OrdinalIgnoreCase) && !refreshTriggered)
                {
                    // If the caller asked for "all" and we skipped refresh above (e.g., scripts-only path),
                    // do a lightweight refresh now. Use ForceSynchronousImport to ensure the refresh
                    // completes before returning, preventing stalls when Unity is backgrounded.
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    refreshTriggered = true;
                }
            }
            catch (Exception ex)
            {
                return new ErrorResponse($"refresh_failed: {ex.Message}");
            }

            if (waitForReady)
            {
                try
                {
                    await WaitForUnityReadyAsync(TimeSpan.FromSeconds(DefaultWaitTimeoutSeconds)).ConfigureAwait(true);
                }
                catch (TimeoutException)
                {
                    return new ErrorResponse("refresh_timeout_waiting_for_ready", new
                    {
                        refresh_triggered = refreshTriggered,
                        compile_requested = compileRequested,
                        resulting_state = "unknown",
                    });
                }
                catch (Exception ex)
                {
                    return new ErrorResponse($"refresh_wait_failed: {ex.Message}");
                }
            }

            string resultingState = EditorApplication.isCompiling
                ? "compiling"
                : (EditorApplication.isUpdating ? "asset_import" : "idle");

            return new SuccessResponse("Refresh requested.", new
            {
                refresh_triggered = refreshTriggered,
                compile_requested = compileRequested,
                resulting_state = resultingState,
                hint = waitForReady
                    ? "Unity refresh completed; editor should be ready."
                    : "If Unity enters compilation/domain reload, poll editor_state until ready_for_tools is true."
            });
        }

        private static Task WaitForUnityReadyAsync(TimeSpan timeout)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var start = DateTime.UtcNow;

            void Tick()
            {
                try
                {
                    if (tcs.Task.IsCompleted)
                    {
                        EditorApplication.update -= Tick;
                        return;
                    }

                    if ((DateTime.UtcNow - start) > timeout)
                    {
                        EditorApplication.update -= Tick;
                        tcs.TrySetException(new TimeoutException());
                        return;
                    }

                    if (!EditorApplication.isCompiling
                        && !EditorApplication.isUpdating
                        && !TestRunStatus.IsRunning
                        && !EditorApplication.isPlayingOrWillChangePlaymode)
                    {
                        EditorApplication.update -= Tick;
                        tcs.TrySetResult(true);
                    }
                }
                catch (Exception ex)
                {
                    EditorApplication.update -= Tick;
                    tcs.TrySetException(ex);
                }
            }

            EditorApplication.update += Tick;
            // Nudge Unity to pump once in case update is throttled.
            try { EditorApplication.QueuePlayerLoopUpdate(); } catch { }
            return tcs.Task;
        }
    }
}
