using System;
using System.IO;
using System.Linq;
using System.Text;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using System.Text.RegularExpressions;

namespace MCPForUnityTests.Editor.Tools
{
    public class AssetBundleExplorerTests
    {
        private string directory;
        private string relativeDirectory;
        private string sourceDirectory;
        private string bundleDirectory;
        private string relativeBundleDirectory;

        [OneTimeSetUp]
        public void BuildFixtureBundles()
        {
            string id = Guid.NewGuid().ToString("N");
            sourceDirectory = "Assets/McpBundleExplorerFixture-" + id;
            relativeBundleDirectory = "Temp/McpBundleExplorerBuild-" + id;
            bundleDirectory = Path.Combine(Path.GetDirectoryName(Application.dataPath), relativeBundleDirectory);
            Directory.CreateDirectory(sourceDirectory);
            Directory.CreateDirectory(bundleDirectory);
            File.WriteAllText(sourceDirectory + "/alpha.txt", "alpha fixture");
            File.WriteAllText(sourceDirectory + "/beta.txt", "beta fixture");
            AssetDatabase.Refresh();
            // Copy the repository fixture rather than replacing or saving the user's open scene.
            File.Copy("Assets/Scenes/SampleScene.unity", sourceDirectory + "/fixture.unity");
            AssetDatabase.ImportAsset(sourceDirectory + "/fixture.unity");
            var manifest = BuildPipeline.BuildAssetBundles(bundleDirectory, new[]
            {
                new AssetBundleBuild { assetBundleName = "assets", assetNames = new[] { sourceDirectory + "/alpha.txt", sourceDirectory + "/beta.txt" } },
                new AssetBundleBuild { assetBundleName = "scenes", assetNames = new[] { sourceDirectory + "/fixture.unity" } }
            }, BuildAssetBundleOptions.UncompressedAssetBundle | BuildAssetBundleOptions.ForceRebuildAssetBundle,
                EditorUserBuildSettings.activeBuildTarget);
            Assert.That(manifest, Is.Not.Null, "Fixture bundles must be built for this Editor's active build target.");
        }

        [OneTimeTearDown]
        public void RemoveFixtureBundles()
        {
            if (!string.IsNullOrEmpty(sourceDirectory)) AssetDatabase.DeleteAsset(sourceDirectory);
            if (!string.IsNullOrEmpty(bundleDirectory) && Directory.Exists(bundleDirectory)) Directory.Delete(bundleDirectory, true);
        }

        [SetUp]
        public void SetUp()
        {
            relativeDirectory = "Temp/McpBundleExplorerTests-" + Guid.NewGuid().ToString("N");
            directory = Path.Combine(Path.GetDirectoryName(Application.dataPath), relativeDirectory);
            Directory.CreateDirectory(directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        private JObject Call(string action, string path, int pageSize = 25, int pageNumber = 1)
        {
            return JObject.FromObject(ManageAsset.HandleCommand(new JObject
            {
                ["action"] = action, ["path"] = path,
                ["pageSize"] = pageSize, ["pageNumber"] = pageNumber
            }));
        }

        [Test]
        public void ListAssetBundles_ReadsHeadersWithoutLoadingOrImportingFiles()
        {
            File.WriteAllBytes(Path.Combine(directory, "characters"), Encoding.ASCII.GetBytes("UnityFS\0fixture"));
            File.WriteAllText(Path.Combine(directory, "characters.manifest"), "ordinary manifest");
            File.WriteAllText(Path.Combine(directory, "notes.txt"), "ordinary file");
            Directory.CreateDirectory(Path.Combine(directory, "nested"));
            int loadedBefore = AssetBundle.GetAllLoadedAssetBundles().Count();

            var response = Call("list_asset_bundles", relativeDirectory);

            Assert.That(response.Value<bool>("success"), Is.True, response.ToString());
            var entries = (JArray)response["data"]["entries"];
            Assert.That(entries.Count, Is.EqualTo(2));
            Assert.That(entries[0].Value<string>("type"), Is.EqualTo("bundle"));
            Assert.That(entries[0].Value<string>("path"), Is.EqualTo(relativeDirectory + "/characters"));
            Assert.That(entries[1].Value<string>("type"), Is.EqualTo("directory"));
            Assert.That(AssetBundle.GetAllLoadedAssetBundles().Count(), Is.EqualTo(loadedBefore));
            Assert.That(File.ReadAllText(Path.Combine(directory, "notes.txt")), Is.EqualTo("ordinary file"));
        }

        [Test]
        public void ListAssetBundles_PagesSortedEntriesAndHandlesAnOverflowingOffset()
        {
            foreach (string name in new[] { "c", "a", "b" })
                File.WriteAllBytes(Path.Combine(directory, name), Encoding.ASCII.GetBytes("UnityFS\0fixture"));
            var first = Call("list_asset_bundles", relativeDirectory, 2, 1)["data"];
            var second = Call("list_asset_bundles", relativeDirectory, 2, 2)["data"];
            Assert.That(first.Value<int>("totalEntries"), Is.EqualTo(3));
            Assert.That(first.Value<int>("nextPageNumber"), Is.EqualTo(2));
            Assert.That(first["entries"].Select(e => e.Value<string>("path")), Is.EqualTo(new[] { relativeDirectory + "/a", relativeDirectory + "/b" }));
            Assert.That(second["entries"].Count(), Is.EqualTo(1));
            Assert.That(second["nextPageNumber"].Type, Is.EqualTo(JTokenType.Null));
            Assert.That(Call("list_asset_bundles", relativeDirectory, 200, int.MaxValue)["data"]["entries"].Count(), Is.Zero);
        }

        [Test]
        public void ListAssetBundles_DoesNotOpenAnEmptyNamedPipe()
        {
            if (Application.platform == RuntimePlatform.WindowsEditor) Assert.Ignore("Unix fixture uses mkfifo.");
            string pipe = Path.Combine(directory, "pipe");
            using (var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/usr/bin/mkfifo", Arguments = "\"" + pipe + "\"",
                UseShellExecute = false, CreateNoWindow = true
            }))
            {
                Assert.That(process.WaitForExit(5000), Is.True);
                Assert.That(process.ExitCode, Is.Zero);
            }
            Assert.That(new FileInfo(pipe).Length, Is.Zero);
            File.WriteAllBytes(Path.Combine(directory, "short"), new byte[] { 1, 2 });
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var response = Call("list_asset_bundles", relativeDirectory);
            Assert.That(response.Value<bool>("success"), Is.True, response.ToString());
            Assert.That(response["data"]["entries"].Count(), Is.Zero);
            Assert.That(watch.ElapsedMilliseconds, Is.LessThan(200));
        }

