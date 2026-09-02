using MCPForUnity.Editor.Tools.Blender;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace MCPForUnityTests.Editor.Blender
{
    /// <summary>
    /// Parameter validation that happens before any socket or file I/O, plus the export-script
    /// builder, so these run without Blender.
    /// </summary>
    public class BlenderBridgeToolTests
    {
        private static JObject Call(JObject p)
            => JObject.Parse(JsonConvert.SerializeObject(BlenderBridgeTool.HandleCommand(p).GetAwaiter().GetResult()));

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
        public void Screenshot_RejectsOutputFolderOutsideAssets()
        {
            JObject resp = Call(new JObject { ["action"] = "screenshot", ["output_folder"] = "Assets/../../outside" });
            Assert.AreEqual(false, (bool)resp["success"]);
            StringAssert.Contains("Assets", (string)resp["error"]);
        }

        [Test]
        public void ActionIsCaseInsensitive()
        {
            JObject resp = Call(new JObject { ["action"] = "RUN_PYTHON" });
            Assert.AreEqual(false, (bool)resp["success"]);
            StringAssert.Contains("'code'", (string)resp["error"]);
        }

        [Test]
        public void ExportScript_EmbedsValuesAsOneJsonLiteral()
        {
            string script = BlenderBridgeTool.BuildExportScript(
                "C:/tmp/O'Brien_1.glb", new[] { "O'Brien", "__APPLY__" }, false, true, "glb");

            StringAssert.Contains("cfg = json.loads(\"", script);
            Assert.IsFalse(script.Contains("__CFG__"), "placeholder must be replaced exactly once");
            StringAssert.Contains("O'Brien", script);
            StringAssert.Contains("__APPLY__", script);

            // The literal between json.loads(" and ") must be the JSON-escaped config, so an
            // apostrophe or a placeholder-looking name never changes the program text.
            int start = script.IndexOf("json.loads(", System.StringComparison.Ordinal) + "json.loads(".Length;
            int end = script.IndexOf(")\n", start, System.StringComparison.Ordinal);
            string literal = script.Substring(start, end - start);
            string decoded = JsonConvert.DeserializeObject<string>(literal);
            JObject cfg = JObject.Parse(decoded);
            Assert.AreEqual("C:/tmp/O'Brien_1.glb", (string)cfg["out"]);
            Assert.AreEqual("O'Brien", (string)cfg["names"][0]);
            Assert.AreEqual("__APPLY__", (string)cfg["names"][1]);
            Assert.AreEqual(true, (bool)cfg["apply_modifiers"]);
            Assert.AreEqual(false, (bool)cfg["selection_only"]);
            Assert.AreEqual("glb", (string)cfg["format"]);
        }

        [Test]
        public void RedactRemoteUrl_StripsEmbeddedCredentials()
        {
            Assert.AreEqual("https://github.com/o/r.git",
                BlenderBridgeTool.RedactRemoteUrl("https://user:ghp_secret@github.com/o/r.git"));
            Assert.AreEqual("https://github.com/o/r.git",
                BlenderBridgeTool.RedactRemoteUrl("https://ghp_secret@github.com/o/r.git"));
            Assert.AreEqual("https://github.com/o/r.git",
                BlenderBridgeTool.RedactRemoteUrl("https://github.com/o/r.git"));
            Assert.AreEqual("git@github.com:o/r.git",
                BlenderBridgeTool.RedactRemoteUrl("git@github.com:o/r.git"));
            Assert.AreEqual(string.Empty, BlenderBridgeTool.RedactRemoteUrl(null));
        }

        [Test]
        public void ExportScript_WithoutNames_UsesEmptyList()
        {
            string script = BlenderBridgeTool.BuildExportScript("C:/tmp/x.fbx", null, true, false, "fbx");
            int start = script.IndexOf("json.loads(", System.StringComparison.Ordinal) + "json.loads(".Length;
            int end = script.IndexOf(")\n", start, System.StringComparison.Ordinal);
            JObject cfg = JObject.Parse(JsonConvert.DeserializeObject<string>(script.Substring(start, end - start)));
            Assert.AreEqual(0, ((JArray)cfg["names"]).Count);
            Assert.AreEqual(true, (bool)cfg["selection_only"]);
            Assert.AreEqual(false, (bool)cfg["apply_modifiers"]);
        }
    }
}
