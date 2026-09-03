using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using MCPForUnity.Editor.Services.Server;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
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
        private static readonly string[] IntPreferenceKeys =
        {
            EditorPrefKeys.LastLocalHttpServerPid,
            EditorPrefKeys.LastLocalHttpServerPort,
        };

        private static readonly string[] StringPreferenceKeys =
        {
            EditorPrefKeys.LastLocalHttpServerStartedUtc,
            EditorPrefKeys.LastLocalHttpServerPidArgsHash,
            EditorPrefKeys.LastLocalHttpServerPidFilePath,
            EditorPrefKeys.LastLocalHttpServerInstanceToken,
        };

        private PidFileManager _manager;
        private string _testPidFilePath;
        private readonly Dictionary<string, int> _savedIntPreferences = new Dictionary<string, int>();
        private readonly Dictionary<string, string> _savedStringPreferences = new Dictionary<string, string>();

        [SetUp]
        public void SetUp()
        {
            _manager = new PidFileManager();
            SnapshotAndClearTestEditorPrefs();
        }

        [TearDown]
        public void TearDown()
        {
            // Clean up test files
            if (!string.IsNullOrEmpty(_testPidFilePath) && File.Exists(_testPidFilePath))
            {
                try { File.Delete(_testPidFilePath); } catch { }
            }
            ClearTestEditorPrefs();
            RestoreTestEditorPrefs();
        }

        private void SnapshotAndClearTestEditorPrefs()
        {
            _savedIntPreferences.Clear();
            _savedStringPreferences.Clear();

            foreach (string baseKey in IntPreferenceKeys)
            {
                string key = ProjectScopedEditorPrefs.GetKey(baseKey);
                if (EditorPrefs.HasKey(key))
                {
                    _savedIntPreferences[key] = EditorPrefs.GetInt(key, 0);
                }
            }

            foreach (string baseKey in StringPreferenceKeys)
            {
                string key = ProjectScopedEditorPrefs.GetKey(baseKey);
                if (EditorPrefs.HasKey(key))
                {
                    _savedStringPreferences[key] = EditorPrefs.GetString(key, string.Empty);
                }
            }

            ClearTestEditorPrefs();
        }

        private static void ClearTestEditorPrefs()
        {
            foreach (string baseKey in IntPreferenceKeys)
            {
                EditorPrefs.DeleteKey(ProjectScopedEditorPrefs.GetKey(baseKey));
            }

            foreach (string baseKey in StringPreferenceKeys)
            {
                EditorPrefs.DeleteKey(ProjectScopedEditorPrefs.GetKey(baseKey));
            }
        }

        private void RestoreTestEditorPrefs()
        {
            foreach (KeyValuePair<string, int> preference in _savedIntPreferences)
            {
                EditorPrefs.SetInt(preference.Key, preference.Value);
            }

            foreach (KeyValuePair<string, string> preference in _savedStringPreferences)
            {
                EditorPrefs.SetString(preference.Key, preference.Value);
            }
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

        #region TryGetPortFromPidFilePath Tests

        [Test]
        public void TryGetPortFromPidFilePath_ValidPath_ReturnsTrue()
        {
            // Arrange
            string path = "/some/path/mcp_http_8080.pid";

            // Act
            bool result = _manager.TryGetPortFromPidFilePath(path, out int port);

            // Assert
            Assert.IsTrue(result);
            Assert.AreEqual(8080, port);
        }

        [Test]
        public void TryGetPortFromPidFilePath_DifferentPort_ParsesCorrectly()
        {
            // Arrange
            string path = "/path/to/mcp_http_9999.pid";

            // Act
            bool result = _manager.TryGetPortFromPidFilePath(path, out int port);

            // Assert
            Assert.IsTrue(result);
            Assert.AreEqual(9999, port);
        }

        [Test]
        public void TryGetPortFromPidFilePath_NullPath_ReturnsFalse()
        {
            // Act
            bool result = _manager.TryGetPortFromPidFilePath(null, out int port);

            // Assert
            Assert.IsFalse(result);
            Assert.AreEqual(0, port);
        }

        [Test]
        public void TryGetPortFromPidFilePath_InvalidPrefix_ReturnsFalse()
        {
            // Arrange
            string path = "/some/path/wrong_prefix_8080.pid";

            // Act
            bool result = _manager.TryGetPortFromPidFilePath(path, out int port);

            // Assert
            Assert.IsFalse(result);
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
            _manager.StoreHandshake(pidFilePath, instanceToken);
            bool result = _manager.TryGetHandshake(out var storedPath, out var storedToken);

            // Assert
            Assert.IsTrue(result);
            Assert.AreEqual(pidFilePath, storedPath);
            Assert.AreEqual(instanceToken, storedToken);
        }

        [Test]
        public void StoreHandshake_DoesNotOverwriteLegacyGlobalKeys()
        {
            const string legacyPath = "/another/project/mcp_http_8082.pid";
            const string legacyToken = "another-project-token";
            bool hadLegacyPath = EditorPrefs.HasKey(EditorPrefKeys.LastLocalHttpServerPidFilePath);
            bool hadLegacyToken = EditorPrefs.HasKey(EditorPrefKeys.LastLocalHttpServerInstanceToken);
            string originalLegacyPath = EditorPrefs.GetString(
                EditorPrefKeys.LastLocalHttpServerPidFilePath,
                string.Empty);
            string originalLegacyToken = EditorPrefs.GetString(
                EditorPrefKeys.LastLocalHttpServerInstanceToken,
                string.Empty);
            EditorPrefs.SetString(EditorPrefKeys.LastLocalHttpServerPidFilePath, legacyPath);
            EditorPrefs.SetString(EditorPrefKeys.LastLocalHttpServerInstanceToken, legacyToken);

            try
            {
                _manager.StoreHandshake("/this/project/mcp_http_8083.pid", "this-project-token");

                Assert.AreEqual(
                    legacyPath,
                    EditorPrefs.GetString(EditorPrefKeys.LastLocalHttpServerPidFilePath, string.Empty));
                Assert.AreEqual(
                    legacyToken,
                    EditorPrefs.GetString(EditorPrefKeys.LastLocalHttpServerInstanceToken, string.Empty));
            }
            finally
            {
                RestoreString(
                    EditorPrefKeys.LastLocalHttpServerPidFilePath,
                    hadLegacyPath,
                    originalLegacyPath);
                RestoreString(
                    EditorPrefKeys.LastLocalHttpServerInstanceToken,
                    hadLegacyToken,
                    originalLegacyToken);
            }
        }

        [Test]
        public void TryGetHandshake_NoHandshake_ReturnsFalse()
        {
            // Act
            bool result = _manager.TryGetHandshake(out var pidFilePath, out var instanceToken);

            // Assert
            Assert.IsFalse(result);
            Assert.IsNull(pidFilePath);
            Assert.IsNull(instanceToken);
        }

        [Test]
        public void StoreHandshake_NullValues_DoesNotThrow()
        {
            // Act & Assert
            Assert.DoesNotThrow(() =>
            {
                _manager.StoreHandshake(null, null);
            });
        }

        #endregion

        #region Tracking Tests

        [Test]
        public void StoreTracking_ValidData_CanBeRetrieved()
        {
            // Arrange
            int pid = 12345;
            int port = 8080;

            // Act
            _manager.StoreTracking(pid, port);
            bool result = _manager.TryGetStoredPid(port, out int storedPid);

            // Assert
            Assert.IsTrue(result);
            Assert.AreEqual(pid, storedPid);
        }

        [Test]
        public void TryGetStoredPid_WrongPort_ReturnsFalse()
        {
            // Arrange
            _manager.StoreTracking(12345, 8080);

            // Act
            bool result = _manager.TryGetStoredPid(9090, out int storedPid);

            // Assert
            Assert.IsFalse(result, "Should return false for wrong port");
        }

        [Test]
        public void TryGetStoredPid_NoTracking_ReturnsFalse()
        {
            // Act
            bool result = _manager.TryGetStoredPid(8080, out int storedPid);

            // Assert
            Assert.IsFalse(result);
            Assert.AreEqual(0, storedPid);
        }

        [Test]
        public void ClearTracking_RemovesAllKeys()
        {
            // Arrange
            _manager.StoreTracking(12345, 8080, "somehash");
            _manager.StoreHandshake("/path.pid", "token");

            // Act
            _manager.ClearTracking();
            bool hasTracking = _manager.TryGetStoredPid(8080, out _);
            bool hasHandshake = _manager.TryGetHandshake(out _, out _);

            // Assert
            Assert.IsFalse(hasTracking);
            Assert.IsFalse(hasHandshake);
        }

        [Test]
        public void GetStoredArgsHash_WithHash_ReturnsHash()
        {
            // Arrange
            _manager.StoreTracking(12345, 8080, "testhash123");

            // Act
            string hash = _manager.GetStoredArgsHash();

            // Assert
            Assert.AreEqual("testhash123", hash);
        }

        [Test]
        public void GetStoredArgsHash_NoHash_ReturnsEmpty()
        {
            // Act
            string hash = _manager.GetStoredArgsHash();

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

        private static void RestoreString(string key, bool hadValue, string value)
        {
            if (hadValue)
            {
                EditorPrefs.SetString(key, value);
            }
            else
            {
                EditorPrefs.DeleteKey(key);
            }
        }
    }
}
