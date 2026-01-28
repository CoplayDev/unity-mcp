using NUnit.Framework;
using MCPForUnity.Editor.Services.Server;

namespace MCPForUnityTests.Editor.Services.Server
{
    /// <summary>
    /// Unit tests for ProcessTerminator component.
    /// Note: Most tests avoid actually terminating processes to prevent test instability.
    /// </summary>
    [TestFixture]
    public class ProcessTerminatorTests
    {
        private ProcessTerminator _terminator;
        private ProcessDetector _detector;

        [SetUp]
        public void SetUp()
        {
            _detector = new ProcessDetector();
            _terminator = new ProcessTerminator(_detector);
        }

        #region Constructor Tests

        [Test]
        public void Constructor_NullDetector_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<System.ArgumentNullException>(() =>
            {
                new ProcessTerminator(null);
            });
        }

        [Test]
        public void Constructor_ValidDetector_Succeeds()
        {
            // Act & Assert
            Assert.DoesNotThrow(() =>
            {
                new ProcessTerminator(_detector);
            });
        }

        #endregion

        #region Terminate Tests

        [Test]
        public void Terminate_InvalidPid_ReturnsFalse()
        {
            // Act
            bool result = _terminator.Terminate(-1);

            // Assert
            Assert.IsFalse(result, "Invalid PID should fail to terminate");
        }

        [Test]
        public void Terminate_ZeroPid_ReturnsFalse()
        {
            // Act
            bool result = _terminator.Terminate(0);

            // Assert
            Assert.IsFalse(result, "Zero PID should fail to terminate");
        }

        [Test]
        public void Terminate_NonExistentPid_ReturnsFalseOrHandlesGracefully()
        {
            // Act - Use a very high PID unlikely to exist
            bool result = _terminator.Terminate(9999999);

            // Assert - Should not throw, result depends on platform behavior
            Assert.Pass($"Terminate returned {result} for non-existent PID (handles gracefully)");
        }

        [Test]
        public void Terminate_DoesNotThrow()
        {
            // Act & Assert - Should handle any PID gracefully
            Assert.DoesNotThrow(() =>
            {
                _terminator.Terminate(12345);
            });
        }

        [Test]
        [Explicit("This test would terminate the Unity process - DO NOT RUN")]
        public void Terminate_CurrentProcess_WouldTerminateUnity()
        {
            // This test documents that terminating the current process would kill Unity
            // It is marked [Explicit] to prevent accidental execution
            int currentPid = _detector.GetCurrentProcessId();
            Assert.Pass($"Current process PID is {currentPid} - test is explicit to prevent termination");
        }

        #endregion

        #region Interface Implementation Tests

        [Test]
        public void ProcessTerminator_ImplementsIProcessTerminator()
        {
            // Assert
            Assert.IsInstanceOf<IProcessTerminator>(_terminator);
        }

        [Test]
        public void ProcessTerminator_CanBeUsedViaInterface()
        {
            // Arrange
            IProcessTerminator terminator = new ProcessTerminator(_detector);

            // Act & Assert - Should be callable via interface
            Assert.DoesNotThrow(() =>
            {
                // Don't actually terminate anything
                terminator.Terminate(-1);
            });
        }

        #endregion

        #region Integration Tests (with real detector)

        [Test]
        public void Terminate_WithRealDetector_HandlesMissingProcess()
        {
            // Arrange
            var realDetector = new ProcessDetector();
            var terminator = new ProcessTerminator(realDetector);

            // Act - Try to terminate a PID that definitely doesn't exist
            bool result = terminator.Terminate(int.MaxValue);

            // Assert - Should return false without throwing
            Assert.IsFalse(result, "Terminating non-existent process should return false");
        }

        #endregion
    }
}
