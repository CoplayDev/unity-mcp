using System;
using NUnit.Framework;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using UnityEditor;

namespace MCPForUnityTests.Editor.Services
{
    /// <summary>
    /// Unit tests for EditorConfigurationCache.
    /// </summary>
    [TestFixture]
    public class EditorConfigurationCacheTests
    {
        private bool _originalUseHttpTransport;
        private bool _originalDebugLogs;
        private string _originalUvxPath;
        private string _scopedUseHttpTransportKey;
        private string _scopedHttpBaseUrlKey;
        private string _scopedHttpTransportScopeKey;
        private string _migrationOwnerKey;
        private bool _hadScopedUseHttpTransport;
        private bool _originalScopedUseHttpTransport;
        private bool _hadScopedHttpBaseUrl;
        private string _originalScopedHttpBaseUrl;
        private bool _hadScopedHttpTransportScope;
        private string _originalScopedHttpTransportScope;
        private bool _hadMigrationOwner;
        private string _originalMigrationOwner;

        [SetUp]
        public void SetUp()
        {
            // Save original values
            _originalUseHttpTransport = EditorPrefs.GetBool(EditorPrefKeys.UseHttpTransport, true);
            _originalDebugLogs = EditorPrefs.GetBool(EditorPrefKeys.DebugLogs, false);
            _originalUvxPath = EditorPrefs.GetString(EditorPrefKeys.UvxPathOverride, string.Empty);
            _scopedUseHttpTransportKey = ProjectScopedEditorPrefs.GetKey(EditorPrefKeys.UseHttpTransport);
            _scopedHttpBaseUrlKey = ProjectScopedEditorPrefs.GetKey(EditorPrefKeys.HttpBaseUrl);
            _scopedHttpTransportScopeKey = ProjectScopedEditorPrefs.GetKey(EditorPrefKeys.HttpTransportScope);
            _migrationOwnerKey = ProjectScopedEditorPrefs.GetMigrationOwnerKey();
            _hadScopedUseHttpTransport = EditorPrefs.HasKey(_scopedUseHttpTransportKey);
            _originalScopedUseHttpTransport = EditorPrefs.GetBool(_scopedUseHttpTransportKey, true);
            _hadScopedHttpBaseUrl = EditorPrefs.HasKey(_scopedHttpBaseUrlKey);
            _originalScopedHttpBaseUrl = EditorPrefs.GetString(_scopedHttpBaseUrlKey, string.Empty);
            _hadScopedHttpTransportScope = EditorPrefs.HasKey(_scopedHttpTransportScopeKey);
            _originalScopedHttpTransportScope = EditorPrefs.GetString(_scopedHttpTransportScopeKey, string.Empty);
            _hadMigrationOwner = EditorPrefs.HasKey(_migrationOwnerKey);
            _originalMigrationOwner = EditorPrefs.GetString(_migrationOwnerKey, string.Empty);

            EditorPrefs.DeleteKey(_scopedUseHttpTransportKey);
            EditorPrefs.DeleteKey(_scopedHttpBaseUrlKey);
            EditorPrefs.DeleteKey(_scopedHttpTransportScopeKey);
            EditorPrefs.DeleteKey(_migrationOwnerKey);

            // Refresh cache to ensure clean state
            EditorConfigurationCache.Instance.Refresh(allowLegacyFallback: false);
        }

        [TearDown]
        public void TearDown()
        {
            // Restore original values
            EditorPrefs.SetBool(EditorPrefKeys.UseHttpTransport, _originalUseHttpTransport);
            EditorPrefs.SetBool(EditorPrefKeys.DebugLogs, _originalDebugLogs);
            EditorPrefs.SetString(EditorPrefKeys.UvxPathOverride, _originalUvxPath);
            RestoreBool(_scopedUseHttpTransportKey, _hadScopedUseHttpTransport, _originalScopedUseHttpTransport);
            RestoreString(_scopedHttpBaseUrlKey, _hadScopedHttpBaseUrl, _originalScopedHttpBaseUrl);
            RestoreString(
                _scopedHttpTransportScopeKey,
                _hadScopedHttpTransportScope,
                _originalScopedHttpTransportScope);
            RestoreString(
                _migrationOwnerKey,
                _hadMigrationOwner,
                _originalMigrationOwner);

            EditorConfigurationCache.Instance.Refresh(allowLegacyFallback: false);
        }

