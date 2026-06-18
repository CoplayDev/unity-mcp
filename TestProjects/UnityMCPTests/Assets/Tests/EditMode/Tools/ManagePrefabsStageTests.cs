using System.IO;
using MCPForUnity.Editor.Tools.Prefabs;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static MCPForUnityTests.Editor.TestUtilities;

namespace MCPForUnityTests.Editor.Tools
{
    public class ManagePrefabsStageTests
    {
        private const string TempDirectory = "Assets/Temp/ManagePrefabsStageTests";

        [SetUp]
        public void SetUp()
        {
            StageUtility.GoToMainStage();
            EnsureFolder(TempDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            StageUtility.GoToMainStage();

            if (AssetDatabase.IsValidFolder(TempDirectory))
            {
                AssetDatabase.DeleteAsset(TempDirectory);
            }

            CleanupEmptyParentFolders(TempDirectory);
        }

        [Test]
        public void OpenPrefabStage_RequiresPrefabPath()
        {
            var result = ToJObject(ManagePrefabs.HandleCommand(new JObject
            {
                ["action"] = "open_prefab_stage"
            }));

            Assert.IsFalse(result.Value<bool>("success"));
            StringAssert.Contains("prefabPath", result.Value<string>("error"));
        }

        [Test]
        public void OpenPrefabStage_RejectsNonPrefabPath()
        {
            var result = ToJObject(ManagePrefabs.HandleCommand(new JObject
            {
                ["action"] = "open_prefab_stage",
                ["prefabPath"] = "Assets/Temp/NotPrefab.txt"
            }));

            Assert.IsFalse(result.Value<bool>("success"));
            StringAssert.Contains(".prefab", result.Value<string>("error"));
        }

        [Test]
        public void OpenPrefabStage_OpensPrefabStageAndReturnsStageData()
        {
            string prefabPath = CreateTestPrefab("OpenStageRoot");

            try
            {
                var result = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "open_prefab_stage",
                    ["prefabPath"] = prefabPath
                }));

                Assert.IsTrue(result.Value<bool>("success"));
                Assert.AreEqual(prefabPath, result["data"].Value<string>("prefabPath"));
                Assert.AreEqual(prefabPath, result["data"].Value<string>("openedPrefabPath"));
                Assert.AreEqual("OpenStageRoot", result["data"].Value<string>("rootName"));

                var stage = PrefabStageUtility.GetCurrentPrefabStage();
                Assert.IsNotNull(stage);
                Assert.AreEqual(prefabPath, stage.assetPath);

                var closeResult = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "close_prefab_stage"
                }));
                Assert.IsTrue(closeResult.Value<bool>("success"));
                Assert.IsNull(PrefabStageUtility.GetCurrentPrefabStage());
            }
            finally
            {
                StageUtility.GoToMainStage();
                SafeDeleteAsset(prefabPath);
            }
        }

        [Test]
        public void OpenPrefabStage_AcceptsPathAlias()
        {
            string prefabPath = CreateTestPrefab("AliasRoot");

            try
            {
                var result = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "open_prefab_stage",
                    ["path"] = prefabPath
                }));

                Assert.IsTrue(result.Value<bool>("success"));
                Assert.AreEqual(prefabPath, result["data"].Value<string>("openedPrefabPath"));
                Assert.IsNotNull(PrefabStageUtility.GetCurrentPrefabStage());
            }
            finally
            {
                StageUtility.GoToMainStage();
                SafeDeleteAsset(prefabPath);
            }
        }

        [Test]
        public void OpenPrefabStage_PrefabPathTakesPrecedenceOverPath()
        {
            string prefabPath = CreateTestPrefab("PrefabPathRoot");
            string aliasPath = CreateTestPrefab("AliasPathRoot");

            try
            {
                var result = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "open_prefab_stage",
                    ["prefabPath"] = prefabPath,
                    ["path"] = aliasPath
                }));

                Assert.IsTrue(result.Value<bool>("success"));

                var currentStage = PrefabStageUtility.GetCurrentPrefabStage();
                Assert.IsNotNull(currentStage, "Expected a prefab stage to be open.");
                Assert.AreEqual(
                    prefabPath,
                    currentStage.assetPath,
                    "prefabPath should take precedence over path when both are provided."
                );
            }
            finally
            {
                StageUtility.GoToMainStage();
                SafeDeleteAsset(prefabPath);
                SafeDeleteAsset(aliasPath);
            }
        }

        [Test]
        public void SavePrefabStage_PersistsPrefabContentChanges()
        {
            string prefabPath = CreateTestPrefab("SaveStageRoot");

            try
            {
                AssertOpenPrefabStage(prefabPath);

                var stage = PrefabStageUtility.GetCurrentPrefabStage();
                Assert.IsNotNull(stage, "Expected prefab stage to be open.");

                var child = new GameObject("SavedChild");
                child.transform.SetParent(stage.prefabContentsRoot.transform, false);

                var saveResult = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "save_prefab_stage"
                }));

                Assert.IsTrue(saveResult.Value<bool>("success"), $"Expected save to succeed but got: {saveResult}");
                Assert.AreEqual(prefabPath, saveResult["data"].Value<string>("prefabPath"));

                StageUtility.GoToMainStage();

                GameObject reloaded = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Assert.IsNotNull(reloaded.transform.Find("SavedChild"), "Saved prefab should contain the new child after save_prefab_stage.");
            }
            finally
            {
                StageUtility.GoToMainStage();
                SafeDeleteAsset(prefabPath);
            }
        }

        [Test]
        public void ClosePrefabStage_SaveBeforeClose_PersistsChanges()
        {
            string prefabPath = CreateTestPrefab("CloseSaveRoot");

            try
            {
                AssertOpenPrefabStage(prefabPath);

                var stage = PrefabStageUtility.GetCurrentPrefabStage();
                Assert.IsNotNull(stage, "Expected prefab stage to be open.");

                var child = new GameObject("CloseSavedChild");
                child.transform.SetParent(stage.prefabContentsRoot.transform, false);

                var closeResult = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "close_prefab_stage",
                    ["saveBeforeClose"] = true
                }));

                Assert.IsTrue(closeResult.Value<bool>("success"), $"Expected close with save to succeed but got: {closeResult}");
                Assert.IsNull(PrefabStageUtility.GetCurrentPrefabStage(), "Prefab stage should be closed after close_prefab_stage.");

                GameObject reloaded = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Assert.IsNotNull(reloaded.transform.Find("CloseSavedChild"), "Saved prefab should contain the new child after close_prefab_stage(saveBeforeClose: true).");
            }
            finally
            {
                StageUtility.GoToMainStage();
                SafeDeleteAsset(prefabPath);
            }
        }

        private static void AssertOpenPrefabStage(string prefabPath)
        {
            var openResult = ToJObject(ManagePrefabs.HandleCommand(new JObject
            {
                ["action"] = "open_prefab_stage",
                ["prefabPath"] = prefabPath
            }));

            Assert.IsTrue(openResult.Value<bool>("success"), $"Expected open to succeed but got: {openResult}");
        }

        private static string CreateTestPrefab(string rootName)
        {
            string prefabPath = Path.Combine(TempDirectory, $"{rootName}.prefab").Replace('\\', '/');
            var root = new GameObject(rootName);

            try
            {
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root, true);
            }

            return prefabPath;
        }
    }
}
