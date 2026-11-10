using System;
using System.Collections;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
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
                var handler = CommandRegistry.GetHandler(toolName);
                Assert.IsNotNull(handler, $"Handler for '{toolName}' should not be null");

                // Verify the handler is actually callable (returns a result, not throws)
                var emptyParams = new Newtonsoft.Json.Linq.JObject();
                var result = handler(emptyParams);
                Assert.IsNotNull(result, $"Handler for '{toolName}' should return a result even for empty params");
            }
        }

        [UnityTest]
        public IEnumerator SyncHandlerThatReturnsATask_IsAwaitedOnBothEntryPoints()
        {
            // manage_scene's play-mode screenshot returns a Task from its synchronous handler,
            // so the capture can wait for the end of the frame. The dispatcher (ExecuteCommand)
            // and batch_execute (InvokeCommandAsync) must both wait for that Task instead of
            // answering with the Task object itself.
            const string name = "__test_sync_handler_returns_task";
            var handlers = (IDictionary)typeof(CommandRegistry)
                .GetField("_handlers", BindingFlags.NonPublic | BindingFlags.Static)
                .GetValue(null);
            var pending = new TaskCompletionSource<object>();
            handlers[name] = new HandlerInfo(name, _ => pending.Task, null);
            try
            {
                var tcs = new TaskCompletionSource<string>();
                Assert.IsNull(CommandRegistry.ExecuteCommand(name, new JObject(), tcs),
                    "the answer must come through the completion source, after the task");
                Assert.AreSame(pending.Task, CommandRegistry.InvokeCommandAsync(name, new JObject()));
                Assert.IsFalse(tcs.Task.IsCompleted, "the command answered before its task finished");

                pending.SetResult("captured");
                float deadline = Time.realtimeSinceStartup + 5f;
                while (!tcs.Task.IsCompleted && Time.realtimeSinceStartup < deadline)
                    yield return null;

                Assert.IsTrue(tcs.Task.IsCompleted, "the command never answered");
                StringAssert.Contains("\"result\":\"captured\"", tcs.Task.Result);
            }
            finally
            {
                handlers.Remove(name);
            }
        }
    }
}
