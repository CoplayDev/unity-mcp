using NUnit.Framework;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Tools.MenuItems;

namespace MCPForUnityTests.Editor.Tools.MenuItems
{
    public class ManageMenuItemTests
    {
        private static JObject ToJO(object o) => JObject.FromObject(o);

        [Test]
        public void HandleCommand_UnknownAction_ReturnsError()
        {
            var res = ManageMenuItem.HandleCommand(new JObject { ["action"] = "unknown_action" });
            var jo = ToJO(res);
            Assert.IsFalse((bool)jo["success"], "Expected success false for unknown action");
            StringAssert.Contains("Unknown action", (string)jo["error"]);
        }

        [Test]
        public void HandleCommand_List_RoutesAndReturnsArray()
        {
            var res = ManageMenuItem.HandleCommand(new JObject { ["action"] = "list" });
            var jo = ToJO(res);
            Assert.IsTrue((bool)jo["success"], "Expected success true");
            Assert.AreEqual(JTokenType.Array, jo["data"].Type, "Expected data to be an array");
        }

        [Test]
        public void HandleCommand_Execute_Blacklisted_RoutesAndErrors()
        {
            var res = ManageMenuItem.HandleCommand(new JObject { ["action"] = "execute", ["menuPath"] = "File/Quit" });
            var jo = ToJO(res);
            Assert.IsFalse((bool)jo["success"], "Expected success false");
            StringAssert.Contains("blocked for safety", (string)jo["error"], "Expected blacklist message");
        }

        [Test]
        public void HandleCommand_Exists_MissingParam_ReturnsError()
        {
            var res = ManageMenuItem.HandleCommand(new JObject { ["action"] = "exists" });
            var jo = ToJO(res);
            Assert.IsFalse((bool)jo["success"], "Expected success false when missing menuPath");
            StringAssert.Contains("Required parameter", (string)jo["error"]);
        }
    }
}
