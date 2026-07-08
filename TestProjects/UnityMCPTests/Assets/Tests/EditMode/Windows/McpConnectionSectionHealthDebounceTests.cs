using NUnit.Framework;
using MCPForUnity.Editor.Windows.Components.Connection;

namespace MCPForUnityTests.Editor.Windows
{
    /// <summary>
    /// Regression tests for the stdio health-verification debounce: a domain reload briefly
    /// rebinds the bridge listener (the port can hop, e.g. 6402 -> 6403), during which a single
    /// VerifyAsync() ping transiently fails with "Bridge not running" even though the bridge
    /// recovers on its own. The health indicator must not flash "broken" on that lone miss —
    /// mirroring the #1207 orphaned-session debounce.
    /// </summary>
    public class McpConnectionSectionHealthDebounceTests
    {
        private static readonly int Threshold = McpConnectionSection.UnhealthyVerificationThreshold;

        [Test]
        public void SingleVerifyMiss_DoesNotReportUnhealthy()
        {
            // The lone transient miss during a reload rebind must be tolerated.
            Assert.IsFalse(McpConnectionSection.ShouldReportUnhealthy(1));
        }

        [Test]
        public void BelowThreshold_DoesNotReportUnhealthy()
        {
            Assert.IsFalse(McpConnectionSection.ShouldReportUnhealthy(Threshold - 1));
        }

        [Test]
        public void AtThreshold_ReportsUnhealthy()
        {
            Assert.IsTrue(McpConnectionSection.ShouldReportUnhealthy(Threshold));
        }

        [Test]
        public void PastThreshold_ReportsUnhealthy()
        {
            Assert.IsTrue(McpConnectionSection.ShouldReportUnhealthy(Threshold + 3));
        }

        [Test]
        public void Threshold_RequiresMoreThanOneFailure()
        {
            // The whole point of the debounce: a single miss must never be enough.
            Assert.GreaterOrEqual(Threshold, 2);
        }
    }
}
