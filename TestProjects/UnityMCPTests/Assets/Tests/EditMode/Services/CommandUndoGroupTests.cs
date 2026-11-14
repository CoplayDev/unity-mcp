using System.Collections;
using System.Linq;
using System.Threading;
using MCPForUnity.Editor.Services.Transport;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MCPForUnityTests.Editor.Services
{
    [TestFixture]
    public class CommandUndoGroupTests
    {
        private const string Prefix = "UndoProbe_";

        [TearDown]
        public void TearDown()
        {
            foreach (var go in Roots().Where(g => g.name.StartsWith(Prefix)))
            {
                Object.DestroyImmediate(go);
            }
        }

        [UnityTest]
        public IEnumerator BatchExecute_IsUndoneInOneStep()
        {
            var batch = new JObject
            {
                ["type"] = "batch_execute",
                ["params"] = new JObject
                {
                    ["commands"] = new JArray
                    {
                        new JObject { ["tool"] = "manage_gameobject", ["params"] = new JObject { ["action"] = "create", ["name"] = Prefix + "A" } },
                        new JObject { ["tool"] = "manage_gameobject", ["params"] = new JObject { ["action"] = "create", ["name"] = Prefix + "B" } },
                    },
                },
            };
            yield return Dispatch(batch.ToString());
            Assert.AreEqual(2, Count(), "batch did not create both objects");
            Assert.AreEqual("MCP: batch_execute", Undo.GetCurrentGroupName());

            Undo.PerformUndo();

            Assert.AreEqual(0, Count(), "one undo must revert the whole batch");
        }

        [UnityTest]
        public IEnumerator SeparateCommands_AreSeparateUndoSteps()
        {
            yield return Dispatch(Create(Prefix + "First"));
            yield return Dispatch(Create(Prefix + "Second"));
            Assert.AreEqual("MCP: manage_gameobject create", Undo.GetCurrentGroupName());

            Undo.PerformUndo();

            Assert.AreEqual(1, Count());
            Assert.IsTrue(Roots().Any(g => g.name == Prefix + "First"));
        }

        [UnityTest]
        public IEnumerator AsyncCommand_DoesNotSwallowACommandThatRanWhileItWaited()
        {
            UndoAsyncProbe.Release = false;
            var discovery = MCPForUnity.Editor.Services.MCPServiceLocator.ToolDiscovery;
            bool wasEnabled = discovery.IsToolEnabled(UndoAsyncProbe.Name);
            discovery.SetToolEnabled(UndoAsyncProbe.Name, true);
            try
            {
                yield return RunAsyncOverlap();
            }
            finally
            {
                discovery.SetToolEnabled(UndoAsyncProbe.Name, wasEnabled);
            }
        }

        private IEnumerator RunAsyncOverlap()
        {
            var asyncReply = TransportCommandDispatcher.ExecuteCommandJsonAsync(
                new JObject { ["type"] = UndoAsyncProbe.Name, ["params"] = new JObject() }.ToString(), CancellationToken.None);
            for (int frame = 0; frame < 600 && !asyncReply.IsCompleted && !Roots().Any(g => g.name == Prefix + "AsyncBefore"); frame++) yield return null;
            Assert.IsTrue(Roots().Any(g => g.name == Prefix + "AsyncBefore"),
                "the async probe did not start: " + (asyncReply.IsCompleted ? asyncReply.Result : "no reply yet"));

            yield return Dispatch(Create(Prefix + "Interleaved"));   // runs while the async command waits
            UndoAsyncProbe.Release = true;
            for (int frame = 0; frame < 600 && !asyncReply.IsCompleted; frame++) yield return null;
            Assert.IsTrue(asyncReply.IsCompleted);

            Undo.PerformUndo();
            Assert.IsTrue(Roots().Any(g => g.name == Prefix + "AsyncBefore"),
                "one undo must not revert the async command's earlier step together with the interleaved command");
        }

        [Test]
        public void EditorUndoAction_IsNotWrapped()
        {
            Assert.AreEqual(-1, TransportCommandDispatcher.BeginUndoGroup("manage_editor", new JObject { ["action"] = "undo" }));
            Assert.AreEqual(-1, TransportCommandDispatcher.BeginUndoGroup("manage_editor", new JObject { ["action"] = "redo" }));
        }

        private static string Create(string name) => new JObject
        {
            ["type"] = "manage_gameobject",
            ["params"] = new JObject { ["action"] = "create", ["name"] = name },
        }.ToString();

        private static IEnumerator Dispatch(string json)
        {
            var reply = TransportCommandDispatcher.ExecuteCommandJsonAsync(json, CancellationToken.None);
            for (int frame = 0; frame < 600 && !reply.IsCompleted; frame++)
            {
                yield return null;
            }
            Assert.IsTrue(reply.IsCompleted, "the dispatcher never answered");
            Assert.AreEqual("success", (string)JObject.Parse(reply.Result)["status"], reply.Result);
        }

        private static GameObject[] Roots() => SceneManager.GetActiveScene().GetRootGameObjects();

        private static int Count() => Roots().Count(g => g.name.StartsWith(Prefix));
    }

    /// <summary>Async test tool: creates one object, waits until released, then creates another.</summary>
    [MCPForUnity.Editor.Tools.McpForUnityTool(UndoAsyncProbe.Name, AutoRegister = false)]
    public static class UndoAsyncProbe
    {
        public const string Name = "test_undo_async_probe";
        public static volatile bool Release;

        public static async System.Threading.Tasks.Task<object> HandleCommand(JObject @params)
        {
            var before = new GameObject("UndoProbe_AsyncBefore");
            Undo.RegisterCreatedObjectUndo(before, "async before");
            var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
            void Tick()
            {
                if (!Release) return;
                EditorApplication.update -= Tick;
                tcs.TrySetResult(true);
            }
            EditorApplication.update += Tick;
            await tcs.Task;
            var after = new GameObject("UndoProbe_AsyncAfter");
            Undo.RegisterCreatedObjectUndo(after, "async after");
            return new { created = 2 };
        }
    }
}
