using System;
using NUnit.Framework;
using MCPForUnity.Editor.Tools;

namespace MCPForUnityTests.Editor.Tools
{
    public class CommandRegistryTests
    {
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            // Ensure CommandRegistry is initialized before tests run
            CommandRegistry.Initialize();
        }

        [Test]
        public void GetHandler_ThrowsException_ForUnknownCommand()
        {
            var unknown = "nonexistent_command_that_should_not_exist";

            Assert.Throws<InvalidOperationException>(() =>
            {
                CommandRegistry.GetHandler(unknown);
            }, "Should throw InvalidOperationException for unknown handler");
        }

        [Test]
        public void AutoDiscovery_RegistersAllBuiltInTools()
        {
            // Verify that all expected built-in tools are registered by trying to get their handlers
            var expectedTools = new[]
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

            foreach (var toolName in expectedTools)
            {
                Assert.DoesNotThrow(() =>
                {
                    var handler = CommandRegistry.GetHandler(toolName);
                    Assert.IsNotNull(handler, $"Handler for '{toolName}' should not be null");
                }, $"Expected tool '{toolName}' to be auto-registered");
            }
        }
    }
}
