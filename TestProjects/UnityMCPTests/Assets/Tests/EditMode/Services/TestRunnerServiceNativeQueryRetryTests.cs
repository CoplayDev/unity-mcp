using NUnit.Framework;
using MCPForUnity.Editor.Services;

namespace MCPForUnityTests.Editor.Services
{
    /// <summary>
    /// Covers the bounded retry decision used when Unity's native "is the test run still active?"
    /// query throws. The run's state is then unknown, so the job keeps ownership and the query is
    /// retried until it has failed continuously for the whole recovery window.
    /// </summary>
    public class TestRunnerServiceNativeQueryRetryTests
    {
        private const double FirstFailure = 100.0;
        private const double Window = 30.0;

        [Test]
        public void KeepsRetrying_WhenNoFailureIsRecorded()
        {
            Assert.IsTrue(TestRunnerService.ShouldKeepRetryingNativeQuery(now: FirstFailure + Window * 10, firstFailureAt: null, windowSeconds: Window));
        }

        [Test]
        public void KeepsRetrying_InsideRecoveryWindow()
        {
            Assert.IsTrue(TestRunnerService.ShouldKeepRetryingNativeQuery(now: FirstFailure, firstFailureAt: FirstFailure, windowSeconds: Window));
            Assert.IsTrue(TestRunnerService.ShouldKeepRetryingNativeQuery(now: FirstFailure + Window - 0.5, firstFailureAt: FirstFailure, windowSeconds: Window));
        }

        [Test]
        public void GivesUp_ExactlyAtRecoveryWindow()
        {
            Assert.IsFalse(TestRunnerService.ShouldKeepRetryingNativeQuery(now: FirstFailure + Window, firstFailureAt: FirstFailure, windowSeconds: Window));
        }

        [Test]
        public void GivesUp_PastRecoveryWindow()
        {
            Assert.IsFalse(TestRunnerService.ShouldKeepRetryingNativeQuery(now: FirstFailure + Window + 1.0, firstFailureAt: FirstFailure, windowSeconds: Window));
        }

        [Test]
        public void RestorePendingError_KeepsRunErrorsAndDropsTheUnconfirmedAnnotation()
        {
            Assert.IsNull(TestRunnerService.RestorePendingError(null));
            Assert.AreEqual("Boom", TestRunnerService.RestorePendingError("Boom").Message);
            Assert.IsNull(TestRunnerService.RestorePendingError(TestRunnerService.NativeStateUnconfirmedError));
            Assert.AreEqual("Boom", TestRunnerService.RestorePendingError(
                TestRunnerService.NativeStateUnconfirmedError + TestRunnerService.RunErrorMarker + "Boom").Message);
        }
    }
}
