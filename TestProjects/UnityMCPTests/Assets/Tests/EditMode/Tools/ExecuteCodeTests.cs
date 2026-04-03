using Newtonsoft.Json.Linq;
using NUnit.Framework;
using MCPForUnity.Editor.Tools;
using static MCPForUnityTests.Editor.TestUtilities;

namespace MCPForUnityTests.Editor.Tools
{
    public class ExecuteCodeTests
    {
        [SetUp]
        public void SetUp()
        {
            ExecuteCode.HandleCommand(new JObject { ["action"] = "clear_history" });
        }

        // ──────────────────── Execute: success cases ────────────────────

        [Test]
        public void Execute_ReturnString_ReturnsSuccess()
        {
            var result = Execute("return \"hello\";");

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual("hello", result["data"]["result"].Value<string>());
        }

        [Test]
        public void Execute_ReturnInt_ReturnsSuccess()
        {
            var result = Execute("return 42;");

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(42, result["data"]["result"].Value<int>());
        }

        [Test]
        public void Execute_ReturnNull_NoResultValue()
        {
            var result = Execute("int x = 1; return null;");

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            // data may contain compiler info but should not have a "result" key
            var data = result["data"] as JObject;
            if (data != null)
                Assert.IsNull(data["result"], "Expected no 'result' key when code returns null");
        }

        [Test]
        public void Execute_VoidReturn_Succeeds()
        {
            var result = Execute("UnityEngine.Debug.Log(\"test\"); return null;");

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
        }

        [Test]
        public void Execute_UnityAPI_CanAccessSceneManager()
        {
            var result = Execute(
                "var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();\n" +
                "return scene.name;");

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.IsNotNull(result["data"]["result"]);
        }

        [Test]
        public void Execute_Generics_ListOfString()
        {
            var result = Execute(
                "var list = new System.Collections.Generic.List<string>();\n" +
                "list.Add(\"a\"); list.Add(\"b\");\n" +
                "return list;");

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var arr = result["data"]["result"] as JArray;
            Assert.IsNotNull(arr, "Expected array result");
            Assert.AreEqual(2, arr.Count);
        }

        [Test]
        public void Execute_LINQ_SelectWorks()
        {
            var result = Execute(
                "var nums = new int[] { 1, 2, 3 };\n" +
                "return nums.Select(n => n * 2).ToList();");

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var arr = result["data"]["result"] as JArray;
            Assert.IsNotNull(arr);
            Assert.AreEqual(3, arr.Count);
            Assert.AreEqual(2, arr[0].Value<int>());
            Assert.AreEqual(6, arr[2].Value<int>());
        }

        [Test]
        public void Execute_Dictionary_ReturnsStructured()
        {
            var result = Execute(
                "var dict = new Dictionary<string, int> { { \"a\", 1 }, { \"b\", 2 } };\n" +
                "return dict;");

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.IsNotNull(result["data"]["result"]);
        }

        // ──────────────────── Execute: error cases ────────────────────

        [Test]
        public void Execute_CompilationError_ReturnsErrors()
        {
            var result = Execute("int x = \"not an int\";");

            Assert.IsFalse(result.Value<bool>("success"), result.ToString());
            StringAssert.Contains("Compilation failed", result.Value<string>("error"));
            Assert.IsNotNull(result["data"]["errors"]);
        }

        [Test]
        public void Execute_RuntimeException_ReturnsError()
        {
            var result = Execute("throw new System.Exception(\"boom\");");

            Assert.IsFalse(result.Value<bool>("success"), result.ToString());
            StringAssert.Contains("boom", result.Value<string>("error"));
        }

        [Test]
        public void Execute_MissingCode_ReturnsError()
        {
            var result = ToJObject(ExecuteCode.HandleCommand(new JObject
            {
                ["action"] = "execute"
            }));

            Assert.IsFalse(result.Value<bool>("success"), result.ToString());
            StringAssert.Contains("code", result.Value<string>("error").ToLowerInvariant());
        }

        [Test]
        public void Execute_EmptyCode_ReturnsError()
        {
            var result = ToJObject(ExecuteCode.HandleCommand(new JObject
            {
                ["action"] = "execute",
                ["code"] = "   "
            }));

            Assert.IsFalse(result.Value<bool>("success"), result.ToString());
        }

        // ──────────────────── Safety checks ────────────────────

        [Test]
        public void Execute_SafetyChecks_BlocksFileDelete()
        {
            var result = Execute("System.IO.File.Delete(\"x\");");

            Assert.IsFalse(result.Value<bool>("success"), result.ToString());
            StringAssert.Contains("Blocked pattern", result.Value<string>("error"));
        }

