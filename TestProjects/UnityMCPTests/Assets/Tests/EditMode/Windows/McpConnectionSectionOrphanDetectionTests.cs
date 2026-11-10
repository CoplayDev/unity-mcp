using NUnit.Framework;
using MCPForUnity.Editor.Windows.Components.Connection;

namespace MCPForUnityTests.Editor.Windows
{
    /// <summary>
    /// Regression tests for #1207: the orphaned-session detector must require several
    /// consecutive failed reachability polls (and a quiet editor) before tearing down an
    /// active session — a single stale 50ms probe reading used to be enough, so busy
    /// machines cycled healthy sessions into reconnect churn.
    /// </summary>
    public class McpConnectionSectionOrphanDetectionTests
    {
        private static readonly int Threshold = McpConnectionSection.OrphanedSessionDownPollThreshold;

        [Test]
        public void EndsSession_AtThreshold_WhenIdleAndRunning()
        {
            Assert.IsTrue(McpConnectionSection.ShouldEndOrphanedSession(
                httpLocalSelected: true, sessionRunning: true, toggleInProgress: false,
                editorBusy: false, consecutiveDownPolls: Threshold));
        }

        [Test]
        public void SingleFailedPoll_DoesNotEndSession()
        {
            Assert.IsFalse(McpConnectionSection.ShouldEndOrphanedSession(
                httpLocalSelected: true, sessionRunning: true, toggleInProgress: false,
                editorBusy: false, consecutiveDownPolls: 1));
        }

        [Test]
        public void BelowThreshold_DoesNotEndSession()
        {
            Assert.IsFalse(McpConnectionSection.ShouldEndOrphanedSession(
                httpLocalSelected: true, sessionRunning: true, toggleInProgress: false,
                editorBusy: false, consecutiveDownPolls: Threshold - 1));
        }

        [Test]
        public void BusyEditor_DefersEvenPastThreshold()
        {
            Assert.IsFalse(McpConnectionSection.ShouldEndOrphanedSession(
                httpLocalSelected: true, sessionRunning: true, toggleInProgress: false,
                editorBusy: true, consecutiveDownPolls: Threshold + 2));
        }

        [Test]
        public void ToggleInProgress_DoesNotEndSession()
        {
            Assert.IsFalse(McpConnectionSection.ShouldEndOrphanedSession(
                httpLocalSelected: true, sessionRunning: true, toggleInProgress: true,
                editorBusy: false, consecutiveDownPolls: Threshold));
        }

        [Test]
        public void NonHttpLocalTransport_NeverEndsSession()
        {
            Assert.IsFalse(McpConnectionSection.ShouldEndOrphanedSession(
                httpLocalSelected: false, sessionRunning: true, toggleInProgress: false,
                editorBusy: false, consecutiveDownPolls: Threshold + 5));
        }

        [Test]
        public void SessionNotRunning_NothingToEnd()
        {
            Assert.IsFalse(McpConnectionSection.ShouldEndOrphanedSession(
                httpLocalSelected: true, sessionRunning: false, toggleInProgress: false,
                editorBusy: false, consecutiveDownPolls: Threshold + 5));
        }
    }
}
