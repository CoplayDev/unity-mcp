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
            bool waitForReady = ParamCoercion.CoerceBool(@params?["wait_for_ready"], false);

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

            // An open scene whose file changed on disk makes Unity raise a modal "Scene(s) Have Been
            // Modified" prompt during the refresh, stalling the Editor until a human answers it.
            // Reconcile disk and memory first so the prompt never has a reason to appear.
            SceneExternalChangeGuard.Outcome sceneOutcome;
            try
            {
                sceneOutcome = SceneExternalChangeGuard.Reconcile(
                    @params?["on_external_scene_change"]?.ToString());
            }
            catch (Exception ex)
            {
                return new ErrorResponse($"scene_reconcile_failed: {ex.Message}");
            }

            if (sceneOutcome.Blocked)
            {
                return new ErrorResponse(sceneOutcome.Error, new
                {
                    reason = "open_scene_changed_on_disk",
                    changed_scenes = sceneOutcome.ChangedScenes,
                    refresh_triggered = false,
                });
            }

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

            // Unity 6+ fix: Skip wait_for_ready when compile was requested.
            // The EditorApplication.update polling in WaitForUnityReadyAsync doesn't survive
            // domain reloads properly in Unity 6+, causing infinite compilation loops.
            // When compilation is requested, return immediately and let client poll editor_state.
            // Earlier Unity versions retain the original behavior.
#if UNITY_6000_0_OR_NEWER
            bool shouldWaitForReady = waitForReady && !compileRequested;
#else
            bool shouldWaitForReady = waitForReady;
#endif
            if (shouldWaitForReady)
            {
                try
                {
                    await WaitForUnityReadyAsync(
                        TimeSpan.FromSeconds(DefaultWaitTimeoutSeconds)).ConfigureAwait(true);
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

            string resultingState = EditorStateCache.GetActualIsCompiling()
                ? "compiling"
                : (EditorApplication.isUpdating ? "asset_import" : "idle");

            // Disk and memory are in sync again; make that the new known-good baseline so the next
            // refresh only reacts to genuinely new external edits.
            SceneExternalChangeGuard.RecordBaseline();

            return new SuccessResponse("Refresh requested.", new
            {
                refresh_triggered = refreshTriggered,
                compile_requested = compileRequested,
                resulting_state = resultingState,
                reloaded_scenes = sceneOutcome.ReloadedScenes,
                overwritten_scenes = sceneOutcome.OverwrittenScenes,
                hint = shouldWaitForReady
                    ? "Unity refresh completed; editor should be ready."
                    : "If Unity enters compilation/domain reload, poll the mcpforunity://editor/state resource until data.advice.ready_for_tools is true."
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

                    if (!EditorStateCache.GetActualIsCompiling()
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
