using NUnit.Framework;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Tools;

namespace MCPForUnityTests.Editor.Tools
{
    public class ExecuteMenuItemTests
    {
        private static JObject ToJO(object o) => JObject.FromObject(o);

        [Test]
        public void Execute_MissingParam_ReturnsError()
        {
            var res = ExecuteMenuItem.HandleCommand(new JObject());
            var jo = ToJO(res);
            Assert.IsFalse((bool)jo["success"], "Expected success false");
            StringAssert.Contains("Required parameter", (string)jo["error"]);
        }

        [Test]
        public void Execute_Blacklisted_ReturnsError()
        {
            var res = ExecuteMenuItem.HandleCommand(new JObject { ["menuPath"] = "File/Quit" });
            var jo = ToJO(res);
            Assert.IsFalse((bool)jo["success"], "Expected success false for blacklisted menu");
            StringAssert.Contains("blocked for safety", (string)jo["error"], "Expected blacklist message");
        }

        [Test]
        public void Execute_NonBlacklisted_ReturnsImmediateSuccess()
        {
            // We don't rely on the menu actually existing; execution is delayed and we only check the immediate response shape
            var res = ExecuteMenuItem.HandleCommand(new JObject { ["menuPath"] = "File/Save Project" });
            var jo = ToJO(res);
            Assert.IsTrue((bool)jo["success"], "Expected immediate success response");
            StringAssert.Contains("Attempted to execute menu item", (string)jo["message"], "Expected attempt message");
        }

        [Test]
        public void Execute_OutOfPolicyPath_IsRejected()
        {
            // harden/security (R6): only allow-listed prefixes execute; build/delete/package
            // and other out-of-policy items are refused even though they are valid menu paths.
            foreach (var path in new[] { "File/Build Settings...", "File/Build And Run", "Assets/Delete" })
            {
                var res = ExecuteMenuItem.HandleCommand(new JObject { ["menuPath"] = path });
                var jo = ToJO(res);
                Assert.IsFalse((bool)jo["success"], $"Expected '{path}' to be rejected");
                StringAssert.Contains("allow-list", (string)jo["error"], "Expected allow-list rejection message");
            }
        }

        [Test]
        public void Execute_AllowListedPrefixes_AreAccepted()
        {
            // Allow-listed prefixes pass the policy gate (response shape only; we don't require
            // the menu item to exist/execute in the test editor).
            foreach (var path in new[] { "GameObject/Create Empty", "Window/General/Console", "Edit/Undo" })
            {
                var res = ExecuteMenuItem.HandleCommand(new JObject { ["menuPath"] = path });
                var jo = ToJO(res);
                // Either it executed (success) or failed to execute, but it must NOT be a
                // policy rejection.
                StringAssert.DoesNotContain("allow-list", (string)jo["error"] ?? "",
                    $"'{path}' should pass the allow-list gate");
            }
        }
    }
}
