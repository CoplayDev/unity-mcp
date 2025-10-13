using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Executes Unity Test Runner suites and returns structured results.
    /// </summary>
    internal sealed class TestRunExecutor : ICallbacks, IDisposable
    {
        private readonly TestRunnerApi _testRunnerApi;
        private readonly List<ITestResultAdaptor> _leafResults = new List<ITestResultAdaptor>();
        private TaskCompletionSource<TestRunResult> _completionSource;

        public TestRunExecutor()
        {
            _testRunnerApi = ScriptableObject.CreateInstance<TestRunnerApi>();
            _testRunnerApi.RegisterCallbacks(this);
        }

        /// <summary>
        /// Execute all tests for the provided mode.
        /// </summary>
        public Task<TestRunResult> RunTestsAsync(TestMode mode)
        {
            if (_completionSource != null && !_completionSource.Task.IsCompleted)
            {
                throw new InvalidOperationException("A test run is already in progress.");
            }

            _leafResults.Clear();
            _completionSource = new TaskCompletionSource<TestRunResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            var filter = new Filter { testMode = mode };
            _testRunnerApi.Execute(new ExecutionSettings(filter));

            return _completionSource.Task;
        }

        public void RunStarted(ITestAdaptor testsToRun)
        {
            _leafResults.Clear();
        }

        public void RunFinished(ITestResultAdaptor result)
        {
            if (_completionSource == null)
            {
                return;
            }

            var payload = TestRunResult.Create(result, _leafResults);
            _completionSource.TrySetResult(payload);
            _completionSource = null;
        }

        public void TestStarted(ITestAdaptor test)
        {
            // No-op: we only need the finished results
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
                // Best effort cleanup
            }

            if (_testRunnerApi != null)
            {
                ScriptableObject.DestroyImmediate(_testRunnerApi);
            }
        }

        /// <summary>
        /// Serializable result for a single test run.
        /// </summary>
        internal sealed class TestRunResult
        {
            private TestRunResult(
                int total,
                int passed,
                int failed,
                int skipped,
                double durationSeconds,
                string resultState,
                IReadOnlyList<TestRunTestResult> tests)
            {
                Total = total;
                Passed = passed;
                Failed = failed;
                Skipped = skipped;
                DurationSeconds = durationSeconds;
                ResultState = resultState;
                Tests = tests;
            }

            public int Total { get; }
            public int Passed { get; }
            public int Failed { get; }
            public int Skipped { get; }
            public double DurationSeconds { get; }
            public string ResultState { get; }
            public IReadOnlyList<TestRunTestResult> Tests { get; }

            public object ToSerializable(string mode)
            {
                return new
                {
                    mode,
                    summary = new
                    {
                        total = Total,
                        passed = Passed,
                        failed = Failed,
                        skipped = Skipped,
                        durationSeconds = DurationSeconds,
                        resultState = ResultState,
                    },
                    results = Tests.Select(t => t.ToSerializable()).ToList(),
                };
            }

            public static TestRunResult Create(ITestResultAdaptor summary, IReadOnlyList<ITestResultAdaptor> testResults)
            {
                var serializedTests = testResults
                    .Select(TestRunTestResult.FromAdaptor)
                    .ToList();

                int passed = summary?.PassCount
                    ?? serializedTests.Count(t => string.Equals(t.State, "Passed", StringComparison.OrdinalIgnoreCase));
                int failed = summary?.FailCount
                    ?? serializedTests.Count(t => string.Equals(t.State, "Failed", StringComparison.OrdinalIgnoreCase));
                int skipped = summary?.SkipCount
                    ?? serializedTests.Count(t => string.Equals(t.State, "Skipped", StringComparison.OrdinalIgnoreCase));

                double duration = summary?.Duration
                    ?? serializedTests.Sum(t => t.DurationSeconds);

                int total = summary != null
                    ? passed + failed + skipped
                    : serializedTests.Count;

                return new TestRunResult(
                    total,
                    passed,
                    failed,
                    skipped,
                    duration,
                    summary?.ResultState ?? "Unknown",
                    serializedTests);
            }
        }

        internal sealed class TestRunTestResult
        {
            private TestRunTestResult(
                string name,
                string fullName,
                string state,
                double duration,
                string message,
                string stackTrace,
                string output)
            {
                Name = name;
                FullName = fullName;
                State = state;
                DurationSeconds = duration;
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

            public object ToSerializable()
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

            public static TestRunTestResult FromAdaptor(ITestResultAdaptor adaptor)
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
}
