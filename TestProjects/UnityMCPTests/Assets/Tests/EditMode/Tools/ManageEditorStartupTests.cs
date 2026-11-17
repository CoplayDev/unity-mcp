using MCPForUnity.Editor.Tools;
using System;
using System.Collections;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using TestNamespace;
using static MCPForUnityTests.Editor.TestUtilities;
using Object = UnityEngine.Object;

namespace MCPForUnityTests.Editor.Tools
{
    public class ManageEditorStartupTests
    {
        [Test]
        public void GetStartup_RequiresJobId()
        {
            var response = ToJObject(ManageEditor.HandleCommand(new JObject { ["action"] = "get_play_mode_startup" }));
            Assert.IsFalse(response.Value<bool>("success"));
            StringAssert.Contains("jobId", response.Value<string>("error"));
        }

        [TestCase("unexpected")]
        [TestCase("")]
        public void InvalidWaitUntil_DoesNotEnterPlayMode(string value)
        {
            bool before = EditorApplication.isPlayingOrWillChangePlaymode;
            var response = ToJObject(ManageEditor.HandleCommand(new JObject { ["action"] = "play", ["waitUntil"] = value }));
            Assert.IsFalse(response.Value<bool>("success"));
            Assert.AreEqual(before, EditorApplication.isPlayingOrWillChangePlaymode);
        }

        [TestCase(0)]
        [TestCase(301)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void InvalidTimeout_DoesNotEnterPlayMode(double value)
        {
            bool before = EditorApplication.isPlayingOrWillChangePlaymode;
            var response = ToJObject(ManageEditor.HandleCommand(new JObject { ["action"] = "play", ["waitUntil"] = "first_frame", ["waitTimeout"] = value }));
            Assert.IsFalse(response.Value<bool>("success"));
            Assert.AreEqual(before, EditorApplication.isPlayingOrWillChangePlaymode);
        }

        [TestCase(" ")]
        [TestCase("")]
        public void EmptyReadyCode_DoesNotEnterPlayMode(string value)
        {
            bool before = EditorApplication.isPlayingOrWillChangePlaymode;
            var response = ToJObject(ManageEditor.HandleCommand(new JObject { ["action"] = "play", ["onReadyCode"] = value }));
            Assert.IsFalse(response.Value<bool>("success"));
            Assert.AreEqual(before, EditorApplication.isPlayingOrWillChangePlaymode);
        }

        [Test]
        public void OversizedCode_DoesNotEnterPlayMode()
        {
            bool before = EditorApplication.isPlayingOrWillChangePlaymode;
            var response = ToJObject(ManageEditor.HandleCommand(new JObject { ["action"] = "play", ["onReadyCode"] = new string('x', 50001) }));
            Assert.IsFalse(response.Value<bool>("success"));
            Assert.AreEqual(before, EditorApplication.isPlayingOrWillChangePlaymode);
        }

        [Test]
        public void InvalidRequestId_DoesNotEnterPlayMode()
        {
            bool before = EditorApplication.isPlayingOrWillChangePlaymode;
            var response = ToJObject(ManageEditor.HandleCommand(new JObject { ["action"] = "play", ["waitUntil"] = "first_frame", ["startupRequestId"] = "invalid" }));
            Assert.IsFalse(response.Value<bool>("success"));
            Assert.AreEqual(before, EditorApplication.isPlayingOrWillChangePlaymode);
        }

        private static JObject Read(string id) => ToJObject(ManageEditor.HandleCommand(new JObject {
            ["action"] = "get_play_mode_startup", ["jobId"] = id }));

        private static IEnumerator WaitForJob(string id)
        {
            double deadline = EditorApplication.timeSinceStartup + 10;
            while (EditorApplication.timeSinceStartup < deadline)
            {
                var data = Read(id)["data"];
                if (data.Value<string>("status") == "completed" || data.Value<string>("status") == "failed") yield break;
                yield return null;
            }
            Assert.Fail("Startup job did not reach a terminal state.");
        }

        [UnityTest]
        public IEnumerator ReadyCode_SeesStartAndCompletedFrame_AndReplayDoesNotRepeat()
        {
            var target = new GameObject("MCPStartupProbe", typeof(PlayModeStartupTestComponent));
            yield return new EnterPlayMode();
            string id = Guid.NewGuid().ToString("N");
            var args = new JObject { ["action"] = "play", ["startupRequestId"] = id,
                ["onReadyCode"] = "var probe = UnityEngine.GameObject.Find(\"MCPStartupProbe\").GetComponent<TestNamespace.PlayModeStartupTestComponent>(); probe.ReadyCalls++; return new { started = probe.StartCalls, late = probe.LateUpdates, calls = probe.ReadyCalls };" };
            var started = ToJObject(ManageEditor.HandleCommand(args));
            Assert.IsTrue(started.Value<bool>("success"), started.ToString());
            yield return WaitForJob(id);
            var completed = Read(id);
            Assert.AreEqual("completed", completed["data"].Value<string>("status"), completed.ToString());
            Assert.IsTrue(completed["data"].Value<bool>("first_frame_complete"));
            var result = completed["data"]["on_ready_result"]["data"]["result"];
            Assert.AreEqual(1, result.Value<int>("started"));
            Assert.GreaterOrEqual(result.Value<int>("late"), 1);
            Assert.AreEqual(1, result.Value<int>("calls"));
            var replay = ToJObject(ManageEditor.HandleCommand(args));
            Assert.AreEqual("completed", replay["data"].Value<string>("status"));
            Assert.AreEqual(1, GameObject.Find("MCPStartupProbe").GetComponent<PlayModeStartupTestComponent>().ReadyCalls);
            yield return new ExitPlayMode();
            if (target != null) Object.DestroyImmediate(target);
        }

        [UnityTest]
        public IEnumerator SceneWaitAndCodeFailure_ReturnTheirActualTerminalStates()
        {
            yield return new EnterPlayMode();
            var waiting = ToJObject(ManageEditor.HandleCommand(new JObject { ["action"] = "play", ["waitUntil"] = "scene_loaded" }));
            string id = waiting["data"].Value<string>("job_id");
            yield return WaitForJob(id);
            Assert.IsTrue(Read(id)["data"].Value<bool>("scene_loaded"));
            foreach (string code in new[] { "throw new System.InvalidOperationException(\"startup failed\");", "System.IO.File.Delete(\"NeverDeleteThis\");" })
            {
                var started = ToJObject(ManageEditor.HandleCommand(new JObject { ["action"] = "play", ["onReadyCode"] = code }));
                id = started["data"].Value<string>("job_id");
                yield return WaitForJob(id);
                var failed = Read(id);
                Assert.AreEqual("failed", failed["data"].Value<string>("status"), failed.ToString());
                Assert.IsFalse(failed["data"]["on_ready_result"].Value<bool>("success"));
            }
            yield return new ExitPlayMode();
        }
    }
}
