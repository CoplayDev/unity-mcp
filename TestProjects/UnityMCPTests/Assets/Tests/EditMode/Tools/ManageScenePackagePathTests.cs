using NUnit.Framework;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Tools;

namespace MCPForUnity.Tests.EditMode.Tools
{
    /// <summary>
    /// Scenes that live under Packages/ used to be re-rooted under Assets/, so a valid
    /// package scene path was rewritten to "Assets/Packages/..." and could never resolve
    /// (issue #1197).
    /// </summary>
    [TestFixture]
    public class ManageScenePackagePathTests
    {
        [TestCase("Assets/Scenes/Main.unity", true)]
        [TestCase("assets/scenes/main.unity", true)]
        [TestCase("Packages/com.example.pkg/Samples/Demo.unity", true)]
        [TestCase("packages/com.example.pkg/Samples/Demo.unity", true)]
        [TestCase("Scenes/Main.unity", false)]
        [TestCase("com.example.pkg/Samples/Demo.unity", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void IsProjectRooted_RecognisesBothRoots(string path, bool expected)
        {
            Assert.AreEqual(expected, ManageScene.IsProjectRooted(path));
        }

        [Test]
        public void Load_MissingPackageScene_ReportsThePackagePath_NotAnAssetsRewrite()
        {
            var p = new JObject
            {
                ["action"] = "load",
                ["path"] = "Packages/com.example.doesnotexist/Samples/Demo.unity"
            };

            var r = ManageScene.HandleCommand(p) as JObject
                    ?? JObject.FromObject(ManageScene.HandleCommand(p));

            Assert.IsFalse(r.Value<bool>("success"), r.ToString());

            string message = r.Value<string>("message") ?? r.ToString();
            StringAssert.Contains("Packages/com.example.doesnotexist/Samples/Demo.unity", message);
            StringAssert.DoesNotContain("Assets/Packages", message);
        }

        [Test]
        public void SceneAssetExists_ReturnsFalse_ForUnknownPaths()
        {
            Assert.IsFalse(ManageScene.SceneAssetExists("Packages/com.example.doesnotexist/A.unity"));
            Assert.IsFalse(ManageScene.SceneAssetExists("Assets/DoesNotExist/A.unity"));
            Assert.IsFalse(ManageScene.SceneAssetExists(null));
        }
    }
}
