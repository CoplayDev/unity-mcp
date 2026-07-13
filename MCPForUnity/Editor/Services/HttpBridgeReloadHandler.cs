using System;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services.Transport;
using MCPForUnity.Editor.Windows;
using UnityEditor;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Ensures HTTP transports resume after domain reloads similar to the legacy stdio bridge.
    /// </summary>
    [InitializeOnLoad]
    internal static class HttpBridgeReloadHandler
    {
        // SessionState, not EditorPrefs: it survives domain reloads but dies with the editor
        // process and is per-editor-instance. EditorPrefs is per-user machine-global, so a
        // second open editor could consume or delete this editor's pending resume, and a
        // crash mid-compile would leave a stale flag that resurrects the bridge on the next
        // launch (#1229).
        internal const string ResumeSessionKey = "MCPForUnity.ResumeHttpAfterReload";

        private static readonly TimeSpan[] ResumeRetrySchedule =
        {
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(3),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(30)
        };

        static HttpBridgeReloadHandler()
        {
            // Migration: the flag lived in EditorPrefs before it moved to SessionState — the
            // key STRING is shared, so renaming ResumeSessionKey would silently break this
            // cleanup. Once per session, not per reload: the EditorPrefs key is machine-global,
            // and an older-version editor open concurrently still uses it for its own resume.
            // Safe to delete this block a few releases after v10.
            const string migratedKey = "MCPForUnity.ResumeHttpAfterReload.Migrated";
            if (!SessionState.GetBool(migratedKey, false))
            {
                EditorPrefs.DeleteKey(ResumeSessionKey);
                SessionState.SetBool(migratedKey, true);
            }

            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
        }

        internal static bool IsResumePending => SessionState.GetBool(ResumeSessionKey, false);

        /// <summary>
        /// Drops a pending reload-resume. Called when the user takes manual control of the
        /// bridge lifecycle (Connect, End Session, transport switch, orphan cleanup); the
        /// retry loop re-checks the flag per attempt, so this also aborts an in-flight loop.
        /// </summary>
        internal static void CancelPendingResume() => SessionState.EraseBool(ResumeSessionKey);

        private static void OnBeforeAssemblyReload()
        {
            try
            {
                OnBeforeAssemblyReloadCore(MCPServiceLocator.TransportManager);
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Failed to evaluate HTTP bridge reload state: {ex.Message}");
            }
        }

        internal static void OnBeforeAssemblyReloadCore(TransportManager transport)
        {
            if (transport.IsRunning(TransportMode.Http))
            {
                SessionState.SetBool(ResumeSessionKey, true);

                // beforeAssemblyReload is synchronous; force a synchronous teardown so we do not
                // leave an orphaned socket due to an unfinished async close handshake.
                transport.ForceStop(TransportMode.Http);
            }
            // When the bridge is not running, leave any pending flag alone: during a multi-pass
            // compile the next reload lands before the deferred resume ran, and deleting the
            // flag here is what used to lose the resume permanently (#1229). Explicit cancel
            // paths (End Session, transport switch, orphan cleanup) erase the flag instead.
        }

        private static void OnAfterAssemblyReload()
        {
            if (OnAfterAssemblyReloadCore())
            {
                EditorApplication.update += ResumeTick;
            }
        }

        /// <summary>
        /// Decision core, separated so EditMode tests can drive it. Returns true when a resume
        /// should be scheduled. Does not consume the flag — it survives until the resume
        /// succeeds, is cancelled, or exhausts its retries, so a further reload in the middle
        /// of any deferral re-enters here instead of losing the resume.
        /// </summary>
        internal static bool OnAfterAssemblyReloadCore()
        {
            try
            {
                if (!SessionState.GetBool(ResumeSessionKey, false)) return false;

                // Only resume HTTP if it is still the selected transport.
                if (!EditorConfigurationCache.Instance.UseHttpTransport)
                {
                    SessionState.EraseBool(ResumeSessionKey);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                // Transport-config read failed (services racing the reload boundary): schedule
                // the resume anyway rather than dropping it — the retry loop re-checks the
                // transport per attempt and erases the flag itself on cancel/switch/exhaustion,
                // so a stale flag cannot wedge the session.
                McpLog.Warn($"Failed to read HTTP bridge reload flag: {ex.Message}");
                return true;
            }
        }

        private static void ResumeTick()
        {
            if (IsEditorBusy()) return;
            EditorApplication.update -= ResumeTick;
            _ = ResumeHttpWithRetriesAsync();
        }

        /// <summary>
        /// Busy gate for the deferral ticks. Uses the #549-aware compiling check because raw
        /// EditorApplication.isCompiling stays true for a whole play session under the
        /// "Recompile After Finished Playing" preference, which would block resume until
        /// play mode exits.
        /// </summary>
        internal static bool IsEditorBusy()
            => EditorStateCache.GetActualIsCompiling() || EditorApplication.isUpdating;

        // scheduleOverride lets EditMode tests pass an all-zero schedule so the loop
        // completes synchronously (the test framework floor cannot run async tests).
        internal static async Task ResumeHttpWithRetriesAsync(TimeSpan[] scheduleOverride = null)
        {
            TimeSpan[] schedule = scheduleOverride ?? ResumeRetrySchedule;
            Exception lastException = null;

            for (int i = 0; i < schedule.Length; i++)
            {
                int attempt = i + 1;
                McpLog.Debug($"[HTTP Reload] Resume attempt {attempt}/{schedule.Length}");

                TimeSpan delay = schedule[i];
                if (delay > TimeSpan.Zero)
                {
                    McpLog.Debug($"[HTTP Reload] Waiting {delay.TotalSeconds:0.#}s before resume attempt {attempt}");
                    try { await Task.Delay(delay); }
                    catch { return; }
                }

                // The flag doubles as the cancel signal (see CancelPendingResume).
                if (!IsResumePending) return;

                try
                {
                    // Inside the attempt try: a service read racing the reload boundary must
                    // burn a retry, not kill this fire-and-forget task with the flag still set
                    // (which would leave nothing scheduled to consume it until the next reload).

                    // Abort retries if the user switched transports while we were waiting.
                    if (!EditorConfigurationCache.Instance.UseHttpTransport)
                    {
                        SessionState.EraseBool(ResumeSessionKey);
                        return;
                    }

                    // Never bounce a session someone else established while we were waiting
                    // (WebSocketTransportClient.StartAsync tears down a live connection first).
                    if (MCPServiceLocator.TransportManager.IsRunning(TransportMode.Http))
                    {
                        SessionState.EraseBool(ResumeSessionKey);
                        return;
                    }

                    bool started = await MCPServiceLocator.TransportManager.StartAsync(TransportMode.Http);
                    if (started)
                    {
                        SessionState.EraseBool(ResumeSessionKey);
                        McpLog.Debug($"[HTTP Reload] Resume succeeded on attempt {attempt}");
                        MCPForUnityEditorWindow.RequestHealthVerification();
                        return;
                    }

                    var state = MCPServiceLocator.TransportManager.GetState(TransportMode.Http);
                    string reason = string.IsNullOrWhiteSpace(state?.Error) ? "no error detail" : state.Error;
                    McpLog.Debug($"[HTTP Reload] Resume attempt {attempt} failed: {reason}");
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    McpLog.Debug($"[HTTP Reload] Resume attempt {attempt} threw: {ex.Message}");
                }
            }

            // Exhausted: erase the flag so later reload boundaries don't replay this failure
            // loop for the rest of the session. This cannot swallow a multi-pass resume — a
            // reload mid-loop kills the task before this line, leaving the flag set.
            SessionState.EraseBool(ResumeSessionKey);

            if (lastException != null)
            {
                McpLog.Warn($"Failed to resume HTTP MCP bridge after domain reload: {lastException.Message}");
            }
            else
            {
                McpLog.Warn("Failed to resume HTTP MCP bridge after domain reload");
            }
        }
    }
}
