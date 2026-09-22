using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MCPForUnity.Editor.Services;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.TestTools;
using RunState = UnityEditor.TestTools.TestRunner.Api.RunState;
using TestStatus = UnityEditor.TestTools.TestRunner.Api.TestStatus;

namespace MCPForUnityTests.Editor.Services
{
    /// <summary>
    /// Exercises reload recovery without starting a nested Unity test run. The synthetic
    /// callbacks have the same result tree contract as TestRunnerApi.RunFinished.
    /// </summary>
    public class TestJobManagerLifecycleTests
    {
        private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
        private const string JobsKey = "MCPForUnity.TestJobsV1";
        private const string CurrentKey = "MCPForUnity.CurrentTestJobIdV1";
        private const string GuardPrefix = "MCPForUnity.PlayModeOptions.";
        private const string MarkerPath = "Library/MCPPlayModeOptionsBackup.txt";
        private Dictionary<string, TestJob> _jobs;
        private Dictionary<string, TestJob> _originalJobs;
        private string _originalCurrent;
        private string _originalSessionJobs;
        private string _originalSessionCurrent;
        private object _originalService;
        private object _originalLastPersist;
        private Dictionary<FieldInfo, object> _originalStatus;
        private bool _originalOptionsEnabled;
        private EnterPlayModeOptions _originalOptions;
        private bool _guardPending;
        private bool _guardEnabled;
        private int _guardOptions;
        private byte[] _marker;

        private static FieldInfo ManagerField(string name) => typeof(TestJobManager).GetField(name, PrivateStatic);
        private static FieldInfo ServiceField => typeof(MCPServiceLocator).GetField("_testRunnerService", PrivateStatic);
        private static void InvokeManager(string name, params object[] args) =>
            typeof(TestJobManager).GetMethod(name, PrivateStatic).Invoke(null, args);

        [SetUp]
        public void SetUp()
        {
            _jobs = (Dictionary<string, TestJob>)ManagerField("Jobs").GetValue(null);
            _originalJobs = new Dictionary<string, TestJob>(_jobs);
            _originalCurrent = TestJobManager.CurrentJobId;
            _originalSessionJobs = SessionState.GetString(JobsKey, string.Empty);
            _originalSessionCurrent = SessionState.GetString(CurrentKey, string.Empty);
            _originalLastPersist = ManagerField("_lastPersistUnixMs").GetValue(null);
            _originalService = ServiceField.GetValue(null);
            _originalStatus = typeof(TestRunStatus).GetFields(PrivateStatic)
                .Where(f => !f.IsInitOnly).ToDictionary(f => f, f => f.GetValue(null));
            _originalOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            _originalOptions = EditorSettings.enterPlayModeOptions;
            _guardPending = SessionState.GetBool(GuardPrefix + "PendingRestore", false);
            _guardEnabled = SessionState.GetBool(GuardPrefix + "OriginalEnabled", false);
            _guardOptions = SessionState.GetInt(GuardPrefix + "OriginalOptions", 0);
            _marker = File.Exists(MarkerPath) ? File.ReadAllBytes(MarkerPath) : null;
            _jobs.Clear();
            ManagerField("_currentJobId").SetValue(null, null);
            ServiceField.SetValue(null, null);
            TestRunStatus.MarkFinished();
            PlayModeOptionsGuard.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            // Do not reset the whole service locator: the outer Editor test run may own it.
            (ServiceField.GetValue(null) as IDisposable)?.Dispose();
            ServiceField.SetValue(null, _originalService);
            _jobs.Clear();
            foreach (var entry in _originalJobs) _jobs.Add(entry.Key, entry.Value);
            ManagerField("_currentJobId").SetValue(null, _originalCurrent);
            ManagerField("_lastPersistUnixMs").SetValue(null, _originalLastPersist);
            SessionState.SetString(JobsKey, _originalSessionJobs);
            SessionState.SetString(CurrentKey, _originalSessionCurrent);
            foreach (var entry in _originalStatus) entry.Key.SetValue(null, entry.Value);
            EditorSettings.enterPlayModeOptions = _originalOptions;
            EditorSettings.enterPlayModeOptionsEnabled = _originalOptionsEnabled;
            SessionState.SetBool(GuardPrefix + "PendingRestore", _guardPending);
            SessionState.SetBool(GuardPrefix + "OriginalEnabled", _guardEnabled);
            SessionState.SetInt(GuardPrefix + "OriginalOptions", _guardOptions);
            if (_marker == null)
            {
                if (File.Exists(MarkerPath)) File.Delete(MarkerPath);
            }
            else File.WriteAllBytes(MarkerPath, _marker);
        }

