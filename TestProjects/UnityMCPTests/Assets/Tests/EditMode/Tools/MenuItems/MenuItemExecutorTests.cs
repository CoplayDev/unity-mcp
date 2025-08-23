using NUnit.Framework;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Tools.MenuItems;

namespace MCPForUnityTests.Editor.Tools.MenuItems
{
    public class MenuItemExecutorTests
    {
        private static JObject ToJO(object o) => JObject.FromObject(o);

        [Test]
        public void Execute_MissingParam_ReturnsError()
        {
            var res = MenuItemExecutor.Execute(new JObject());
            var jo = ToJO(res);
            Assert.IsFalse((bool)jo["success"], "Expected success false");
            StringAssert.Contains("Required parameter", (string)jo["error"]);
        }

        [Test]
        public void Execute_Blacklisted_ReturnsError()
        {
            var res = MenuItemExecutor.Execute(new JObject { ["menuPath"] = "File/Quit" });
            var jo = ToJO(res);
            Assert.IsFalse((bool)jo["success"], "Expected success false for blacklisted menu");
            StringAssert.Contains("blocked for safety", (string)jo["error"], "Expected blacklist message");
        }

        [Test]
        public void Execute_NonBlacklisted_ReturnsImmediateSuccess()
        {
            // We don't rely on the menu actually existing; execution is delayed and we only check the immediate response shape
            var res = MenuItemExecutor.Execute(new JObject { ["menuPath"] = "File/Save Project" });
            var jo = ToJO(res);
            Assert.IsTrue((bool)jo["success"], "Expected immediate success response");
            StringAssert.Contains("Attempted to execute menu item", (string)jo["message"], "Expected attempt message");
        }
    }
}
