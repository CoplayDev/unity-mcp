using System;
using System.Threading.Tasks;
using NUnit.Framework;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Services.Transport;
using UnityEditor;

namespace MCPForUnityTests.Editor.Services
{
    /// <summary>
    /// Tests for the HTTP reload-resume flag semantics (#1229): the flag must survive
    /// multi-pass compile boundaries and only be consumed on success, cancel, or exhaustion.
    /// Uses fake transports and a zero-delay retry schedule so every path completes
    /// synchronously (UTF 1.1 cannot run async tests).
    /// </summary>
    public class HttpBridgeReloadHandlerTests
    {
        private static readonly TimeSpan[] ZeroSchedule =
        {
            TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero
        };

        private FakeTransportClient _fakeClient;
        private TransportManager _manager;
        private TransportManager _savedManager;
        private bool _savedResumeFlag;
        private bool _savedUseHttpTransport;

        [SetUp]
        public void SetUp()
        {
            _savedResumeFlag = SessionState.GetBool(HttpBridgeReloadHandler.ResumeSessionKey, false);
            _savedUseHttpTransport = EditorPrefs.GetBool(EditorPrefKeys.UseHttpTransport, true);
            _savedManager = MCPServiceLocator.TransportManager;

            SessionState.EraseBool(HttpBridgeReloadHandler.ResumeSessionKey);

            _fakeClient = new FakeTransportClient();
            _manager = new TransportManager();
            _manager.Configure(() => _fakeClient, () => _fakeClient);
            MCPServiceLocator.Register(_manager);

            EditorPrefs.SetBool(EditorPrefKeys.UseHttpTransport, true);
            EditorConfigurationCache.Instance.Refresh();
        }

        [TearDown]
        public void TearDown()
        {
            // Stored-false and absent are indistinguishable: every read uses GetBool(key, false).
            SessionState.SetBool(HttpBridgeReloadHandler.ResumeSessionKey, _savedResumeFlag);
            EditorPrefs.SetBool(EditorPrefKeys.UseHttpTransport, _savedUseHttpTransport);
            EditorConfigurationCache.Instance.Refresh();
            MCPServiceLocator.Register(_savedManager);
        }

        private static bool ResumeFlagSet =>
            SessionState.GetBool(HttpBridgeReloadHandler.ResumeSessionKey, false);

        private void StartBridge()
        {
            Task<bool> start = _manager.StartAsync(TransportMode.Http);
            Assert.IsTrue(start.IsCompleted && start.Result, "fake bridge should start synchronously");
        }

        [Test]
        public void BeforeReloadCore_BridgeRunning_SetsFlagAndForceStops()
        {
            StartBridge();

            HttpBridgeReloadHandler.OnBeforeAssemblyReloadCore(_manager);

            Assert.IsTrue(ResumeFlagSet, "flag should be set when the bridge was running");
            Assert.IsFalse(_manager.IsRunning(TransportMode.Http), "bridge should be force-stopped before reload");
        }

        [Test]
        public void BeforeReloadCore_BridgeNotRunning_PreservesPendingFlag()
        {
            SessionState.SetBool(HttpBridgeReloadHandler.ResumeSessionKey, true);

            HttpBridgeReloadHandler.OnBeforeAssemblyReloadCore(_manager);

            Assert.IsTrue(ResumeFlagSet,
                "a pending resume must survive a reload boundary where the bridge is down (#1229 multi-pass compile)");
        }

        [Test]
        public void AfterReloadCore_NoFlag_DoesNotResume()
        {
            Assert.IsFalse(HttpBridgeReloadHandler.OnAfterAssemblyReloadCore());
        }

        [Test]
        public void AfterReloadCore_FlagSetHttpSelected_ResumesAndKeepsFlag()
        {
            SessionState.SetBool(HttpBridgeReloadHandler.ResumeSessionKey, true);

            Assert.IsTrue(HttpBridgeReloadHandler.OnAfterAssemblyReloadCore());
            Assert.IsTrue(ResumeFlagSet, "flag is only consumed when the resume actually succeeds");
        }

        [Test]
        public void AfterReloadCore_FlagSetStdioSelected_ClearsFlagAndSkips()
        {
            SessionState.SetBool(HttpBridgeReloadHandler.ResumeSessionKey, true);
            EditorPrefs.SetBool(EditorPrefKeys.UseHttpTransport, false);
            EditorConfigurationCache.Instance.Refresh();

            Assert.IsFalse(HttpBridgeReloadHandler.OnAfterAssemblyReloadCore());
            Assert.IsFalse(ResumeFlagSet, "switching transports cancels the pending resume");
        }