        #region Singleton Tests

        [Test]
        public void Instance_ReturnsSameInstance()
        {
            // Act
            var instance1 = EditorConfigurationCache.Instance;
            var instance2 = EditorConfigurationCache.Instance;

            // Assert
            Assert.AreSame(instance1, instance2, "Should return the same singleton instance");
        }

        [Test]
        public void Instance_IsNotNull()
        {
            // Assert
            Assert.IsNotNull(EditorConfigurationCache.Instance);
        }

        #endregion

        #region Read Tests

        [Test]
        public void UseHttpTransport_ReturnsProjectScopedEditorPrefsValue()
        {
            // Arrange
            ProjectScopedEditorPrefs.SetBool(EditorPrefKeys.UseHttpTransport, true);
            EditorConfigurationCache.Instance.Refresh();

            // Assert
            Assert.IsTrue(EditorConfigurationCache.Instance.UseHttpTransport);

            // Arrange - change value
            ProjectScopedEditorPrefs.SetBool(EditorPrefKeys.UseHttpTransport, false);
            EditorConfigurationCache.Instance.Refresh();

            // Assert
            Assert.IsFalse(EditorConfigurationCache.Instance.UseHttpTransport);
        }

        [Test]
        public void DebugLogs_ReturnsEditorPrefsValue()
        {
            // Arrange
            EditorPrefs.SetBool(EditorPrefKeys.DebugLogs, true);
            EditorConfigurationCache.Instance.Refresh();

            // Assert
            Assert.IsTrue(EditorConfigurationCache.Instance.DebugLogs);
        }

        [Test]
        public void UvxPathOverride_ReturnsEditorPrefsValue()
        {
            // Arrange
            string testPath = "/custom/path/to/uvx";
            EditorPrefs.SetString(EditorPrefKeys.UvxPathOverride, testPath);
            EditorConfigurationCache.Instance.Refresh();

            // Assert
            Assert.AreEqual(testPath, EditorConfigurationCache.Instance.UvxPathOverride);
        }

        #endregion

        #region Write Tests

        [Test]
        public void SetUseHttpTransport_UpdatesCacheAndEditorPrefs()
        {
            // Arrange
            bool initialValue = EditorConfigurationCache.Instance.UseHttpTransport;
            bool newValue = !initialValue;
            EditorPrefs.SetBool(EditorPrefKeys.UseHttpTransport, initialValue);

            // Act
            EditorConfigurationCache.Instance.SetUseHttpTransport(newValue);

            // Assert - cache is updated
            Assert.AreEqual(newValue, EditorConfigurationCache.Instance.UseHttpTransport);

            // Assert - only the current project's EditorPrefs value is updated
            Assert.AreEqual(newValue, EditorPrefs.GetBool(_scopedUseHttpTransportKey, !newValue));
            Assert.AreEqual(initialValue, EditorPrefs.GetBool(EditorPrefKeys.UseHttpTransport, !initialValue));
        }

        [Test]
        public void ProjectScopedKeys_DifferByProjectHash()
        {
            string first = ProjectScopedEditorPrefs.GetKey(EditorPrefKeys.HttpBaseUrl, "project-a");
            string second = ProjectScopedEditorPrefs.GetKey(EditorPrefKeys.HttpBaseUrl, "project-b");

            Assert.AreNotEqual(first, second);
        }

