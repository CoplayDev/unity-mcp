using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditorInternal;
using UnityEditor.TestTools.TestRunner.Api;

namespace MCPForUnity.Editor.Services
{
    internal enum TestJobStatus
    {
        Running,
        Succeeded,
        Failed
    }

    internal sealed class TestJobFailure
    {
        public string FullName { get; set; }
        public string Message { get; set; }
    }

    internal sealed class TestJob
    {
        public string JobId { get; set; }
        public TestJobStatus Status { get; set; }
        public string Mode { get; set; }
        public long StartedUnixMs { get; set; }
        public long? FinishedUnixMs { get; set; }
        public long LastUpdateUnixMs { get; set; }
        public int? TotalTests { get; set; }
        public int CompletedTests { get; set; }
        public string CurrentTestFullName { get; set; }
        public long? CurrentTestStartedUnixMs { get; set; }
        public string LastFinishedTestFullName { get; set; }
        public long? LastFinishedUnixMs { get; set; }
        public List<TestJobFailure> FailuresSoFar { get; set; }
        public string Error { get; set; }
        public TestRunResult Result { get; set; }
    }

    /// <summary>
    /// Tracks async test jobs started via MCP tools. This is not intended to capture manual Test Runner UI runs.
    /// </summary>
    internal static class TestJobManager
    {
        // Keep this small to avoid ballooning payloads during polling.
        private const int FailureCap = 25;
        private const long StuckThresholdMs = 60_000;
        private const long InitializationTimeoutMs = 15_000; // 15 seconds to call OnRunStarted, else fail
        private const int MaxJobsToKeep = 10;
        private const long MinPersistIntervalMs = 1000; // Throttle persistence to reduce overhead

        // SessionState survives domain reloads within the same Unity Editor session.
        private const string SessionKeyJobs = "MCPForUnity.TestJobsV1";
        private const string SessionKeyCurrentJobId = "MCPForUnity.CurrentTestJobIdV1";

        private static readonly object LockObj = new();
        private static readonly Dictionary<string, TestJob> Jobs = new();
        private static string _currentJobId;
        private static long _lastPersistUnixMs;

        static TestJobManager()
        {
            // Restore after domain reloads (e.g., compilation while a job is running).
            TryRestoreFromSessionState();
        }

        public static string CurrentJobId
        {
            get { lock (LockObj) return _currentJobId; }
        }

        public static bool HasRunningJob
        {
            get
            {
                lock (LockObj)
                {
                    return !string.IsNullOrEmpty(_currentJobId);
                }
            }
        }

        /// <summary>
        /// Force-clears any stuck or orphaned test job. Call this when tests get stuck due to
        /// assembly reloads or other interruptions.
        /// </summary>
        /// <returns>True if a job was cleared, false if no running job exists.</returns>
        public static bool ClearStuckJob()
        {
            bool cleared = false;
            lock (LockObj)
            {
                if (string.IsNullOrEmpty(_currentJobId))
                {
                    return false;
                }

                if (Jobs.TryGetValue(_currentJobId, out var job) && job.Status == TestJobStatus.Running)
                {
                    long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    job.Status = TestJobStatus.Failed;
                    job.Error = "Job cleared manually (stuck or orphaned)";
                    job.FinishedUnixMs = now;
                    job.LastUpdateUnixMs = now;
                    McpLog.Warn($"[TestJobManager] Manually cleared stuck job {_currentJobId}");
                    cleared = true;
                }

                _currentJobId = null;
            }
            PersistToSessionState(force: true);
            return cleared;
        }

        private sealed class PersistedState
        {
            public string current_job_id { get; set; }
            public List<PersistedJob> jobs { get; set; }
        }

        private sealed class PersistedJob
        {
            public string job_id { get; set; }
            public string status { get; set; }
            public string mode { get; set; }
            public long started_unix_ms { get; set; }
            public long? finished_unix_ms { get; set; }
            public long last_update_unix_ms { get; set; }
            public int? total_tests { get; set; }
            public int completed_tests { get; set; }
            public string current_test_full_name { get; set; }
            public long? current_test_started_unix_ms { get; set; }
            public string last_finished_test_full_name { get; set; }
            public long? last_finished_unix_ms { get; set; }
            public List<TestJobFailure> failures_so_far { get; set; }
            public string error { get; set; }
        }

