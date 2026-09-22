using System.IO;
using NUnit.Framework;
using MCPForUnity.Editor.Services.Server;
using UnityEditor;
using UnityEngine;

namespace MCPForUnityTests.Editor.Services.Server
{
    /// <summary>
    /// Unit tests for PidFileManager component.
    /// </summary>
    [TestFixture]
    public class PidFileManagerTests
    {
        // Ports used by handshake/tracking tests. Kept away from the default 8080 so a server the
        // host editor actually launched is never touched, and cleared per port because the slots
        // are per project+port now.
        private const int PortA = 58080;
        private const int PortB = 58081;

        private PidFileManager _manager;
        private string _testPidFilePath;
        private int _savedLaunchedPort;

        [SetUp]
        public void SetUp()
        {
            _manager = new PidFileManager();
            // The launch marker is one SessionState slot per editor process; preserve whatever the
            // host editor recorded so running these tests never drops its own server ownership.
            _savedLaunchedPort = SessionState.GetInt(PidFileManager.LaunchedPortSessionKey, 0);
            ClearTestState();
        }

        [TearDown]
        public void TearDown()
        {
            // Clean up test files
            if (!string.IsNullOrEmpty(_testPidFilePath) && File.Exists(_testPidFilePath))
            {
                try { File.Delete(_testPidFilePath); } catch { }
            }
            ClearTestState();
            if (_savedLaunchedPort > 0)
            {
                SessionState.SetInt(PidFileManager.LaunchedPortSessionKey, _savedLaunchedPort);
            }
        }

        private void ClearTestState()
        {
            _manager.ClearTracking(PortA);
            _manager.ClearTracking(PortB);
            SessionState.EraseInt(PidFileManager.LaunchedPortSessionKey);
        }

        #region GetPidFilePath Tests

        [Test]
        public void GetPidFilePath_ValidPort_ReturnsCorrectPath()
        {
            // Act
            string path = _manager.GetPidFilePath(8080);

            // Assert
            Assert.IsNotNull(path);
            Assert.That(path, Does.Contain("mcp_http_8080.pid"));
            Assert.That(path, Does.Contain("MCPForUnity"));
        }

        [Test]
        public void GetPidFilePath_DifferentPorts_ReturnsDifferentPaths()
        {
            // Act
            string path1 = _manager.GetPidFilePath(8080);
            string path2 = _manager.GetPidFilePath(9090);

            // Assert
            Assert.AreNotEqual(path1, path2);
        }

        [Test]
        public void GetPidDirectory_ReturnsValidPath()
        {
            // Act
            string dir = _manager.GetPidDirectory();

            // Assert
            Assert.IsNotNull(dir);
            Assert.That(dir, Does.Contain("MCPForUnity"));
            Assert.That(dir, Does.Contain("RunState"));
        }

        #endregion

        #region TryReadPid Tests

        [Test]
        public void TryReadPid_ValidFile_ReturnsTrueWithPid()
        {
            // Arrange
            _testPidFilePath = _manager.GetPidFilePath(59998);
            File.WriteAllText(_testPidFilePath, "12345");

            // Act
            bool result = _manager.TryReadPid(_testPidFilePath, out int pid);

            // Assert
            Assert.IsTrue(result);
            Assert.AreEqual(12345, pid);
        }

        [Test]
        public void TryReadPid_FileWithWhitespace_ParsesCorrectly()
        {
            // Arrange
            _testPidFilePath = _manager.GetPidFilePath(59997);
            File.WriteAllText(_testPidFilePath, "  12345  \n");

            // Act
            bool result = _manager.TryReadPid(_testPidFilePath, out int pid);

            // Assert
            Assert.IsTrue(result);
            Assert.AreEqual(12345, pid);
        }

        [Test]
        public void TryReadPid_MissingFile_ReturnsFalse()
        {
            // Act
            bool result = _manager.TryReadPid("/nonexistent/path/file.pid", out int pid);

            // Assert
            Assert.IsFalse(result);
            Assert.AreEqual(0, pid);
        }

        [Test]
        public void TryReadPid_NullPath_ReturnsFalse()
        {
            // Act
            bool result = _manager.TryReadPid(null, out int pid);

            // Assert
            Assert.IsFalse(result);
            Assert.AreEqual(0, pid);
        }

        [Test]
        public void TryReadPid_EmptyPath_ReturnsFalse()
        {
            // Act
            bool result = _manager.TryReadPid(string.Empty, out int pid);

            // Assert
            Assert.IsFalse(result);
            Assert.AreEqual(0, pid);
        }

        [Test]
        public void TryReadPid_InvalidContent_ReturnsFalse()
        {
            // Arrange
            _testPidFilePath = _manager.GetPidFilePath(59996);
            File.WriteAllText(_testPidFilePath, "not a number");

            // Act
            bool result = _manager.TryReadPid(_testPidFilePath, out int pid);

            // Assert
            Assert.IsFalse(result);
            Assert.AreEqual(0, pid);
        }

