using NUnit.Framework;
using MCPForUnity.Editor.Services.Transport.Transports;

namespace MCPForUnityTests.Editor.Transport
{
    /// <summary>
    /// Regression tests for the Start Session readiness race: stdio StartAsync used to return true
    /// unconditionally and callers immediately verified StdioBridgeHost.IsRunning — which is still
    /// false while the previous port releases after a domain reload, producing a spurious
    /// "Bridge not running" until the user clicked Start Session several times. StartAsync now waits
    /// (bounded) for the bridge to actually bind; this covers the wait predicate.
    /// </summary>
    public class StdioTransportClientReadinessTests
    {
        private static readonly double Timeout = StdioTransportClient.ReadyWaitTimeoutSeconds;

        [Test]
        public void KeepsWaiting_WhenNotReady_AndWithinWindow()
        {
            Assert.IsTrue(StdioTransportClient.ShouldKeepWaitingForReady(bridgeReady: false, secondsWaited: 0.0));
            Assert.IsTrue(StdioTransportClient.ShouldKeepWaitingForReady(bridgeReady: false, secondsWaited: Timeout - 0.1));
        }

        [Test]
        public void StopsWaiting_AsSoonAsReady()
        {
            Assert.IsFalse(StdioTransportClient.ShouldKeepWaitingForReady(bridgeReady: true, secondsWaited: 0.0));
        }

        [Test]
        public void StopsWaiting_WhenWindowElapses_EvenIfNotReady()
        {
            Assert.IsFalse(StdioTransportClient.ShouldKeepWaitingForReady(bridgeReady: false, secondsWaited: Timeout));
            Assert.IsFalse(StdioTransportClient.ShouldKeepWaitingForReady(bridgeReady: false, secondsWaited: Timeout + 1.0));
        }

        [Test]
        public void ReadyWindow_IsGenerousEnoughForPortRelease()
        {
            // Must cover the OS port-release delay + the BusyPortFallbackWindowSeconds (3s) fallback.
            Assert.GreaterOrEqual(Timeout, 3.0);
        }
    }
}
