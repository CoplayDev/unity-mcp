using System;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Tools;
using static MCPForUnityTests.Editor.TestUtilities;

namespace MCPForUnityTests.Editor.Tools
{
    public class RefreshUnityTests
    {
        [Test]
        public void HandleCommand_CompileNone_NoWait_CompletesSynchronously()
        {
            // scope=scripts skips AssetDatabase.Refresh, compile=none skips the
            // request, wait_for_ready=false skips both waits: nothing on this path
            // yields, so the task must already be complete when it is handed back.
            var task = RefreshUnity.HandleCommand(new JObject
            {
                ["mode"] = "if_dirty",
                ["scope"] = "scripts",
                ["compile"] = "none",
                ["wait_for_ready"] = false,
            });

            Assert.IsTrue(task.IsCompleted, "compile=none with wait_for_ready=false must not defer");

            var result = ToJObject(task.Result);
            if (TestRunStatus.IsRunning)
            {
                // Under the bridge's run_tests the handler short-circuits before the
                // refresh logic; that exit is synchronous too, and is all we can check.
                Assert.IsFalse(result.Value<bool>("success"), result.ToString());
                Assert.AreEqual("tests_running", result["data"]?["reason"]?.ToString(), result.ToString());
                return;
            }

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var data = result["data"];
            Assert.IsFalse(data.Value<bool>("refresh_triggered"), result.ToString());
            Assert.IsFalse(data.Value<bool>("compile_requested"), result.ToString());
            Assert.AreEqual(JTokenType.Null, data["compile_started"].Type,
                "compile_started must be null when no compile was waited for");
        }

        [Test]
        public void WaitForCompilationToStart_CounterAlreadyMoved_CompletesSynchronouslyTrue()
        {
            // A compile that began and ended inside AssetDatabase.Refresh leaves only
            // the counter behind. Presenting a stale "before" value reproduces that
            // state without triggering a compile.
            var task = RefreshUnity.WaitForCompilationToStartAsync(
                EditorStateCache.CompileCount - 1,
                TimeSpan.FromSeconds(10));

            Assert.IsTrue(task.IsCompleted, "counter already moved must resolve without a tick");
            Assert.IsTrue(task.Result, "a moved counter is a start, not a grace expiry");
        }
    }
}
