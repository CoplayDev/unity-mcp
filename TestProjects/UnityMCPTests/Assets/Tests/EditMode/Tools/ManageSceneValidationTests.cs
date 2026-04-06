using NUnit.Framework;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Tools;

namespace MCPForUnity.Tests.EditMode.Tools
{
    [TestFixture]
    public class ManageSceneValidationTests
    {
        [Test]
        public void Validate_CleanScene_ReturnsNoIssues()
        {
            var p = new JObject { ["action"] = "validate" };
            var result = ManageScene.HandleCommand(p);
            var r = result as JObject ?? JObject.FromObject(result);
            Assert.IsTrue(r.Value<bool>("success"), r.ToString());
            var data = r["data"];
            Assert.IsNotNull(data);
            Assert.AreEqual(0, data.Value<int>("totalIssues"));
        }

        [Test]
        public void Validate_WithAutoRepair_CleanScene_RepairsNothing()
        {
            var p = new JObject { ["action"] = "validate", ["autoRepair"] = true };
            var result = ManageScene.HandleCommand(p);
            var r = result as JObject ?? JObject.FromObject(result);
            Assert.IsTrue(r.Value<bool>("success"), r.ToString());
            Assert.AreEqual(0, r["data"].Value<int>("repaired"));
        }
    }
}
