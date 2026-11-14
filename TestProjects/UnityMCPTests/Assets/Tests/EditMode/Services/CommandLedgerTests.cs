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

        [Test]
        public void RunningRequest_IsNotEvictedUnderCapacityPressure()
        {
            CommandLedger.Begin("active", "slow_tool", out _, out _);
            var tcs = new TaskCompletionSource<string>();
            CommandLedger.SetInFlight("active", tcs.Task);
            for (int i = 0; i < CommandLedger.MaxEntries * 2; i++)
            {
                CommandLedger.Begin("p" + i, "t", out _, out _);
                CommandLedger.Complete("p" + i, "{\"status\":\"success\",\"result\":{}}");
            }

            Assert.IsTrue(CommandLedger.Contains("active"), "a running request was evicted");
            Assert.AreEqual(CommandLedger.Decision.Attach, CommandLedger.Begin("active", "slow_tool", out _, out var inFlight),
                "a resend of the running request must attach, not execute again");
            Assert.AreSame(tcs.Task, inFlight);
            Assert.IsFalse(CommandLedger.Contains("p0"), "finished entries are still evicted");

            CommandLedger.Complete("active", "{\"status\":\"success\",\"result\":{}}");
            for (int i = 0; i < CommandLedger.MaxEntries + 1; i++)
            {
                CommandLedger.Begin("q" + i, "t", out _, out _);
                CommandLedger.Complete("q" + i, "{\"status\":\"success\",\"result\":{}}");
            }
            Assert.IsFalse(CommandLedger.Contains("active"), "once finished it is evicted like any other entry");
        }

        [Test]
        public void TooManyRunningRequests_RefuseNewWorkInsteadOfEvicting()
        {
            for (int i = 0; i < CommandLedger.MaxActiveEntries; i++)
            {
                Assert.AreEqual(CommandLedger.Decision.Execute, CommandLedger.Begin("run" + i, "t", out _, out _));
            }

            Assert.AreEqual(CommandLedger.Decision.Replay, CommandLedger.Begin("one-too-many", "t", out var busy, out _));
            var obj = JObject.Parse(busy);
            Assert.AreEqual(CommandLedger.BusyCode, (string)obj["code"]);
            Assert.AreEqual("retry", (string)obj["hint"]);
            Assert.IsTrue(CommandLedger.Contains("run0"), "no running id was dropped");
            Assert.IsFalse(CommandLedger.Contains("one-too-many"), "the refused id is not recorded, so a retry can run it");
        }

        [Test]
        public void OversizedMessageAndError_AreClippedAfterCompaction()
        {
            var huge = new string('m', 1_000_000);
            var response = new JObject
            {
                ["status"] = "error",
                ["error"] = new string('e', 1_000_000),
                ["result"] = new JObject { ["success"] = false, ["message"] = huge },
            };
            CommandLedger.Begin("big", "t", out _, out _);
            CommandLedger.Complete("big", response.ToString());

            string stored = CommandLedger.StoredResponse("big");
            Assert.LessOrEqual(stored.Length, CommandLedger.MaxStoredResponseChars);
            var obj = JObject.Parse(stored);
            Assert.IsTrue((bool)obj["truncated"]);
            Assert.AreEqual("error", (string)obj["status"]);
            Assert.LessOrEqual(((string)obj["error"]).Length, CommandLedger.MaxStoredTextChars + 20);
            Assert.LessOrEqual(((string)obj["result"]["message"]).Length, CommandLedger.MaxStoredTextChars + 120);
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
