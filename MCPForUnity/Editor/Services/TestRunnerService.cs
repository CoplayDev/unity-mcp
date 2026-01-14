using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Concrete implementation of <see cref="ITestRunnerService"/>.
    /// Coordinates Unity Test Runner operations and produces structured results.
    /// </summary>
    internal sealed class TestRunnerService : ITestRunnerService, ICallbacks, IDisposable
    {
        private static readonly TestMode[] AllModes = { TestMode.EditMode, TestMode.PlayMode };

        private readonly TestRunnerApi _testRunnerApi;
        private readonly SemaphoreSlim _operationLock = new SemaphoreSlim(1, 1);
        private readonly List<ITestResultAdaptor> _leafResults = new List<ITestResultAdaptor>();
        private TaskCompletionSource<TestRunResult> _runCompletionSource;

        public TestRunnerService()
        {
            _testRunnerApi = ScriptableObject.CreateInstance<TestRunnerApi>();
            _testRunnerApi.RegisterCallbacks(this);
        }

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
            await _operationLock.WaitAsync().ConfigureAwait(true);
            Task<TestRunResult> runTask;
            bool adjustedPlayModeOptions = false;
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

                // Apply no-throttling preemptively for PlayMode tests. This ensures Unity
                // isn't throttled during the Play mode transition (which requires multiple
                // editor frames). Without this, unfocused Unity may never reach RunStarted
                // where throttling would normally be disabled.
                if (mode == TestMode.PlayMode)
                {
                    TestRunnerNoThrottle.ApplyNoThrottlingPreemptive();
                }

                _testRunnerApi.Execute(settings);

                runTask = _runCompletionSource.Task;
            }
            catch
            {
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

            // Clean up state regardless of _runCompletionSource - these methods safely handle
            // the case where no MCP job exists (e.g., manual test runs via Unity UI).
            TestRunStatus.MarkFinished();
            TestJobManager.OnRunFinished();
            TestJobManager.FinalizeCurrentJobFromRunFinished(payload);

            // Report result to awaiting caller if we have a completion source
            if (_runCompletionSource != null)
            {
                _runCompletionSource.TrySetResult(payload);
                _runCompletionSource = null;
            }
        }

        public void TestStarted(ITestAdaptor test)
        {
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

            if (!result.HasChildren)
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

                    TestJobManager.OnLeafTestFinished(fullName, isFailure, message);
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

            var desired = originalEnterPlayModeOptions | EnterPlayModeOptions.DisableDomainReload;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = desired;
            return true;
        }

        private static void RestoreEnterPlayModeOptions(bool originalEnabled, EnterPlayModeOptions originalOptions)
        {
            EditorSettings.enterPlayModeOptions = originalOptions;
            EditorSettings.enterPlayModeOptionsEnabled = originalEnabled;
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
            var materializedTests = tests.Select(TestRunTestResult.FromAdaptor).ToList();

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
    }

    public sealed class TestRunSummary
    {
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
