using MCPForUnity.Editor.Tools.Blender;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace MCPForUnityTests.Editor.Blender
{
    /// <summary>
    /// Parameter validation that happens before any socket or file I/O, so these run without Blender.
    /// </summary>
    public class BlenderBridgeToolTests
    {
        private static JObject Call(JObject p)
            => JObject.Parse(JsonConvert.SerializeObject(BlenderBridgeTool.HandleCommand(p)));

        [Test]
        public void NullParams_ReturnsError()
        {
            JObject resp = Call(null);
            Assert.AreEqual(false, (bool)resp["success"]);
        }

        [Test]
        public void UnknownAction_ListsValidActions()
        {
            JObject resp = Call(new JObject { ["action"] = "nope" });
            Assert.AreEqual(false, (bool)resp["success"]);
            string error = (string)resp["error"];
            StringAssert.Contains("nope", error);
            StringAssert.Contains("import_model", error);
            StringAssert.Contains("sync_addon", error);
        }

        [Test]
        public void RunPython_WithoutCode_ReturnsError()
        {
            JObject resp = Call(new JObject { ["action"] = "run_python" });
            Assert.AreEqual(false, (bool)resp["success"]);
            StringAssert.Contains("'code'", (string)resp["error"]);
        }

        [Test]
        public void ObjectInfo_WithoutName_ReturnsError()
        {
            JObject resp = Call(new JObject { ["action"] = "object_info" });
            Assert.AreEqual(false, (bool)resp["success"]);
            StringAssert.Contains("'object_name'", (string)resp["error"]);
        }

        [Test]
        public void ImportModel_RejectsUnsupportedFormat()
        {
            JObject resp = Call(new JObject { ["action"] = "import_model", ["format"] = "obj" });
            Assert.AreEqual(false, (bool)resp["success"]);
            StringAssert.Contains("glb or fbx", (string)resp["error"]);
        }

        [Test]
        public void ActionIsCaseInsensitive()
        {
            JObject resp = Call(new JObject { ["action"] = "RUN_PYTHON" });
            Assert.AreEqual(false, (bool)resp["success"]);
            StringAssert.Contains("'code'", (string)resp["error"]);
        }
    }
}