        private static TestJobStatus ParseStatus(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return TestJobStatus.Running;
            }

            string s = status.Trim().ToLowerInvariant();
            return s switch
            {
                "succeeded" => TestJobStatus.Succeeded,
                "failed" => TestJobStatus.Failed,
                _ => TestJobStatus.Running
            };
        }

        private static void TryRestoreFromSessionState()
        {
            try
            {
                string json = SessionState.GetString(SessionKeyJobs, string.Empty);
                if (string.IsNullOrWhiteSpace(json))
                {
                    var legacy = SessionState.GetString(SessionKeyCurrentJobId, string.Empty);
                    _currentJobId = string.IsNullOrWhiteSpace(legacy) ? null : legacy;
                    return;
                }

                var state = JsonConvert.DeserializeObject<PersistedState>(json);
                if (state?.jobs == null)
                {
                    return;
                }

                lock (LockObj)
                {
                    Jobs.Clear();
                    foreach (var pj in state.jobs)
                    {
                        if (pj == null || string.IsNullOrWhiteSpace(pj.job_id))
                        {
                            continue;
                        }

                        Jobs[pj.job_id] = new TestJob
                        {
                            JobId = pj.job_id,
                            Status = ParseStatus(pj.status),
                            Mode = pj.mode,
                            StartedUnixMs = pj.started_unix_ms,
                            FinishedUnixMs = pj.finished_unix_ms,
                            LastUpdateUnixMs = pj.last_update_unix_ms,
                            TotalTests = pj.total_tests,
                            CompletedTests = pj.completed_tests,
                            CurrentTestFullName = pj.current_test_full_name,
                            CurrentTestStartedUnixMs = pj.current_test_started_unix_ms,
                            LastFinishedTestFullName = pj.last_finished_test_full_name,
                            LastFinishedUnixMs = pj.last_finished_unix_ms,
                            FailuresSoFar = pj.failures_so_far ?? new List<TestJobFailure>(),
                            Error = pj.error,
                            // Intentionally not persisted to avoid ballooning SessionState.
                            Result = null
                        };
                    }

                    _currentJobId = string.IsNullOrWhiteSpace(state.current_job_id) ? null : state.current_job_id;
                    if (!string.IsNullOrEmpty(_currentJobId) && !Jobs.ContainsKey(_currentJobId))
                    {
                        _currentJobId = null;
                    }

                    // Detect and clean up stale "running" jobs that were orphaned by domain reload.
                    // After a domain reload, TestRunStatus resets to not-running, but _currentJobId
                    // may still be set. If the job hasn't been updated recently, it's likely orphaned.
                    if (!string.IsNullOrEmpty(_currentJobId) && Jobs.TryGetValue(_currentJobId, out var currentJob))
                    {
                        if (currentJob.Status == TestJobStatus.Running)
                        {
                            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                            long staleCutoffMs = 5 * 60 * 1000; // 5 minutes
                            if (now - currentJob.LastUpdateUnixMs > staleCutoffMs)
                            {
                                McpLog.Warn($"[TestJobManager] Clearing stale job {_currentJobId} (last update {(now - currentJob.LastUpdateUnixMs) / 1000}s ago)");
                                currentJob.Status = TestJobStatus.Failed;
                                currentJob.Error = "Job orphaned after domain reload";
                                currentJob.FinishedUnixMs = now;
                                _currentJobId = null;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Restoration is best-effort; never block editor load.
                McpLog.Warn($"[TestJobManager] Failed to restore SessionState: {ex.Message}");
            }
        }

        private static void PersistToSessionState(bool force = false)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            
            // Throttle non-critical updates to reduce overhead during large test runs
            if (!force && (now - _lastPersistUnixMs) < MinPersistIntervalMs)
            {
                return;
            }
            
            try
            {
                PersistedState snapshot;
                lock (LockObj)
                {
                    var jobs = Jobs.Values
                        .OrderByDescending(j => j.LastUpdateUnixMs)
                        .Take(MaxJobsToKeep)
                        .Select(j => new PersistedJob
                        {
                            job_id = j.JobId,
                            status = j.Status.ToString().ToLowerInvariant(),
                            mode = j.Mode,
                            started_unix_ms = j.StartedUnixMs,
                            finished_unix_ms = j.FinishedUnixMs,
                            last_update_unix_ms = j.LastUpdateUnixMs,
                            total_tests = j.TotalTests,
                            completed_tests = j.CompletedTests,
                            current_test_full_name = j.CurrentTestFullName,
                            current_test_started_unix_ms = j.CurrentTestStartedUnixMs,
                            last_finished_test_full_name = j.LastFinishedTestFullName,
                            last_finished_unix_ms = j.LastFinishedUnixMs,
                            failures_so_far = (j.FailuresSoFar ?? new List<TestJobFailure>()).Take(FailureCap).ToList(),
                            error = j.Error
                        })
                        .ToList();

                    snapshot = new PersistedState
                    {
                        current_job_id = _currentJobId,
                        jobs = jobs
                    };
                }

                SessionState.SetString(SessionKeyCurrentJobId, snapshot.current_job_id ?? string.Empty);
                SessionState.SetString(SessionKeyJobs, JsonConvert.SerializeObject(snapshot));
                _lastPersistUnixMs = now;
            }
            catch (Exception ex)
            {
                McpLog.Warn($"[TestJobManager] Failed to persist SessionState: {ex.Message}");
            }
        }

        public static string StartJob(TestMode mode, TestFilterOptions filterOptions = null)
        {
            string jobId = Guid.NewGuid().ToString("N");
            long started = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string modeStr = mode.ToString();

            var job = new TestJob
            {
                JobId = jobId,
                Status = TestJobStatus.Running,
                Mode = modeStr,
                StartedUnixMs = started,
                FinishedUnixMs = null,
                LastUpdateUnixMs = started,
                TotalTests = null,
                CompletedTests = 0,
                CurrentTestFullName = null,
                CurrentTestStartedUnixMs = null,
                LastFinishedTestFullName = null,
                LastFinishedUnixMs = null,
                FailuresSoFar = new List<TestJobFailure>(),
                Error = null,
                Result = null
            };

            // Single lock scope for check-and-set to avoid TOCTOU race
            lock (LockObj)
            {
                if (!string.IsNullOrEmpty(_currentJobId))
                {
                    throw new InvalidOperationException("A Unity test run is already in progress.");
                }
                Jobs[jobId] = job;
                _currentJobId = jobId;
            }
            PersistToSessionState(force: true);

            // Kick the run (must be called on main thread; our command handlers already run there).
            Task<TestRunResult> task = MCPServiceLocator.Tests.RunTestsAsync(mode, filterOptions);

            void FinalizeJob(Action finalize)
            {
                // Ensure state mutation happens on main thread to avoid Unity API surprises.
                EditorApplication.delayCall += () =>
                {
                    try { finalize(); }
                    catch (Exception ex) { McpLog.Error($"[TestJobManager] Finalize failed: {ex.Message}\n{ex.StackTrace}"); }
                };
            }

            task.ContinueWith(t =>
            {
                // NOTE: We now finalize jobs deterministically from the TestRunnerService RunFinished callback.
                // This continuation is retained as a safety net in case RunFinished is not delivered.
                FinalizeJob(() => FinalizeFromTask(jobId, t));
            }, TaskScheduler.Default);

            return jobId;
        }

        public static void FinalizeCurrentJobFromRunFinished(TestRunResult resultPayload)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            lock (LockObj)
            {
                if (string.IsNullOrEmpty(_currentJobId) || !Jobs.TryGetValue(_currentJobId, out var job))
                {
                    return;
                }

                job.LastUpdateUnixMs = now;
                job.FinishedUnixMs = now;
                job.Status = resultPayload != null && resultPayload.Failed > 0
                    ? TestJobStatus.Failed
                    : TestJobStatus.Succeeded;
                job.Error = null;
                job.Result = resultPayload;
                job.CurrentTestFullName = null;
                _currentJobId = null;
            }
            PersistToSessionState(force: true);
        }

        public static void OnRunStarted(int? totalTests)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            lock (LockObj)
            {
                if (string.IsNullOrEmpty(_currentJobId) || !Jobs.TryGetValue(_currentJobId, out var job))
                {
                    return;
                }

                job.LastUpdateUnixMs = now;
                job.TotalTests = totalTests;
                job.CompletedTests = 0;
                job.CurrentTestFullName = null;
                job.CurrentTestStartedUnixMs = null;
                job.LastFinishedTestFullName = null;
                job.LastFinishedUnixMs = null;
                job.FailuresSoFar ??= new List<TestJobFailure>();
                job.FailuresSoFar.Clear();
            }
            PersistToSessionState(force: true);
        }

        public static void OnTestStarted(string testFullName)
        {
            if (string.IsNullOrWhiteSpace(testFullName))
            {
                return;
            }

            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            lock (LockObj)
            {
                if (string.IsNullOrEmpty(_currentJobId) || !Jobs.TryGetValue(_currentJobId, out var job))
                {
                    return;
                }

                job.LastUpdateUnixMs = now;
                job.CurrentTestFullName = testFullName;
                job.CurrentTestStartedUnixMs = now;
            }
            PersistToSessionState();
        }

        public static void OnLeafTestFinished(string testFullName, bool isFailure, string message)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            lock (LockObj)
            {
                if (string.IsNullOrEmpty(_currentJobId) || !Jobs.TryGetValue(_currentJobId, out var job))
                {
                    return;
                }

                job.LastUpdateUnixMs = now;
                job.CompletedTests = Math.Max(0, job.CompletedTests + 1);
                job.LastFinishedTestFullName = testFullName;
                job.LastFinishedUnixMs = now;

                if (isFailure)
                {
                    job.FailuresSoFar ??= new List<TestJobFailure>();
                    if (job.FailuresSoFar.Count < FailureCap)
                    {
                        job.FailuresSoFar.Add(new TestJobFailure
                        {
                            FullName = testFullName,
                            Message = string.IsNullOrWhiteSpace(message) ? "Test failed" : message
                        });
                    }
                }
            }
            PersistToSessionState();
        }

