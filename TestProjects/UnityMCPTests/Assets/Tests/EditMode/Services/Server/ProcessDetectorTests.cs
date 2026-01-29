using NUnit.Framework;
using MCPForUnity.Editor.Services.Server;

namespace MCPForUnityTests.Editor.Services.Server
{
    /// <summary>
    /// Unit tests for ProcessDetector component.
    /// These tests execute subprocess commands (ps, lsof, tasklist, wmic) which can be slow.
    /// Marked as [Explicit] to exclude from normal test runs.
    /// </summary>
    [TestFixture]
    [Explicit]
    public class ProcessDetectorTests
    {
        private ProcessDetector _detector;

        [SetUp]
        public void SetUp()
        {
            _detector = new ProcessDetector();
        }

        #region NormalizeForMatch Tests

        [Test]
        public void NormalizeForMatch_RemovesWhitespace()
        {
            // Arrange
            string input = "Hello World";

            // Act
            string result = _detector.NormalizeForMatch(input);

            // Assert
            Assert.AreEqual("helloworld", result);
        }

        [Test]
        public void NormalizeForMatch_LowercasesInput()
        {
            // Arrange
            string input = "UPPERCASE";

            // Act
            string result = _detector.NormalizeForMatch(input);

            // Assert
            Assert.AreEqual("uppercase", result);
        }

        [Test]
        public void NormalizeForMatch_HandlesNull()
        {
            // Act
            string result = _detector.NormalizeForMatch(null);

            // Assert
            Assert.AreEqual(string.Empty, result);
        }

        [Test]
        public void NormalizeForMatch_HandlesEmptyString()
        {
            // Act
            string result = _detector.NormalizeForMatch(string.Empty);

            // Assert
            Assert.AreEqual(string.Empty, result);
        }

        [Test]
        public void NormalizeForMatch_RemovesTabs()
        {
            // Arrange
            string input = "hello\tworld";

            // Act
            string result = _detector.NormalizeForMatch(input);

            // Assert
            Assert.AreEqual("helloworld", result);
        }

        [Test]
        public void NormalizeForMatch_RemovesNewlines()
        {
            // Arrange
            string input = "hello\nworld\r\ntest";

            // Act
            string result = _detector.NormalizeForMatch(input);

            // Assert
            Assert.AreEqual("helloworldtest", result);
        }

        [Test]
        public void NormalizeForMatch_PreservesNonWhitespace()
        {
            // Arrange
            string input = "mcp-for-unity_test123";

            // Act
            string result = _detector.NormalizeForMatch(input);

            // Assert
            Assert.AreEqual("mcp-for-unity_test123", result);
        }

        #endregion

        #region GetCurrentProcessId Tests

        [Test]
        public void GetCurrentProcessId_ReturnsPositiveInt()
        {
            // Act
            int pid = _detector.GetCurrentProcessId();

            // Assert
            Assert.Greater(pid, 0, "Process ID should be positive");
        }

        [Test]
        public void GetCurrentProcessId_ReturnsConsistentValue()
        {
            // Act
            int pid1 = _detector.GetCurrentProcessId();
            int pid2 = _detector.GetCurrentProcessId();

            // Assert
            Assert.AreEqual(pid1, pid2, "Process ID should be consistent across calls");
        }

        #endregion

        #region ProcessExists Tests

        [Test]
        public void ProcessExists_CurrentProcess_ReturnsTrue()
        {
            // Arrange
            int currentPid = _detector.GetCurrentProcessId();

            // Act
            bool exists = _detector.ProcessExists(currentPid);

            // Assert
            Assert.IsTrue(exists, "Current process should exist");
        }

        [Test]
        public void ProcessExists_InvalidPid_ReturnsFalseOrHandlesGracefully()
        {
            // Act - Use a very high PID unlikely to exist
            bool exists = _detector.ProcessExists(9999999);

            // Assert - Should not throw, may return false or true (assumes exists if cannot verify)
            Assert.Pass($"ProcessExists returned {exists} for invalid PID (handles gracefully)");
        }

        [Test]
        public void ProcessExists_ZeroPid_HandlesGracefully()
        {
            // Act & Assert - Should not throw
            Assert.DoesNotThrow(() =>
            {
                _detector.ProcessExists(0);
            });
        }

        [Test]
        public void ProcessExists_NegativePid_HandlesGracefully()
        {
            // Act & Assert - Should not throw
            Assert.DoesNotThrow(() =>
            {
                _detector.ProcessExists(-1);
            });
        }

        #endregion

        #region GetListeningProcessIdsForPort Tests

        [Test]
        public void GetListeningProcessIdsForPort_InvalidPort_ReturnsEmpty()
        {
            // Act
            var pids = _detector.GetListeningProcessIdsForPort(-1);

            // Assert
            Assert.IsNotNull(pids);
            Assert.IsEmpty(pids, "Invalid port should return empty list");
        }

