using NUnit.Framework;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Tools;
using UnityEngine.SceneManagement;

namespace MCPForUnity.Tests.EditMode.Tools
{
    [TestFixture]
    public class ManageSceneMultiSceneTests
    {
        [Test]
        public void GetLoadedScenes_ReturnsAtLeastOne()
        {
            var p = new JObject { ["action"] = "get_loaded_scenes" };
            var result = ManageScene.HandleCommand(p);
            var r = result as JObject ?? JObject.FromObject(result);
            Assert.IsTrue(r.Value<bool>("success"), r.ToString());
            var scenes = r["data"]?["scenes"] as JArray;
            Assert.IsNotNull(scenes);
            Assert.GreaterOrEqual(scenes.Count, 1);
        }

        [Test]
        public void CloseScene_LastScene_ReturnsError()
        {
            if (SceneManager.sceneCount > 1)
            {
                Assert.Ignore("Test requires a single scene; editor has additive scenes open.");
                return;
            }
            var active = SceneManager.GetActiveScene();
            var p = new JObject
            {
                ["action"] = "close_scene",
                ["sceneName"] = active.name
            };
            var result = ManageScene.HandleCommand(p);
            var r = result as JObject ?? JObject.FromObject(result);
            Assert.IsFalse(r.Value<bool>("success"), "Should fail to close last scene");
        }

        [Test]
        public void MoveToScene_MissingTarget_ReturnsError()
        {
            var p = new JObject
            {
                ["action"] = "move_to_scene",
                ["sceneName"] = "SomeScene"
            };
            var result = ManageScene.HandleCommand(p);
            var r = result as JObject ?? JObject.FromObject(result);
            Assert.IsFalse(r.Value<bool>("success"));
        }

        [Test]
        public void MoveToScene_NonExistentGO_ReturnsError()
        {
            var p = new JObject
            {
                ["action"] = "move_to_scene",
                ["target"] = "NonExistentGO_99999",
                ["sceneName"] = "SomeScene"
            };
            var result = ManageScene.HandleCommand(p);
            var r = result as JObject ?? JObject.FromObject(result);
            Assert.IsFalse(r.Value<bool>("success"));
        }

        [Test]
        public void ModifyBuildSettings_RedirectsToManageBuild()
        {
            var p = new JObject
            {
                ["action"] = "modify_build_settings",
                ["scenePath"] = "Assets/Scenes/Test.unity",
                ["operation"] = "add"
            };
            var result = ManageScene.HandleCommand(p);
            var r = result as JObject ?? JObject.FromObject(result);
            Assert.IsFalse(r.Value<bool>("success"));
            Assert.IsTrue(r.Value<string>("error").Contains("manage_build"));
        }

        [Test]
        public void SetActiveScene_NotFound_ReturnsError()
        {
            var p = new JObject
            {
                ["action"] = "set_active_scene",
                ["sceneName"] = "NonExistentScene_99999"
            };
            var result = ManageScene.HandleCommand(p);
            var r = result as JObject ?? JObject.FromObject(result);
            Assert.IsFalse(r.Value<bool>("success"));
        }
    }
}
