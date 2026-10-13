using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Restores <see cref="EditorSettings.enterPlayModeOptionsEnabled"/> and
    /// <see cref="EditorSettings.enterPlayModeOptions"/> if a previous test run was interrupted
    /// (e.g. by domain reload or editor crash) before <see cref="TestRunnerService"/> could restore them.
    /// Two persistence layers:
    /// <list type="bullet">
    /// <item><see cref="SessionState"/> — survives domain reloads within the same editor session.</item>
    /// <item>A marker file in <c>Library/</c> — survives editor crashes and force quits.</item>
    /// </list>
    /// </summary>
    [InitializeOnLoad]
    internal static class PlayModeOptionsGuard
    {
        private const string KeyPending = "MCPForUnity.PlayModeOptions.PendingRestore";
        private const string KeyEnabled = "MCPForUnity.PlayModeOptions.OriginalEnabled";
        private const string KeyOptions = "MCPForUnity.PlayModeOptions.OriginalOptions";

        // Library/ is project-local and gitignored by default.
        private static readonly string MarkerPath = Path.Combine("Library", "MCPPlayModeOptionsBackup.txt");

        static PlayModeOptionsGuard()
        {
            RestoreIfIdle();
        }

        internal static void RestoreIfIdle()
        {
            // After domain reload or editor restart: if a restore is pending and no test run
            // is active, restore now. TryLoad checks SessionState first, then the marker file.
            if (TryLoad(out _, out _) && !TestRunStatus.IsRunning && !TestJobManager.HasRunningJob)
            {
                Restore();
            }
        }

        internal static bool IsPending => TryLoad(out _, out _);

        internal static void Save(bool originalEnabled, EnterPlayModeOptions originalOptions)
        {
            // SessionState (domain reload)
            SessionState.SetBool(KeyEnabled, originalEnabled);
            SessionState.SetInt(KeyOptions, (int)originalOptions);
            SessionState.SetBool(KeyPending, true);

            // Marker file (crash recovery). Two lines: enabled flag, then options int.
            try
            {
                File.WriteAllText(MarkerPath, $"{(originalEnabled ? 1 : 0)}\n{(int)originalOptions}");
            }
            catch (Exception ex)
            {
                McpLog.Warn($"[PlayModeOptionsGuard] Failed to write marker file: {ex.Message}");
            }
        }

        internal static void Restore()
        {
            if (!TryLoad(out bool origEnabled, out EnterPlayModeOptions origOptions))
            {
                return;
            }

            EditorSettings.enterPlayModeOptions = origOptions;
            EditorSettings.enterPlayModeOptionsEnabled = origEnabled;
            Clear();
            McpLog.Info("[PlayModeOptionsGuard] Restored enterPlayModeOptions after interrupted test run.");
        }

        internal static void Clear()
        {
            SessionState.SetBool(KeyPending, false);
            try
            {
                if (File.Exists(MarkerPath))
                {
                    File.Delete(MarkerPath);
                }
            }
            catch
            {
                // Best-effort cleanup.
            }
        }

        /// <summary>
        /// Checks SessionState first (available after domain reload), then falls back to the
        /// marker file (available after editor crash/restart).
        /// </summary>
        private static bool TryLoad(out bool originalEnabled, out EnterPlayModeOptions originalOptions)
        {
            // Fast path: SessionState is available after domain reload.
            if (SessionState.GetBool(KeyPending, false))
            {
                originalEnabled = SessionState.GetBool(KeyEnabled, false);
                originalOptions = (EnterPlayModeOptions)SessionState.GetInt(KeyOptions, 0);
                return true;
            }

            // Slow path: marker file survives editor crash/restart.
            originalEnabled = false;
            originalOptions = EnterPlayModeOptions.None;
            try
            {
                if (!File.Exists(MarkerPath))
                {
                    return false;
                }

                string[] lines = File.ReadAllLines(MarkerPath);
                if (lines.Length < 2)
                {
                    return false;
                }

                if (!int.TryParse(lines[0].Trim(), out int enabledInt) ||
                    !int.TryParse(lines[1].Trim(), out int optionsInt))
                {
                    return false;
                }

                originalEnabled = enabledInt != 0;
                originalOptions = (EnterPlayModeOptions)optionsInt;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Concrete implementation of <see cref="ITestRunnerService"/>.
    /// Coordinates Unity Test Runner operations and produces structured results.
    /// </summary>
    internal sealed class TestRunnerService : ITestRunnerService, IErrorCallbacks, IDisposable
    {
        private static readonly TestMode[] AllModes = { TestMode.EditMode, TestMode.PlayMode };

        private readonly TestRunnerApi _testRunnerApi;
        private readonly SemaphoreSlim _operationLock = new SemaphoreSlim(1, 1);
        private readonly List<ITestResultAdaptor> _leafResults = new List<ITestResultAdaptor>();
        private TaskCompletionSource<TestRunResult> _runCompletionSource;
        private string _trackedJobId;
        private string _unityRunId;
        private bool _initializationTimedOut;
        private bool _nativeRunStarted;
        private TestRunResult _pendingResult;
        private Exception _pendingError;
        // A failed native activity query (for example around a domain reload) leaves the run's
        // state unknown. Retry it for this long, then end in an explicit job failure.
        private const double NativeQueryRecoveryWindowSeconds = 30.0;
        private const double NativeQueryFailureLogIntervalSeconds = 5.0;
        internal const string NativeStateUnconfirmedError =
            "Unity Test Runner state could not be confirmed, so this job keeps the test run lock to avoid overlapping runs. " +
            "If Unity is no longer running tests, reload scripts from the Unity Editor or restart it to re-check. " +
            "run_tests(clear_stuck=true) drops this job from the queue but may not release the test run lock.";
        internal const string RunErrorMarker = " Run error: ";
        private double? _nativeQueryFailedSince;
        private double _nativeQueryFailureLoggedAt;
        private static readonly MethodInfo CancelRunMethod = typeof(TestRunnerApi).GetMethod("CancelTestRun", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(string) }, null);
        private static readonly MethodInfo IsRunningMethod =
            typeof(TestRunnerApi).GetMethod("IsRunActive", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(string) }, null) ??
            typeof(TestRunnerApi).GetMethod("IsRunning", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(string) }, null);
        private static readonly PropertyInfo RunnerHolderProperty = typeof(TestRunnerApi).GetProperty("m_testJobDataHolder", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly MethodInfo GetRunnerMethod = RunnerHolderProperty?.PropertyType.GetMethod("GetRunner");
        private static readonly MethodInfo IsRunningJobMethod = GetRunnerMethod?.ReturnType.GetMethod("IsRunningJob");
        private static readonly bool CanQueryNativeRun = IsRunningMethod != null || IsRunningJobMethod != null;

        public TestRunnerService()
        {
            _testRunnerApi = ScriptableObject.CreateInstance<TestRunnerApi>();
            _testRunnerApi.hideFlags = HideFlags.HideAndDontSave;
            _testRunnerApi.RegisterCallbacks(this);
        }

        internal void ResumeJobAfterReload(string jobId, string mode, string unityRunId = null,
            bool initializationTimedOut = false, bool nativeRunStarted = false,
            bool nativeCleanupPending = false, TestRunResult pendingResult = null, string pendingError = null)
        {
            if (_runCompletionSource != null || _trackedJobId != null ||
                string.IsNullOrEmpty(jobId) || TestJobManager.CurrentJobId != jobId)
            {
                return;
            }

            _trackedJobId = jobId;
            _nativeQueryFailedSince = null;
            _unityRunId = unityRunId;
            _nativeRunStarted = nativeRunStarted;
            _initializationTimedOut = initializationTimedOut;
            _pendingResult = pendingResult;
            _pendingError = RestorePendingError(pendingError);
            if (Enum.TryParse<TestMode>(mode, out var testMode))
            {
                TestRunStatus.MarkStarted(testMode);
            }
            if (nativeCleanupPending && CanQueryNativeRun)
            {
                EditorApplication.update += CompleteRunWhenIdle;
            }
            else if (initializationTimedOut) CancelForInitializationTimeout(jobId);
        }

        internal void CancelForInitializationTimeout(string jobId)
        {
            if (_trackedJobId != jobId || string.IsNullOrEmpty(_unityRunId)) return;
            _initializationTimedOut = true;
            _pendingError = new InvalidOperationException(TestJobManager.InitializationTimeoutError);
            // Older Test Framework releases have no public per-run activity query. Keep
            // ownership until cleanup finishes, including cancellation before RunStarted
            // where Unity may skip RunFinished entirely.
            if (CancelRunMethod == null || !CanQueryNativeRun)
            {
                McpLog.Warn("[TestRunnerService] Test Framework cancellation monitoring is unavailable; waiting for the timed-out run to finish.");
                return;
            }
            EditorApplication.update -= CompleteRunWhenIdle;
            EditorApplication.update += CompleteRunWhenIdle;
            // Before RunStarted, cancellation can skip Unity's scene snapshot but still
            // run its scene restore task. Wait until setup has captured the open scenes.
            if (!_nativeRunStarted) return;
            try
            {
                if (IsNativeRunActive()) CancelRunMethod.Invoke(null, new object[] { _unityRunId });
            }
            catch (Exception ex)
            {
                McpLog.Warn($"[TestRunnerService] Failed to cancel timed-out run: {ex.GetBaseException().Message}");
            }
        }

        private bool IsNativeRunActive()
        {
            if (IsRunningMethod != null)
            {
                return (bool)IsRunningMethod.Invoke(null, new object[] { _unityRunId });
            }
            // Test Framework 1.1.33 exposes only a global activity query. Ask its
            // holder for this GUID so a manual run cannot keep an MCP lock alive.
            var holder = RunnerHolderProperty.GetValue(null);
            var runner = GetRunnerMethod.Invoke(holder, new object[] { _unityRunId });
            return runner != null && (bool)IsRunningJobMethod.Invoke(runner, null);
        }

        // A query exception means the native state is unknown. Report it as active so a failed
        // query can never release ownership; the idle monitor bounds the retries.
        private bool IsNativeRunActiveOrUnknown()
        {
            try
            {
                bool active = IsNativeRunActive();
                _nativeQueryFailedSince = null;
                return active;
            }
            catch (Exception ex)
            {
                double now = EditorApplication.timeSinceStartup;
                string reason = ex.GetBaseException().Message;
                if (!_nativeQueryFailedSince.HasValue)
                {
                    _nativeQueryFailedSince = now;
                    _nativeQueryFailureLoggedAt = now;
                    McpLog.Warn($"[TestRunnerService] Failed to query native test cleanup; retrying for up to {NativeQueryRecoveryWindowSeconds}s: {reason}");
                }
                else if (now - _nativeQueryFailureLoggedAt >= NativeQueryFailureLogIntervalSeconds)
                {
                    _nativeQueryFailureLoggedAt = now;
                    McpLog.Warn($"[TestRunnerService] Native test cleanup query still failing after {now - _nativeQueryFailedSince.Value:0}s: {reason}");
                }
                return true;
            }
        }

        // Pure predicate (unit-testable): keep retrying a failing native query until it has failed
        // continuously for the whole window. No recorded failure means keep monitoring.
        internal static bool ShouldKeepRetryingNativeQuery(double now, double? firstFailureAt, double windowSeconds)
            => !firstFailureAt.HasValue || now - firstFailureAt.Value < windowSeconds;

        // The persisted job error becomes the pending error again after a domain reload. The
        // "unconfirmed" text is this service's own annotation, not a run error, so drop it and keep only
        // the original run error. A later successful check then reports the run's real outcome.
        internal static Exception RestorePendingError(string persisted)
        {
            if (persisted == null) return null;
            if (persisted.StartsWith(NativeStateUnconfirmedError, StringComparison.Ordinal))
            {
                string rest = persisted.Substring(NativeStateUnconfirmedError.Length);
                persisted = rest.StartsWith(RunErrorMarker, StringComparison.Ordinal) ? rest.Substring(RunErrorMarker.Length) : null;
                if (string.IsNullOrEmpty(persisted)) return null;
            }
            return new InvalidOperationException(persisted);
        }

        // The native state stayed unknown for the whole window. Stop polling but keep ownership,
        // and fail the job with a message that tells the client how to clear it.
        private void StopMonitoringUnconfirmedRun()
        {
            EditorApplication.update -= CompleteRunWhenIdle;
            string message = NativeStateUnconfirmedError;
            string reason = _pendingError?.Message;
            if (!string.IsNullOrEmpty(reason)) message += RunErrorMarker + reason;
            McpLog.Warn($"[TestRunnerService] Stopped querying native test cleanup after {NativeQueryRecoveryWindowSeconds}s of failures. {message}");
            if (IsTrackingCurrentJob)
            {
                TestJobManager.FinalizeCurrentJobFromRunError(message, keepOwnership: true);
            }
        }

        private void CompleteRunWhenIdle()
        {
            try
            {
                if (!IsNativeRunActiveOrUnknown())
                {
                    CompleteRun(_pendingResult, _pendingError);
                }
                else if (!ShouldKeepRetryingNativeQuery(EditorApplication.timeSinceStartup, _nativeQueryFailedSince, NativeQueryRecoveryWindowSeconds))
                {
                    StopMonitoringUnconfirmedRun();
                }
            }
            catch (Exception ex)
            {
                EditorApplication.update -= CompleteRunWhenIdle;
                McpLog.Warn($"[TestRunnerService] Failed to monitor native test cleanup: {ex.GetBaseException().Message}");
            }
        }

        private bool IsTrackingCurrentJob =>
            !string.IsNullOrEmpty(_trackedJobId) && TestJobManager.CurrentJobId == _trackedJobId;

        public async Task<IReadOnlyList<Dictionary<string, string>>> GetTestsAsync(TestMode? mode)
        {
            await _operationLock.WaitAsync().ConfigureAwait(true);
            try
            {
                var modes = mode.HasValue ? new[] { mode.Value } : AllModes;

                var results = new List<Dictionary<string, string>>();
                var seen = new HashSet<string>(StringComparer.Ordinal);

                foreach (var m in modes)
                {
                    var root = await RetrieveTestRootAsync(m).ConfigureAwait(true);
                    if (root != null)
                    {
                        CollectFromNode(root, m, results, seen, new List<string>());
                    }
                }

                return results;
            }
            finally
            {
                _operationLock.Release();
            }
        }

        public async Task<TestRunResult> RunTestsAsync(TestMode mode, TestFilterOptions filterOptions = null)
        {
            // The pre-reload Task no longer exists, but its Unity run may still be active.
            // Clearing a job must not let a second run consume the first run's callbacks.
            if (_trackedJobId != null && _runCompletionSource == null)
            {
                throw new InvalidOperationException("A recovered Unity test run is still in progress.");
            }

            await _operationLock.WaitAsync().ConfigureAwait(true);
            Task<TestRunResult> runTask;
            bool adjustedPlayModeOptions = false;
            bool appliedNoThrottling = false;
            bool originalEnterPlayModeOptionsEnabled = false;
            EnterPlayModeOptions originalEnterPlayModeOptions = EnterPlayModeOptions.None;
            try
            {
                if (_runCompletionSource != null && !_runCompletionSource.Task.IsCompleted)
                {
                    throw new InvalidOperationException("A Unity test run is already in progress.");
                }

                if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    throw new InvalidOperationException("Cannot start a test run while the Editor is in or entering Play Mode. Stop Play Mode and try again.");
                }

                if (mode == TestMode.PlayMode)
                {
                    // PlayMode runs transition the editor into play across multiple update ticks. Unity's
                    // built-in pipeline schedules SaveModifiedSceneTask early, but that task uses
                    // EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo which throws once play mode is
                    // active. To minimize that window we pre-save dirty scenes and disable domain reload (so the
                    // MCP bridge stays alive). We do NOT force runSynchronously here because that can freeze the
                    // editor in some projects. If the TestRunner still hits the save task after entering play, the
                    // run can fail; in that case, rerun from a clean Edit Mode state.
                    adjustedPlayModeOptions = EnsurePlayModeRunsWithoutDomainReload(
                        out originalEnterPlayModeOptionsEnabled,
                        out originalEnterPlayModeOptions);
                }

                _leafResults.Clear();
                _trackedJobId = TestJobManager.CurrentJobId;
                _nativeQueryFailedSince = null;
                _runCompletionSource = new TaskCompletionSource<TestRunResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                // Mark running immediately so readiness snapshots reflect the busy state even before callbacks fire.
                TestRunStatus.MarkStarted(mode);

                var filter = new Filter
                {
                    testMode = mode,
                    testNames = filterOptions?.TestNames,
                    groupNames = filterOptions?.GroupNames,
                    categoryNames = filterOptions?.CategoryNames,
                    assemblyNames = filterOptions?.AssemblyNames
                };
                var settings = new ExecutionSettings(filter);

                // Save dirty scenes for all test modes to prevent modal dialogs blocking MCP
                // (Issue #525: EditMode tests were blocked by save dialog)
                SaveDirtyScenesIfNeeded();

                // EditMode setup also advances over editor frames before RunStarted.
                // Disable background throttling before Execute for both test modes.
                appliedNoThrottling = true;
                TestRunnerNoThrottle.ApplyNoThrottlingPreemptive();

                _unityRunId = _testRunnerApi.Execute(settings);
                TestJobManager.OnRunScheduled(_trackedJobId, _unityRunId);

                runTask = _runCompletionSource.Task;
            }
            catch
            {
                _trackedJobId = null;
                _unityRunId = null;
                _runCompletionSource = null;
                if (appliedNoThrottling) TestRunnerNoThrottle.RestoreThrottling();
                // Ensure the status is cleared if we failed to start the run.
                TestRunStatus.MarkFinished();
                if (adjustedPlayModeOptions)
                {
                    RestoreEnterPlayModeOptions(originalEnterPlayModeOptionsEnabled, originalEnterPlayModeOptions);
                }

                _operationLock.Release();
                throw;
            }

            try
            {
                return await runTask.ConfigureAwait(true);
            }
            finally
            {
                if (adjustedPlayModeOptions)
                {
                    RestoreEnterPlayModeOptions(originalEnterPlayModeOptionsEnabled, originalEnterPlayModeOptions);
                }

                _operationLock.Release();
            }
        }

        public void Dispose()
        {
            EditorApplication.update -= CompleteRunWhenIdle;
            _nativeQueryFailedSince = null;
            try
            {
                _testRunnerApi?.UnregisterCallbacks(this);
            }
            catch
            {
                // Ignore cleanup errors
            }

            if (_testRunnerApi != null)
            {
                ScriptableObject.DestroyImmediate(_testRunnerApi);
            }

            _operationLock.Dispose();
        }

        #region TestRunnerApi callbacks

        public void RunStarted(ITestAdaptor testsToRun)
        {
            _leafResults.Clear();
            if (!IsTrackingCurrentJob)
            {
                return;
            }
            _nativeRunStarted = true;
            if (_initializationTimedOut) CancelForInitializationTimeout(_trackedJobId);
            try
            {
                // Best-effort progress info for async polling (avoid heavy payloads).
                int? total = null;
                if (testsToRun != null)
                {
                    total = CountLeafTests(testsToRun);
                }
                TestJobManager.OnRunStarted(total);
            }
            catch
            {
                TestJobManager.OnRunStarted(null);
            }
        }

        public void RunFinished(ITestResultAdaptor result)
        {
            // Always create payload and clean up job state, even if _runCompletionSource is null.
            // This handles domain reload scenarios (e.g., PlayMode tests) where the TestRunnerService
            // is recreated and _runCompletionSource is lost, but TestJobManager state persists via
            // SessionState and the Test Runner still delivers the RunFinished callback.
            var payload = TestRunResult.Create(result, _leafResults);
            CompleteRun(payload, null);
        }

        public void OnError(string message)
        {
            // Build/prebuild failures use IErrorCallbacks instead of RunFinished.
            CompleteRun(null, new InvalidOperationException(message ?? "Unity test run failed."));
        }

        private void CompleteRun(TestRunResult payload, Exception error)
        {
            if (_initializationTimedOut)
            {
                error = new InvalidOperationException(TestJobManager.InitializationTimeoutError);
            }
            if (!string.IsNullOrEmpty(_unityRunId) && CanQueryNativeRun)
            {
                _pendingResult = payload;
                _pendingError = error;
                // A failed query is handled like an active run: the monitor retries it.
                if (IsNativeRunActiveOrUnknown())
                {
                    // RunFinished/OnError precede native scene and preference teardown.
                    // Persist the result, but retain ownership until all cleanup is done.
                    if (IsTrackingCurrentJob)
                    {
                        if (error == null) TestJobManager.FinalizeCurrentJobFromRunFinished(payload, keepOwnership: true);
                        else TestJobManager.FinalizeCurrentJobFromRunError(error.Message, keepOwnership: true);
                    }
                    EditorApplication.update -= CompleteRunWhenIdle;
                    EditorApplication.update += CompleteRunWhenIdle;
                    return;
                }
            }
            EditorApplication.update -= CompleteRunWhenIdle;

            // A late callback from a cleared job must not finish a newer job or restore
            // settings belonging to it. Callbacks do not contain an MCP job identifier.
            bool ownsCurrentJob = IsTrackingCurrentJob;
            bool canCleanUp = ownsCurrentJob || !TestJobManager.HasRunningJob;
            if (canCleanUp)
            {
                TestRunStatus.MarkFinished();
                TestRunnerNoThrottle.RestoreThrottling();
            }
            if (ownsCurrentJob)
            {
                if (error == null)
                {
                    TestJobManager.OnRunFinished();
                    TestJobManager.FinalizeCurrentJobFromRunFinished(payload);
                }
                else
                {
                    TestJobManager.FinalizeCurrentJobFromRunError(error.Message);
                }
            }

            // If a domain reload destroyed the original RunTestsAsync caller, the finally block
            // that would normally restore EditorSettings never ran. Restore from SessionState.
            if (canCleanUp && _trackedJobId != null && _runCompletionSource == null && PlayModeOptionsGuard.IsPending)
            {
                PlayModeOptionsGuard.Restore();
            }

            // Report result to awaiting caller if we have a completion source.
            // The caller's finally block handles restoration in this case.
            var completion = _runCompletionSource;
            _runCompletionSource = null;
            _trackedJobId = null;
            _unityRunId = null;
            _initializationTimedOut = false;
            _nativeRunStarted = false;
            _pendingResult = null;
            _pendingError = null;
            if (completion != null)
            {
                if (error == null) completion.TrySetResult(payload);
                else completion.TrySetException(error);
            }
        }

        public void TestStarted(ITestAdaptor test)
        {
            if (!IsTrackingCurrentJob)
            {
                return;
            }
            try
            {
                // Prefer FullName for uniqueness; fall back to Name.
                string fullName = test?.FullName;
                if (string.IsNullOrWhiteSpace(fullName))
                {
                    fullName = test?.Name;
                }
                TestJobManager.OnTestStarted(fullName);
            }
            catch
            {
                // ignore
            }
        }

        public void TestFinished(ITestResultAdaptor result)
        {
            if (result == null)
            {
                return;
            }

            if (!result.HasChildren && result.Test?.IsSuite != true)
            {
                _leafResults.Add(result);
                try
                {
                    string fullName = result.Test?.FullName;
                    if (string.IsNullOrWhiteSpace(fullName))
                    {
                        fullName = result.Test?.Name;
                    }

                    bool isFailure = false;
                    string message = null;
                    try
                    {
                        // NUnit outcomes are strings in the adaptor; keep it simple.
                        string outcome = result.ResultState;
                        if (!string.IsNullOrWhiteSpace(outcome))
                        {
                            var o = outcome.Trim().ToLowerInvariant();
                            isFailure = o.Contains("failed") || o.Contains("error");
                        }
                        message = result.Message;
                    }
                    catch
                    {
                        // ignore adaptor quirks
                    }

                    if (IsTrackingCurrentJob)
                    {
                        TestJobManager.OnLeafTestFinished(fullName, isFailure, message);
                    }
                }
                catch
                {
                    // ignore
                }
            }
        }

        #endregion

        private static int CountLeafTests(ITestAdaptor node)
        {
            if (node == null)
            {
                return 0;
            }

            if (!node.HasChildren)
            {
                return 1;
            }

            int total = 0;
            try
            {
                foreach (var child in node.Children)
                {
                    total += CountLeafTests(child);
                }
            }
            catch
            {
                // If Unity changes the adaptor behavior, treat it as "unknown total".
                return 0;
            }

            return total;
        }

        private static bool EnsurePlayModeRunsWithoutDomainReload(
            out bool originalEnterPlayModeOptionsEnabled,
            out EnterPlayModeOptions originalEnterPlayModeOptions)
        {
            originalEnterPlayModeOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            originalEnterPlayModeOptions = EditorSettings.enterPlayModeOptions;

            // When Play Mode triggers a domain reload, the MCP connection is torn down and the pending
            // test run response never makes it back to the caller. To keep the bridge alive for this
            // invocation, temporarily enable Enter Play Mode Options with domain reload disabled.
            bool domainReloadDisabled = (originalEnterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) != 0;
            bool needsChange = !originalEnterPlayModeOptionsEnabled || !domainReloadDisabled;
            if (!needsChange)
            {
                return false;
            }

            // Persist originals to SessionState so they survive domain reloads. If the run is
            // interrupted (domain reload, crash), PlayModeOptionsGuard restores them on next load.
            PlayModeOptionsGuard.Save(originalEnterPlayModeOptionsEnabled, originalEnterPlayModeOptions);

            var desired = originalEnterPlayModeOptions | EnterPlayModeOptions.DisableDomainReload;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = desired;
            return true;
        }

        private static void RestoreEnterPlayModeOptions(bool originalEnabled, EnterPlayModeOptions originalOptions)
        {
            EditorSettings.enterPlayModeOptions = originalOptions;
            EditorSettings.enterPlayModeOptionsEnabled = originalEnabled;
            PlayModeOptionsGuard.Clear();
        }

        private static void SaveDirtyScenesIfNeeded()
        {
            int sceneCount = SceneManager.sceneCount;
            for (int i = 0; i < sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isDirty)
                {
                    if (string.IsNullOrEmpty(scene.path))
                    {
                        McpLog.Warn($"[TestRunnerService] Skipping unsaved scene '{scene.name}': save it manually before running tests.");
                        continue;
                    }
                    try
                    {
                        EditorSceneManager.SaveScene(scene);
                    }
                    catch (Exception ex)
                    {
                        McpLog.Warn($"[TestRunnerService] Failed to save dirty scene '{scene.name}': {ex.Message}");
                    }
                }
            }
        }

        #region Test list helpers

        private async Task<ITestAdaptor> RetrieveTestRootAsync(TestMode mode)
        {
            var tcs = new TaskCompletionSource<ITestAdaptor>(TaskCreationOptions.RunContinuationsAsynchronously);

            _testRunnerApi.RetrieveTestList(mode, root =>
            {
                tcs.TrySetResult(root);
            });

            // Ensure the editor pumps at least one additional update in case the window is unfocused.
            EditorApplication.QueuePlayerLoopUpdate();

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(30))).ConfigureAwait(true);
            if (completed != tcs.Task)
            {
                McpLog.Warn($"[TestRunnerService] Timeout waiting for test retrieval callback for {mode}");
                return null;
            }

            try
            {
                return await tcs.Task.ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                McpLog.Error($"[TestRunnerService] Error retrieving tests for {mode}: {ex.Message}\n{ex.StackTrace}");
                return null;
            }
        }

        private static void CollectFromNode(
            ITestAdaptor node,
            TestMode mode,
            List<Dictionary<string, string>> output,
            HashSet<string> seen,
            List<string> path)
        {
            if (node == null)
            {
                return;
            }

            bool hasName = !string.IsNullOrEmpty(node.Name);
            if (hasName)
            {
                path.Add(node.Name);
            }

            bool hasChildren = node.HasChildren && node.Children != null;

            if (!hasChildren)
            {
                string fullName = string.IsNullOrEmpty(node.FullName) ? node.Name ?? string.Empty : node.FullName;
                string key = $"{mode}:{fullName}";

                if (!string.IsNullOrEmpty(fullName) && seen.Add(key))
                {
                    string computedPath = path.Count > 0 ? string.Join("/", path) : fullName;
                    output.Add(new Dictionary<string, string>
                    {
                        ["name"] = node.Name ?? fullName,
                        ["full_name"] = fullName,
                        ["path"] = computedPath,
                        ["mode"] = mode.ToString(),
                    });
                }
            }
            else if (node.Children != null)
            {
                foreach (var child in node.Children)
                {
                    CollectFromNode(child, mode, output, seen, path);
                }
            }

            if (hasName && path.Count > 0)
            {
                path.RemoveAt(path.Count - 1);
            }
        }

        #endregion
    }

    /// <summary>
    /// Summary of a Unity test run.
    /// </summary>
    public sealed class TestRunResult
    {
        [JsonConstructor]
        internal TestRunResult(TestRunSummary summary, IReadOnlyList<TestRunTestResult> results)
        {
            Summary = summary;
            Results = results;
        }

        public TestRunSummary Summary { get; }
        public IReadOnlyList<TestRunTestResult> Results { get; }

        public int Total => Summary.Total;
        public int Passed => Summary.Passed;
        public int Failed => Summary.Failed;
        public int Skipped => Summary.Skipped;

        public object ToSerializable(string mode, bool includeDetails = false, bool includeFailedTests = false)
        {
            // Determine which results to include
            IEnumerable<object> resultsToSerialize;
            if (includeDetails)
            {
                // Include all test results
                resultsToSerialize = Results.Select(r => r.ToSerializable());
            }
            else if (includeFailedTests)
            {
                // Include only failed and skipped tests
                resultsToSerialize = Results
                    .Where(r => !string.Equals(r.State, "Passed", StringComparison.OrdinalIgnoreCase))
                    .Select(r => r.ToSerializable());
            }
            else
            {
                // No individual test results
                resultsToSerialize = null;
            }

            return new
            {
                mode,
                summary = Summary.ToSerializable(),
                results = resultsToSerialize?.ToList(),
            };
        }

        internal static TestRunResult Create(ITestResultAdaptor summary, IReadOnlyList<ITestResultAdaptor> tests)
        {
            // RunFinished includes the complete result tree, including tests completed
            // before a domain reload erased the service's per-test callback list.
            var resultLeaves = new List<ITestResultAdaptor>();
            if (summary != null && summary.HasChildren)
            {
                CollectResultLeaves(summary, resultLeaves);
            }
            var materializedTests = (resultLeaves.Count > 0 ? resultLeaves : tests)
                .Where(t => t != null && t.Test?.IsSuite != true)
                .Select(TestRunTestResult.FromAdaptor).ToList();

            int passed = summary?.PassCount
                ?? materializedTests.Count(t => string.Equals(t.State, "Passed", StringComparison.OrdinalIgnoreCase));
            int failed = summary?.FailCount
                ?? materializedTests.Count(t => string.Equals(t.State, "Failed", StringComparison.OrdinalIgnoreCase));
            int skipped = summary?.SkipCount
                ?? materializedTests.Count(t => string.Equals(t.State, "Skipped", StringComparison.OrdinalIgnoreCase));

            double duration = summary?.Duration
                ?? materializedTests.Sum(t => t.DurationSeconds);

            int total = summary != null ? passed + failed + skipped : materializedTests.Count;

            var summaryPayload = new TestRunSummary(
                total,
                passed,
                failed,
                skipped,
                duration,
                summary?.ResultState ?? "Unknown");

            return new TestRunResult(summaryPayload, materializedTests);
        }

        private static void CollectResultLeaves(ITestResultAdaptor result, List<ITestResultAdaptor> leaves)
        {
            if (result == null)
            {
                return;
            }
            if (!result.HasChildren)
            {
                if (result.Test?.IsSuite != true) leaves.Add(result);
                return;
            }
            if (result.Children != null)
            {
                foreach (var child in result.Children)
                {
                    CollectResultLeaves(child, leaves);
                }
            }
        }
    }

    public sealed class TestRunSummary
    {
        [JsonConstructor]
        internal TestRunSummary(int total, int passed, int failed, int skipped, double durationSeconds, string resultState)
        {
            Total = total;
            Passed = passed;
            Failed = failed;
            Skipped = skipped;
            DurationSeconds = durationSeconds;
            ResultState = resultState;
        }

        public int Total { get; }
        public int Passed { get; }
        public int Failed { get; }
        public int Skipped { get; }
        public double DurationSeconds { get; }
        public string ResultState { get; }

        internal object ToSerializable()
        {
            return new
            {
                total = Total,
                passed = Passed,
                failed = Failed,
                skipped = Skipped,
                durationSeconds = DurationSeconds,
                resultState = ResultState,
            };
        }
    }

    public sealed class TestRunTestResult
    {
        [JsonConstructor]
        internal TestRunTestResult(
            string name,
            string fullName,
            string state,
            double durationSeconds,
            string message,
            string stackTrace,
            string output)
        {
            Name = name;
            FullName = fullName;
            State = state;
            DurationSeconds = durationSeconds;
            Message = message;
            StackTrace = stackTrace;
            Output = output;
        }

        public string Name { get; }
        public string FullName { get; }
        public string State { get; }
        public double DurationSeconds { get; }
        public string Message { get; }
        public string StackTrace { get; }
        public string Output { get; }

        internal object ToSerializable()
        {
            return new
            {
                name = Name,
                fullName = FullName,
                state = State,
                durationSeconds = DurationSeconds,
                message = Message,
                stackTrace = StackTrace,
                output = Output,
            };
        }

        internal static TestRunTestResult FromAdaptor(ITestResultAdaptor adaptor)
        {
            if (adaptor == null)
            {
                return new TestRunTestResult(string.Empty, string.Empty, "Unknown", 0.0, string.Empty, string.Empty, string.Empty);
            }

            return new TestRunTestResult(
                adaptor.Name,
                adaptor.FullName,
                adaptor.ResultState,
                adaptor.Duration,
                adaptor.Message,
                adaptor.StackTrace,
                adaptor.Output);
        }
    }
}
