using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
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

        /// <summary>
        /// Discovery is TypeCache-only, so the built-in tools must all still surface — this is the
        /// guard that dropping the exhaustive assembly walk did not lose any of them.
        /// </summary>
        [Test]
        public void DiscoverAllTools_FindsEveryBuiltInTool()
        {
            string[] expected =
            {
                "manage_asset",
                "manage_editor",
                "manage_gameobject",
                "manage_scene",
                "manage_script",
                "manage_shader",
                "read_console",
                "execute_menu_item",
                "manage_prefabs"
            };

            var service = new ToolDiscoveryService();
            var discovered = service.DiscoverAllTools().Select(tool => tool.Name).ToList();

            foreach (string name in expected)
            {
                CollectionAssert.Contains(discovered, name, $"built-in tool '{name}' should be discovered");
            }
        }

        /// <summary>
        /// A duplicate tool name overwrites the previous registration, so registration order
        /// decides the winner. TypeCache documents no order, hence the explicit sort — without it
        /// a name collision could resolve differently from one domain reload to the next.
        /// </summary>
        [Test]
        public void DiscoverAllTools_RegistersInAStableOrder()
        {
            var service = new ToolDiscoveryService();

            var first = service.DiscoverAllTools().Select(tool => tool.Name).ToList();
            service.InvalidateCache();
            var second = service.DiscoverAllTools().Select(tool => tool.Name).ToList();

            CollectionAssert.AreEqual(first, second,
                "repeated discovery must produce the same registration order");
        }

        /// <summary>
        /// FullName alone is not a total order — two assemblies can declare the same full type
        /// name. If their tool names also collide the later registration wins, so the tie has to
        /// resolve the same way every reload. Two identically named types cannot coexist in one
        /// assembly, so this emits them into separate dynamic assemblies to build a real tie.
        /// </summary>
        [Test]
        public void InRegistrationOrder_BreaksFullNameTiesByAssembly()
        {
            const string sharedName = "McpTieBreak.Namespace.DuplicateTool";
            Type fromA = EmitTypeInOwnAssembly("McpTieBreakAssemblyA", sharedName);
            Type fromB = EmitTypeInOwnAssembly("McpTieBreakAssemblyB", sharedName);

            Assert.AreEqual(fromA.FullName, fromB.FullName,
                "precondition: the two types must share a full name for this to test a tie");
            Assert.AreNotEqual(fromA.Assembly.FullName, fromB.Assembly.FullName,
                "precondition: the two types must live in different assemblies");

            var forward = ToolDiscoveryService.InRegistrationOrder(new[] { fromA, fromB }).ToList();
            var reversed = ToolDiscoveryService.InRegistrationOrder(new[] { fromB, fromA }).ToList();

            CollectionAssert.AreEqual(forward, reversed,
                "input order must not decide the winner once full names tie");
            Assert.AreSame(fromA, forward[0], "assembly A sorts before assembly B");
            Assert.AreSame(fromB, forward[1], "assembly B registers last, so it would win a name collision");
        }

        private static Type EmitTypeInOwnAssembly(string assemblyName, string typeFullName)
        {
            AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(
                new AssemblyName(assemblyName), AssemblyBuilderAccess.Run);
            ModuleBuilder module = assembly.DefineDynamicModule(assemblyName);
            TypeBuilder type = module.DefineType(typeFullName, TypeAttributes.Public);
            return type.CreateType();
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
