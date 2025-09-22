using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MCPForUnity.Editor.Tools.Prefabs;
using MCPForUnity.Editor.Tools;

namespace MCPForUnityTests.Editor.Tools
{
    public class ManagePrefabsTests
    {
        private const string TempDirectory = "Assets/Temp/ManagePrefabsTests";

        [SetUp]
        public void SetUp()
        {
            StageUtility.GoToMainStage();
            EnsureTempDirectoryExists();
        }

        [TearDown]
        public void TearDown()
        {
            StageUtility.GoToMainStage();
        }

        [OneTimeTearDown]
        public void CleanupAll()
        {
            StageUtility.GoToMainStage();
            if (AssetDatabase.IsValidFolder(TempDirectory))
            {
                AssetDatabase.DeleteAsset(TempDirectory);
            }
        }

        [Test]
        public void OpenStage_OpensPrefabInIsolation()
        {
            string prefabPath = CreateTestPrefab("OpenStageCube");

            try
            {
                var openParams = new JObject
                {
                    ["action"] = "open_stage",
                    ["path"] = prefabPath
                };

                var openResult = ToJObject(ManagePrefabs.HandleCommand(openParams));

                Assert.IsTrue(openResult.Value<bool>("success"), "open_stage should succeed for a valid prefab.");

                PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
                Assert.IsNotNull(stage, "Prefab stage should be open after open_stage.");
                Assert.AreEqual(prefabPath, stage.assetPath, "Opened stage should match prefab path.");

                var stageInfo = ToJObject(ManageEditor.HandleCommand(new JObject { ["action"] = "get_prefab_stage" }));
                Assert.IsTrue(stageInfo.Value<bool>("success"), "get_prefab_stage should succeed when stage is open.");

                var data = stageInfo["data"] as JObject;
                Assert.IsNotNull(data, "Stage info should include data payload.");
                Assert.IsTrue(data.Value<bool>("isOpen"));
                Assert.AreEqual(prefabPath, data.Value<string>("assetPath"));
            }
            finally
            {
                StageUtility.GoToMainStage();
                AssetDatabase.DeleteAsset(prefabPath);
            }
        }

        [Test]
        public void CloseStage_ReturnsSuccess_WhenNoStageOpen()
        {
            StageUtility.GoToMainStage();
            var closeResult = ToJObject(ManagePrefabs.HandleCommand(new JObject
            {
                ["action"] = "close_stage"
            }));

            Assert.IsTrue(closeResult.Value<bool>("success"), "close_stage should succeed even if no stage is open.");
        }

        [Test]
        public void CloseStage_ClosesOpenPrefabStage()
        {
            string prefabPath = CreateTestPrefab("CloseStageCube");

            try
            {
                ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "open_stage",
                    ["path"] = prefabPath
                });

                var closeResult = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "close_stage"
                }));

                Assert.IsTrue(closeResult.Value<bool>("success"), "close_stage should succeed when stage is open.");
                Assert.IsNull(PrefabStageUtility.GetCurrentPrefabStage(), "Prefab stage should be closed after close_stage.");
            }
            finally
            {
                StageUtility.GoToMainStage();
                AssetDatabase.DeleteAsset(prefabPath);
            }
        }

        [Test]
        public void SaveOpenStage_SavesDirtyChanges()
        {
            string prefabPath = CreateTestPrefab("SaveStageCube");

            try
            {
                ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "open_stage",
                    ["path"] = prefabPath
                });

                PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
                Assert.IsNotNull(stage, "Stage should be open before modifying.");

                stage.prefabContentsRoot.transform.localScale = new Vector3(2f, 2f, 2f);

                var saveResult = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "save_open_stage"
                }));

                Assert.IsTrue(saveResult.Value<bool>("success"), "save_open_stage should succeed when stage is open.");
                Assert.IsFalse(stage.scene.isDirty, "Stage scene should not be dirty after saving.");

                GameObject reloaded = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Assert.AreEqual(new Vector3(2f, 2f, 2f), reloaded.transform.localScale, "Saved prefab asset should include changes from open stage.");
            }
            finally
            {
                StageUtility.GoToMainStage();
                AssetDatabase.DeleteAsset(prefabPath);
            }
        }

        [Test]
        public void SaveOpenStage_ReturnsError_WhenNoStageOpen()
        {
            StageUtility.GoToMainStage();

            var saveResult = ToJObject(ManagePrefabs.HandleCommand(new JObject
            {
                ["action"] = "save_open_stage"
            }));

            Assert.IsFalse(saveResult.Value<bool>("success"), "save_open_stage should fail when no stage is open.");
        }

        [Test]
        public void ApplyInstanceOverrides_UpdatesPrefabAsset()
        {
            string prefabPath = CreateTestPrefab("ApplyOverridesCube");
            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset);
            instance.name = "ApplyOverridesInstance";

            try
            {
                instance.transform.localScale = new Vector3(3f, 3f, 3f);

                var applyResult = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "apply_instance_overrides",
                    ["instanceId"] = instance.GetInstanceID()
                }));

                Assert.IsTrue(applyResult.Value<bool>("success"), "apply_instance_overrides should succeed for prefab instance.");

                GameObject reloaded = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Assert.AreEqual(new Vector3(3f, 3f, 3f), reloaded.transform.localScale, "Prefab asset should reflect applied overrides.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
                AssetDatabase.DeleteAsset(prefabPath);
            }
        }

        [Test]
        public void RevertInstanceOverrides_RevertsToPrefabDefaults()
        {
            string prefabPath = CreateTestPrefab("RevertOverridesCube");
            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset);
            instance.name = "RevertOverridesInstance";

            try
            {
                instance.transform.localScale = new Vector3(4f, 4f, 4f);

                var revertResult = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "revert_instance_overrides",
                    ["instanceId"] = instance.GetInstanceID()
                }));

                Assert.IsTrue(revertResult.Value<bool>("success"), "revert_instance_overrides should succeed for prefab instance.");
                Assert.AreEqual(Vector3.one, instance.transform.localScale, "Prefab instance should revert to default scale.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
                AssetDatabase.DeleteAsset(prefabPath);
            }
        }

        private static string CreateTestPrefab(string name)
        {
            EnsureTempDirectoryExists();

            GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            temp.name = name;

            string path = Path.Combine(TempDirectory, name + ".prefab").Replace('\\', '/');
            PrefabUtility.SaveAsPrefabAsset(temp, path, out bool success);
            UnityEngine.Object.DestroyImmediate(temp);

            Assert.IsTrue(success, "PrefabUtility.SaveAsPrefabAsset should succeed for test prefab.");
            return path;
        }

        private static void EnsureTempDirectoryExists()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Temp"))
            {
                AssetDatabase.CreateFolder("Assets", "Temp");
            }

            if (!AssetDatabase.IsValidFolder(TempDirectory))
            {
                AssetDatabase.CreateFolder("Assets/Temp", "ManagePrefabsTests");
            }
        }

        private static JObject ToJObject(object result)
        {
            return result as JObject ?? JObject.FromObject(result);
        }
    }
}
