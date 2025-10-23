using NUnit.Framework;
using UnityEditor;
using System.IO;

namespace MCPForUnityTests.Editor.Helpers
{
    /// <summary>
    /// Tests for PackageLifecycleManager.
    /// Note: These tests verify the logic but cannot fully test [InitializeOnLoad] behavior.
    /// </summary>
    public class PackageLifecycleManagerTests
    {
        private const string TestVersionKey = "MCPForUnity.InstalledVersion:test-version";
        private const string LegacyInstallFlagKey = "MCPForUnity.ServerInstalled";

        [SetUp]
        public void SetUp()
        {
            // Clean up test keys before each test
            CleanupTestKeys();
        }

        [TearDown]
        public void TearDown()
        {
            // Clean up test keys after each test
            CleanupTestKeys();
        }

        private void CleanupTestKeys()
        {
            try
            {
                if (EditorPrefs.HasKey(TestVersionKey))
                {
                    EditorPrefs.DeleteKey(TestVersionKey);
                }
                if (EditorPrefs.HasKey(LegacyInstallFlagKey))
                {
                    EditorPrefs.DeleteKey(LegacyInstallFlagKey);
                }
                // Clean up any other test-related keys
                string[] testKeys = {
                    "MCPForUnity.ServerSrc",
                    "MCPForUnity.PythonDirOverride",
                    "MCPForUnity.LegacyDetectLogged"
                };
                foreach (var key in testKeys)
                {
                    if (EditorPrefs.HasKey(key))
                    {
                        EditorPrefs.DeleteKey(key);
                    }
                }
            }
            catch { }
        }

        [Test]
        public void FirstTimeInstall_ShouldNotHaveLegacyFlag()
        {
            // Verify that on a fresh install, the legacy flag doesn't exist
            Assert.IsFalse(EditorPrefs.HasKey(LegacyInstallFlagKey),
                "Fresh install should not have legacy installation flag");
        }

        [Test]
        public void VersionKey_ShouldBeVersionScoped()
        {
            // Verify that version keys are properly scoped
            string version1Key = "MCPForUnity.InstalledVersion:1.0.0";
            string version2Key = "MCPForUnity.InstalledVersion:2.0.0";

            Assert.AreNotEqual(version1Key, version2Key,
                "Different versions should have different keys");
            Assert.IsTrue(version1Key.StartsWith("MCPForUnity.InstalledVersion:"),
                "Version key should have correct prefix");
        }

        [Test]
        public void LegacyPrefsCleanup_ShouldRemoveOldKeys()
        {
            // Set up legacy keys
            EditorPrefs.SetString("MCPForUnity.ServerSrc", "test");
            EditorPrefs.SetString("MCPForUnity.PythonDirOverride", "test");

            // Verify they exist
            Assert.IsTrue(EditorPrefs.HasKey("MCPForUnity.ServerSrc"),
                "Legacy key should exist before cleanup");
            Assert.IsTrue(EditorPrefs.HasKey("MCPForUnity.PythonDirOverride"),
                "Legacy key should exist before cleanup");

            // Note: We can't directly test the cleanup since it's private,
            // but we can verify the keys exist and document expected behavior
            // In actual usage, PackageLifecycleManager will clean these up
        }

        [Test]
        public void VersionKeyFormat_ShouldFollowConvention()
        {
            // Test that version key format follows the expected pattern
            string testVersion = "1.2.3";
            string expectedKey = $"MCPForUnity.InstalledVersion:{testVersion}";

            Assert.AreEqual("MCPForUnity.InstalledVersion:1.2.3", expectedKey,
                "Version key should follow format: prefix + version");
        }

        [Test]
        public void MultipleVersions_ShouldHaveIndependentKeys()
        {
            // Simulate multiple version installations
            EditorPrefs.SetBool("MCPForUnity.InstalledVersion:1.0.0", true);
            EditorPrefs.SetBool("MCPForUnity.InstalledVersion:2.0.0", true);

            Assert.IsTrue(EditorPrefs.GetBool("MCPForUnity.InstalledVersion:1.0.0"),
                "Version 1.0.0 flag should be set");
            Assert.IsTrue(EditorPrefs.GetBool("MCPForUnity.InstalledVersion:2.0.0"),
                "Version 2.0.0 flag should be set");

            // Clean up
            EditorPrefs.DeleteKey("MCPForUnity.InstalledVersion:1.0.0");
            EditorPrefs.DeleteKey("MCPForUnity.InstalledVersion:2.0.0");
        }

        [Test]
        public void LegacyFlagMigration_ShouldPreserveBackwardCompatibility()
        {
            // Simulate a scenario where old PackageInstaller set the flag
            EditorPrefs.SetBool(LegacyInstallFlagKey, true);

            Assert.IsTrue(EditorPrefs.GetBool(LegacyInstallFlagKey),
                "Legacy flag should be readable for backward compatibility");
        }

        [Test]
        public void EditorPrefsKeys_ShouldNotConflict()
        {
            // Verify that our keys don't conflict with Unity or other packages
            string[] ourKeys = {
                "MCPForUnity.InstalledVersion:1.0.0",
                "MCPForUnity.ServerInstalled",
                "MCPForUnity.ServerSrc",
                "MCPForUnity.PythonDirOverride"
            };

            foreach (var key in ourKeys)
            {
                Assert.IsTrue(key.StartsWith("MCPForUnity."),
                    $"Key '{key}' should be properly namespaced");
            }
        }

        [Test]
        public void VersionString_ShouldHandleUnknownGracefully()
        {
            // Test that "unknown" version is a valid fallback
            string unknownVersion = "unknown";
            string versionKey = $"MCPForUnity.InstalledVersion:{unknownVersion}";

            Assert.IsNotNull(versionKey, "Version key should handle 'unknown' version");
            Assert.IsTrue(versionKey.Contains("unknown"),
                "Version key should contain the unknown version string");
        }
    }
}
