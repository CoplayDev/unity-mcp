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

        /// <summary>Backstop on the wait for compilation to begin. Not the normal
        /// exit — RequestScriptCompilation always runs a pass, so the start edge
        /// arrives within a tick or two; this only bounds the wait if the pipeline
        /// never starts at all.</summary>
        private const int CompileStartGraceSeconds = 10;

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
            int compileCountBefore = EditorStateCache.CompileCount;

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

            // RequestScriptCompilation only queues; the pipeline starts on a later
            // editor tick. Sampling the state here therefore reported "idle" for a
            // compile that was about to run, and the caller's readiness poll — which
            // begins the moment this returns — saw a ready editor and returned
            // immediately, so wait_for_ready silently did nothing for exactly the call
            // it exists for (issue #814). Waiting for the start edge first makes
            // resulting_state, and every readiness decision downstream of it, truthful.
            //
            // Unlike WaitForUnityReadyAsync this cannot span a domain reload: it
            // resolves the moment compilation *starts*, long before assemblies swap.
            // That is why it is safe on Unity 6+ where waiting for readiness is not.
            if (compileRequested)
            {
                await WaitForCompilationToStartAsync(
                    compileCountBefore,
                    TimeSpan.FromSeconds(CompileStartGraceSeconds)).ConfigureAwait(true);
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

            return new SuccessResponse("Refresh requested.", new
            {
                refresh_triggered = refreshTriggered,
                compile_requested = compileRequested,
                resulting_state = resultingState,
                hint = shouldWaitForReady
                    ? "Unity refresh completed; editor should be ready."
                    : "If Unity enters compilation/domain reload, poll the mcpforunity://editor/state resource until data.advice.ready_for_tools is true."
            });
        }

        /// <summary>
        /// Resolves once a compilation is under way — or once it provably will not
        /// start. Three exits, none of them a fault, because "nothing needed
        /// compiling" is a normal outcome rather than a timeout:
        /// <list type="bullet">
        /// <item>the pipeline is running;</item>
        /// <item><see cref="EditorStateCache.CompileCount"/> moved past
        /// <paramref name="compileCountBefore"/> — a short compile can begin and end
        /// inside AssetDatabase.Refresh, before this is even armed, and the counter is
        /// the only thing that still sees it;</item>
        /// <item>the grace elapsed with neither.</item>
        /// </list>
        /// The first two are also tested synchronously on entry, so the case where a
        /// reload is already imminent never leaves this command queued as a
        /// continuation — see the note on the fast path below.
        /// </summary>
        private static Task WaitForCompilationToStartAsync(int compileCountBefore, TimeSpan grace)
        {
            // Synchronous fast path, and the reason it matters: the counter check is
            // there for a compile that began *and ended* inside AssetDatabase.Refresh
            // above, and in that state the domain reload is already imminent. Resolving
            // it from Tick would hand the rest of this command to the synchronization
            // context as a queued continuation, which the reload discards along with
            // the rest of the domain — losing the response. An already-completed task
            // resumes the await inline instead, so nothing is left queued.
            if (EditorStateCache.CompileCount != compileCountBefore
                || EditorStateCache.GetActualIsCompiling())
            {
                return Task.CompletedTask;
            }

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

                    if (EditorStateCache.GetActualIsCompiling()
                        || EditorStateCache.CompileCount != compileCountBefore)
                    {
                        EditorApplication.update -= Tick;
                        tcs.TrySetResult(true);
                        return;
                    }

                    if ((DateTime.UtcNow - start) > grace)
                    {
                        EditorApplication.update -= Tick;
                        tcs.TrySetResult(false);
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
