using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Services.Server;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.TestTools;

namespace MCPForUnityTests.Editor.Services.Server
{
    public class ServerShutdownTests
    {
        bool hadUrl;
        string savedUrl;

        [SetUp]
        public void SetUp()
        {
            hadUrl = EditorPrefs.HasKey(EditorPrefKeys.HttpBaseUrl);
            savedUrl = EditorPrefs.GetString(EditorPrefKeys.HttpBaseUrl);
            EditorPrefs.SetString(EditorPrefKeys.HttpBaseUrl, "http://127.0.0.1:59997");
        }

        [TearDown]
        public void TearDown()
        {
            if (hadUrl) EditorPrefs.SetString(EditorPrefKeys.HttpBaseUrl, savedUrl);
            else EditorPrefs.DeleteKey(EditorPrefKeys.HttpBaseUrl);
        }

        [UnityTest]
        public IEnumerator CooperativeExit_DoesNotTerminateProcess()
        {
            var fixture = new Fixture { ExitDuringWait = true };
            var task = fixture.Service.StopLocalHttpServerAsync();
            Assert.IsFalse(task.IsCompleted, "Shutdown must yield to the Editor");
            while (!task.IsCompleted) yield return null;
            Assert.IsTrue(task.GetAwaiter().GetResult());
            Assert.AreEqual(0, fixture.Terminations);
            Assert.IsTrue(fixture.Deleted && fixture.Cleared);
        }

        [UnityTest]
        public IEnumerator ReplacedPidFile_DoesNotDeleteNewServerTracking()
        {
            var fixture = new Fixture { ExitDuringWait = true, ReplacePidFileDuringWait = true };
            var task = fixture.Service.StopLocalHttpServerAsync();
            while (!task.IsCompleted) yield return null;
            Assert.IsTrue(task.GetAwaiter().GetResult());
            Assert.IsFalse(fixture.Deleted || fixture.Cleared);
            Assert.AreEqual(0, fixture.Terminations);
        }

        [UnityTest]
        public IEnumerator AcknowledgedButStuck_RevalidatesAndTerminates()
        {
            var fixture = new Fixture();
            var task = fixture.Service.StopLocalHttpServerAsync();
            while (!task.IsCompleted) yield return null;
            Assert.IsTrue(task.GetAwaiter().GetResult());
            Assert.AreEqual(1, fixture.Terminations);
            Assert.IsTrue(fixture.IdentityChecked);
        }

        [UnityTest]
        public IEnumerator ChangedIdentity_RefusesToTerminateOrClearTracking()
        {
            var fixture = new Fixture { IdentityMatches = false };
            var task = fixture.Service.StopLocalHttpServerAsync();
            while (!task.IsCompleted) yield return null;
            Assert.IsFalse(task.GetAwaiter().GetResult());
            Assert.AreEqual(0, fixture.Terminations);
            Assert.IsFalse(fixture.Deleted || fixture.Cleared);
        }

        [UnityTest]
        public IEnumerator OlderServer_UsesGuardedFallbackWithoutGracePeriod()
        {
            var fixture = new Fixture { Accepted = false };
            var task = fixture.Service.StopLocalHttpServerAsync();
            while (!task.IsCompleted) yield return null;
            Assert.IsTrue(task.GetAwaiter().GetResult());
            Assert.AreEqual(1, fixture.Terminations);
            Assert.IsFalse(fixture.Waited);
        }

        [UnityTest]
        public IEnumerator ProtectedPid_DoesNotSendShutdownOrTerminate()
        {
            var fixture = new Fixture { Pid = 42 };
            var task = fixture.Service.StopLocalHttpServerAsync();
            while (!task.IsCompleted) yield return null;
            Assert.IsFalse(task.GetAwaiter().GetResult());
            Assert.IsFalse(fixture.Requested);
            Assert.AreEqual(0, fixture.Terminations);
        }

        private class Service : ServerManagementService
        {
            readonly Fixture fixture;
            internal Service(Fixture fixture) : base(fixture, fixture, fixture) { this.fixture = fixture; }
            internal override async Task<bool> RequestShutdownAsync(Uri url, int pid, string token)
            {
                fixture.Requested = true;
                Assert.AreEqual(fixture.Pid, pid);
                Assert.AreEqual("launch-token", token);
                await Task.Yield();
                return fixture.Accepted;
            }
            internal override async Task<bool> WaitForServerExitAsync(int pid)
            {
                fixture.Waited = true;
                await Task.Yield();
                if (fixture.ExitDuringWait) fixture.Alive = false;
                if (fixture.ReplacePidFileDuringWait) fixture.CurrentFilePid = 67890;
                return !fixture.Alive;
            }
            internal override bool TryProcessCommandLineContainsInstanceToken(int pid, string token, out bool matches)
            {
                fixture.IdentityChecked = true;
                matches = fixture.IdentityMatches;
                return true;
            }
        }

        private class Fixture : IProcessDetector, IPidFileManager, IProcessTerminator
        {
            internal readonly Service Service;
            internal bool Accepted = true, IdentityMatches = true, Alive = true;
            internal bool ExitDuringWait, Requested, Waited, IdentityChecked, Deleted, Cleared;
            internal bool ReplacePidFileDuringWait;
            internal int? CurrentFilePid;
            internal int Terminations, Pid = 12345;
            internal Fixture() { Service = new Service(this); }
            public bool Terminate(int pid) { Terminations++; Alive = false; return true; }
            public bool ProcessExists(int pid) => Alive;
            public int GetCurrentProcessId() => 42;
            public bool LooksLikeMcpServerProcess(int pid) => false;
            public List<int> GetListeningProcessIdsForPort(int port) => new List<int> { Pid };
            public string NormalizeForMatch(string value) => value;
            public bool TryGetProcessCommandLine(int pid, out string args) { args = ""; return false; }
            public string GetPidFilePath(int port) => "fixture.pid";
            public string GetPidDirectory() => "fixture";
            public bool TryReadPid(string file, out int pid) { pid = CurrentFilePid ?? Pid; return true; }
            public bool TryGetPortFromPidFilePath(string file, out int port) { port = 59997; return true; }
            public void DeletePidFile(string file) { Deleted = true; }
            public void StoreHandshake(string file, string token) { }
            public bool TryGetHandshake(out string file, out string token)
            { file = "fixture.pid"; token = "launch-token"; return true; }
            public void StoreTracking(int pid, int port, string hash = null) { }
            public bool TryGetStoredPid(int port, out int pid) { pid = 0; return false; }
            public string GetStoredArgsHash() => "";
            public void ClearTracking() { Cleared = true; }
            public string ComputeShortHash(string value) => value;
        }
    }
}
