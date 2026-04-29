using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using MCPForUnity.Editor.Services;

namespace MCPForUnityTests.Editor.Services
{
    /// <summary>
    /// Tests for TestJobManager's per-job InitTimeoutMs feature.
    /// Uses reflection to manipulate internal state since StartJob triggers a real test run.
    /// </summary>
    public class TestJobManagerInitTimeoutTests
    {
        private FieldInfo _jobsField;
        private FieldInfo _currentJobIdField;
        private MethodInfo _getJobMethod;
        private MethodInfo _persistMethod;
        private MethodInfo _restoreMethod;
        private Type _testJobType;

        private string _originalJobId;

        [SetUp]
        public void SetUp()
        {
            var asm = typeof(MCPServiceLocator).Assembly;
            var managerType = asm.GetType("MCPForUnity.Editor.Services.TestJobManager");
            Assert.NotNull(managerType, "Could not find TestJobManager");

            _testJobType = asm.GetType("MCPForUnity.Editor.Services.TestJob");
            Assert.NotNull(_testJobType, "Could not find TestJob");

            _jobsField = managerType.GetField("Jobs", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(_jobsField, "Could not find Jobs field");

            _currentJobIdField = managerType.GetField("_currentJobId", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(_currentJobIdField, "Could not find _currentJobId field");

            _getJobMethod = managerType.GetMethod("GetJob", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(_getJobMethod, "Could not find GetJob method");

            _persistMethod = managerType.GetMethod("PersistToSessionState", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(_persistMethod, "Could not find PersistToSessionState method");

            _restoreMethod = managerType.GetMethod("TryRestoreFromSessionState", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(_restoreMethod, "Could not find TryRestoreFromSessionState method");

            // Snapshot original state
            _originalJobId = _currentJobIdField.GetValue(null) as string;
            // We'll restore _currentJobId in TearDown; Jobs dictionary is shared static state
        }

        [TearDown]
        public void TearDown()
        {
            // Restore original state
            _currentJobIdField.SetValue(null, _originalJobId);
            // Clean up any test jobs we inserted
            var jobs = _jobsField.GetValue(null) as System.Collections.IDictionary;
            jobs?.Remove("test-init-timeout-job");
            jobs?.Remove("test-init-timeout-default");
            jobs?.Remove("test-init-timeout-persist");
            // Flush cleaned state to SessionState so synthetic jobs don't survive domain reloads.
            // The persist test writes to SessionState; without this, the stub job would be
            // restored on the next [InitializeOnLoadMethod] and pollute later test runs.
            _persistMethod.Invoke(null, new object[] { true });
        }

        [Test]
        public void GetJob_WithCustomInitTimeout_UsesPerJobTimeout()
        {
            // Arrange: insert a job with a custom init timeout and a start time far enough in the
            // past to exceed the default 15s but within the custom 120s.
            var jobs = _jobsField.GetValue(null) as System.Collections.IDictionary;
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var job = Activator.CreateInstance(_testJobType);
            _testJobType.GetProperty("JobId").SetValue(job, "test-init-timeout-job");
            _testJobType.GetProperty("Status").SetValue(job, TestJobStatus.Running);
            _testJobType.GetProperty("Mode").SetValue(job, "PlayMode");
            _testJobType.GetProperty("StartedUnixMs").SetValue(job, now - 30_000); // 30s ago
            _testJobType.GetProperty("LastUpdateUnixMs").SetValue(job, now - 30_000);
            _testJobType.GetProperty("TotalTests").SetValue(job, null); // Not initialized yet
            _testJobType.GetProperty("InitTimeoutMs").SetValue(job, 120_000L); // 120s custom timeout
            _testJobType.GetProperty("FailuresSoFar").SetValue(job, new List<TestJobFailure>());

            jobs["test-init-timeout-job"] = job;
            _currentJobIdField.SetValue(null, "test-init-timeout-job");

            // Act: GetJob should NOT auto-fail because 30s < 120s custom timeout
            var result = _getJobMethod.Invoke(null, new object[] { "test-init-timeout-job" });

            // Assert: job should still be running
            var status = (TestJobStatus)_testJobType.GetProperty("Status").GetValue(result);
            Assert.AreEqual(TestJobStatus.Running, status,
                "Job with 120s custom timeout should not auto-fail after 30s");
        }

        [Test]
        public void GetJob_WithDefaultTimeout_AutoFailsAfter15Seconds()
        {
            // Arrange: insert a job with InitTimeoutMs=0 (use default) and start time 20s ago
            var jobs = _jobsField.GetValue(null) as System.Collections.IDictionary;
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var job = Activator.CreateInstance(_testJobType);
            _testJobType.GetProperty("JobId").SetValue(job, "test-init-timeout-default");
            _testJobType.GetProperty("Status").SetValue(job, TestJobStatus.Running);
            _testJobType.GetProperty("Mode").SetValue(job, "EditMode");
            _testJobType.GetProperty("StartedUnixMs").SetValue(job, now - 20_000); // 20s ago
            _testJobType.GetProperty("LastUpdateUnixMs").SetValue(job, now - 20_000);
            _testJobType.GetProperty("TotalTests").SetValue(job, null);
            _testJobType.GetProperty("InitTimeoutMs").SetValue(job, 0L); // Use default
            _testJobType.GetProperty("FailuresSoFar").SetValue(job, new List<TestJobFailure>());

            jobs["test-init-timeout-default"] = job;
            _currentJobIdField.SetValue(null, "test-init-timeout-default");

            // Act: GetJob should auto-fail because 20s > 15s default
            var result = _getJobMethod.Invoke(null, new object[] { "test-init-timeout-default" });

            // Assert: job should be failed
            var status = (TestJobStatus)_testJobType.GetProperty("Status").GetValue(result);
            Assert.AreEqual(TestJobStatus.Failed, status,
                "Job with default timeout should auto-fail after 20s");
        }

        [Test]
        public void InitTimeoutMs_SurvivesPersistAndRestore()
        {
            // Arrange: insert a job with custom InitTimeoutMs
            var jobs = _jobsField.GetValue(null) as System.Collections.IDictionary;
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var job = Activator.CreateInstance(_testJobType);
            _testJobType.GetProperty("JobId").SetValue(job, "test-init-timeout-persist");
            _testJobType.GetProperty("Status").SetValue(job, TestJobStatus.Running);
            _testJobType.GetProperty("Mode").SetValue(job, "PlayMode");
            _testJobType.GetProperty("StartedUnixMs").SetValue(job, now);
            _testJobType.GetProperty("LastUpdateUnixMs").SetValue(job, now);
            _testJobType.GetProperty("TotalTests").SetValue(job, null);
            _testJobType.GetProperty("InitTimeoutMs").SetValue(job, 90_000L);
            _testJobType.GetProperty("FailuresSoFar").SetValue(job, new List<TestJobFailure>());

            jobs["test-init-timeout-persist"] = job;
            _currentJobIdField.SetValue(null, "test-init-timeout-persist");

            // Act: persist then restore (simulates domain reload)
            _persistMethod.Invoke(null, new object[] { true });
            // Clear in-memory state
            jobs.Remove("test-init-timeout-persist");
            _currentJobIdField.SetValue(null, null);
            // Restore from SessionState
            _restoreMethod.Invoke(null, null);

            // Assert: restored job should have the same InitTimeoutMs
            var restoredJobs = _jobsField.GetValue(null) as System.Collections.IDictionary;
            Assert.IsTrue(restoredJobs.Contains("test-init-timeout-persist"),
                "Job should be restored from SessionState");

            var restoredJob = restoredJobs["test-init-timeout-persist"];
            var restoredTimeout = (long)_testJobType.GetProperty("InitTimeoutMs").GetValue(restoredJob);
            Assert.AreEqual(90_000L, restoredTimeout,
                "InitTimeoutMs should survive persist/restore cycle");
        }
    }
}
