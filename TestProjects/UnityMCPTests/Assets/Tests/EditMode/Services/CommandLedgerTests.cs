using System.Collections;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MCPForUnity.Editor.Services.Transport;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MCPForUnityTests.Editor.Services
{
    [TestFixture]
    public class CommandLedgerTests
    {
        private const string ProbePrefix = "LedgerProbe_";

        [SetUp]
        public void SetUp() => CommandLedger.Clear();

        [TearDown]
        public void TearDown()
        {
            CommandLedger.Clear();
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects().Where(g => g.name.StartsWith(ProbePrefix)))
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Begin_WithoutRequestId_AlwaysExecutes()
        {
            Assert.AreEqual(CommandLedger.Decision.Execute, CommandLedger.Begin(null, "x", out _, out _));
            Assert.AreEqual(CommandLedger.Decision.Execute, CommandLedger.Begin("", "x", out _, out _));
        }

        [Test]
        public void CompletedRequest_IsReplayedWithFlag()
        {
            Assert.AreEqual(CommandLedger.Decision.Execute, CommandLedger.Begin("r1", "manage_scene", out _, out _));
            CommandLedger.Complete("r1", "{\"status\":\"success\",\"result\":{\"success\":true,\"message\":\"done\"}}");

            Assert.AreEqual(CommandLedger.Decision.Replay, CommandLedger.Begin("r1", "manage_scene", out var replay, out _));
            var obj = JObject.Parse(replay);
            Assert.AreEqual("success", (string)obj["status"]);
            Assert.AreEqual("done", (string)obj["result"]["message"]);
            Assert.IsTrue((bool)obj["replayed"]);
        }

        [Test]
        public void RunningRequest_InSameDomain_Attaches()
        {
            CommandLedger.Begin("r2", "slow_tool", out _, out _);
            var tcs = new TaskCompletionSource<string>();
            CommandLedger.SetInFlight("r2", tcs.Task);

            Assert.AreEqual(CommandLedger.Decision.Attach, CommandLedger.Begin("r2", "slow_tool", out _, out var inFlight));
            Assert.AreSame(tcs.Task, inFlight);
        }

        [Test]
        public void RequestInterruptedByReload_ReportsUnknownOutcome()
        {
            CommandLedger.Begin("r3", "slow_tool", out _, out _);
            CommandLedger.SetInFlight("r3", new TaskCompletionSource<string>().Task);

            // Simulate the domain boundary: persisted before unload, restored after.
            CommandLedger.Persist();
            CommandLedger.Restore();

            Assert.AreEqual(CommandLedger.Decision.Replay, CommandLedger.Begin("r3", "slow_tool", out var replay, out _));
            var obj = JObject.Parse(replay);
            Assert.AreEqual("error", (string)obj["status"]);
            Assert.AreEqual(CommandLedger.OutcomeUnknownCode, (string)obj["code"]);
        }

        [Test]
        public void CompletedRequest_SurvivesReload()
        {
            CommandLedger.Begin("r4", "manage_gameobject", out _, out _);
            CommandLedger.Complete("r4", "{\"status\":\"success\",\"result\":{\"success\":true}}");
            CommandLedger.Persist();
            CommandLedger.Restore();

            Assert.AreEqual(CommandLedger.Decision.Replay, CommandLedger.Begin("r4", "manage_gameobject", out var replay, out _));
            Assert.IsTrue((bool)JObject.Parse(replay)["replayed"]);
        }

        [Test]
        public void LargeResponse_IsCompacted()
        {
            var big = new JObject
            {
                ["status"] = "success",
                ["result"] = new JObject
                {
                    ["success"] = true,
                    ["message"] = "Screenshot captured.",
                    ["data"] = new JObject { ["imageBase64"] = new string('A', CommandLedger.MaxStoredResponseChars * 2) },
                },
            };
            CommandLedger.Begin("r5", "manage_camera", out _, out _);
            CommandLedger.Complete("r5", big.ToString());

            CommandLedger.Begin("r5", "manage_camera", out var replay, out _);
            Assert.Less(replay.Length, CommandLedger.MaxStoredResponseChars);
            var obj = JObject.Parse(replay);
            Assert.IsTrue((bool)obj["truncated"]);
            StringAssert.StartsWith("Screenshot captured.", (string)obj["result"]["message"]);
        }

        [Test]
        public void OldestEntries_AreEvicted()
        {
            for (int i = 0; i <= CommandLedger.MaxEntries; i++)
            {
                CommandLedger.Begin("e" + i, "t", out _, out _);
                CommandLedger.Complete("e" + i, "{\"status\":\"success\",\"result\":{}}");
            }

            Assert.AreEqual(CommandLedger.Decision.Execute, CommandLedger.Begin("e0", "t", out _, out _));
            Assert.AreEqual(CommandLedger.Decision.Replay, CommandLedger.Begin("e" + CommandLedger.MaxEntries, "t", out _, out _));
        }

        [UnityTest]
        public IEnumerator Dispatcher_RunsResentMutationOnce()
        {
            string name = ProbePrefix + "Once";
            string json = new JObject
            {
                ["type"] = "manage_gameobject",
                ["params"] = new JObject { ["action"] = "create", ["name"] = name },
                ["request_id"] = "dispatch-1",
            }.ToString();

            JObject first = null, second = null;
            yield return Dispatch(json, r => first = r);
            yield return Dispatch(json, r => second = r);

            Assert.AreEqual("success", (string)first["status"]);
            Assert.IsNull(first["replayed"]);
            Assert.IsTrue((bool)second["replayed"]);
            Assert.AreEqual(1, CountRoots(name), "a resend with the same request_id must not create a second object");
        }

        [UnityTest]
        public IEnumerator Dispatcher_WithoutRequestId_KeepsLegacyBehaviour()
        {
            string name = ProbePrefix + "Twice";
            string json = new JObject
            {
                ["type"] = "manage_gameobject",
                ["params"] = new JObject { ["action"] = "create", ["name"] = name },
            }.ToString();

            yield return Dispatch(json, _ => { });
            yield return Dispatch(json, _ => { });

            Assert.AreEqual(2, CountRoots(name));
        }

        private static IEnumerator Dispatch(string json, System.Action<JObject> onReply)
        {
            var reply = TransportCommandDispatcher.ExecuteCommandJsonAsync(json, CancellationToken.None);
            for (int frame = 0; frame < 600 && !reply.IsCompleted; frame++)
            {
                yield return null;
            }
            Assert.IsTrue(reply.IsCompleted, "the dispatcher never answered");
            onReply(JObject.Parse(reply.Result));
        }

        private static int CountRoots(string name) =>
            SceneManager.GetActiveScene().GetRootGameObjects().Count(g => g.name == name);
    }
}
