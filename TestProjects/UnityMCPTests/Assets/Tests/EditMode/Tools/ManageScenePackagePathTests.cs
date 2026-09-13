using System.IO;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
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

        /// <summary>
        /// The AssetDatabase does not know about a file written to disk until it is imported.
        /// Swapping File.Exists for an AssetDatabase lookup would have made such a scene
        /// unloadable, so the check accepts either answer.
        /// </summary>
        [Test]
        public void SceneAssetExists_FindsUnimportedFileOnDisk()
        {
            string dir = Path.Combine(Application.dataPath, "ManageScenePackagePathTests_Tmp");
            string relative = "Assets/ManageScenePackagePathTests_Tmp/NotImported.unity";
            string full = Path.Combine(dir, "NotImported.unity");

            Directory.CreateDirectory(dir);
            try
            {
                // Written directly, deliberately without AssetDatabase.Refresh().
                File.WriteAllText(full, "%YAML 1.1\n");

                Assert.IsNull(AssetDatabase.LoadAssetAtPath<SceneAsset>(relative),
                    "sanity: the AssetDatabase must not know about this file yet");
                Assert.IsTrue(ManageScene.SceneAssetExists(relative),
                    "a scene present on disk must still be found");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
                string meta = dir + ".meta";
                if (File.Exists(meta)) File.Delete(meta);
                AssetDatabase.Refresh();
            }
        }
    }
}