        private TestJob AddJob(string id = "lifecycle-job")
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var job = new TestJob
            {
                JobId = id, Mode = "EditMode", Status = TestJobStatus.Running,
                StartedUnixMs = now, LastUpdateUnixMs = now,
                FailuresSoFar = new List<TestJobFailure>()
            };
            _jobs[id] = job;
            ManagerField("_currentJobId").SetValue(null, id);
            return job;
        }

        private TestRunnerService RestoreCallbacks()
        {
            InvokeManager("RestoreRunningJobCallbacks");
            return ServiceField.GetValue(null) as TestRunnerService;
        }

        [Test]
        public void RestoreRunningJob_RecreatesLazyServiceOnce_WithoutRestartingRun()
        {
            var job = AddJob();
            job.CompletedTests = 3;
            job.TotalTests = 8;
            InvokeManager("PersistToSessionState", true);
            _jobs.Clear();
            ManagerField("_currentJobId").SetValue(null, null);
            InvokeManager("TryRestoreFromSessionState");

            var service = RestoreCallbacks();
            Assert.NotNull(service);
            Assert.AreSame(service, RestoreCallbacks(), "Repeated recovery must not register a second service.");
            Assert.IsTrue(TestRunStatus.IsRunning);
            Assert.AreEqual(TestMode.EditMode, TestRunStatus.Mode);
            Assert.AreEqual(3, _jobs[job.JobId].CompletedTests, "Recovery must not reset progress or execute tests again.");
            Assert.AreEqual(8, _jobs[job.JobId].TotalTests);
        }

        [Test]
        public void RestoreCallbacks_WithoutRunningJob_DoesNotCreateService()
        {
            Assert.IsNull(RestoreCallbacks());
            Assert.IsFalse(TestRunStatus.IsRunning);
        }

        [Test]
        public void BeforeReload_FlushesProgressDespitePersistenceThrottle()
        {
            var job = AddJob();
            InvokeManager("PersistToSessionState", true);
            ManagerField("_lastPersistUnixMs").SetValue(null, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 1000);
            TestJobManager.OnLeafTestFinished("Fixture.LastTest", true, "assertion failed");
            Assert.AreEqual(0, (int)JObject.Parse(SessionState.GetString(JobsKey, ""))["jobs"][0]["completed_tests"]);

            InvokeManager("BeforeAssemblyReload");
            _jobs.Clear();
            InvokeManager("TryRestoreFromSessionState");

            Assert.AreEqual(1, _jobs[job.JobId].CompletedTests);
            Assert.AreEqual("Fixture.LastTest", _jobs[job.JobId].LastFinishedTestFullName);
            Assert.AreEqual("assertion failed", _jobs[job.JobId].FailuresSoFar.Single().Message);
        }

        [Test]
        public void RecoveredRunFinished_FinalizesWithoutOriginalTask_AndRetainsEarlierResults()
        {
            var job = AddJob();
            var service = RestoreCallbacks();
            var passedBeforeReload = new ResultStub("Fixture.BeforeReload", "Passed");
            var failedAfterReload = new ResultStub("Fixture.AfterReload", "Failed", "assertion failed");
            service.TestFinished(failedAfterReload);
            service.RunFinished(ResultStub.Suite(passedBeforeReload, failedAfterReload));

            Assert.AreEqual(TestJobStatus.Failed, job.Status);
            Assert.IsNull(TestJobManager.CurrentJobId);
            Assert.IsFalse(TestRunStatus.IsRunning);
            Assert.AreEqual(2, job.CompletedTests);
            Assert.AreEqual(2, job.Result.Results.Count);
            var payload = JObject.FromObject(TestJobManager.ToSerializable(job, false, true));
            Assert.AreEqual(1, (int)payload["result"]["summary"]["failed"]);
            Assert.AreEqual("assertion failed", (string)payload["result"]["results"][0]["message"]);
        }

