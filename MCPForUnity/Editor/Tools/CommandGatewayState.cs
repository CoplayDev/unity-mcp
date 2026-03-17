using System.Threading;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using UnityEditor;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Singleton state for the command gateway queue.
    /// Hooks into EditorApplication.update for tick processing.
    /// Persists queue state across domain reloads via SessionState.
    /// </summary>
    [InitializeOnLoad]
    public static class CommandGatewayState
    {
        const string SessionKey = "MCPForUnity.GatewayQueueV1";

        public static readonly CommandQueue Queue = new();

        static CommandGatewayState()
        {
            // Restore queue state from previous domain reload
            string json = SessionState.GetString(SessionKey, "");
            if (!string.IsNullOrEmpty(json))
                Queue.RestoreFromJson(json);

            Queue.IsEditorBusy = () =>
                TestJobManager.HasRunningJob
                || TestRunStatus.IsRunning
                || EditorApplication.isCompiling;

            // Persist before next domain reload
            AssemblyReloadEvents.beforeAssemblyReload += () =>
                SessionState.SetString(SessionKey, Queue.PersistToJson());

            EditorApplication.update += OnUpdate;
        }

        static void OnUpdate()
        {
            Queue.ProcessTick(async (tool, @params, ct) =>
                await CommandRegistry.InvokeCommandAsync(tool, @params, ct));
        }

        /// <summary>
        /// Emergency flush: cancel all queued and running jobs, clear stuck test jobs,
        /// and reset the queue. Safe to call from menu items or keyboard shortcuts.
        /// </summary>
        public static void EmergencyFlush()
        {
            int flushed = Queue.FlushAll();

            // Also clear any stuck test job that may be holding IsEditorBusy
            if (TestJobManager.HasRunningJob)
                TestJobManager.ClearStuckJob();

            McpLog.Warn($"[EmergencyFlush] Flushed {flushed} job(s), cleared stuck test state.");
        }

        /// <summary>
        /// Returns a Task that completes when the given BatchJob leaves Queued/Running state.
        /// Uses EditorApplication.update polling — no spin-wait or sleep.
        /// For single-command jobs, returns the first result. For multi-command, returns all results.
        /// </summary>
        public static Task<object> AwaitJob(BatchJob job)
        {
            // Already finished — return immediately
            switch (job.Status)
            {
                case JobStatus.Done:
                    return Task.FromResult(job.Results.Count > 0 ? job.Results[0] : null);
                case JobStatus.Failed:
                    return Task.FromResult<object>(new ErrorResponse(job.Error ?? "Job failed"));
                case JobStatus.Cancelled:
                    return Task.FromResult<object>(new ErrorResponse("Job cancelled"));
            }

            var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Check()
            {
                if (job.Status == JobStatus.Queued || job.Status == JobStatus.Running)
                    return;

                EditorApplication.update -= Check;

                switch (job.Status)
                {
                    case JobStatus.Done:
                        tcs.TrySetResult(job.Results.Count > 0 ? job.Results[0] : null);
                        break;
                    case JobStatus.Cancelled:
                        tcs.TrySetResult(new ErrorResponse("Job cancelled"));
                        break;
                    default: // Failed
                        tcs.TrySetResult(new ErrorResponse(job.Error ?? "Job failed"));
                        break;
                }
            }

            EditorApplication.update += Check;
            return tcs.Task;
        }
    }
}
