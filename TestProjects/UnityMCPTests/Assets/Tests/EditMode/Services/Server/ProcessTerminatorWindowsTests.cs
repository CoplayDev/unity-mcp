using System.Collections.Generic;
using MCPForUnity.Editor.Services.Server;
using NUnit.Framework;

namespace MCPForUnityTests.Editor.Services.Server
{
    [Platform("Win")]
    public class ProcessTerminatorWindowsTests
    {
        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(42)]
        public void Terminate_ProtectedPid_DoesNotRunTaskKill(int pid)
        {
            var terminator = new StubTerminator(new StubDetector());

            Assert.IsFalse(terminator.Terminate(pid));
            Assert.IsEmpty(terminator.ForcedCalls);
        }

        [TestCase(true, true, true)]
        [TestCase(true, false, false)]
        [TestCase(false, true, true)]
        [TestCase(false, false, false)]
        public void Terminate_FallbackReportsActualExit(bool commandSucceeded, bool exits, bool expected)
        {
            var terminator = new StubTerminator(new StubDetector())
                { ForcedSucceeds = commandSucceeded, ExitsOnForce = exits };
            Assert.AreEqual(expected, terminator.Terminate(12345));
            CollectionAssert.AreEqual(new[] { true }, terminator.ForcedCalls);
            Assert.IsTrue(terminator.Waited);
            Assert.AreEqual(1000, terminator.WaitTimeoutMilliseconds);
        }

        [Test]
        public void Terminate_WhenForcedExitCompletesLater_ReportsSuccess()
        {
            var detector = new StubDetector { ChecksUntilExit = 3 };
            var terminator = new StubTerminator(detector);
            Assert.IsTrue(terminator.Terminate(12345));
            Assert.IsFalse(detector.Alive);
            Assert.AreEqual(1000, terminator.WaitTimeoutMilliseconds);
        }

        [Test]
        public void Terminate_WhenProcessIsAlreadyAbsent_DoesNotRunTaskKill()
        {
            var terminator = new StubTerminator(new StubDetector { Alive = false });

            Assert.IsFalse(terminator.Terminate(12345));
            Assert.IsEmpty(terminator.ForcedCalls);
        }

        [Test]
        public void ProcessExists_CurrentEditor_ReturnsTrue()
        {
            var detector = new ProcessDetector();
            Assert.IsTrue(detector.ProcessExists(detector.GetCurrentProcessId()));
        }

        [Test]
        public void ProcessExists_MissingPid_ReturnsFalse()
        {
            Assert.IsFalse(new ProcessDetector().ProcessExists(int.MaxValue));
        }

        private class StubTerminator : ProcessTerminator
        {
            readonly StubDetector detector;
            internal readonly List<bool> ForcedCalls = new List<bool>();
            internal bool ExitsOnForce;
            internal bool ForcedSucceeds = true;
            internal bool Waited;
            internal int WaitTimeoutMilliseconds;
            internal StubTerminator(StubDetector detector) : base(detector) { this.detector = detector; }
            internal override bool RunTaskKill(int pid, bool force)
            {
                ForcedCalls.Add(force);
                if (ExitsOnForce) detector.Alive = false;
                return ForcedSucceeds;
            }
            internal override bool WaitForProcessExit(int pid, int timeoutMilliseconds = 8000)
            {
                Waited = true;
                WaitTimeoutMilliseconds = timeoutMilliseconds;
                return base.WaitForProcessExit(pid, timeoutMilliseconds);
            }
        }

        private class StubDetector : IProcessDetector
        {
            internal bool Alive = true;
            internal int ChecksUntilExit;
            public bool ProcessExists(int pid)
            {
                if (ChecksUntilExit > 0 && --ChecksUntilExit == 0) Alive = false;
                return Alive;
            }
            public int GetCurrentProcessId() => 42;
            public bool LooksLikeMcpServerProcess(int pid) => true;
            public List<int> GetListeningProcessIdsForPort(int port) => new List<int>();
            public string NormalizeForMatch(string input) => input;
            public bool TryGetProcessCommandLine(int pid, out string argsLower)
            {
                argsLower = string.Empty;
                return false;
            }
        }
    }
}
