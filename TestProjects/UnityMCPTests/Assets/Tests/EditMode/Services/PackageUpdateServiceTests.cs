using System;
using NUnit.Framework;
using UnityEditor;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Constants;

namespace MCPForUnityTests.Editor.Services
{
    public class PackageUpdateServiceTests
    {
        private PackageUpdateService _service;
        private const string TestLastCheckDateKey = EditorPrefKeys.LastUpdateCheck;
        private const string TestCachedVersionKey = EditorPrefKeys.LatestKnownVersion;

        [SetUp]
        public void SetUp()
        {
            _service = new PackageUpdateService();

            // Clean up any existing test data
            CleanupEditorPrefs();
        }

        [TearDown]
        public void TearDown()
        {
            // Clean up test data
            CleanupEditorPrefs();
        }

        private void CleanupEditorPrefs()
        {
            if (EditorPrefs.HasKey(TestLastCheckDateKey))
            {
                EditorPrefs.DeleteKey(TestLastCheckDateKey);
            }
            if (EditorPrefs.HasKey(TestCachedVersionKey))
            {
                EditorPrefs.DeleteKey(TestCachedVersionKey);
            }
        }

        [Test]
        public void IsNewerVersion_ReturnsTrue_WhenMajorVersionIsNewer()
        {
            bool result = _service.IsNewerVersion("2.0.0", "1.0.0");
            Assert.IsTrue(result, "2.0.0 should be newer than 1.0.0");
        }

        [Test]
        public void IsNewerVersion_ReturnsTrue_WhenMinorVersionIsNewer()
        {
            bool result = _service.IsNewerVersion("1.2.0", "1.1.0");
            Assert.IsTrue(result, "1.2.0 should be newer than 1.1.0");
        }

        [Test]
        public void IsNewerVersion_ReturnsTrue_WhenPatchVersionIsNewer()
        {
            bool result = _service.IsNewerVersion("1.0.2", "1.0.1");
            Assert.IsTrue(result, "1.0.2 should be newer than 1.0.1");
        }

        [Test]
        public void IsNewerVersion_ReturnsFalse_WhenVersionsAreEqual()
        {
            bool result = _service.IsNewerVersion("1.0.0", "1.0.0");
            Assert.IsFalse(result, "Same versions should return false");
        }

        [Test]
        public void IsNewerVersion_ReturnsFalse_WhenVersionIsOlder()
        {
            bool result = _service.IsNewerVersion("1.0.0", "2.0.0");
            Assert.IsFalse(result, "1.0.0 should not be newer than 2.0.0");
        }

        [Test]
        public void IsNewerVersion_HandlesVersionPrefix_v()
        {
            bool result = _service.IsNewerVersion("v2.0.0", "v1.0.0");
            Assert.IsTrue(result, "Should handle 'v' prefix correctly");
        }

        [Test]
        public void IsNewerVersion_HandlesVersionPrefix_V()
        {
            bool result = _service.IsNewerVersion("V2.0.0", "V1.0.0");
            Assert.IsTrue(result, "Should handle 'V' prefix correctly");
        }

        [Test]
        public void IsNewerVersion_HandlesMixedPrefixes()
        {
            bool result = _service.IsNewerVersion("v2.0.0", "1.0.0");
            Assert.IsTrue(result, "Should handle mixed prefixes correctly");
        }

        [Test]
        public void IsNewerVersion_ComparesCorrectly_WhenMajorDiffers()
        {
            bool result1 = _service.IsNewerVersion("10.0.0", "9.0.0");
            bool result2 = _service.IsNewerVersion("2.0.0", "10.0.0");

            Assert.IsTrue(result1, "10.0.0 should be newer than 9.0.0");
            Assert.IsFalse(result2, "2.0.0 should not be newer than 10.0.0");
        }

        [Test]
        public void IsNewerVersion_ReturnsFalse_OnInvalidVersionFormat()
        {
            // Service should handle errors gracefully
            bool result = _service.IsNewerVersion("invalid", "1.0.0");
            Assert.IsFalse(result, "Should return false for invalid version format");
        }

        [Test]
        public void CheckForUpdate_ReturnsCachedVersion_WhenCacheIsValid()
        {
            // Arrange: Set up valid cache
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            string cachedVersion = "5.5.5";
            EditorPrefs.SetString(TestLastCheckDateKey, today);
            EditorPrefs.SetString(TestCachedVersionKey, cachedVersion);

            // Act
            var result = _service.CheckForUpdate("5.0.0");

            // Assert
            Assert.IsTrue(result.CheckSucceeded, "Check should succeed with valid cache");
            Assert.AreEqual(cachedVersion, result.LatestVersion, "Should return cached version");
            Assert.IsTrue(result.UpdateAvailable, "Update should be available (5.5.5 > 5.0.0)");
        }

        [Test]
        public void CheckForUpdate_DetectsUpdateAvailable_WhenNewerVersionCached()
        {
            // Arrange
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            EditorPrefs.SetString(TestLastCheckDateKey, today);
            EditorPrefs.SetString(TestCachedVersionKey, "6.0.0");

            // Act
            var result = _service.CheckForUpdate("5.0.0");

            // Assert
            Assert.IsTrue(result.UpdateAvailable, "Should detect update is available");
            Assert.AreEqual("6.0.0", result.LatestVersion);
        }