        public static void OnRunFinished()
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            lock (LockObj)
            {
                if (string.IsNullOrEmpty(_currentJobId) || !Jobs.TryGetValue(_currentJobId, out var job))
                {
                    return;
                }

                job.LastUpdateUnixMs = now;
                job.CurrentTestFullName = null;
            }
            PersistToSessionState(force: true);
        }

        internal static TestJob GetJob(string jobId)
        {
            if (string.IsNullOrWhiteSpace(jobId))
            {
                return null;
            }

            TestJob jobToReturn = null;
            bool shouldPersist = false;
            lock (LockObj)
            {
                if (!Jobs.TryGetValue(jobId, out var job))
                {
                    return null;
                }

                // Check if job is stuck in "running" state without having called OnRunStarted (TotalTests still null).
                // This happens when tests fail to initialize (e.g., unsaved scene, compilation issues).
                // After 15 seconds without initialization, auto-fail the job to prevent hanging.
                if (job.Status == TestJobStatus.Running && job.TotalTests == null)
                {
                    long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    if (!EditorApplication.isCompiling && !EditorApplication.isUpdating && now - job.StartedUnixMs > InitializationTimeoutMs)
                    {
                        McpLog.Warn($"[TestJobManager] Job {jobId} failed to initialize within {InitializationTimeoutMs}ms, auto-failing");
                        job.Status = TestJobStatus.Failed;
                        job.Error = "Test job failed to initialize (tests did not start within timeout)";
                        job.FinishedUnixMs = now;
                        job.LastUpdateUnixMs = now;
                        if (_currentJobId == jobId)
                        {
                            _currentJobId = null;
                        }
                        shouldPersist = true;
                    }
                }

                jobToReturn = job;
            }

            if (shouldPersist)
            {
                PersistToSessionState(force: true);
            }
            return jobToReturn;
        }

        internal static object ToSerializable(TestJob job, bool includeDetails, bool includeFailedTests)
        {
            if (job == null)
            {
                return null;
            }

            object resultPayload = null;
            if (job.Status == TestJobStatus.Succeeded && job.Result != null)
            {
                resultPayload = job.Result.ToSerializable(job.Mode, includeDetails, includeFailedTests);
            }

            return new
            {
                job_id = job.JobId,
                status = job.Status.ToString().ToLowerInvariant(),
                mode = job.Mode,
                started_unix_ms = job.StartedUnixMs,
                finished_unix_ms = job.FinishedUnixMs,
                last_update_unix_ms = job.LastUpdateUnixMs,
                progress = new
                {
                    completed = job.CompletedTests,
                    total = job.TotalTests,
                    current_test_full_name = job.CurrentTestFullName,
                    current_test_started_unix_ms = job.CurrentTestStartedUnixMs,
                    last_finished_test_full_name = job.LastFinishedTestFullName,
                    last_finished_unix_ms = job.LastFinishedUnixMs,
                    stuck_suspected = IsStuck(job),
                    editor_is_focused = InternalEditorUtility.isApplicationActive,
                    blocked_reason = GetBlockedReason(job),
                    failures_so_far = BuildFailuresPayload(job.FailuresSoFar),
                    failures_capped = (job.FailuresSoFar != null && job.FailuresSoFar.Count >= FailureCap)
                },
                error = job.Error,
                result = resultPayload
            };
        }

        private static string GetBlockedReason(TestJob job)
        {
            if (job == null || job.Status != TestJobStatus.Running)
            {
                return null;
            }

            if (!IsStuck(job))
            {
                return null;
            }

            // This matches the real-world symptom you observed: background Unity can get heavily throttled by OS/Editor.
            if (!InternalEditorUtility.isApplicationActive)
            {
                return "editor_unfocused";
            }

            if (EditorApplication.isCompiling)
            {
                return "compiling";
            }

            if (EditorApplication.isUpdating)
            {
                return "asset_import";
            }

            return "unknown";
        }

        private static bool IsStuck(TestJob job)
        {
            if (job == null || job.Status != TestJobStatus.Running)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(job.CurrentTestFullName) || !job.CurrentTestStartedUnixMs.HasValue)
            {
                return false;
            }

            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return (now - job.CurrentTestStartedUnixMs.Value) > StuckThresholdMs;
        }

        private static object[] BuildFailuresPayload(List<TestJobFailure> failures)
        {
            if (failures == null || failures.Count == 0)
            {
                return Array.Empty<object>();
            }

            var list = new object[failures.Count];
            for (int i = 0; i < failures.Count; i++)
            {
                var f = failures[i];
                list[i] = new { full_name = f?.FullName, message = f?.Message };
            }
            return list;
        }

        private static void FinalizeFromTask(string jobId, Task<TestRunResult> task)
        {
            lock (LockObj)
            {
                if (!Jobs.TryGetValue(jobId, out var existing))
                {
                    if (_currentJobId == jobId) _currentJobId = null;
                    return;
                }

                // If RunFinished already finalized the job, do nothing.
                if (existing.Status != TestJobStatus.Running)
                {
                    if (_currentJobId == jobId) _currentJobId = null;
                    return;
                }

                existing.LastUpdateUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                existing.FinishedUnixMs = existing.LastUpdateUnixMs;

                if (task.IsFaulted)
                {
                    existing.Status = TestJobStatus.Failed;
                    existing.Error = task.Exception?.GetBaseException()?.Message ?? "Unknown test job failure";
                    existing.Result = null;
                }
                else if (task.IsCanceled)
                {
                    existing.Status = TestJobStatus.Failed;
                    existing.Error = "Test job canceled";
                    existing.Result = null;
                }
                else
                {
                    var result = task.Result;
                    existing.Status = result != null && result.Failed > 0
                        ? TestJobStatus.Failed
                        : TestJobStatus.Succeeded;
                    existing.Error = null;
                    existing.Result = result;
                }

                if (_currentJobId == jobId)
                {
                    _currentJobId = null;
                }
            }
            PersistToSessionState(force: true);
        }
    }
}