        [Test]
        public void LegacyPreference_ClaimedByOneProjectOnly()
        {
            const string baseKey = EditorPrefKeys.HttpBaseUrl;
            string firstProject = $"test-project-a-{Guid.NewGuid():N}";
            string secondProject = $"test-project-b-{Guid.NewGuid():N}";
            string firstKey = ProjectScopedEditorPrefs.GetKey(baseKey, firstProject);
            string secondKey = ProjectScopedEditorPrefs.GetKey(baseKey, secondProject);
            string ownerKey = ProjectScopedEditorPrefs.GetMigrationOwnerKey();
            bool hadLegacy = EditorPrefs.HasKey(baseKey);
            string originalLegacy = EditorPrefs.GetString(baseKey, string.Empty);
            bool hadFirst = EditorPrefs.HasKey(firstKey);
            string originalFirst = EditorPrefs.GetString(firstKey, string.Empty);
            bool hadSecond = EditorPrefs.HasKey(secondKey);
            string originalSecond = EditorPrefs.GetString(secondKey, string.Empty);
            bool hadOwner = EditorPrefs.HasKey(ownerKey);
            string originalOwner = EditorPrefs.GetString(ownerKey, string.Empty);

            try
            {
                EditorPrefs.SetString(baseKey, "legacy");
                EditorPrefs.DeleteKey(firstKey);
                EditorPrefs.DeleteKey(secondKey);
                EditorPrefs.DeleteKey(ownerKey);

                Assert.AreEqual("legacy", ProjectScopedEditorPrefs.GetString(baseKey, "default", firstProject));
                Assert.AreEqual("legacy", EditorPrefs.GetString(firstKey, "default"));
                Assert.AreEqual("default", ProjectScopedEditorPrefs.GetString(baseKey, "default", secondProject));
            }
            finally
            {
                RestoreString(baseKey, hadLegacy, originalLegacy);
                RestoreString(firstKey, hadFirst, originalFirst);
                RestoreString(secondKey, hadSecond, originalSecond);
                RestoreString(ownerKey, hadOwner, originalOwner);
            }
        }

        [Test]
        public void LegacyPreferenceBundle_ClaimedAtomicallyByOneProject()
        {
            const string firstBaseKey = EditorPrefKeys.HttpBaseUrl;
            const string secondBaseKey = EditorPrefKeys.HttpTransportScope;
            string firstProject = $"test-project-a-{Guid.NewGuid():N}";
            string secondProject = $"test-project-b-{Guid.NewGuid():N}";
            string firstProjectFirstKey = ProjectScopedEditorPrefs.GetKey(firstBaseKey, firstProject);
            string firstProjectSecondKey = ProjectScopedEditorPrefs.GetKey(secondBaseKey, firstProject);
            string secondProjectSecondKey = ProjectScopedEditorPrefs.GetKey(secondBaseKey, secondProject);
            string ownerKey = ProjectScopedEditorPrefs.GetMigrationOwnerKey();
            bool hadFirstLegacy = EditorPrefs.HasKey(firstBaseKey);
            string originalFirstLegacy = EditorPrefs.GetString(firstBaseKey, string.Empty);
            bool hadSecondLegacy = EditorPrefs.HasKey(secondBaseKey);
            string originalSecondLegacy = EditorPrefs.GetString(secondBaseKey, string.Empty);
            bool hadFirstProjectFirst = EditorPrefs.HasKey(firstProjectFirstKey);
            string originalFirstProjectFirst = EditorPrefs.GetString(firstProjectFirstKey, string.Empty);
            bool hadFirstProjectSecond = EditorPrefs.HasKey(firstProjectSecondKey);
            string originalFirstProjectSecond = EditorPrefs.GetString(firstProjectSecondKey, string.Empty);
            bool hadSecondProjectSecond = EditorPrefs.HasKey(secondProjectSecondKey);
            string originalSecondProjectSecond = EditorPrefs.GetString(secondProjectSecondKey, string.Empty);
            bool hadOwner = EditorPrefs.HasKey(ownerKey);
            string originalOwner = EditorPrefs.GetString(ownerKey, string.Empty);

            try
            {
                EditorPrefs.SetString(firstBaseKey, "first-legacy");
                EditorPrefs.SetString(secondBaseKey, "second-legacy");
                EditorPrefs.DeleteKey(firstProjectFirstKey);
                EditorPrefs.DeleteKey(firstProjectSecondKey);
                EditorPrefs.DeleteKey(secondProjectSecondKey);
                EditorPrefs.DeleteKey(ownerKey);

                Assert.AreEqual(
                    "first-legacy",
                    ProjectScopedEditorPrefs.GetString(firstBaseKey, "default", firstProject));
                Assert.AreEqual(
                    "default",
                    ProjectScopedEditorPrefs.GetString(secondBaseKey, "default", secondProject));
                Assert.AreEqual(
                    "second-legacy",
                    ProjectScopedEditorPrefs.GetString(secondBaseKey, "default", firstProject));
            }
            finally
            {
                RestoreString(firstBaseKey, hadFirstLegacy, originalFirstLegacy);
                RestoreString(secondBaseKey, hadSecondLegacy, originalSecondLegacy);
                RestoreString(firstProjectFirstKey, hadFirstProjectFirst, originalFirstProjectFirst);
                RestoreString(firstProjectSecondKey, hadFirstProjectSecond, originalFirstProjectSecond);
                RestoreString(secondProjectSecondKey, hadSecondProjectSecond, originalSecondProjectSecond);
                RestoreString(ownerKey, hadOwner, originalOwner);
            }
        }