        [Test]
        public void LateCallbacksFromRecoveredJob_DoNotMutateNewJobOrItsSettings()
        {
            AddJob("old-job");
            var service = RestoreCallbacks();
            var newer = AddJob("new-job");
            newer.CompletedTests = 7;
            newer.TotalTests = 9;
            PlayModeOptionsGuard.Save(false, EnterPlayModeOptions.None);
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;

            service.RunStarted(null);
            service.TestFinished(new ResultStub("Old.Finished", "Failed"));
            service.RunFinished(ResultStub.Suite(new ResultStub("Old.Finished", "Failed")));

            Assert.AreEqual("new-job", TestJobManager.CurrentJobId);
            Assert.AreEqual(TestJobStatus.Running, newer.Status);
            Assert.AreEqual(7, newer.CompletedTests);
            Assert.AreEqual(9, newer.TotalTests);
            Assert.IsTrue(TestRunStatus.IsRunning);
            Assert.IsTrue(PlayModeOptionsGuard.IsPending);
            Assert.IsTrue(EditorSettings.enterPlayModeOptionsEnabled);
            Assert.AreEqual(EnterPlayModeOptions.DisableDomainReload, EditorSettings.enterPlayModeOptions);
        }

        [Test]
        public void RecoveredRun_CannotStartOverlappingRunAfterJobIsCleared()
        {
            AddJob();
            var service = RestoreCallbacks();
            TestJobManager.ClearStuckJob();
            Assert.Throws<InvalidOperationException>(() => service.RunTestsAsync(TestMode.EditMode).GetAwaiter().GetResult());
        }

        [Test]
        public void RecoveredInitializationError_FailsJobAndRestoresSettings()
        {
            var job = AddJob();
            var service = RestoreCallbacks();
            PlayModeOptionsGuard.Save(false, EnterPlayModeOptions.None);
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;

            ((IErrorCallbacks)service).OnError("Prebuild setup failed");

            Assert.AreEqual(TestJobStatus.Failed, job.Status);
            Assert.AreEqual("Prebuild setup failed", job.Error);
            Assert.IsNull(TestJobManager.CurrentJobId);
            Assert.IsFalse(TestRunStatus.IsRunning);
            Assert.IsFalse(PlayModeOptionsGuard.IsPending);
            Assert.IsFalse(EditorSettings.enterPlayModeOptionsEnabled);
        }

        [UnityTest]
        public IEnumerator ClearedRecoveredRun_AfterRunFinished_CanStartNextRun() => CheckRecoveredRunRestart(false);

        [UnityTest]
        public IEnumerator ClearedRecoveredRun_AfterError_CanStartNextRun() => CheckRecoveredRunRestart(true);