        [Test]
        public void TryReadPid_ZeroPid_ReturnsFalse()
        {
            // Arrange
            _testPidFilePath = _manager.GetPidFilePath(59995);
            File.WriteAllText(_testPidFilePath, "0");

            // Act
            bool result = _manager.TryReadPid(_testPidFilePath, out int pid);

            // Assert
            Assert.IsFalse(result, "Zero PID should be rejected");
        }

        [Test]
        public void TryReadPid_NegativePid_ReturnsFalse()
        {
            // Arrange
            _testPidFilePath = _manager.GetPidFilePath(59994);
            File.WriteAllText(_testPidFilePath, "-1");

            // Act
            bool result = _manager.TryReadPid(_testPidFilePath, out int pid);

            // Assert
            Assert.IsFalse(result, "Negative PID should be rejected");
        }

        #endregion

        #region Handshake Tests

        [Test]
        public void StoreHandshake_ValidData_StoresInEditorPrefs()
        {
            // Arrange
            string pidFilePath = "/test/path.pid";
            string instanceToken = "test-token-123";

            // Act
            _manager.StoreHandshake(PortA, pidFilePath, instanceToken);
            bool result = _manager.TryGetHandshake(PortA, out var storedPath, out var storedToken);

            // Assert
            Assert.IsTrue(result);
            Assert.AreEqual(pidFilePath, storedPath);
            Assert.AreEqual(instanceToken, storedToken);
        }

        [Test]
        public void TryGetHandshake_NoHandshake_ReturnsFalse()
        {
            // Act
            bool result = _manager.TryGetHandshake(PortA, out var pidFilePath, out var instanceToken);

            // Assert
            Assert.IsFalse(result);
            Assert.IsNull(pidFilePath);
            Assert.IsNull(instanceToken);
        }

        [Test]
        public void TryGetHandshake_OtherPort_ReturnsFalse()
        {
            // Two Unity-managed servers on different ports must not clobber each other's handshake.
            _manager.StoreHandshake(PortA, "/a.pid", "token-a");

            Assert.IsFalse(_manager.TryGetHandshake(PortB, out _, out _));
        }

        [Test]
        public void StoreHandshake_TwoPorts_KeepBothSlots()
        {
            _manager.StoreHandshake(PortA, "/a.pid", "token-a");
            _manager.StoreHandshake(PortB, "/b.pid", "token-b");

            Assert.IsTrue(_manager.TryGetHandshake(PortA, out var pathA, out var tokenA));
            Assert.IsTrue(_manager.TryGetHandshake(PortB, out var pathB, out var tokenB));
            Assert.AreEqual("/a.pid", pathA);
            Assert.AreEqual("token-a", tokenA);
            Assert.AreEqual("/b.pid", pathB);
            Assert.AreEqual("token-b", tokenB);
        }

        [Test]
        public void StoreHandshake_NullValues_DoesNotThrow()
        {
            // Act & Assert
            Assert.DoesNotThrow(() =>
            {
                _manager.StoreHandshake(PortA, null, null);
            });
        }

        [Test]
        public void StoreHandshake_InvalidPort_StoresNothing()
        {
            _manager.StoreHandshake(0, "/a.pid", "token-a");

            Assert.IsFalse(_manager.TryGetLaunchedPort(out _));
        }

        #endregion

        #region Launch Marker Tests

        [Test]
        public void TryGetLaunchedPort_NothingLaunched_ReturnsFalse()
        {
            // Regression for the multi-editor shutdown bug: an editor that never launched the server
            // (it only connected to one another editor started) must not resolve a launch to stop.
            Assert.IsFalse(_manager.TryGetLaunchedPort(out int port));
            Assert.AreEqual(0, port);
        }

        [Test]
        public void StoreHandshake_RecordsLaunchedPort()
        {
            _manager.StoreHandshake(PortA, "/a.pid", "token-a");

            Assert.IsTrue(_manager.TryGetLaunchedPort(out int port));
            Assert.AreEqual(PortA, port);
        }

        [Test]
        public void ClearTracking_SamePort_ClearsLaunchedPort()
        {
            _manager.StoreHandshake(PortA, "/a.pid", "token-a");

            _manager.ClearTracking(PortA);

            Assert.IsFalse(_manager.TryGetLaunchedPort(out _));
        }

        [Test]
        public void ClearTracking_OtherPort_KeepsLaunchedPort()
        {
            _manager.StoreHandshake(PortA, "/a.pid", "token-a");

            _manager.ClearTracking(PortB);

            Assert.IsTrue(_manager.TryGetLaunchedPort(out int port));
            Assert.AreEqual(PortA, port);
        }

        #endregion

        #region Tracking Tests

        [Test]
        public void StoreTracking_ValidData_CanBeRetrieved()
        {
            // Arrange
            int pid = 12345;

            // Act
            _manager.StoreTracking(pid, PortA);
            bool result = _manager.TryGetStoredPid(PortA, out int storedPid);

            // Assert
            Assert.IsTrue(result);
            Assert.AreEqual(pid, storedPid);
        }