        [Test]
        public void SetUseHttpTransport_SameValuePersistsMissingScopedPreference()
        {
            bool currentValue = EditorConfigurationCache.Instance.UseHttpTransport;
            EditorPrefs.DeleteKey(_scopedUseHttpTransportKey);

            EditorConfigurationCache.Instance.SetUseHttpTransport(currentValue);

            Assert.IsTrue(EditorPrefs.HasKey(_scopedUseHttpTransportKey));
            Assert.AreEqual(currentValue, EditorPrefs.GetBool(_scopedUseHttpTransportKey, !currentValue));
        }

        [Test]
        public void Refresh_LegacyFallbackDisabled_DoesNotMigrateOrClaimOwner()
        {
            EditorPrefs.SetBool(EditorPrefKeys.UseHttpTransport, false);
            EditorPrefs.DeleteKey(_scopedUseHttpTransportKey);
            EditorPrefs.DeleteKey(_migrationOwnerKey);

            EditorConfigurationCache.Instance.Refresh(allowLegacyFallback: false);

            Assert.IsTrue(EditorConfigurationCache.Instance.UseHttpTransport);
            Assert.IsFalse(EditorPrefs.HasKey(_scopedUseHttpTransportKey));
            Assert.IsFalse(EditorPrefs.HasKey(_migrationOwnerKey));
        }

        [Test]
        public void ResolveStorageKey_UsesProjectScopeOnlyForKnownScopedPreferences()
        {
            const string unscopedKey = "MCPForUnity.Tests.UnscopedPreference";

            Assert.AreEqual(
                ProjectScopedEditorPrefs.GetKey(EditorPrefKeys.HttpBaseUrl),
                ProjectScopedEditorPrefs.ResolveStorageKey(EditorPrefKeys.HttpBaseUrl));
            Assert.AreEqual(
                EditorPrefKeys.DebugLogs,
                ProjectScopedEditorPrefs.ResolveStorageKey(EditorPrefKeys.DebugLogs));
            Assert.AreEqual(
                unscopedKey,
                ProjectScopedEditorPrefs.ResolveStorageKey(unscopedKey));

            try
            {
                ProjectScopedEditorPrefs.SetString(unscopedKey, "value");
                Assert.AreEqual("value", EditorPrefs.GetString(unscopedKey, string.Empty));
                Assert.IsFalse(EditorPrefs.HasKey(ProjectScopedEditorPrefs.GetKey(unscopedKey)));
            }
            finally
            {
                ProjectScopedEditorPrefs.DeleteKey(unscopedKey);
                EditorPrefs.DeleteKey(ProjectScopedEditorPrefs.GetKey(unscopedKey));
            }
        }

        [Test]
        public void LocalHttpUrl_ProcessEnvironmentOverrideTakesPriority()
        {
            string originalEnvironment = Environment.GetEnvironmentVariable("UNITY_MCP_HTTP_URL");
            string scopeKey = ProjectScopedEditorPrefs.GetKey(EditorPrefKeys.HttpTransportScope);
            bool hadScope = EditorPrefs.HasKey(scopeKey);
            string originalScope = ProjectScopedEditorPrefs.GetString(
                EditorPrefKeys.HttpTransportScope,
                "local",
                allowLegacyFallback: false);
            try
            {
                HttpEndpointUtility.SaveLocalBaseUrl("http://127.0.0.1:59991");
                EditorConfigurationCache.Instance.SetHttpTransportScope("remote");
                Environment.SetEnvironmentVariable("UNITY_MCP_HTTP_URL", "http://127.0.0.1:59992/mcp");

                Assert.AreEqual("http://127.0.0.1:59992", HttpEndpointUtility.GetLocalBaseUrl());
                Assert.AreEqual("http://127.0.0.1:59992", HttpEndpointUtility.GetBaseUrl());
                Assert.IsFalse(HttpEndpointUtility.IsRemoteScope());
                Assert.AreEqual(
                    "http://127.0.0.1:59991",
                    EditorPrefs.GetString(_scopedHttpBaseUrlKey, string.Empty));
            }
            finally
            {
                Environment.SetEnvironmentVariable("UNITY_MCP_HTTP_URL", originalEnvironment);
                if (hadScope)
                    EditorConfigurationCache.Instance.SetHttpTransportScope(originalScope);
                else
                    ProjectScopedEditorPrefs.DeleteKey(EditorPrefKeys.HttpTransportScope);
                EditorConfigurationCache.Instance.Refresh(allowLegacyFallback: false);
            }
        }

