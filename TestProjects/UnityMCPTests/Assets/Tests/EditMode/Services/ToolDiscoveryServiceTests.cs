using System.Linq;
using NUnit.Framework;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Services;
using UnityEditor;

namespace MCPForUnity.Editor.Tests.EditMode.Services
{
    [TestFixture]
    public class ToolDiscoveryServiceTests
    {
        private const string TestToolName = "test_tool_for_testing";

        [SetUp]
        public void SetUp()
        {
            // Clean up any test preferences
            string testKey = EditorPrefKeys.ToolEnabledPrefix + TestToolName;
            if (EditorPrefs.HasKey(testKey))
            {
                EditorPrefs.DeleteKey(testKey);
            }
        }

        [TearDown]
        public void TearDown()
        {
            // Clean up test preferences after each test
            string testKey = EditorPrefKeys.ToolEnabledPrefix + TestToolName;
            if (EditorPrefs.HasKey(testKey))
            {
                EditorPrefs.DeleteKey(testKey);
            }
        }

        [Test]
        public void SetToolEnabled_WritesToEditorPrefs()
        {
            // Arrange
            var service = new ToolDiscoveryService();

            // Act
            service.SetToolEnabled(TestToolName, false);

            // Assert
            string key = EditorPrefKeys.ToolEnabledPrefix + TestToolName;
            Assert.IsTrue(EditorPrefs.HasKey(key), "Preference key should exist after SetToolEnabled");
            Assert.IsFalse(EditorPrefs.GetBool(key, true), "Preference should be set to false");
        }

        [Test]
        public void IsToolEnabled_ReturnsFalse_WhenToolDoesNotExist()
        {
            // Arrange - Ensure no preference exists
            string key = EditorPrefKeys.ToolEnabledPrefix + TestToolName;
            if (EditorPrefs.HasKey(key))
            {
                EditorPrefs.DeleteKey(key);
            }

            var service = new ToolDiscoveryService();

            // Act - For a non-existent tool, IsToolEnabled should return false
            // (since metadata.AutoRegister defaults to false for non-existent tools)
            bool result = service.IsToolEnabled(TestToolName);

            // Assert - Non-existent tools return false (no metadata found)
            Assert.IsFalse(result, "Non-existent tool should return false");
        }

        [Test]
        public void IsToolEnabled_ReturnsStoredValue_WhenPreferenceExists()
        {
            // Arrange
            string key = EditorPrefKeys.ToolEnabledPrefix + TestToolName;
            EditorPrefs.SetBool(key, false);  // Store false value
            var service = new ToolDiscoveryService();

            // Act
            bool result = service.IsToolEnabled(TestToolName);

            // Assert
            Assert.IsFalse(result, "Should return the stored preference value (false)");
        }

        [Test]
        public void IsToolEnabled_ReturnsTrue_WhenPreferenceSetToTrue()
        {
            // Arrange
            string key = EditorPrefKeys.ToolEnabledPrefix + TestToolName;
            EditorPrefs.SetBool(key, true);
            var service = new ToolDiscoveryService();

            // Act
            bool result = service.IsToolEnabled(TestToolName);

            // Assert
            Assert.IsTrue(result, "Should return the stored preference value (true)");
        }

        [Test]
        public void ToolToggle_PersistsAcrossServiceInstances()
        {
            // Arrange
            var service1 = new ToolDiscoveryService();
            service1.SetToolEnabled(TestToolName, false);

            // Act - Create a new service instance
            var service2 = new ToolDiscoveryService();
            bool result = service2.IsToolEnabled(TestToolName);

            // Assert - The disabled state should persist
            Assert.IsFalse(result, "Tool state should persist across service instances");
        }

        [Test]
        public void DiscoverAllTools_DoesNotOverrideStoredFalse_ForBuiltInAutoRegisterFalseTool()
        {
            // Arrange
            var service = new ToolDiscoveryService();
            var builtInTool = service.DiscoverAllTools()
                .FirstOrDefault(tool => tool.IsBuiltIn && !tool.AutoRegister);

            Assert.IsNotNull(builtInTool, "Expected at least one built-in tool with AutoRegister=false.");

            string key = EditorPrefKeys.ToolEnabledPrefix + builtInTool.Name;
            bool hadOriginalKey = EditorPrefs.HasKey(key);
            bool originalValue = hadOriginalKey && EditorPrefs.GetBool(key, true);

            try
            {
                EditorPrefs.SetBool(key, false);
                service.InvalidateCache();

                // Act
                service.DiscoverAllTools();
                bool enabled = service.IsToolEnabled(builtInTool.Name);

                // Assert
                Assert.IsFalse(enabled, $"Built-in tool '{builtInTool.Name}' should remain disabled when preference is false.");
            }
            finally
            {
                if (hadOriginalKey)
                {
                    EditorPrefs.SetBool(key, originalValue);
                }
                else
                {
                    EditorPrefs.DeleteKey(key);
                }
            }
        }
    }
}
