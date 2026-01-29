using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using MCPForUnity.Editor.Tools.Prefabs;
using static MCPForUnityTests.Editor.TestUtilities;

namespace MCPForUnityTests.Editor.Tools
{
    /// <summary>
    /// Tests for Prefab CRUD operations: create_from_gameobject, get_info, get_hierarchy, modify_contents.
    /// </summary>
    public class ManagePrefabsCrudTests
    {
        private const string TempDirectory = "Assets/Temp/ManagePrefabsCrudTests";

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

        #region CREATE Tests

        [Test]
        public void CreateFromGameObject_CreatesNewPrefab()
        {
            string prefabPath = Path.Combine(TempDirectory, "NewPrefab.prefab").Replace('\\', '/');
            GameObject sceneObject = new GameObject("TestObject");

            try
            {
                var result = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "create_from_gameobject",
                    ["target"] = sceneObject.name,
                    ["prefabPath"] = prefabPath
                }));

                Assert.IsTrue(result.Value<bool>("success"));
                Assert.AreEqual(prefabPath, result["data"].Value<string>("prefabPath"));
                Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));
            }
            finally
            {
                SafeDeleteAsset(prefabPath);
                if (sceneObject != null) UnityEngine.Object.DestroyImmediate(sceneObject, true);
            }
        }

        [Test]
        public void CreateFromGameObject_HandlesExistingPrefabsAndLinks()
        {
            // Tests: unlinkIfInstance, allowOverwrite, unique path generation
            string prefabPath = Path.Combine(TempDirectory, "Existing.prefab").Replace('\\', '/');
            GameObject sourceObject = new GameObject("SourceObject");

            try
            {
                // Create initial prefab and link source object
                PrefabUtility.SaveAsPrefabAssetAndConnect(sourceObject, prefabPath, InteractionMode.AutomatedAction);
                Assert.IsTrue(PrefabUtility.IsAnyPrefabInstanceRoot(sourceObject));

                // Without unlink - should fail (already linked)
                string newPath = Path.Combine(TempDirectory, "New.prefab").Replace('\\', '/');
                var failResult = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "create_from_gameobject",
                    ["target"] = sourceObject.name,
                    ["prefabPath"] = newPath
                }));
                Assert.IsFalse(failResult.Value<bool>("success"));
                Assert.IsTrue(failResult.Value<string>("error").Contains("already linked"));

                // With unlinkIfInstance - should succeed
                var unlinkResult = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "create_from_gameobject",
                    ["target"] = sourceObject.name,
                    ["prefabPath"] = newPath,
                    ["unlinkIfInstance"] = true
                }));
                Assert.IsTrue(unlinkResult.Value<bool>("success"));
                Assert.IsTrue(unlinkResult["data"].Value<bool>("wasUnlinked"));

                // With allowOverwrite - should replace
                GameObject anotherObject = new GameObject("AnotherObject");
                var overwriteResult = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "create_from_gameobject",
                    ["target"] = anotherObject.name,
                    ["prefabPath"] = newPath,
                    ["allowOverwrite"] = true
                }));
                Assert.IsTrue(overwriteResult.Value<bool>("success"));
                Assert.IsTrue(overwriteResult["data"].Value<bool>("wasReplaced"));
                UnityEngine.Object.DestroyImmediate(anotherObject, true);

                // Without overwrite on existing - should generate unique path
                GameObject thirdObject = new GameObject("ThirdObject");
                var uniqueResult = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "create_from_gameobject",
                    ["target"] = thirdObject.name,
                    ["prefabPath"] = newPath
                }));
                Assert.IsTrue(uniqueResult.Value<bool>("success"));
                Assert.AreNotEqual(newPath, uniqueResult["data"].Value<string>("prefabPath"));
                SafeDeleteAsset(uniqueResult["data"].Value<string>("prefabPath"));
                UnityEngine.Object.DestroyImmediate(thirdObject, true);
            }
            finally
            {
                SafeDeleteAsset(prefabPath);
                SafeDeleteAsset(Path.Combine(TempDirectory, "New.prefab").Replace('\\', '/'));
                if (sourceObject != null) UnityEngine.Object.DestroyImmediate(sourceObject, true);
            }
        }

        [Test]
        public void CreateFromGameObject_FindsInactiveObject_WhenSearchInactiveIsTrue()
        {
            string prefabPath = Path.Combine(TempDirectory, "InactiveTest.prefab").Replace('\\', '/');
            GameObject inactiveObject = new GameObject("InactiveObject");
            inactiveObject.SetActive(false);

            try
            {
                // Without searchInactive - should fail to find inactive object
                var failResult = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "create_from_gameobject",
                    ["target"] = inactiveObject.name,
                    ["prefabPath"] = prefabPath
                }));
                Assert.IsFalse(failResult.Value<bool>("success"));

                // With searchInactive - should succeed
                var successResult = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "create_from_gameobject",
                    ["target"] = inactiveObject.name,
                    ["prefabPath"] = prefabPath,
                    ["searchInactive"] = true
                }));
                Assert.IsTrue(successResult.Value<bool>("success"));
                Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));
            }
            finally
            {
                SafeDeleteAsset(prefabPath);
                if (inactiveObject != null) UnityEngine.Object.DestroyImmediate(inactiveObject, true);
            }
        }

        #endregion

        #region READ Tests

        [Test]
        public void GetInfo_ReturnsMetadata()
        {
            string prefabPath = CreateTestPrefab("InfoTest");

            try
            {
                var result = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "get_info",
                    ["prefabPath"] = prefabPath
                }));

                Assert.IsTrue(result.Value<bool>("success"));
                var data = result["data"] as JObject;
                Assert.AreEqual(prefabPath, data.Value<string>("assetPath"));
                Assert.IsNotNull(data.Value<string>("guid"));
                Assert.AreEqual("Regular", data.Value<string>("prefabType"));
                Assert.AreEqual("InfoTest", data.Value<string>("rootObjectName"));
            }
            finally
            {
                SafeDeleteAsset(prefabPath);
            }
        }

        [Test]
        public void GetHierarchy_ReturnsHierarchyWithNestingInfo()
        {
            // Create a prefab with nested prefab instance
            string childPrefabPath = CreateTestPrefab("ChildPrefab");
            string containerPath = null;

            try
            {
                GameObject container = new GameObject("Container");
                GameObject child1 = new GameObject("Child1");
                child1.transform.parent = container.transform;

                // Add nested prefab instance
                GameObject nestedInstance = PrefabUtility.InstantiatePrefab(
                    AssetDatabase.LoadAssetAtPath<GameObject>(childPrefabPath)) as GameObject;
                nestedInstance.transform.parent = container.transform;

                containerPath = Path.Combine(TempDirectory, "Container.prefab").Replace('\\', '/');
                PrefabUtility.SaveAsPrefabAsset(container, containerPath, out bool _);
                UnityEngine.Object.DestroyImmediate(container);
                AssetDatabase.Refresh();

                var result = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "get_hierarchy",
                    ["prefabPath"] = containerPath
                }));

                Assert.IsTrue(result.Value<bool>("success"));
                var data = result["data"] as JObject;
                var items = data["items"] as JArray;
                Assert.IsTrue(data.Value<int>("total") >= 3); // Container, Child1, nested prefab

                // Verify root and nested prefab info
                var root = items.Cast<JObject>().FirstOrDefault(j => j["prefab"]["isRoot"].Value<bool>());
                Assert.IsNotNull(root);
                Assert.AreEqual("Container", root.Value<string>("name"));

                var nested = items.Cast<JObject>().FirstOrDefault(j => j["prefab"]["isNestedRoot"].Value<bool>());
                Assert.IsNotNull(nested);
                Assert.AreEqual(1, nested["prefab"]["nestingDepth"].Value<int>());
            }
            finally
            {
                if (containerPath != null) SafeDeleteAsset(containerPath);
                SafeDeleteAsset(childPrefabPath);
            }
        }

        #endregion

        #region UPDATE Tests (ModifyContents)

        [Test]
        public void ModifyContents_ModifiesTransformWithoutOpeningStage()
        {
            string prefabPath = CreateTestPrefab("ModifyTest");

            try
            {
                StageUtility.GoToMainStage();
                Assert.IsNull(PrefabStageUtility.GetCurrentPrefabStage());

                var result = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "modify_contents",
                    ["prefabPath"] = prefabPath,
                    ["position"] = new JArray(1f, 2f, 3f),
                    ["rotation"] = new JArray(45f, 0f, 0f),
                    ["scale"] = new JArray(2f, 2f, 2f)
                }));

                Assert.IsTrue(result.Value<bool>("success"));

                // Verify no stage was opened (headless editing)
                Assert.IsNull(PrefabStageUtility.GetCurrentPrefabStage());

                // Verify changes persisted
                GameObject reloaded = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Assert.AreEqual(new Vector3(1f, 2f, 3f), reloaded.transform.localPosition);
                Assert.AreEqual(new Vector3(2f, 2f, 2f), reloaded.transform.localScale);
            }
            finally
            {
                SafeDeleteAsset(prefabPath);
            }
        }

        [Test]
        public void ModifyContents_TargetsChildrenByNameAndPath()
        {
            string prefabPath = CreateNestedTestPrefab("TargetTest");

            try
            {
                // Target by name
                var nameResult = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "modify_contents",
                    ["prefabPath"] = prefabPath,
                    ["target"] = "Child1",
                    ["position"] = new JArray(10f, 10f, 10f)
                }));
                Assert.IsTrue(nameResult.Value<bool>("success"));

                // Target by path
                var pathResult = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "modify_contents",
                    ["prefabPath"] = prefabPath,
                    ["target"] = "Child1/Grandchild",
                    ["scale"] = new JArray(3f, 3f, 3f)
                }));
                Assert.IsTrue(pathResult.Value<bool>("success"));

                // Verify changes
                GameObject reloaded = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Assert.AreEqual(new Vector3(10f, 10f, 10f), reloaded.transform.Find("Child1").localPosition);
                Assert.AreEqual(new Vector3(3f, 3f, 3f), reloaded.transform.Find("Child1/Grandchild").localScale);
            }
            finally
            {
                SafeDeleteAsset(prefabPath);
            }
        }

        [Test]
        public void ModifyContents_AddsAndRemovesComponents()
        {
            string prefabPath = CreateTestPrefab("ComponentTest");
            // Cube primitive has BoxCollider by default

            try
            {
                // Add Rigidbody
                var addResult = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "modify_contents",
                    ["prefabPath"] = prefabPath,
                    ["componentsToAdd"] = new JArray("Rigidbody")
                }));
                Assert.IsTrue(addResult.Value<bool>("success"));

                // Remove BoxCollider
                var removeResult = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "modify_contents",
                    ["prefabPath"] = prefabPath,
                    ["componentsToRemove"] = new JArray("BoxCollider")
                }));
                Assert.IsTrue(removeResult.Value<bool>("success"));

                // Verify
                GameObject reloaded = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Assert.IsNotNull(reloaded.GetComponent<Rigidbody>());
                Assert.IsNull(reloaded.GetComponent<BoxCollider>());
            }
            finally
            {
                SafeDeleteAsset(prefabPath);
            }
        }

        [Test]
        public void ModifyContents_SetsPropertiesAndRenames()
        {
            string prefabPath = CreateNestedTestPrefab("PropertiesTest");

            try
            {
                var result = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "modify_contents",
                    ["prefabPath"] = prefabPath,
                    ["target"] = "Child1",
                    ["name"] = "RenamedChild",
                    ["tag"] = "MainCamera",
                    ["layer"] = "UI",
                    ["setActive"] = false
                }));

                Assert.IsTrue(result.Value<bool>("success"));

                GameObject reloaded = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Transform renamed = reloaded.transform.Find("RenamedChild");
                Assert.IsNotNull(renamed);
                Assert.IsNull(reloaded.transform.Find("Child1")); // Old name gone
                Assert.AreEqual("MainCamera", renamed.gameObject.tag);
                Assert.AreEqual(LayerMask.NameToLayer("UI"), renamed.gameObject.layer);
                Assert.IsFalse(renamed.gameObject.activeSelf);
            }
            finally
            {
                SafeDeleteAsset(prefabPath);
            }
        }

        [Test]
        public void ModifyContents_WorksOnComplexMultiComponentPrefab()
        {
            // Create a complex prefab: Vehicle with multiple children, each with multiple components
            string prefabPath = CreateComplexTestPrefab("Vehicle");

            try
            {
                // Modify root - add Rigidbody
                var rootResult = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "modify_contents",
                    ["prefabPath"] = prefabPath,
                    ["componentsToAdd"] = new JArray("Rigidbody")
                }));
                Assert.IsTrue(rootResult.Value<bool>("success"));

                // Modify child by name - reposition FrontWheel, add SphereCollider
                var wheelResult = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "modify_contents",
                    ["prefabPath"] = prefabPath,
                    ["target"] = "FrontWheel",
                    ["position"] = new JArray(0f, 0.5f, 2f),
                    ["componentsToAdd"] = new JArray("SphereCollider")
                }));
                Assert.IsTrue(wheelResult.Value<bool>("success"));

                // Modify nested child by path - scale Barrel inside Turret
                var barrelResult = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "modify_contents",
                    ["prefabPath"] = prefabPath,
                    ["target"] = "Turret/Barrel",
                    ["scale"] = new JArray(0.5f, 0.5f, 3f),
                    ["tag"] = "Player"
                }));
                Assert.IsTrue(barrelResult.Value<bool>("success"));

                // Remove component from child
                var removeResult = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "modify_contents",
                    ["prefabPath"] = prefabPath,
                    ["target"] = "BackWheel",
                    ["componentsToRemove"] = new JArray("BoxCollider")
                }));
                Assert.IsTrue(removeResult.Value<bool>("success"));

                // Verify all changes persisted
                GameObject reloaded = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

                // Root has Rigidbody
                Assert.IsNotNull(reloaded.GetComponent<Rigidbody>(), "Root should have Rigidbody");

                // FrontWheel repositioned and has SphereCollider
                Transform frontWheel = reloaded.transform.Find("FrontWheel");
                Assert.AreEqual(new Vector3(0f, 0.5f, 2f), frontWheel.localPosition);
                Assert.IsNotNull(frontWheel.GetComponent<SphereCollider>(), "FrontWheel should have SphereCollider");

                // Turret/Barrel scaled and tagged
                Transform barrel = reloaded.transform.Find("Turret/Barrel");
                Assert.AreEqual(new Vector3(0.5f, 0.5f, 3f), barrel.localScale);
                Assert.AreEqual("Player", barrel.gameObject.tag);

                // BackWheel BoxCollider removed
                Transform backWheel = reloaded.transform.Find("BackWheel");
                Assert.IsNull(backWheel.GetComponent<BoxCollider>(), "BackWheel BoxCollider should be removed");
            }
            finally
            {
                SafeDeleteAsset(prefabPath);
            }
        }

        [Test]
        public void ModifyContents_ReparentsChildWithinPrefab()
        {
            string prefabPath = CreateNestedTestPrefab("ReparentTest");

            try
            {
                // Reparent Child2 under Child1
                var result = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "modify_contents",
                    ["prefabPath"] = prefabPath,
                    ["target"] = "Child2",
                    ["parent"] = "Child1"
                }));

                Assert.IsTrue(result.Value<bool>("success"));

                // Verify Child2 is now under Child1
                GameObject reloaded = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Assert.IsNull(reloaded.transform.Find("Child2"), "Child2 should no longer be direct child of root");
                Assert.IsNotNull(reloaded.transform.Find("Child1/Child2"), "Child2 should now be under Child1");
            }
            finally
            {
                SafeDeleteAsset(prefabPath);
            }
        }

        [Test]
        public void ModifyContents_PreventsHierarchyLoops()
        {
            string prefabPath = CreateNestedTestPrefab("HierarchyLoopTest");

            try
            {
                // Attempt to parent Child1 under its own descendant (Grandchild)
                var result = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "modify_contents",
                    ["prefabPath"] = prefabPath,
                    ["target"] = "Child1",
                    ["parent"] = "Child1/Grandchild"
                }));

                Assert.IsFalse(result.Value<bool>("success"));
                Assert.IsTrue(result.Value<string>("error").Contains("hierarchy loop") ||
                    result.Value<string>("error").Contains("would create"),
                    "Error should mention hierarchy loop prevention");
            }
            finally
            {
                SafeDeleteAsset(prefabPath);
            }
        }

        [Test]
        public void ModifyContents_CreateChild_AddsSingleChildWithPrimitive()
        {
            string prefabPath = CreateTestPrefab("CreateChildTest");

            try
            {
                var result = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "modify_contents",
                    ["prefabPath"] = prefabPath,
                    ["createChild"] = new JObject
                    {
                        ["name"] = "NewSphere",
                        ["primitive_type"] = "Sphere",
                        ["position"] = new JArray(1f, 2f, 3f),
                        ["scale"] = new JArray(0.5f, 0.5f, 0.5f)
                    }
                }));

                Assert.IsTrue(result.Value<bool>("success"));

                GameObject reloaded = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Transform child = reloaded.transform.Find("NewSphere");
                Assert.IsNotNull(child, "Child should exist");
                Assert.AreEqual(new Vector3(1f, 2f, 3f), child.localPosition);
                Assert.AreEqual(new Vector3(0.5f, 0.5f, 0.5f), child.localScale);
                Assert.IsNotNull(child.GetComponent<SphereCollider>(), "Sphere primitive should have SphereCollider");
            }
            finally
            {
                SafeDeleteAsset(prefabPath);
            }
        }

        [Test]
        public void ModifyContents_CreateChild_AddsEmptyGameObject()
        {
            string prefabPath = CreateTestPrefab("EmptyChildTest");

            try
            {
                var result = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "modify_contents",
                    ["prefabPath"] = prefabPath,
                    ["createChild"] = new JObject
                    {
                        ["name"] = "EmptyChild",
                        ["position"] = new JArray(0f, 5f, 0f)
                    }
                }));

                Assert.IsTrue(result.Value<bool>("success"));

                GameObject reloaded = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Transform child = reloaded.transform.Find("EmptyChild");
                Assert.IsNotNull(child, "Empty child should exist");
                Assert.AreEqual(new Vector3(0f, 5f, 0f), child.localPosition);
                // Empty GO should only have Transform
                Assert.AreEqual(1, child.GetComponents<Component>().Length, "Empty child should only have Transform");
            }
            finally
            {
                SafeDeleteAsset(prefabPath);
            }
        }

        [Test]
        public void ModifyContents_CreateChild_AddsMultipleChildrenFromArray()
        {
            string prefabPath = CreateTestPrefab("MultiChildTest");

            try
            {
                var result = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "modify_contents",
                    ["prefabPath"] = prefabPath,
                    ["createChild"] = new JArray
                    {
                        new JObject { ["name"] = "Child1", ["primitive_type"] = "Cube", ["position"] = new JArray(1f, 0f, 0f) },
                        new JObject { ["name"] = "Child2", ["primitive_type"] = "Sphere", ["position"] = new JArray(-1f, 0f, 0f) },
                        new JObject { ["name"] = "Child3", ["position"] = new JArray(0f, 1f, 0f) }
                    }
                }));

                Assert.IsTrue(result.Value<bool>("success"));

                GameObject reloaded = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Assert.IsNotNull(reloaded.transform.Find("Child1"), "Child1 should exist");
                Assert.IsNotNull(reloaded.transform.Find("Child2"), "Child2 should exist");
                Assert.IsNotNull(reloaded.transform.Find("Child3"), "Child3 should exist");
                Assert.IsNotNull(reloaded.transform.Find("Child1").GetComponent<BoxCollider>(), "Child1 should be Cube");
                Assert.IsNotNull(reloaded.transform.Find("Child2").GetComponent<SphereCollider>(), "Child2 should be Sphere");
            }
            finally
            {
                SafeDeleteAsset(prefabPath);
            }
        }

        [Test]
        public void ModifyContents_CreateChild_SupportsNestedParenting()
        {
            string prefabPath = CreateNestedTestPrefab("NestedCreateChildTest");

            try
            {
                var result = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "modify_contents",
                    ["prefabPath"] = prefabPath,
                    ["createChild"] = new JObject
                    {
                        ["name"] = "NewGrandchild",
                        ["parent"] = "Child1",
                        ["primitive_type"] = "Capsule"
                    }
                }));

                Assert.IsTrue(result.Value<bool>("success"));

                GameObject reloaded = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Transform newChild = reloaded.transform.Find("Child1/NewGrandchild");
                Assert.IsNotNull(newChild, "NewGrandchild should be under Child1");
                Assert.IsNotNull(newChild.GetComponent<CapsuleCollider>(), "Should be Capsule primitive");
            }
            finally
            {
                SafeDeleteAsset(prefabPath);
            }
        }

        [Test]
        public void ModifyContents_CreateChild_ReturnsErrorForInvalidInput()
        {
            string prefabPath = CreateTestPrefab("InvalidChildTest");

            try
            {
                // Missing required 'name' field
                var missingName = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "modify_contents",
                    ["prefabPath"] = prefabPath,
                    ["createChild"] = new JObject
                    {
                        ["primitive_type"] = "Cube"
                    }
                }));
                Assert.IsFalse(missingName.Value<bool>("success"));
                Assert.IsTrue(missingName.Value<string>("error").Contains("name"));

                // Invalid parent
                var invalidParent = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "modify_contents",
                    ["prefabPath"] = prefabPath,
                    ["createChild"] = new JObject
                    {
                        ["name"] = "TestChild",
                        ["parent"] = "NonexistentParent"
                    }
                }));
                Assert.IsFalse(invalidParent.Value<bool>("success"));
                Assert.IsTrue(invalidParent.Value<string>("error").Contains("not found"));

                // Invalid primitive type
                var invalidPrimitive = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "modify_contents",
                    ["prefabPath"] = prefabPath,
                    ["createChild"] = new JObject
                    {
                        ["name"] = "TestChild",
                        ["primitive_type"] = "InvalidType"
                    }
                }));
                Assert.IsFalse(invalidPrimitive.Value<bool>("success"));
                Assert.IsTrue(invalidPrimitive.Value<string>("error").Contains("Invalid primitive type"));
            }
            finally
            {
                SafeDeleteAsset(prefabPath);
            }
        }

        #endregion

        #region Error Handling

        [Test]
        public void HandleCommand_ValidatesParameters()
        {
            // Null params
            var nullResult = ToJObject(ManagePrefabs.HandleCommand(null));
            Assert.IsFalse(nullResult.Value<bool>("success"));
            Assert.IsTrue(nullResult.Value<string>("error").Contains("null"));

            // Missing action
            var missingAction = ToJObject(ManagePrefabs.HandleCommand(new JObject()));
            Assert.IsFalse(missingAction.Value<bool>("success"));
            Assert.IsTrue(missingAction.Value<string>("error").Contains("Action parameter is required"));

            // Unknown action
            var unknownAction = ToJObject(ManagePrefabs.HandleCommand(new JObject { ["action"] = "invalid" }));
            Assert.IsFalse(unknownAction.Value<bool>("success"));
            Assert.IsTrue(unknownAction.Value<string>("error").Contains("Unknown action"));

            // Path traversal
            GameObject testObj = new GameObject("Test");
            var traversal = ToJObject(ManagePrefabs.HandleCommand(new JObject
            {
                ["action"] = "create_from_gameobject",
                ["target"] = "Test",
                ["prefabPath"] = "../../etc/passwd"
            }));
            Assert.IsFalse(traversal.Value<bool>("success"));
            Assert.IsTrue(traversal.Value<string>("error").Contains("path traversal") ||
                traversal.Value<string>("error").Contains("Invalid"));
            UnityEngine.Object.DestroyImmediate(testObj, true);
        }

        [Test]
        public void ModifyContents_ReturnsErrorsForInvalidInputs()
        {
            string prefabPath = CreateTestPrefab("ErrorTest");

            try
            {
                // Invalid target
                var invalidTarget = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "modify_contents",
                    ["prefabPath"] = prefabPath,
                    ["target"] = "NonexistentChild"
                }));
                Assert.IsFalse(invalidTarget.Value<bool>("success"));
                Assert.IsTrue(invalidTarget.Value<string>("error").Contains("not found"));

                // Invalid path
                LogAssert.Expect(LogType.Error, new Regex(".*modify_contents.*does not exist.*"));
                var invalidPath = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "modify_contents",
                    ["prefabPath"] = "Assets/Nonexistent.prefab"
                }));
                Assert.IsFalse(invalidPath.Value<bool>("success"));
            }
            finally
            {
                SafeDeleteAsset(prefabPath);
            }
        }

        #endregion

        #region Test Helpers

        private static string CreateTestPrefab(string name)
        {
            EnsureFolder(TempDirectory);
            GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            temp.name = name;

            string path = Path.Combine(TempDirectory, name + ".prefab").Replace('\\', '/');
            PrefabUtility.SaveAsPrefabAsset(temp, path, out bool success);
            UnityEngine.Object.DestroyImmediate(temp);
            AssetDatabase.Refresh();

            if (!success) throw new Exception($"Failed to create test prefab at {path}");
            return path;
        }

        private static string CreateNestedTestPrefab(string name)
        {
            EnsureFolder(TempDirectory);
            GameObject root = new GameObject(name);
            GameObject child1 = new GameObject("Child1") { transform = { parent = root.transform } };
            GameObject child2 = new GameObject("Child2") { transform = { parent = root.transform } };
            GameObject grandchild = new GameObject("Grandchild") { transform = { parent = child1.transform } };

            string path = Path.Combine(TempDirectory, name + ".prefab").Replace('\\', '/');
            PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
            UnityEngine.Object.DestroyImmediate(root);
            AssetDatabase.Refresh();

            if (!success) throw new Exception($"Failed to create nested test prefab at {path}");
            return path;
        }

        private static string CreateComplexTestPrefab(string name)
        {
            // Creates: Vehicle (root with BoxCollider)
            //   - FrontWheel (Cube with MeshRenderer, BoxCollider)
            //   - BackWheel (Cube with MeshRenderer, BoxCollider)
            //   - Turret (empty)
            //       - Barrel (Cylinder with MeshRenderer, CapsuleCollider)
            EnsureFolder(TempDirectory);

            GameObject root = new GameObject(name);
            root.AddComponent<BoxCollider>();

            GameObject frontWheel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            frontWheel.name = "FrontWheel";
            frontWheel.transform.parent = root.transform;
            frontWheel.transform.localPosition = new Vector3(0, 0.5f, 1f);

            GameObject backWheel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            backWheel.name = "BackWheel";
            backWheel.transform.parent = root.transform;
            backWheel.transform.localPosition = new Vector3(0, 0.5f, -1f);

            GameObject turret = new GameObject("Turret");
            turret.transform.parent = root.transform;
            turret.transform.localPosition = new Vector3(0, 1f, 0);

            GameObject barrel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            barrel.name = "Barrel";
            barrel.transform.parent = turret.transform;
            barrel.transform.localPosition = new Vector3(0, 0, 1f);

            string path = Path.Combine(TempDirectory, name + ".prefab").Replace('\\', '/');
            PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
            UnityEngine.Object.DestroyImmediate(root);
            AssetDatabase.Refresh();

            if (!success) throw new Exception($"Failed to create complex test prefab at {path}");
            return path;
        }

        #endregion
    }
}