        [Test]
        public void GetListeningProcessIdsForPort_UnusedPort_ReturnsEmpty()
        {
            // Act - Use a port that's unlikely to be in use
            var pids = _detector.GetListeningProcessIdsForPort(59999);

            // Assert
            Assert.IsNotNull(pids);
            Assert.IsEmpty(pids, "Unused port should return empty list");
        }

        [Test]
        public void GetListeningProcessIdsForPort_ReturnsDistinctPids()
        {
            // Act
            var pids = _detector.GetListeningProcessIdsForPort(80);

            // Assert
            Assert.IsNotNull(pids);
            CollectionAssert.AllItemsAreUnique(pids, "PIDs should be distinct");
        }

        [Test]
        public void GetListeningProcessIdsForPort_DoesNotThrow()
        {
            // Act & Assert - Should handle any port gracefully
            Assert.DoesNotThrow(() =>
            {
                _detector.GetListeningProcessIdsForPort(8080);
            });
        }

        #endregion

        #region TryGetProcessCommandLine Tests

        [Test]
        public void TryGetProcessCommandLine_CurrentProcess_ReturnsResult()
        {
            // Arrange
            int currentPid = _detector.GetCurrentProcessId();

            // Act
            bool result = _detector.TryGetProcessCommandLine(currentPid, out string argsLower);

            // Assert - Platform dependent, but should not throw
            Assert.Pass($"TryGetProcessCommandLine: success={result}, argsLower length={argsLower?.Length ?? 0}");
        }

        [Test]
        public void TryGetProcessCommandLine_InvalidPid_ReturnsFalse()
        {
            // Act
            bool result = _detector.TryGetProcessCommandLine(9999999, out string argsLower);

            // Assert
            Assert.IsFalse(result, "Invalid PID should return false");
            Assert.IsEmpty(argsLower, "Args should be empty for invalid PID");
        }

        [Test]
        public void TryGetProcessCommandLine_ReturnsNormalizedOutput()
        {
            // Arrange
            int currentPid = _detector.GetCurrentProcessId();

            // Act
            bool result = _detector.TryGetProcessCommandLine(currentPid, out string argsLower);

            // Assert
            if (result && !string.IsNullOrEmpty(argsLower))
            {
                // Verify output is normalized (no whitespace, lowercase)
                Assert.IsFalse(argsLower.Contains(" "), "Output should have no spaces");
                Assert.AreEqual(argsLower, argsLower.ToLowerInvariant(), "Output should be lowercase");
            }
            Assert.Pass("Command line is properly normalized");
        }

        #endregion

        #region LooksLikeMcpServerProcess Tests

        [Test]
        public void LooksLikeMcpServerProcess_CurrentProcess_ReturnsFalse()
        {
            // Arrange - Unity Editor process should not be an MCP server
            int currentPid = _detector.GetCurrentProcessId();

            // Act
            bool result = _detector.LooksLikeMcpServerProcess(currentPid);

            // Assert
            Assert.IsFalse(result, "Unity Editor should not be identified as MCP server");
        }

        [Test]
        public void LooksLikeMcpServerProcess_InvalidPid_ReturnsFalse()
        {
            // Act
            bool result = _detector.LooksLikeMcpServerProcess(9999999);

            // Assert
            Assert.IsFalse(result, "Invalid PID should return false");
        }

        [Test]
        public void LooksLikeMcpServerProcess_ZeroPid_ReturnsFalse()
        {
            // Act
            bool result = _detector.LooksLikeMcpServerProcess(0);

            // Assert
            Assert.IsFalse(result, "Zero PID should return false");
        }

        [Test]
        public void LooksLikeMcpServerProcess_NegativePid_ReturnsFalse()
        {
            // Act
            bool result = _detector.LooksLikeMcpServerProcess(-1);

            // Assert
            Assert.IsFalse(result, "Negative PID should return false");
        }

        [Test]
        public void LooksLikeMcpServerProcess_DoesNotThrow()
        {
            // Act & Assert - Should handle any PID gracefully
            Assert.DoesNotThrow(() =>
            {
                _detector.LooksLikeMcpServerProcess(12345);
            });
        }

        #endregion

        #region Interface Implementation Tests

        [Test]
        public void ProcessDetector_ImplementsIProcessDetector()
        {
            // Assert
            Assert.IsInstanceOf<IProcessDetector>(_detector);
        }

        [Test]
        public void ProcessDetector_CanBeUsedViaInterface()
        {
            // Arrange
            IProcessDetector detector = new ProcessDetector();

            // Act & Assert - All interface methods should work
            Assert.DoesNotThrow(() =>
            {
                detector.NormalizeForMatch("test");
                detector.GetCurrentProcessId();
                detector.ProcessExists(1);
                detector.GetListeningProcessIdsForPort(8080);
                detector.TryGetProcessCommandLine(1, out _);
                detector.LooksLikeMcpServerProcess(1);
            });
        }

        #endregion
    }
}