        private IEnumerator CheckRecoveredRunRestart(bool error)
        {
            AddJob("old-job");
            var service = RestoreCallbacks();
            TestJobManager.ClearStuckJob();
            if (error) service.OnError("Old initialization failed");
            else service.RunFinished(ResultStub.Suite(new ResultStub("Old.Pass", "Passed")));

            // Replace only this API instance's scheduler so this does not execute a nested
            // test run. The project pins Test Framework 1.1.33, which exposes this test seam.
            var api = typeof(TestRunnerService).GetField("_testRunnerApi", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(service);
            var scheduler = typeof(TestRunnerApi).GetField("ScheduleJob", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(scheduler);
            bool scheduled = false;
            scheduler.SetValue(api, new Func<ExecutionSettings, string>(_ => { scheduled = true; return "synthetic-run"; }));
            var nextJob = AddJob("next-job");
            var pending = service.RunTestsAsync(TestMode.EditMode);
            Assert.IsTrue(scheduled, "Terminal callbacks must release recovered ownership.");
            service.RunFinished(ResultStub.Suite(new ResultStub("Next.Pass", "Passed")));
            double deadline = EditorApplication.timeSinceStartup + 5;
            while (!pending.IsCompleted && EditorApplication.timeSinceStartup < deadline) yield return null;
            Assert.IsTrue(pending.IsCompleted, "Completion must release the original async waiter.");
            var result = pending.GetAwaiter().GetResult();
            Assert.AreEqual(1, result.Passed);
            Assert.AreEqual(TestJobStatus.Succeeded, nextJob.Status);
        }

        [Test]
        public void ResultTree_ExcludesEmptySuitesFromIndividualResults()
        {
            var emptySuite = new ResultStub("Fixture.EmptySuite", "Failed") { IsSuite = true };
            var leaf = new ResultStub("Fixture.Test", "Passed");
            var result = TestRunResult.Create(ResultStub.Suite(emptySuite, leaf), new ITestResultAdaptor[] { emptySuite });
            Assert.AreEqual(1, result.Results.Count);
            Assert.AreEqual("Fixture.Test", result.Results[0].FullName);
        }

        [Test]
        public void PlayModeGuard_KeepsOriginalSettingsBackupUntilRecoveredRunFinishes()
        {
            AddJob();
            PlayModeOptionsGuard.Save(false, EnterPlayModeOptions.None);
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            // TestRunStatus is transient and initially false after a reload.
            PlayModeOptionsGuard.RestoreIfIdle();
            Assert.IsTrue(PlayModeOptionsGuard.IsPending);
            Assert.IsTrue(EditorSettings.enterPlayModeOptionsEnabled);
            var service = RestoreCallbacks();
            service.RunFinished(ResultStub.Suite(new ResultStub("Fixture.Pass", "Passed")));
            Assert.IsFalse(PlayModeOptionsGuard.IsPending);
            Assert.IsFalse(EditorSettings.enterPlayModeOptionsEnabled);
            Assert.AreEqual(EnterPlayModeOptions.None, EditorSettings.enterPlayModeOptions);
        }

        [Test]
        public void FailedRun_ReturnsSummaryWithoutDetailsUnlessRequested()
        {
            var job = AddJob();
            job.Status = TestJobStatus.Failed;
            job.Result = TestRunResult.Create(ResultStub.Suite(new ResultStub("Fixture.Fail", "Failed")), Array.Empty<ITestResultAdaptor>());
            var payload = JObject.FromObject(TestJobManager.ToSerializable(job, false, false));
            Assert.AreEqual("failed", (string)payload["status"]);
            Assert.AreEqual(1, (int)payload["result"]["summary"]["failed"]);
            Assert.AreEqual(JTokenType.Null, payload["result"]["results"].Type);
        }

        [Test]
        public void Snapshot_ReportsActualRunInBackgroundSetting()
        {
            var payload = JObject.FromObject(TestJobManager.ToSerializable(AddJob(), false, false));
            Assert.AreEqual(Application.runInBackground, (bool)payload["progress"]["run_in_background"]);
        }

        [Test]
        public void NoThrottle_ErrorBeforeRunStarted_RestoresCapturedPreferences()
        {
            const string activeKey = "TestRunnerNoThrottle_TestRunActive";
            const string capturedKey = "TestRunnerNoThrottle_SettingsCaptured";
            const string idleKey = "TestRunnerNoThrottle_PrevIdleTime";
            const string modeKey = "TestRunnerNoThrottle_PrevInteractionMode";
            bool active = SessionState.GetBool(activeKey, false);
            bool captured = SessionState.GetBool(capturedKey, false);
            int previousIdle = SessionState.GetInt(idleKey, 4);
            int previousMode = SessionState.GetInt(modeKey, 0);
            bool hadIdle = EditorPrefs.HasKey("ApplicationIdleTime");
            bool hadMode = EditorPrefs.HasKey("InteractionMode");
            int idle = EditorPrefs.GetInt("ApplicationIdleTime", 4);
            int mode = EditorPrefs.GetInt("InteractionMode", 0);
            try
            {
                SessionState.SetBool(capturedKey, false);
                EditorPrefs.SetInt("ApplicationIdleTime", 7);
                EditorPrefs.SetInt("InteractionMode", 0);
                TestRunnerNoThrottle.ApplyNoThrottlingPreemptive();
                Assert.AreEqual(0, EditorPrefs.GetInt("ApplicationIdleTime"));
                Assert.AreEqual(1, EditorPrefs.GetInt("InteractionMode"));

                var callbackType = typeof(TestRunnerNoThrottle).GetNestedType("TestCallbacks", BindingFlags.NonPublic);
                var callback = Activator.CreateInstance(callbackType, true) as IErrorCallbacks;
                Assert.NotNull(callback, "Initialization errors must be observed even before RunStarted.");
                callback.OnError("Prebuild setup failed");

                Assert.AreEqual(7, EditorPrefs.GetInt("ApplicationIdleTime"));
                Assert.AreEqual(0, EditorPrefs.GetInt("InteractionMode"));
                Assert.IsFalse(SessionState.GetBool(activeKey, true));
                Assert.IsFalse(SessionState.GetBool(capturedKey, true));
            }
            finally
            {
                SessionState.SetBool(activeKey, active);
                SessionState.SetBool(capturedKey, captured);
                SessionState.SetInt(idleKey, previousIdle);
                SessionState.SetInt(modeKey, previousMode);
                if (hadIdle) EditorPrefs.SetInt("ApplicationIdleTime", idle);
                else EditorPrefs.DeleteKey("ApplicationIdleTime");
                if (hadMode) EditorPrefs.SetInt("InteractionMode", mode);
                else EditorPrefs.DeleteKey("InteractionMode");
                typeof(TestRunnerNoThrottle).GetMethod("ForceEditorToApplyInteractionPrefs", PrivateStatic).Invoke(null, null);
            }
        }

        private sealed class ResultStub : ITestResultAdaptor
        {
            private readonly ITestResultAdaptor[] _children;
            public ResultStub(string name, string state, string message = null, params ITestResultAdaptor[] children)
            {
                Name = FullName = name;
                ResultState = state;
                Message = message;
                _children = children;
            }
            public static ResultStub Suite(params ITestResultAdaptor[] children) => new ResultStub("Suite", "Failed", null, children);
            public bool IsSuite { get; set; }
            public ITestAdaptor Test => new TestStub(FullName, IsSuite || HasChildren);
            public string Name { get; }
            public string FullName { get; }
            public string ResultState { get; }
            public TestStatus TestStatus => ResultState == "Passed" ? TestStatus.Passed : TestStatus.Failed;
            public double Duration => 0.1;
            public DateTime StartTime => DateTime.UtcNow;
            public DateTime EndTime => DateTime.UtcNow;
            public string Message { get; }
            public string StackTrace => "Fixture.cs:12";
            public int AssertCount => 1;
            public int FailCount => HasChildren ? _children.Sum(c => c.FailCount) : ResultState == "Failed" ? 1 : 0;
            public int PassCount => HasChildren ? _children.Sum(c => c.PassCount) : ResultState == "Passed" ? 1 : 0;
            public int SkipCount => 0;
            public int InconclusiveCount => 0;
            public bool HasChildren => _children.Length > 0;
            public IEnumerable<ITestResultAdaptor> Children => _children;
            public string Output => "test output";
            public TNode ToXml() => new TNode("test-case");
        }

        private sealed class TestStub : ITestAdaptor
        {
            public TestStub(string name, bool isSuite) { Name = name; IsSuite = isSuite; }
            public string Id => Name;
            public string Name { get; }
            public string FullName => Name;
            public int TestCaseCount => IsSuite ? 0 : 1;
            public bool HasChildren => false;
            public bool IsSuite { get; }
            public IEnumerable<ITestAdaptor> Children => Array.Empty<ITestAdaptor>();
            public ITestAdaptor Parent => null;
            public int TestCaseTimeout => 0;
            public ITypeInfo TypeInfo => null;
            public IMethodInfo Method => null;
            public string[] Categories => Array.Empty<string>();
            public bool IsTestAssembly => false;
            public RunState RunState => RunState.Runnable;
            public string Description => null;
            public string SkipReason => null;
            public string ParentId => null;
            public string ParentFullName => null;
            public string UniqueName => Name;
            public string ParentUniqueName => null;
            public int ChildIndex => 0;
            public TestMode TestMode => TestMode.EditMode;
        }
    }
}