        [Test]
        public void LocalHttpUrl_InvalidEnvironmentOverrideDoesNotBypassRemotePolicy()
        {
            string originalEnvironment = Environment.GetEnvironmentVariable("UNITY_MCP_HTTP_URL");
            string scopeKey = ProjectScopedEditorPrefs.GetKey(EditorPrefKeys.HttpTransportScope);
            bool hadScope = EditorPrefs.HasKey(scopeKey);
            string originalScope = ProjectScopedEditorPrefs.GetString(
                EditorPrefKeys.HttpTransportScope,
                "local",
                allowLegacyFallback: false);
            bool hadRemote = EditorPrefs.HasKey(EditorPrefKeys.HttpRemoteBaseUrl);
            string originalRemote = EditorPrefs.GetString(
                EditorPrefKeys.HttpRemoteBaseUrl,
                string.Empty);

            try
            {
                EditorConfigurationCache.Instance.SetHttpTransportScope("remote");
                HttpEndpointUtility.SaveRemoteBaseUrl("https://configured.example");

                Environment.SetEnvironmentVariable(
                    "UNITY_MCP_HTTP_URL",
                    "https://external.example");
                Assert.IsTrue(HttpEndpointUtility.IsRemoteScope());
                Assert.AreEqual("https://configured.example", HttpEndpointUtility.GetBaseUrl());

                Environment.SetEnvironmentVariable(
                    "UNITY_MCP_HTTP_URL",
                    "ftp://127.0.0.1:59992");
                Assert.IsTrue(HttpEndpointUtility.IsRemoteScope());
                Assert.AreEqual("https://configured.example", HttpEndpointUtility.GetBaseUrl());
                Assert.IsFalse(
                    HttpEndpointUtility.IsHttpLocalUrlAllowedForLaunch(
                        "ftp://127.0.0.1:59992",
                        out _));
            }
            finally
            {
                Environment.SetEnvironmentVariable("UNITY_MCP_HTTP_URL", originalEnvironment);
                if (hadScope)
                    EditorConfigurationCache.Instance.SetHttpTransportScope(originalScope);
                else
                    ProjectScopedEditorPrefs.DeleteKey(EditorPrefKeys.HttpTransportScope);

                if (hadRemote)
                    EditorPrefs.SetString(EditorPrefKeys.HttpRemoteBaseUrl, originalRemote);
                else
                    EditorPrefs.DeleteKey(EditorPrefKeys.HttpRemoteBaseUrl);

                EditorConfigurationCache.Instance.Refresh(allowLegacyFallback: false);
            }
        }

        [Test]
        public void LocalHttpUrl_ProcessEnvironmentOverrideMatchesExpectedValue()
        {
            string expected = Environment.GetEnvironmentVariable("UNITY_MCP_EXPECTED_HTTP_URL");
            if (string.IsNullOrWhiteSpace(expected))
            {
                Assert.Ignore("Only used by the isolated multi-Editor endpoint stress route.");
            }

            Assert.AreEqual(expected, HttpEndpointUtility.GetLocalBaseUrl());
            Assert.AreEqual(expected, HttpEndpointUtility.GetBaseUrl());
        }

        [Test]
        public void SetDebugLogs_UpdatesCacheAndEditorPrefs()
        {
            // Act
            EditorConfigurationCache.Instance.SetDebugLogs(true);

            // Assert
            Assert.IsTrue(EditorConfigurationCache.Instance.DebugLogs);
            Assert.IsTrue(EditorPrefs.GetBool(EditorPrefKeys.DebugLogs, false));
        }

        [Test]
        public void SetUvxPathOverride_UpdatesCacheAndEditorPrefs()
        {
            // Arrange
            string testPath = "/test/uvx/path";

            // Act
            EditorConfigurationCache.Instance.SetUvxPathOverride(testPath);

            // Assert
            Assert.AreEqual(testPath, EditorConfigurationCache.Instance.UvxPathOverride);
            Assert.AreEqual(testPath, EditorPrefs.GetString(EditorPrefKeys.UvxPathOverride, string.Empty));
        }

