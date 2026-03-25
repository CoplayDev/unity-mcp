using NUnit.Framework;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Tools;

namespace MCPForUnity.Tests.EditMode.Tools
{
    [TestFixture]
    public class ManageSceneTemplateTests
    {
        [Test]
        public void Create_UnknownTemplate_ReturnsError()
        {
            var p = new JObject
            {
                ["action"] = "create",
                ["name"] = "TemplateTest",
                ["path"] = "Assets/Scenes",
                ["template"] = "nonexistent_template"
            };
            var result = ManageScene.HandleCommand(p);
            var r = result as JObject ?? JObject.FromObject(result);
            Assert.IsFalse(r.Value<bool>("success"), "Unknown template should fail");
        }
    }
}
