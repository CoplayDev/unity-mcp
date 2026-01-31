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
        private const string TestAssetStoreLastCheckDateKey = EditorPrefKeys.LastAssetStoreUpdateCheck;
        private const string TestAssetStoreCachedVersionKey = EditorPrefKeys.LatestKnownAssetStoreVersion;

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
            if (EditorPrefs.HasKey(TestAssetStoreLastCheckDateKey))
            {
                EditorPrefs.DeleteKey(TestAssetStoreLastCheckDateKey);
            }
            if (EditorPrefs.HasKey(TestAssetStoreCachedVersionKey))
            {
                EditorPrefs.DeleteKey(TestAssetStoreCachedVersionKey);
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
        public void CheckForUpdate_UsesAssetStoreCache_WhenCacheIsValid()
        {
            // Arrange: Set up valid Asset Store cache
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            string cachedVersion = "9.0.1";
            EditorPrefs.SetString(TestAssetStoreLastCheckDateKey, today);
            EditorPrefs.SetString(TestAssetStoreCachedVersionKey, cachedVersion);

            var mockService = new TestablePackageUpdateService
            {
                IsGitInstallationResult = false,
                AssetStoreFetchResult = "9.9.9"
            };

            // Act
            var result = mockService.CheckForUpdate("9.0.0");

            // Assert
            Assert.IsTrue(result.CheckSucceeded, "Check should succeed with valid Asset Store cache");
            Assert.AreEqual(cachedVersion, result.LatestVersion, "Should return cached Asset Store version");
            Assert.IsTrue(result.UpdateAvailable, "Update should be available (9.0.1 > 9.0.0)");
            Assert.IsFalse(mockService.AssetStoreFetchCalled, "Should not fetch when Asset Store cache is valid");
        }

        [Test]
        public void CheckForUpdate_FetchesAssetStoreJson_WhenCacheExpired()
        {
            // Arrange: Set expired Asset Store cache and a valid Git cache to ensure separation
            string yesterday = DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd");
            EditorPrefs.SetString(TestAssetStoreLastCheckDateKey, yesterday);
            EditorPrefs.SetString(TestAssetStoreCachedVersionKey, "9.0.0");
            EditorPrefs.SetString(TestLastCheckDateKey, DateTime.Now.ToString("yyyy-MM-dd"));
            EditorPrefs.SetString(TestCachedVersionKey, "99.0.0");

            var mockService = new TestablePackageUpdateService
            {
                IsGitInstallationResult = false,
                AssetStoreFetchResult = "9.1.0"
            };

            // Act
            var result = mockService.CheckForUpdate("9.0.0");

            // Assert
            Assert.IsTrue(result.CheckSucceeded, "Check should succeed when fetch returns a version");
            Assert.AreEqual("9.1.0", result.LatestVersion, "Should use fetched Asset Store version");
            Assert.IsTrue(mockService.AssetStoreFetchCalled, "Should fetch when Asset Store cache is expired");
        }

        [Test]
        public void CheckForUpdate_ReturnsAssetStoreFailureMessage_WhenFetchFails()
        {
            // Arrange
            var mockService = new TestablePackageUpdateService
            {
                IsGitInstallationResult = false,
                AssetStoreFetchResult = null
            };

            // Act
            var result = mockService.CheckForUpdate("9.0.0");

            // Assert
            Assert.IsFalse(result.CheckSucceeded, "Check should fail when Asset Store fetch fails");
            Assert.IsFalse(result.UpdateAvailable, "No update should be reported when fetch fails");
            Assert.AreEqual("Failed to check for Asset Store updates (network issue or offline)", result.Message);
            Assert.IsNull(result.LatestVersion, "Latest version should be null when fetch fails");
        }

        [Test]
        public void ClearCache_RemovesAllCachedData()
        {
            // Arrange: Set up cache
            EditorPrefs.SetString(TestLastCheckDateKey, DateTime.Now.ToString("yyyy-MM-dd"));
            EditorPrefs.SetString(TestCachedVersionKey, "5.0.0");
            EditorPrefs.SetString(TestAssetStoreLastCheckDateKey, DateTime.Now.ToString("yyyy-MM-dd"));
            EditorPrefs.SetString(TestAssetStoreCachedVersionKey, "9.0.0");

            // Verify cache exists
            Assert.IsTrue(EditorPrefs.HasKey(TestLastCheckDateKey), "Cache should exist before clearing");
            Assert.IsTrue(EditorPrefs.HasKey(TestCachedVersionKey), "Cache should exist before clearing");
            Assert.IsTrue(EditorPrefs.HasKey(TestAssetStoreLastCheckDateKey), "Asset Store cache should exist before clearing");
            Assert.IsTrue(EditorPrefs.HasKey(TestAssetStoreCachedVersionKey), "Asset Store cache should exist before clearing");

            // Act
            _service.ClearCache();

            // Assert
            Assert.IsFalse(EditorPrefs.HasKey(TestLastCheckDateKey), "Date cache should be cleared");
            Assert.IsFalse(EditorPrefs.HasKey(TestCachedVersionKey), "Version cache should be cleared");
            Assert.IsFalse(EditorPrefs.HasKey(TestAssetStoreLastCheckDateKey), "Asset Store date cache should be cleared");
            Assert.IsFalse(EditorPrefs.HasKey(TestAssetStoreCachedVersionKey), "Asset Store version cache should be cleared");
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
    /// Testable implementation that allows forcing install type and fetch results.
    /// </summary>
    internal class TestablePackageUpdateService : PackageUpdateService
    {
        public bool IsGitInstallationResult { get; set; } = true;
        public string GitFetchResult { get; set; }
        public string AssetStoreFetchResult { get; set; }
        public bool GitFetchCalled { get; private set; }
        public bool AssetStoreFetchCalled { get; private set; }

        public override bool IsGitInstallation()
        {
            return IsGitInstallationResult;
        }

        protected override string FetchLatestVersionFromGitHub()
        {
            GitFetchCalled = true;
            return GitFetchResult;
        }

        protected override string FetchLatestVersionFromAssetStoreJson()
        {
            AssetStoreFetchCalled = true;
            return AssetStoreFetchResult;
        }
    }
}
