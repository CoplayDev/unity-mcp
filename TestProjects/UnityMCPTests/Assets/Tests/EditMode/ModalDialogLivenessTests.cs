using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Services.Transport;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MCPForUnityTests.Editor
{
    /// <summary>
    /// Covers the off-main-thread liveness path that lets the bridge report a modal dialog instead
    /// of an anonymous timeout.
    ///
    /// The blocked case itself cannot be driven from a test: raising a real modal would stall the
    /// Editor main thread the test runner is running on. What is testable here is everything that
    /// decides whether the report can be produced at all — the command routing that keeps these
    /// off the main-thread queue, and the payload shape the server classifies against.
    /// </summary>
    public class ModalDialogLivenessTests
    {
        [Test]
        public void LivenessAndAnswerDialogBypassTheMainThreadQueue()
        {
            Assert.IsTrue(OffMainThreadCommands.IsOffMainThreadCommand("liveness"));
            Assert.IsTrue(OffMainThreadCommands.IsOffMainThreadCommand("answer_dialog"));
            Assert.IsTrue(OffMainThreadCommands.IsOffMainThreadCommand("LIVENESS"),
                "command names arrive in whatever casing the caller used");
        }

        [Test]
        public void OrdinaryCommandsStillGoThroughTheDispatcher()
        {
            Assert.IsFalse(OffMainThreadCommands.IsOffMainThreadCommand("read_console"));
            Assert.IsFalse(OffMainThreadCommands.IsOffMainThreadCommand("refresh_unity"));
            Assert.IsFalse(OffMainThreadCommands.IsOffMainThreadCommand("ping"));
        }

        [Test]
        public void LivenessPayloadCarriesEverythingTheServerClassifiesOn()
        {
            JObject payload = EditorLivenessProbe.Capture();

            Assert.IsNotNull(payload["main_thread_stall_ms"]);
            Assert.IsNotNull(payload["main_thread_ticks"]);
            Assert.IsNotNull(payload["pending_commands"]);
            Assert.IsNotNull(payload["sample_age_ms"],
                "a snapshot that stopped advancing is itself evidence of a non-pumping stall");

            var modal = payload["modal"] as JObject;
            Assert.IsNotNull(modal);
            Assert.IsNotNull(modal["supported"]);
            Assert.IsNotNull(modal["blocked"]);
        }

        [Test]
        public void HeartbeatIsFreshWhileTheMainThreadIsRunning()
        {
            // This test body runs on the main thread, so the update loop is by definition alive.
            Assert.Less(MainThreadHeartbeat.StallMs, 30000);
        }

        [Test]
        public void TryHandleRawAnswersALivenessFrame()
        {
            bool handled = OffMainThreadCommands.TryHandleRaw(
                "{\"type\":\"liveness\",\"params\":{}}", out string response);

            Assert.IsTrue(handled);
            var parsed = JObject.Parse(response);
            Assert.AreEqual("success", parsed.Value<string>("status"));
            Assert.IsNotNull(parsed["result"]["data"]["main_thread_stall_ms"]);
        }

        [Test]
        public void TryHandleRawLeavesOtherFramesAlone()
        {
            Assert.IsFalse(OffMainThreadCommands.TryHandleRaw(
                "{\"type\":\"read_console\",\"params\":{}}", out _));
            Assert.IsFalse(OffMainThreadCommands.TryHandleRaw("ping", out _));
            Assert.IsFalse(OffMainThreadCommands.TryHandleRaw("not json at all", out _));
            Assert.IsFalse(OffMainThreadCommands.TryHandleRaw("", out _));
        }

        [Test]
        public void AnsweringWithoutAButtonIsRejected()
        {
            string response = OffMainThreadCommands.Handle("answer_dialog", new JObject());

            var result = JObject.Parse(response)["result"];
            Assert.IsFalse(result.Value<bool>("success"));
            StringAssert.Contains("button", result.Value<string>("error"));
        }

        [Test]
        public void AnsweringWhenNoDialogIsOpenSaysSoInsteadOfPressingSomething()
        {
            // No modal can be open: the test runner owns the main thread.
            string response = OffMainThreadCommands.Handle(
                "answer_dialog", new JObject { ["button"] = "Reload" });

            var result = JObject.Parse(response)["result"];
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.AreEqual("no_dialog_open", result["data"].Value<string>("reason"));
        }

        [Test]
        public void ProbeReportsNoDialogWhileTheMainThreadIsRunning()
        {
            ModalDialogInfo info = ModalDialogProbe.Capture();

            Assert.IsFalse(info.Blocked);
            Assert.AreEqual(Application.platform == RuntimePlatform.WindowsEditor, info.Supported);
        }
    }
}