        [Test]
        public void Resume_Success_ConnectsAndClearsFlag()
        {
            SessionState.SetBool(HttpBridgeReloadHandler.ResumeSessionKey, true);

            Task resume = HttpBridgeReloadHandler.ResumeHttpWithRetriesAsync(ZeroSchedule);

            Assert.IsTrue(resume.IsCompleted, "resume should complete synchronously with fakes");
            Assert.IsTrue(_manager.IsRunning(TransportMode.Http));
            Assert.AreEqual(1, _fakeClient.StartCalls);
            Assert.IsFalse(ResumeFlagSet);
        }

        [Test]
        public void Resume_Exhaustion_ClearsFlagAfterAllAttempts()
        {
            _fakeClient.StartResult = false;
            SessionState.SetBool(HttpBridgeReloadHandler.ResumeSessionKey, true);

            Task resume = HttpBridgeReloadHandler.ResumeHttpWithRetriesAsync(ZeroSchedule);

            Assert.IsTrue(resume.IsCompleted, "resume should complete synchronously with fakes");
            Assert.AreEqual(ZeroSchedule.Length, _fakeClient.StartCalls);
            Assert.IsFalse(ResumeFlagSet,
                "exhaustion erases the flag so later reload boundaries don't replay the failure loop");
        }

        [Test]
        public void Resume_FlagErasedMidLoop_StopsRetrying()
        {
            _fakeClient.StartResult = false;
            // Simulates End Session / transport switch cancelling while a retry is in flight.
            _fakeClient.OnStart = () => SessionState.EraseBool(HttpBridgeReloadHandler.ResumeSessionKey);
            SessionState.SetBool(HttpBridgeReloadHandler.ResumeSessionKey, true);

            Task resume = HttpBridgeReloadHandler.ResumeHttpWithRetriesAsync(ZeroSchedule);

            Assert.IsTrue(resume.IsCompleted, "resume should complete synchronously with fakes");
            Assert.AreEqual(1, _fakeClient.StartCalls, "erasing the flag must abort the retry loop");
        }

        [Test]
        public void Resume_BridgeAlreadyRunning_ClearsFlagWithoutRestart()
        {
            StartBridge();
            SessionState.SetBool(HttpBridgeReloadHandler.ResumeSessionKey, true);

            Task resume = HttpBridgeReloadHandler.ResumeHttpWithRetriesAsync(ZeroSchedule);

            Assert.IsTrue(resume.IsCompleted, "resume should complete synchronously with fakes");
            Assert.AreEqual(1, _fakeClient.StartCalls,
                "a session established while the resume waited must not be bounced");
            Assert.IsFalse(ResumeFlagSet);
        }

        [Test]
        public void Scenario_MultiPassCompile_ResumesAtSecondBoundary()
        {
            // Pass 1 begins: bridge running when the first reload hits.
            StartBridge();
            HttpBridgeReloadHandler.OnBeforeAssemblyReloadCore(_manager);
            Assert.IsTrue(ResumeFlagSet);
            Assert.IsFalse(_manager.IsRunning(TransportMode.Http));

            // After pass 1: still compiling, so the resume tick defers — flag must not be consumed.
            Assert.IsTrue(HttpBridgeReloadHandler.OnAfterAssemblyReloadCore());
            Assert.IsTrue(ResumeFlagSet);

            // Pass 2 begins with the bridge down. The old code deleted the flag at this
            // boundary, which is exactly how #1229 lost the resume permanently.
            HttpBridgeReloadHandler.OnBeforeAssemblyReloadCore(_manager);
            Assert.IsTrue(ResumeFlagSet);

            // After pass 2: editor idle, resume runs and reconnects.
            Assert.IsTrue(HttpBridgeReloadHandler.OnAfterAssemblyReloadCore());
            Task resume = HttpBridgeReloadHandler.ResumeHttpWithRetriesAsync(ZeroSchedule);
            Assert.IsTrue(resume.IsCompleted, "resume should complete synchronously with fakes");
            Assert.IsTrue(_manager.IsRunning(TransportMode.Http));
            Assert.IsFalse(ResumeFlagSet);
        }
    }
}
