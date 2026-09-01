using System;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using MCPForUnity.Editor.Tools;
using static MCPForUnityTests.Editor.TestUtilities;

namespace MCPForUnityTests.Editor.Tools
{
    public class ReadConsoleTests
    {
        [Test]
        public void HandleCommand_Clear_Works()
        {
            // Arrange
            // Ensure there's something to clear
            Debug.Log("Log to clear");
            
            // Verify content exists before clear
            var getBefore = ToJObject(ReadConsole.HandleCommand(new JObject { ["action"] = "get", ["types"] = new JArray { "error", "warning", "log" }, ["count"] = 10 }));
            Assert.IsTrue(getBefore.Value<bool>("success"), getBefore.ToString());
            var entriesBefore = getBefore["data"] as JArray;
            
            // Ideally we'd assert count > 0, but other tests/system logs might affect this.
            // Just ensuring the call doesn't fail is a baseline, but let's try to be stricter if possible.
            // Since we just logged, there should be at least one entry.
            Assert.IsTrue(entriesBefore != null && entriesBefore.Count > 0, "Setup failed: console should have logs.");

            // Act
            var result = ToJObject(ReadConsole.HandleCommand(new JObject { ["action"] = "clear" }));

            // Assert
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            
            // Verify clear effect
            var getAfter = ToJObject(ReadConsole.HandleCommand(new JObject { ["action"] = "get", ["types"] = new JArray { "error", "warning", "log" }, ["count"] = 10 }));
            Assert.IsTrue(getAfter.Value<bool>("success"), getAfter.ToString());
            var entriesAfter = getAfter["data"] as JArray;
            Assert.IsTrue(entriesAfter == null || entriesAfter.Count == 0, "Console should be empty after clear.");
        }

        [Test]
        public void HandleCommand_Get_Works()
        {
            // Arrange
            string uniqueMessage = $"Test Log Message {Guid.NewGuid()}";
            Debug.Log(uniqueMessage);
            
            var paramsObj = new JObject
            {
                ["action"] = "get",
                ["types"] = new JArray { "error", "warning", "log" },
                ["format"] = "detailed",
                ["count"] = 1000 // Fetch enough to likely catch our message
            };

            // Act
            var result = ToJObject(ReadConsole.HandleCommand(paramsObj));

            // Assert
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var data = result["data"] as JArray;
            Assert.IsNotNull(data, "Data array should not be null.");
            Assert.IsTrue(data.Count > 0, "Should retrieve at least one log entry.");

            // Verify content
            bool found = false;
            foreach (var entry in data)
            {
                if (entry["message"]?.ToString().Contains(uniqueMessage) == true)
                {
                    found = true;
                    break;
                }
            }
            Assert.IsTrue(found, $"The unique log message '{uniqueMessage}' was not found in retrieved logs.");
        }

        [Test]
        public void HandleCommand_Get_PreservesMultilineMessageBody()
        {
            string id = Guid.NewGuid().ToString();
            string firstLine = $"First line {id}";
            string secondLine = $"Second line {id}";
            Debug.Log($"{firstLine}\n\n{secondLine}");

            var paramsObj = new JObject
            {
                ["action"] = "get",
                ["types"] = new JArray { "error", "warning", "log" },
                ["format"] = "detailed",
                ["count"] = 1000
            };

            var result = ToJObject(ReadConsole.HandleCommand(paramsObj));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var data = result["data"] as JArray;
            Assert.IsNotNull(data, "Data array should not be null.");

            string message = null;
            foreach (var entry in data)
            {
                string candidate = entry["message"]?.ToString();
                if (candidate != null && candidate.Contains(firstLine))
                {
                    message = candidate;
                    break;
                }
            }

            Assert.IsNotNull(message, "Multi-line log entry was not found.");
            StringAssert.Contains($"{firstLine}\n\n{secondLine}", message);
            StringAssert.DoesNotContain("UnityEngine.Debug", message);
        }

        // ──────────────────── LogEntry.mode severity mapping (issue #1348) ────────────────────

        // 0x804400 and 0x804100 were captured from a real Unity 6000.5.4f1 console via
        // reflection in issue #1348; 0x804200 is the same envelope with the ScriptingWarning
        // bit. The remaining cases exercise one ConsoleWindow.Mode bit each. The old table
        // was off by one for every scripting bit, surfacing Logs as Warnings and Warnings
        // as Errors.
        [TestCase(0x804400, LogType.Log, TestName = "ScriptingLog bit (1<<10) maps to Log")]
        [TestCase(0x804200, LogType.Warning, TestName = "ScriptingWarning bit (1<<9) maps to Warning")]
        [TestCase(0x804100, LogType.Error, TestName = "ScriptingError bit (1<<8) maps to Error")]
        [TestCase(1 << 2, LogType.Log, TestName = "Log bit (1<<2) maps to Log")]
        [TestCase(1 << 0, LogType.Error, TestName = "Error bit (1<<0) maps to Error")]
        [TestCase(1 << 1, LogType.Assert, TestName = "Assert bit (1<<1) maps to Assert")]
        [TestCase(1 << 4, LogType.Error, TestName = "Fatal bit (1<<4) maps to Error")]
        [TestCase(1 << 6, LogType.Error, TestName = "AssetImportError bit (1<<6) maps to Error")]
        [TestCase(1 << 7, LogType.Warning, TestName = "AssetImportWarning bit (1<<7) maps to Warning")]
        [TestCase(1 << 11, LogType.Error, TestName = "ScriptCompileError bit (1<<11) maps to Error")]
        [TestCase(1 << 12, LogType.Warning, TestName = "ScriptCompileWarning bit (1<<12) maps to Warning")]
        [TestCase(1 << 17, LogType.Exception, TestName = "ScriptingException bit (1<<17) maps to Exception")]
        [TestCase(1 << 21, LogType.Assert, TestName = "ScriptingAssertion bit (1<<21) maps to Assert")]
        public void GetLogTypeFromMode_MapsUnityConsoleModeBits(int mode, LogType expected)
        {
            Assert.AreEqual(expected, ReadConsole.GetLogTypeFromMode(mode));
        }

        [Test]
        public void GetLogTypeFromMode_ExceptionWins_WhenCombinedWithErrorBit()
        {
            // Unity sets the Error bit alongside ScriptingException; Exception must win.
            int mode = (1 << 17) | (1 << 0);
            Assert.AreEqual(LogType.Exception, ReadConsole.GetLogTypeFromMode(mode));
        }

        [Test]
        public void GetLogTypeFromMode_UnknownBits_FallBackToLog()
        {
            Assert.AreEqual(LogType.Log, ReadConsole.GetLogTypeFromMode(1 << 14));
        }

    }
}
