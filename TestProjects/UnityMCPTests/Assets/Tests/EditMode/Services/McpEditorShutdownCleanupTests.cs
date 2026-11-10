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
    }
}