        [Test]
        public void SetUvxPathOverride_NullBecomesEmptyString()
        {
            // Act
            EditorConfigurationCache.Instance.SetUvxPathOverride(null);

            // Assert
            Assert.AreEqual(string.Empty, EditorConfigurationCache.Instance.UvxPathOverride);
        }

        #endregion

        #region Change Notification Tests

        [Test]
        public void SetUseHttpTransport_FiresOnConfigurationChanged()
        {
            // Arrange
            string changedKey = null;
            EditorConfigurationCache.Instance.OnConfigurationChanged += (key) => changedKey = key;
            bool initialValue = EditorConfigurationCache.Instance.UseHttpTransport;

            // Act
            EditorConfigurationCache.Instance.SetUseHttpTransport(!initialValue);

            // Assert
            Assert.AreEqual(nameof(EditorConfigurationCache.UseHttpTransport), changedKey);

            // Cleanup
            EditorConfigurationCache.Instance.OnConfigurationChanged -= (key) => changedKey = key;
        }

        [Test]
        public void SetSameValue_DoesNotFireOnConfigurationChanged()
        {
            // Arrange
            int eventCount = 0;
            EditorConfigurationCache.Instance.OnConfigurationChanged += (key) => eventCount++;
            bool currentValue = EditorConfigurationCache.Instance.UseHttpTransport;

            // Act - set same value
            EditorConfigurationCache.Instance.SetUseHttpTransport(currentValue);

            // Assert - no event fired
            Assert.AreEqual(0, eventCount, "Should not fire event when value doesn't change");

            // Cleanup
            EditorConfigurationCache.Instance.OnConfigurationChanged -= (key) => eventCount++;
        }

        #endregion

        #region InvalidateKey Tests

        [Test]
        public void InvalidateKey_RefreshesSingleValue()
        {
            // Arrange
            EditorConfigurationCache.Instance.SetDebugLogs(false);
            Assert.IsFalse(EditorConfigurationCache.Instance.DebugLogs);

            // Directly modify EditorPrefs (simulating external change)
            EditorPrefs.SetBool(EditorPrefKeys.DebugLogs, true);

            // Act
            EditorConfigurationCache.Instance.InvalidateKey(nameof(EditorConfigurationCache.DebugLogs));

            // Assert
            Assert.IsTrue(EditorConfigurationCache.Instance.DebugLogs);
        }

        [Test]
        public void InvalidateKey_FiresOnConfigurationChanged()
        {
            // Arrange
            string changedKey = null;
            EditorConfigurationCache.Instance.OnConfigurationChanged += (key) => changedKey = key;

            // Act
            EditorConfigurationCache.Instance.InvalidateKey(nameof(EditorConfigurationCache.DebugLogs));

            // Assert
            Assert.AreEqual(nameof(EditorConfigurationCache.DebugLogs), changedKey);

            // Cleanup
            EditorConfigurationCache.Instance.OnConfigurationChanged -= (key) => changedKey = key;
        }

        #endregion

        #region Refresh Tests

        [Test]
        public void Refresh_UpdatesAllCachedValues()
        {
            // Arrange - directly set EditorPrefs
            ProjectScopedEditorPrefs.SetBool(EditorPrefKeys.UseHttpTransport, false);
            EditorPrefs.SetBool(EditorPrefKeys.DebugLogs, true);
            EditorPrefs.SetString(EditorPrefKeys.UvxPathOverride, "/refreshed/path");

            // Act
            EditorConfigurationCache.Instance.Refresh();

            // Assert
            Assert.IsFalse(EditorConfigurationCache.Instance.UseHttpTransport);
            Assert.IsTrue(EditorConfigurationCache.Instance.DebugLogs);
            Assert.AreEqual("/refreshed/path", EditorConfigurationCache.Instance.UvxPathOverride);
        }

        #endregion

        private static void RestoreBool(string key, bool hadValue, bool value)
        {
            if (hadValue)
            {
                EditorPrefs.SetBool(key, value);
            }
            else
            {
                EditorPrefs.DeleteKey(key);
            }
        }

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