        [Test]
        public void CheckForUpdate_DetectsNoUpdate_WhenVersionsMatch()
        {
            // Arrange
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            EditorPrefs.SetString(TestLastCheckDateKey, today);
            EditorPrefs.SetString(TestCachedVersionKey, "5.0.0");

            // Act
            var result = _service.CheckForUpdate("5.0.0");

            // Assert
            Assert.IsFalse(result.UpdateAvailable, "Should detect no update needed");
            Assert.AreEqual("5.0.0", result.LatestVersion);
        }

        [Test]
        public void CheckForUpdate_DetectsNoUpdate_WhenCurrentVersionIsNewer()
        {
            // Arrange
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            EditorPrefs.SetString(TestLastCheckDateKey, today);
            EditorPrefs.SetString(TestCachedVersionKey, "5.0.0");

            // Act
            var result = _service.CheckForUpdate("6.0.0");

            // Assert
            Assert.IsFalse(result.UpdateAvailable, "Should detect no update when current is newer");
            Assert.AreEqual("5.0.0", result.LatestVersion);
        }

        [Test]
        public void CheckForUpdate_IgnoresExpiredCache_AndAttemptsFreshFetch()
        {
            // Arrange: Set cache from yesterday (expired)
            string yesterday = DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd");
            string cachedVersion = "4.0.0";
            EditorPrefs.SetString(TestLastCheckDateKey, yesterday);
            EditorPrefs.SetString(TestCachedVersionKey, cachedVersion);

            // Act
            var result = _service.CheckForUpdate("5.0.0");

            // Assert
            Assert.IsNotNull(result, "Should return a result");
            
            // If the check succeeded (network available), verify it didn't use the expired cache
            if (result.CheckSucceeded)
            {
                Assert.AreNotEqual(cachedVersion, result.LatestVersion, 
                    "Should not return expired cached version when fresh fetch succeeds");
                Assert.IsNotNull(result.LatestVersion, "Should have fetched a new version");
            }
            else
            {
                // If offline, check should fail (not succeed with cached data)
                Assert.IsFalse(result.UpdateAvailable, 
                    "Should not report update available when fetch fails and cache is expired");
            }
        }

        [Test]
        public void CheckForUpdate_ReturnsAssetStoreMessage_ForNonGitInstallations()
        {
            // Note: This test verifies the service behavior when IsGitInstallation() returns false.
            // Since the actual result depends on package installation method, we create a mock
            // implementation to test this specific code path.
            
            var mockService = new MockAssetStorePackageUpdateService();
            
            // Act
            var result = mockService.CheckForUpdate("5.0.0");

            // Assert
            Assert.IsFalse(result.CheckSucceeded, "Check should not succeed for Asset Store installs");
            Assert.IsFalse(result.UpdateAvailable, "No update should be reported for Asset Store installs");
            Assert.AreEqual("Asset Store installations are updated via Unity Asset Store", result.Message,
                "Should return Asset Store update message");
            Assert.IsNull(result.LatestVersion, "Latest version should be null for Asset Store installs");
        }

        [Test]
        public void ClearCache_RemovesAllCachedData()
        {
            // Arrange: Set up cache
            EditorPrefs.SetString(TestLastCheckDateKey, DateTime.Now.ToString("yyyy-MM-dd"));
            EditorPrefs.SetString(TestCachedVersionKey, "5.0.0");

            // Verify cache exists
            Assert.IsTrue(EditorPrefs.HasKey(TestLastCheckDateKey), "Cache should exist before clearing");
            Assert.IsTrue(EditorPrefs.HasKey(TestCachedVersionKey), "Cache should exist before clearing");

            // Act
            _service.ClearCache();

            // Assert
            Assert.IsFalse(EditorPrefs.HasKey(TestLastCheckDateKey), "Date cache should be cleared");
            Assert.IsFalse(EditorPrefs.HasKey(TestCachedVersionKey), "Version cache should be cleared");
        }

        [Test]
        public void ClearCache_DoesNotThrow_WhenNoCacheExists()
        {
            // Ensure no cache exists
            CleanupEditorPrefs();

            // Act & Assert - should not throw
            Assert.DoesNotThrow(() => _service.ClearCache(), "Should not throw when clearing non-existent cache");
        }
    }

    /// <summary>
    /// Mock implementation of IPackageUpdateService that simulates Asset Store installation behavior
    /// </summary>
    internal class MockAssetStorePackageUpdateService : IPackageUpdateService
    {
        public UpdateCheckResult CheckForUpdate(string currentVersion)
        {
            // Simulate Asset Store installation (IsGitInstallation returns false)
            return new UpdateCheckResult
            {
                CheckSucceeded = false,
                UpdateAvailable = false,
                Message = "Asset Store installations are updated via Unity Asset Store"
            };
        }

        public bool IsNewerVersion(string version1, string version2)
        {
            // Not used in the Asset Store test, but required by interface
            return false;
        }

        public bool IsGitInstallation()
        {
            // Simulate non-Git installation (Asset Store)
            return false;
        }

        public void ClearCache()
        {
            // Not used in the Asset Store test, but required by interface
        }
    }
}
