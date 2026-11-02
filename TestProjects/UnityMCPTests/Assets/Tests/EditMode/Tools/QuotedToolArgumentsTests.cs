using System;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace MCPForUnityTests.Editor.Tools
{
    public class QuotedToolArgumentsTests
    {
        private const string MenuPath = "Tools/MCPForUnityTests/Quoted argument probe";
        private static int _menuCalls;

        [MenuItem(MenuPath)]
        private static void OnTestMenu() => _menuCalls++;

        [SetUp]
        public void SetUp() => _menuCalls = 0;

        [Test]
        public void Console_GetWithJsonEncodedAction_ReturnsMatchingLog()
        {
            string marker = "Quoted action probe " + Guid.NewGuid().ToString("N");
            Debug.Log(marker);
            var result = JObject.FromObject(ReadConsole.HandleCommand(new JObject
            {
                ["action"] = JsonConvert.SerializeObject("get"),
                ["types"] = new JArray("log"), ["format"] = "plain", ["filterText"] = marker, ["count"] = 10
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            StringAssert.Contains(marker, result["data"][0].Value<string>());
        }

        [Test]
        public void Console_JsonEncodedFormat_ReturnsPlainStrings()
        {
            string marker = "Quoted format probe " + Guid.NewGuid().ToString("N");
            Debug.Log(marker);
            var result = JObject.FromObject(ReadConsole.HandleCommand(new JObject
            {
                ["action"] = "get", ["types"] = new JArray("log"),
                ["format"] = JsonConvert.SerializeObject("plain"), ["filterText"] = marker, ["count"] = 10
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(JTokenType.String, result["data"][0].Type);
            StringAssert.Contains(marker, result["data"][0].Value<string>());
        }

        [TestCase("\"get")]
        [TestCase("\"delete\"")]
        public void Console_MalformedOrUnknownAction_ReturnsError(string action)
        {
            var result = JObject.FromObject(ReadConsole.HandleCommand(new JObject { ["action"] = action }));
            Assert.IsFalse(result.Value<bool>("success"));
            StringAssert.Contains("Unknown action", result.Value<string>("error"));
        }

        [Test]
        public void Console_NestedJsonString_IsNotDecodedRecursively()
        {
            string encoded = JsonConvert.SerializeObject(JsonConvert.SerializeObject("get"));
            var result = JObject.FromObject(ReadConsole.HandleCommand(new JObject { ["action"] = encoded }));
            Assert.IsFalse(result.Value<bool>("success"));
        }

        [Test]
        public void Console_QuotedFilter_IsPreserved()
        {
            string marker = "Literal filter probe " + Guid.NewGuid().ToString("N");
            Debug.Log(marker);
            Debug.Log("\"" + marker + "\"");
            var result = JObject.FromObject(ReadConsole.HandleCommand(new JObject
            {
                ["action"] = "get", ["types"] = new JArray("log"), ["format"] = "plain",
                ["filterText"] = "\"" + marker + "\"", ["count"] = 10
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(1, ((JArray)result["data"]).Count);
            StringAssert.StartsWith("\"" + marker + "\"", result["data"][0].Value<string>());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Menu_Path_InvokesOwnedMenuOnce(bool encoded)
        {
            string supplied = encoded ? JsonConvert.SerializeObject(MenuPath) : MenuPath;
            var result = JObject.FromObject(ExecuteMenuItem.HandleCommand(new JObject { ["menuPath"] = supplied }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(1, _menuCalls);
        }

        [Test]
        public void Menu_JsonEncodedQuit_RemainsBlocked()
        {
            var result = JObject.FromObject(ExecuteMenuItem.HandleCommand(new JObject
            {
                ["menuPath"] = JsonConvert.SerializeObject("File/Quit")
            }));
            Assert.IsFalse(result.Value<bool>("success"));
            StringAssert.Contains("blocked for safety", result.Value<string>("error"));
            Assert.AreEqual(0, _menuCalls);
        }

        [Test]
        public void Menu_NestedJsonString_IsNotDecodedRecursively()
        {
            string encoded = JsonConvert.SerializeObject(JsonConvert.SerializeObject(MenuPath));
            // Unity versions emit different error logs for an intentionally missing menu.
            // Keep this allowance scoped to the call and assert its response and side effects.
            bool originalIgnore = LogAssert.ignoreFailingMessages;
            try
            {
                LogAssert.ignoreFailingMessages = true;
                var result = JObject.FromObject(ExecuteMenuItem.HandleCommand(new JObject { ["menuPath"] = encoded }));
                Assert.IsFalse(result.Value<bool>("success"));
                Assert.AreEqual(0, _menuCalls);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = originalIgnore;
            }
        }
    }
}