        [Test]
        public void Execute_SafetyChecks_BlocksProcessStart()
        {
            var result = Execute("Process.Start(\"cmd\");");

            Assert.IsFalse(result.Value<bool>("success"), result.ToString());
            StringAssert.Contains("Blocked pattern", result.Value<string>("error"));
        }

        [Test]
        public void Execute_SafetyChecks_BlocksInfiniteLoop()
        {
            var result = Execute("while (true) { }");

            Assert.IsFalse(result.Value<bool>("success"), result.ToString());
            StringAssert.Contains("Blocked pattern", result.Value<string>("error"));
        }

        [Test]
        public void Execute_SafetyChecksDisabled_AllowsBlockedPattern()
        {
            var result = ToJObject(ExecuteCode.HandleCommand(new JObject
            {
                ["action"] = "execute",
                ["code"] = "while (true) { break; }  return null;",
                ["safety_checks"] = false
            }));

            if (!result.Value<bool>("success"))
            {
                var error = result.Value<string>("error") ?? "";
                Assert.IsFalse(error.Contains("Blocked pattern"),
                    "Safety checks should be disabled but still blocked");
            }
        }

        // ──────────────────── History ────────────────────

        [Test]
        public void GetHistory_Empty_ReturnsZero()
        {
            var result = ToJObject(ExecuteCode.HandleCommand(new JObject
            {
                ["action"] = "get_history"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(0, result["data"]["total"].Value<int>());
        }

        [Test]
        public void GetHistory_AfterExecution_RecordsEntry()
        {
            Execute("return 1;");

            var result = ToJObject(ExecuteCode.HandleCommand(new JObject
            {
                ["action"] = "get_history"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(1, result["data"]["total"].Value<int>());
            var entries = result["data"]["entries"] as JArray;
            Assert.IsNotNull(entries);
            Assert.AreEqual(1, entries.Count);
            Assert.IsTrue(entries[0]["success"].Value<bool>());
        }

        [Test]
        public void GetHistory_Limit_RespectsParameter()
        {
            Execute("return 1;");
            Execute("return 2;");
            Execute("return 3;");

            var result = ToJObject(ExecuteCode.HandleCommand(new JObject
            {
                ["action"] = "get_history",
                ["limit"] = 2
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(3, result["data"]["total"].Value<int>());
            var entries = result["data"]["entries"] as JArray;
            Assert.AreEqual(2, entries.Count);
        }

        [Test]
        public void ClearHistory_RemovesAll()
        {
            Execute("return 1;");
            Execute("return 2;");

            var clearResult = ToJObject(ExecuteCode.HandleCommand(new JObject
            {
                ["action"] = "clear_history"
            }));
            Assert.IsTrue(clearResult.Value<bool>("success"), clearResult.ToString());

            var historyResult = ToJObject(ExecuteCode.HandleCommand(new JObject
            {
                ["action"] = "get_history"
            }));
            Assert.AreEqual(0, historyResult["data"]["total"].Value<int>());
        }

        // ──────────────────── Replay ────────────────────

        [Test]
        public void Replay_ValidIndex_ReExecutes()
        {
            Execute("return 42;");

            var result = ToJObject(ExecuteCode.HandleCommand(new JObject
            {
                ["action"] = "replay",
                ["index"] = 0
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(42, result["data"]["result"].Value<int>());
        }

        [Test]
        public void Replay_InvalidIndex_ReturnsError()
        {
            Execute("return 1;");

            var result = ToJObject(ExecuteCode.HandleCommand(new JObject
            {
                ["action"] = "replay",
                ["index"] = 99
            }));

            Assert.IsFalse(result.Value<bool>("success"), result.ToString());
            StringAssert.Contains("Invalid history index", result.Value<string>("error"));
        }

        [Test]
        public void Replay_EmptyHistory_ReturnsError()
        {
            var result = ToJObject(ExecuteCode.HandleCommand(new JObject
            {
                ["action"] = "replay",
                ["index"] = 0
            }));

            Assert.IsFalse(result.Value<bool>("success"), result.ToString());
        }

        // ──────────────────── Action validation ────────────────────

        [Test]
        public void UnknownAction_ReturnsError()
        {
            var result = ToJObject(ExecuteCode.HandleCommand(new JObject
            {
                ["action"] = "invalid_action"
            }));

            Assert.IsFalse(result.Value<bool>("success"), result.ToString());
            StringAssert.Contains("Unknown action", result.Value<string>("error"));
        }

        [Test]
        public void NullParams_ReturnsError()
        {
            var result = ToJObject(ExecuteCode.HandleCommand(null));

            Assert.IsFalse(result.Value<bool>("success"), result.ToString());
        }

        // ──────────────────── Helpers ────────────────────

        private static JObject Execute(string code)
        {
            return ToJObject(ExecuteCode.HandleCommand(new JObject
            {
                ["action"] = "execute",
                ["code"] = code
            }));
        }

    }
}
