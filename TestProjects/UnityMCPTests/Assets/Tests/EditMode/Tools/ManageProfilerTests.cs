using NUnit.Framework;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Tools;

namespace MCPForUnityTests.Editor.Tools
{
    /// <summary>
    /// EditMode tests for the ManageProfiler tool.
    /// Tests parameter validation, action routing, and error handling.
    /// </summary>
    [TestFixture]
    public class ManageProfilerTests
    {
        private static JObject ToJO(object o) => JObject.FromObject(o);

        [Test]
        public void HandleCommand_NullParams_ReturnsError()
        {
            var result = ManageProfiler.HandleCommand(null);
            var jo = ToJO(result);
            Assert.IsFalse((bool)jo["success"]);
            Assert.IsNotNull(jo["error"]);
            Assert.That((string)jo["error"], Does.Contain("cannot be null"));
        }

        [Test]
        public void HandleCommand_MissingAction_ReturnsError()
        {
            var result = ManageProfiler.HandleCommand(new JObject());
            var jo = ToJO(result);
            Assert.IsFalse((bool)jo["success"]);
            Assert.IsNotNull(jo["error"]);
        }

        [Test]
        public void HandleCommand_UnknownAction_ReturnsError()
        {
            var result = ManageProfiler.HandleCommand(new JObject { ["action"] = "invalid_action" });
            var jo = ToJO(result);
            Assert.IsFalse((bool)jo["success"]);
            Assert.That((string)jo["error"], Does.Contain("Unknown action"));
        }

        [Test]
        public void HandleCommand_ActionCaseInsensitive()
        {
            // Both "STATUS" and "status" should be recognized
            var upper = ManageProfiler.HandleCommand(new JObject { ["action"] = "STATUS" });
            var lower = ManageProfiler.HandleCommand(new JObject { ["action"] = "status" });
            var upperJo = ToJO(upper);
            var lowerJo = ToJO(lower);
            Assert.AreEqual((bool)upperJo["success"], (bool)lowerJo["success"]);
        }

        [Test]
        public void HandleCommand_Status_ReturnsProfilerState()
        {
            var result = ManageProfiler.HandleCommand(new JObject { ["action"] = "status" });
            var jo = ToJO(result);
            Assert.IsTrue((bool)jo["success"]);
            Assert.IsNotNull(jo["data"]);

            var data = jo["data"];
            Assert.IsNotNull(data["enabled"], "Should report enabled state");
            Assert.IsNotNull(data["firstFrame"], "Should report first frame index");
            Assert.IsNotNull(data["lastFrame"], "Should report last frame index");
            Assert.IsNotNull(data["frameCount"], "Should report frame count");
        }

        [Test]
        public void HandleCommand_EnableDisable_Succeeds()
        {
            // Enable
            var enableResult = ManageProfiler.HandleCommand(new JObject { ["action"] = "enable" });
            var enableJo = ToJO(enableResult);
            Assert.IsTrue((bool)enableJo["success"]);

            // Disable
            var disableResult = ManageProfiler.HandleCommand(new JObject { ["action"] = "disable" });
            var disableJo = ToJO(disableResult);
            Assert.IsTrue((bool)disableJo["success"]);
        }

        [Test]
        public void HandleCommand_Clear_Succeeds()
        {
            var result = ManageProfiler.HandleCommand(new JObject { ["action"] = "clear" });
            var jo = ToJO(result);
            Assert.IsTrue((bool)jo["success"]);
        }

        [Test]
        public void HandleCommand_ReadFrames_WhenProfilerDisabled_ReturnsError()
        {
            // Ensure profiler is disabled first
            ManageProfiler.HandleCommand(new JObject { ["action"] = "disable" });

            var result = ManageProfiler.HandleCommand(new JObject { ["action"] = "read_frames" });
            var jo = ToJO(result);
            Assert.IsFalse((bool)jo["success"]);
            Assert.That((string)jo["error"], Does.Contain("not enabled"));
        }
    }
}
