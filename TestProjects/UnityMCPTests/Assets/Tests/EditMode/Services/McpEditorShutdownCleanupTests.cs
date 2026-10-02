using System;
using NUnit.Framework;
using MCPForUnity.Editor.Services;
using UnityEngine;

namespace MCPForUnityTests.Editor.Services
{
    [TestFixture]
    public class McpEditorShutdownCleanupTests
    {
        [Test]
        public void ShouldRunCleanup_InteractiveEditor_RunsCleanup()
        {
            Assert.IsTrue(McpEditorShutdownCleanup.ShouldRunCleanup(isBatchMode: false, allowBatchEnv: null));
        }

        [Test]
        public void ShouldRunCleanup_BatchWithoutOverride_IsNoOp()
        {
            // Regression for #1196/#1010: a -batchmode/CI instance must not stop the
            // interactive editor's server resolved via the global pidfile+port handshake.
            Assert.IsFalse(McpEditorShutdownCleanup.ShouldRunCleanup(isBatchMode: true, allowBatchEnv: null));
        }

        [Test]
        public void ShouldRunCleanup_BatchWithBlankOverride_IsNoOp()
        {
            // Whitespace is treated as unset, mirroring string.IsNullOrWhiteSpace in the sibling guards.
            Assert.IsFalse(McpEditorShutdownCleanup.ShouldRunCleanup(isBatchMode: true, allowBatchEnv: ""));
            Assert.IsFalse(McpEditorShutdownCleanup.ShouldRunCleanup(isBatchMode: true, allowBatchEnv: "   "));
        }

        [Test]
        public void ShouldRunCleanup_BatchWithOverride_RunsCleanup()
        {
            Assert.IsTrue(McpEditorShutdownCleanup.ShouldRunCleanup(isBatchMode: true, allowBatchEnv: "1"));
        }

        [Test]
        public void ShouldRunCleanup_Parameterless_MatchesEnvironment()
        {
            // Proves the wiring to Application.isBatchMode / UNITY_MCP_ALLOW_BATCH is correct
            // without assuming how this test run was launched (GUI Test Runner vs -batchmode CI).
            bool expected = !Application.isBatchMode
                || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("UNITY_MCP_ALLOW_BATCH"));
            Assert.AreEqual(expected, McpEditorShutdownCleanup.ShouldRunCleanup());
        }

        [Test]
        public void ShouldStopManagedServer_NoOtherInstances_Stops()
        {
            // Last one out: the launching editor is the only Unity instance left on the server.
            Assert.IsTrue(McpEditorShutdownCleanup.ShouldStopManagedServer(otherConnectedInstances: 0));
        }

        [Test]
        public void ShouldStopManagedServer_OtherInstancesConnected_LeavesServerRunning()
        {
            // Two editors share one HTTP-local server; the launcher quitting must not disconnect the other.
            Assert.IsFalse(McpEditorShutdownCleanup.ShouldStopManagedServer(otherConnectedInstances: 1));
            Assert.IsFalse(McpEditorShutdownCleanup.ShouldStopManagedServer(otherConnectedInstances: 3));
        }

        [Test]
        public void ShouldStopManagedServer_ProbeFailed_LeavesServerRunning()
        {
            // null = the server did not answer within the quit-time budget. Fail toward not killing:
            // a stray headless process is recoverable, a torn-down shared server is not.
            Assert.IsFalse(McpEditorShutdownCleanup.ShouldStopManagedServer(otherConnectedInstances: null));
        }
    }
}