        [Test]
        public void TryGetStoredPid_WrongPort_ReturnsFalse()
        {
            // Arrange
            _manager.StoreTracking(12345, PortA);

            // Act
            bool result = _manager.TryGetStoredPid(PortB, out int storedPid);

            // Assert
            Assert.IsFalse(result, "Should return false for wrong port");
        }

        [Test]
        public void TryGetStoredPid_NoTracking_ReturnsFalse()
        {
            // Act
            bool result = _manager.TryGetStoredPid(PortA, out int storedPid);

            // Assert
            Assert.IsFalse(result);
            Assert.AreEqual(0, storedPid);
        }

        [Test]
        public void ClearTracking_RemovesAllKeysForPort()
        {
            // Arrange
            _manager.StoreTracking(12345, PortA, "somehash");
            _manager.StoreHandshake(PortA, "/path.pid", "token");

            // Act
            _manager.ClearTracking(PortA);
            bool hasTracking = _manager.TryGetStoredPid(PortA, out _);
            bool hasHandshake = _manager.TryGetHandshake(PortA, out _, out _);

            // Assert
            Assert.IsFalse(hasTracking);
            Assert.IsFalse(hasHandshake);
            Assert.AreEqual(string.Empty, _manager.GetStoredArgsHash(PortA));
        }

        [Test]
        public void ClearTracking_OtherPort_LeavesSlotIntact()
        {
            _manager.StoreTracking(12345, PortA, "somehash");
            _manager.StoreHandshake(PortA, "/path.pid", "token");

            _manager.ClearTracking(PortB);

            Assert.IsTrue(_manager.TryGetStoredPid(PortA, out int storedPid));
            Assert.AreEqual(12345, storedPid);
            Assert.IsTrue(_manager.TryGetHandshake(PortA, out _, out _));
        }

        [Test]
        public void GetStoredArgsHash_WithHash_ReturnsHash()
        {
            // Arrange
            _manager.StoreTracking(12345, PortA, "testhash123");

            // Act
            string hash = _manager.GetStoredArgsHash(PortA);

            // Assert
            Assert.AreEqual("testhash123", hash);
        }

        [Test]
        public void GetStoredArgsHash_NoHash_ReturnsEmpty()
        {
            // Act
            string hash = _manager.GetStoredArgsHash(PortA);

            // Assert
            Assert.AreEqual(string.Empty, hash);
        }

        #endregion

        #region ComputeShortHash Tests

        [Test]
        public void ComputeShortHash_ValidInput_Returns16CharHash()
        {
            // Arrange
            string input = "test input string";

            // Act
            string hash = _manager.ComputeShortHash(input);

            // Assert
            Assert.IsNotNull(hash);
            Assert.AreEqual(16, hash.Length);
        }

        [Test]
        public void ComputeShortHash_SameInput_ReturnsSameHash()
        {
            // Arrange
            string input = "consistent input";

            // Act
            string hash1 = _manager.ComputeShortHash(input);
            string hash2 = _manager.ComputeShortHash(input);

            // Assert
            Assert.AreEqual(hash1, hash2);
        }

        [Test]
        public void ComputeShortHash_DifferentInput_ReturnsDifferentHash()
        {
            // Act
            string hash1 = _manager.ComputeShortHash("input1");
            string hash2 = _manager.ComputeShortHash("input2");

            // Assert
            Assert.AreNotEqual(hash1, hash2);
        }

        [Test]
        public void ComputeShortHash_NullInput_ReturnsEmpty()
        {
            // Act
            string hash = _manager.ComputeShortHash(null);

            // Assert
            Assert.AreEqual(string.Empty, hash);
        }

        [Test]
        public void ComputeShortHash_EmptyInput_ReturnsEmpty()
        {
            // Act
            string hash = _manager.ComputeShortHash(string.Empty);

            // Assert
            Assert.AreEqual(string.Empty, hash);
        }

        #endregion

        #region DeletePidFile Tests

        [Test]
        public void DeletePidFile_ExistingFile_DeletesFile()
        {
            // Arrange
            _testPidFilePath = _manager.GetPidFilePath(59993);
            File.WriteAllText(_testPidFilePath, "12345");
            Assert.IsTrue(File.Exists(_testPidFilePath));

            // Act
            _manager.DeletePidFile(_testPidFilePath);

            // Assert
            Assert.IsFalse(File.Exists(_testPidFilePath));
        }

        [Test]
        public void DeletePidFile_NonExistentFile_DoesNotThrow()
        {
            // Act & Assert
            Assert.DoesNotThrow(() =>
            {
                _manager.DeletePidFile("/nonexistent/file.pid");
            });
        }

        [Test]
        public void DeletePidFile_NullPath_DoesNotThrow()
        {
            // Act & Assert
            Assert.DoesNotThrow(() =>
            {
                _manager.DeletePidFile(null);
            });
        }

        #endregion

        #region Interface Implementation Tests

        [Test]
        public void PidFileManager_ImplementsIPidFileManager()
        {
            // Assert
            Assert.IsInstanceOf<IPidFileManager>(_manager);
        }

        #endregion
    }
}