        [TestCase("UnityFS\0fixture")]
        [TestCase("UnityRaw\0fixture")]
        [TestCase("UnityWeb\0fixture")]
        public void ListAssetBundles_RecognizesUnityBundleSignatures(string header)
        {
            File.WriteAllBytes(Path.Combine(directory, "bundle"), Encoding.ASCII.GetBytes(header));
            File.WriteAllBytes(Path.Combine(directory, "not-a-bundle"), Encoding.ASCII.GetBytes("UnityFS-without-null"));
            Assert.That(Call("list_asset_bundles", relativeDirectory)["data"]["entries"].Count(), Is.EqualTo(1));
        }

        [TestCase("list_asset_bundles", 0, 1)]
        [TestCase("list_asset_bundles", 201, 1)]
        [TestCase("get_asset_bundle_info", 25, 0)]
        public void BundleActions_RejectInvalidPaging(string action, int pageSize, int pageNumber)
        {
            Assert.That(Call(action, relativeDirectory, pageSize, pageNumber).Value<bool>("success"), Is.False);
        }

        [TestCase("list_asset_bundles")]
        [TestCase("get_asset_bundle_info")]
        public void BundleActions_RejectOutsideProjectPaths(string action)
        {
            var response = Call(action, Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Application.dataPath)), "outside"));
            Assert.That(response.Value<bool>("success"), Is.False);
            Assert.That(response.Value<string>("error"), Does.Contain("inside the Unity project"));
        }

        [Test]
        public void ListAssetBundles_AcceptsAnAbsoluteProjectPathAndNormalizesDotSegments()
        {
            Directory.CreateDirectory(Path.Combine(directory, "nested"));
            Assert.That(Call("list_asset_bundles", directory).Value<bool>("success"), Is.True);
            Assert.That(Call("list_asset_bundles", relativeDirectory + "/nested/..").Value<bool>("success"), Is.True);
        }

        [Test]
        public void BundleActions_DoNotFollowSymbolicLinks()
        {
            if (Application.platform == RuntimePlatform.WindowsEditor) Assert.Ignore("Unix fixture uses ln; production rejects Windows reparse points too.");
            string outside = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Application.dataPath)), "McpBundleOutside-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outside);
            try
            {
                File.WriteAllBytes(Path.Combine(outside, "bundle"), Encoding.ASCII.GetBytes("UnityFS\0fixture"));
                using (var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "/bin/ln", Arguments = "-s \"" + outside + "\" \"" + Path.Combine(directory, "link") + "\"",
                    UseShellExecute = false, CreateNoWindow = true
                }))
                {
                    Assert.That(process.WaitForExit(5000), Is.True);
                    Assert.That(process.ExitCode, Is.Zero);
                }
                Assert.That(Call("list_asset_bundles", relativeDirectory)["data"]["entries"].Count(), Is.Zero);
                Assert.That(Call("list_asset_bundles", relativeDirectory + "/link").Value<bool>("success"), Is.False);
                Assert.That(Call("get_asset_bundle_info", relativeDirectory + "/link/bundle").Value<bool>("success"), Is.False);
            }
            finally { Directory.Delete(outside, true); }
        }

        [Test]
        public void GetAssetBundleInfo_RejectsOrdinaryFilesAndMissingPaths()
        {
            File.WriteAllText(Path.Combine(directory, "notes.txt"), "ordinary file");
            Assert.That(Call("get_asset_bundle_info", relativeDirectory + "/notes.txt").Value<bool>("success"), Is.False);
            Assert.That(Call("get_asset_bundle_info", relativeDirectory + "/missing").Value<bool>("success"), Is.False);
            Assert.That(Call("list_asset_bundles", relativeDirectory + "/notes.txt").Value<bool>("success"), Is.False);
            Assert.That(Call("list_asset_bundles", "").Value<bool>("success"), Is.False);
        }

        [Test]
        public void GetAssetBundleInfo_InspectsRealAssetsAndReleasesItsHandleOnEveryPage()
        {
            int loadedBefore = AssetBundle.GetAllLoadedAssetBundles().Count();
            byte[] bytes = File.ReadAllBytes(Path.Combine(bundleDirectory, "assets"));
            int sceneCount = SceneManager.sceneCount;
            var first = Call("get_asset_bundle_info", relativeBundleDirectory + "/assets", 1, 1);
            var second = Call("get_asset_bundle_info", relativeBundleDirectory + "/assets", 1, 2);
            Assert.That(first.Value<bool>("success"), Is.True, first.ToString());
            Assert.That(second.Value<bool>("success"), Is.True, second.ToString());
            Assert.That(first["data"].Value<int>("assetCount"), Is.EqualTo(2));
            Assert.That(first["data"].Value<int>("sceneCount"), Is.Zero);
            Assert.That(first["data"]["entries"][0].Value<string>("name"), Does.EndWith("/alpha.txt"));
            Assert.That(second["data"]["entries"][0].Value<string>("name"), Does.EndWith("/beta.txt"));
            Assert.That(AssetBundle.GetAllLoadedAssetBundles().Count(), Is.EqualTo(loadedBefore));
            Assert.That(SceneManager.sceneCount, Is.EqualTo(sceneCount));
            Assert.That(File.ReadAllBytes(Path.Combine(bundleDirectory, "assets")), Is.EqualTo(bytes));
        }

        [Test]
        public void GetAssetBundleInfo_ListsScenesWithoutChangingLoadedAssetsOrScenes()
        {
            var existing = AssetBundle.LoadFromFile(Path.Combine(bundleDirectory, "assets"));
            Assert.That(existing, Is.Not.Null);
            try
            {
                var asset = existing.LoadAsset<TextAsset>(existing.GetAllAssetNames()[0]);
                int loadedBefore = AssetBundle.GetAllLoadedAssetBundles().Count();
                int scenesBefore = SceneManager.sceneCount;
                var response = Call("get_asset_bundle_info", relativeBundleDirectory + "/scenes");
                Assert.That(response.Value<bool>("success"), Is.True, response.ToString());
                Assert.That(response["data"].Value<bool>("isStreamedSceneAssetBundle"), Is.True);
                Assert.That(response["data"].Value<int>("sceneCount"), Is.EqualTo(1));
                Assert.That(response["data"]["entries"][0].Value<string>("type"), Is.EqualTo("scene"));
                Assert.That(response["data"]["entries"][0].Value<string>("name"), Does.EndWith("/fixture.unity"));
                Assert.That(AssetBundle.GetAllLoadedAssetBundles().Count(), Is.EqualTo(loadedBefore));
                Assert.That(SceneManager.sceneCount, Is.EqualTo(scenesBefore));
                Assert.That(asset.text, Is.EqualTo("alpha fixture"));
            }
            finally { existing.Unload(true); }
        }

        [Test]
        public void GetAssetBundleInfo_DoesNotUnloadAnAlreadyLoadedSelectedBundle()
        {
            var existing = AssetBundle.LoadFromFile(Path.Combine(bundleDirectory, "assets"));
            Assert.That(existing, Is.Not.Null);
            try
            {
                LogAssert.Expect(LogType.Error, new Regex("already loaded", RegexOptions.IgnoreCase));
                var response = Call("get_asset_bundle_info", relativeBundleDirectory + "/assets");
                Assert.That(response.Value<bool>("success"), Is.False);
                Assert.That(existing.GetAllAssetNames().Length, Is.EqualTo(2));
                Assert.That(AssetBundle.GetAllLoadedAssetBundles().Contains(existing), Is.True);
            }
            finally { existing.Unload(true); }
        }
    }
}
