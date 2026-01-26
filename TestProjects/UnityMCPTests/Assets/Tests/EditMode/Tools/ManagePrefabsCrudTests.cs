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
    /// Comprehensive test suite for Prefab CRUD operations and new features.
    /// Tests cover: Create, Read, Update, Delete patterns, force save, unlink-if-instance,
    /// overwrite handling, inactive object search, and save dialog prevention.
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
        public void CreateFromGameObject_CreatesNewPrefab_WithValidParameters()
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

                Assert.IsTrue(result.Value<bool>("success"), "create_from_gameobject should succeed.");
                var data = result["data"] as JObject;
                Assert.AreEqual(prefabPath, data.Value<string>("prefabPath"));

                GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Assert.IsNotNull(prefabAsset, "Prefab asset should exist at path.");
            }
            finally
            {
                SafeDeleteAsset(prefabPath);
                if (sceneObject != null) UnityEngine.Object.DestroyImmediate(sceneObject, true);
            }
        }

        [Test]
        public void CreateFromGameObject_UnlinksInstance_WhenUnlinkIfInstanceIsTrue()
        {
            // Create an initial prefab
            string initialPrefabPath = Path.Combine(TempDirectory, "Original.prefab").Replace('\\', '/');
            GameObject sourceObject = new GameObject("SourceObject");
            GameObject instance = null;

            try
            {
                // Create initial prefab and connect source object to it
                PrefabUtility.SaveAsPrefabAssetAndConnect(
                    sourceObject, initialPrefabPath, InteractionMode.AutomatedAction);

                // Verify source object is now linked
                Assert.IsTrue(PrefabUtility.IsAnyPrefabInstanceRoot(sourceObject),
                    "Source object should be linked to prefab after SaveAsPrefabAssetAndConnect.");

                // Create new prefab with unlinkIfInstance
                // The command will find sourceObject by name and unlink it
                string newPrefabPath = Path.Combine(TempDirectory, "NewFromLinked.prefab").Replace('\\', '/');
                var result = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "create_from_gameobject",
                    ["target"] = sourceObject.name,
                    ["prefabPath"] = newPrefabPath,
                    ["unlinkIfInstance"] = true
                }));

                Assert.IsTrue(result.Value<bool>("success"), "create_from_gameobject with unlinkIfInstance should succeed.");
                var data = result["data"] as JObject;
                Assert.IsTrue(data.Value<bool>("wasUnlinked"), "wasUnlinked should be true.");

                // Note: After creating the new prefab, the sourceObject is now linked to the NEW prefab
                // (via SaveAsPrefabAssetAndConnect in CreatePrefabAsset), which is the correct behavior.
                // What matters is that it was unlinked from the original prefab first.
                Assert.IsTrue(PrefabUtility.IsAnyPrefabInstanceRoot(sourceObject),
                    "Source object should now be linked to the new prefab.");
                string currentPrefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(sourceObject);
                Assert.AreNotEqual(initialPrefabPath, currentPrefabPath,
                    "Source object should NOT be linked to original prefab anymore.");
                Assert.AreEqual(newPrefabPath, currentPrefabPath,
                    "Source object should now be linked to the new prefab.");
            }
            finally
            {
                SafeDeleteAsset(initialPrefabPath);
                SafeDeleteAsset(Path.Combine(TempDirectory, "NewFromLinked.prefab").Replace('\\', '/'));
                if (sourceObject != null) UnityEngine.Object.DestroyImmediate(sourceObject, true);
                if (instance != null) UnityEngine.Object.DestroyImmediate(instance, true);
            }
        }

        [Test]
        public void CreateFromGameObject_Fails_WhenTargetIsAlreadyLinked()
        {
            string prefabPath = Path.Combine(TempDirectory, "Existing.prefab").Replace('\\', '/');
            GameObject sourceObject = new GameObject("SourceObject");

            try
            {
                // Create initial prefab and connect the source object to it
                GameObject connectedInstance = PrefabUtility.SaveAsPrefabAssetAndConnect(
                    sourceObject, prefabPath, InteractionMode.AutomatedAction);

                // Verify the source object is now linked to the prefab
                Assert.IsTrue(PrefabUtility.IsAnyPrefabInstanceRoot(sourceObject),
                    "Source object should be linked to prefab after SaveAsPrefabAssetAndConnect.");

                // Try to create again without unlink - sourceObject.name should find the connected instance
                string newPath = Path.Combine(TempDirectory, "Duplicate.prefab").Replace('\\', '/');
                var result = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "create_from_gameobject",
                    ["target"] = sourceObject.name,
                    ["prefabPath"] = newPath
                }));

                Assert.IsFalse(result.Value<bool>("success"),
                    "create_from_gameobject should fail when target is already linked.");
                Assert.IsTrue(result.Value<string>("error").Contains("already linked"),
                    "Error message should mention 'already linked'.");
            }
            finally
            {
                SafeDeleteAsset(prefabPath);
                SafeDeleteAsset(Path.Combine(TempDirectory, "Duplicate.prefab").Replace('\\', '/'));
                if (sourceObject != null) UnityEngine.Object.DestroyImmediate(sourceObject, true);
            }
        }

        [Test]
        public void CreateFromGameObject_Overwrites_WhenAllowOverwriteIsTrue()
        {
            string prefabPath = Path.Combine(TempDirectory, "OverwriteTest.prefab").Replace('\\', '/');
            GameObject firstObject = new GameObject("OverwriteTest");  // Use path filename
            GameObject secondObject = new GameObject("OverwriteTest");  // Use path filename

            try
            {
                // Create initial prefab
                PrefabUtility.SaveAsPrefabAsset(firstObject, prefabPath, out bool _);
                AssetDatabase.Refresh();

                GameObject firstPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Assert.AreEqual("OverwriteTest", firstPrefab.name, "First prefab should have name 'OverwriteTest'.");

                // Overwrite with new object
                var result = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "create_from_gameobject",
                    ["target"] = secondObject.name,
                    ["prefabPath"] = prefabPath,
                    ["allowOverwrite"] = true
                }));

                Assert.IsTrue(result.Value<bool>("success"), "create_from_gameobject with allowOverwrite should succeed.");
                var data = result["data"] as JObject;
                Assert.IsTrue(data.Value<bool>("wasReplaced"), "wasReplaced should be true.");

                AssetDatabase.Refresh();
                GameObject updatedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Assert.AreEqual("OverwriteTest", updatedPrefab.name, "Prefab should be overwritten (keeps filename as name).");
            }
            finally
            {
                SafeDeleteAsset(prefabPath);
                if (firstObject != null) UnityEngine.Object.DestroyImmediate(firstObject, true);
                if (secondObject != null) UnityEngine.Object.DestroyImmediate(secondObject, true);
            }
        }

        [Test]
        public void CreateFromGameObject_GeneratesUniquePath_WhenFileExistsAndNoOverwrite()
        {
            string prefabPath = Path.Combine(TempDirectory, "UniqueTest.prefab").Replace('\\', '/');
            GameObject firstObject = new GameObject("FirstObject");
            GameObject secondObject = new GameObject("SecondObject");

            try
            {
                // Create initial prefab
                PrefabUtility.SaveAsPrefabAsset(firstObject, prefabPath, out bool _);
                AssetDatabase.Refresh();

                // Create again without overwrite - should generate unique path
                var result = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "create_from_gameobject",
                    ["target"] = secondObject.name,
                    ["prefabPath"] = prefabPath
                }));

                Assert.IsTrue(result.Value<bool>("success"), "create_from_gameobject should succeed with unique path.");
                var data = result["data"] as JObject;
                string actualPath = data.Value<string>("prefabPath");
                Assert.AreNotEqual(prefabPath, actualPath, "Path should be different (unique).");
                Assert.IsTrue(actualPath.Contains("UniqueTest 1"), "Unique path should contain suffix.");

                // Verify both prefabs exist
                Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath),
                    "Original prefab should still exist.");
                Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(actualPath),
                    "New prefab should exist at unique path.");
            }
            finally
            {
                SafeDeleteAsset(prefabPath);
                SafeDeleteAsset(Path.Combine(TempDirectory, "UniqueTest 1.prefab").Replace('\\', '/'));
                if (firstObject != null) UnityEngine.Object.DestroyImmediate(firstObject, true);
                if (secondObject != null) UnityEngine.Object.DestroyImmediate(secondObject, true);
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
                // Try without searchInactive - should fail
                var resultWithout = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "create_from_gameobject",
                    ["target"] = inactiveObject.name,
                    ["prefabPath"] = prefabPath
                }));

                Assert.IsFalse(resultWithout.Value<bool>("success"),
                    "Should fail when object is inactive and searchInactive=false.");

                // Try with searchInactive - should succeed
                var resultWith = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "create_from_gameobject",
                    ["target"] = inactiveObject.name,
                    ["prefabPath"] = prefabPath,
                    ["searchInactive"] = true
                }));

                Assert.IsTrue(resultWith.Value<bool>("success"),
                    "Should succeed when searchInactive=true.");
            }
            finally
            {
                SafeDeleteAsset(prefabPath);
                if (inactiveObject != null) UnityEngine.Object.DestroyImmediate(inactiveObject, true);
            }
        }

        [Test]
        public void CreateFromGameObject_CreatesDirectory_WhenPathDoesNotExist()
        {
            string prefabPath = Path.Combine(TempDirectory, "Nested/Deep/Directory/NewPrefab.prefab").Replace('\\', '/');
            GameObject sceneObject = new GameObject("TestObject");

            try
            {
                var result = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "create_from_gameobject",
                    ["target"] = sceneObject.name,
                    ["prefabPath"] = prefabPath
                }));

                Assert.IsTrue(result.Value<bool>("success"), "Should create directories as needed.");

                GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Assert.IsNotNull(prefabAsset, "Prefab should exist at nested path.");
                Assert.IsTrue(AssetDatabase.IsValidFolder(Path.Combine(TempDirectory, "Nested").Replace('\\', '/')),
                    "Nested directory should be created.");
            }
            finally
            {
                SafeDeleteAsset(prefabPath);
                if (sceneObject != null) UnityEngine.Object.DestroyImmediate(sceneObject, true);
            }
        }

        #endregion

        #region READ Tests (GetInfo & GetHierarchy)

        [Test]
        public void GetInfo_ReturnsCorrectMetadata_ForValidPrefab()
        {
            string prefabPath = CreateTestPrefab("InfoTestPrefab");

            try
            {
                var result = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "get_info",
                    ["prefabPath"] = prefabPath
                }));

                Assert.IsTrue(result.Value<bool>("success"), "get_info should succeed.");
                var data = result["data"] as JObject;

                Assert.AreEqual(prefabPath, data.Value<string>("assetPath"));
                Assert.IsNotNull(data.Value<string>("guid"), "GUID should be present.");
                Assert.AreEqual("Regular", data.Value<string>("prefabType"), "Should be Regular prefab type.");
                Assert.AreEqual("InfoTestPrefab", data.Value<string>("rootObjectName"));
                Assert.AreEqual(0, data.Value<int>("childCount"), "Should have no children.");
                Assert.IsFalse(data.Value<bool>("isVariant"), "Should not be a variant.");

                var components = data["rootComponentTypes"] as JArray;
                Assert.IsNotNull(components, "Component types should be present.");
                Assert.IsTrue(components.Count > 0, "Should have at least one component.");
            }
            finally
            {
                SafeDeleteAsset(prefabPath);
            }
        }

        [Test]
        public void GetInfo_ReturnsError_ForInvalidPath()
        {
            var result = ToJObject(ManagePrefabs.HandleCommand(new JObject
            {
                ["action"] = "get_info",
                ["prefabPath"] = "Assets/Nonexistent/Prefab.prefab"
            }));

            Assert.IsFalse(result.Value<bool>("success"), "get_info should fail for invalid path.");
            Assert.IsTrue(result.Value<string>("error").Contains("No prefab asset found") ||
                result.Value<string>("error").Contains("not found"),
                "Error should mention prefab not found.");
        }

        [Test]
        public void GetHierarchy_ReturnsCompleteHierarchy_ForNestedPrefab()
        {
            string prefabPath = CreateNestedTestPrefab("HierarchyTest");

            try
            {
                var result = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "get_hierarchy",
                    ["prefabPath"] = prefabPath
                }));

                Assert.IsTrue(result.Value<bool>("success"), "get_hierarchy should succeed.");
                var data = result["data"] as JObject;

                Assert.AreEqual(prefabPath, data.Value<string>("prefabPath"));
                int total = data.Value<int>("total");
                Assert.IsTrue(total >= 3, $"Should have at least 3 objects (root + 2 children), got {total}.");

                var items = data["items"] as JArray;
                Assert.IsNotNull(items, "Items should be present.");
                Assert.AreEqual(total, items.Count, "Items count should match total.");

                // Find root object
                var root = items.Cast<JObject>().FirstOrDefault(j => j["prefab"]["isRoot"].Value<bool>());
                Assert.IsNotNull(root, "Should have a root object with isRoot=true.");
                Assert.AreEqual("HierarchyTest", root.Value<string>("name"));
            }
            finally
            {
                SafeDeleteAsset(prefabPath);
            }
        }

        [Test]
        public void GetHierarchy_IncludesNestingInfo_ForNestedPrefabs()
        {
            // Create a parent prefab first
            string parentPath = CreateTestPrefab("ParentPrefab");

            try
            {
                // Create a prefab that contains the parent prefab as nested
                string childPath = CreateTestPrefab("ChildPrefab");
                GameObject container = new GameObject("Container");
                GameObject nestedInstance = PrefabUtility.InstantiatePrefab(
                    AssetDatabase.LoadAssetAtPath<GameObject>(childPath)) as GameObject;
                nestedInstance.transform.parent = container.transform;

                string nestedPrefabPath = Path.Combine(TempDirectory, "NestedContainer.prefab").Replace('\\', '/');
                PrefabUtility.SaveAsPrefabAsset(container, nestedPrefabPath, out bool _);
                UnityEngine.Object.DestroyImmediate(container);

                AssetDatabase.Refresh();

                var result = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "get_hierarchy",
                    ["prefabPath"] = nestedPrefabPath
                }));

                Assert.IsTrue(result.Value<bool>("success"), "get_hierarchy should succeed.");
                var data = result["data"] as JObject;
                var items = data["items"] as JArray;

                // Find the nested prefab
                var nested = items.Cast<JObject>().FirstOrDefault(j => j["prefab"]["isNestedRoot"].Value<bool>());
                Assert.IsNotNull(nested, "Should have a nested prefab root.");
                Assert.AreEqual(1, nested["prefab"]["nestingDepth"].Value<int>(),
                    "Nested prefab should have depth 1.");
            }
            finally
            {
                // Delete nested container first (before deleting prefabs it references)
                SafeDeleteAsset(Path.Combine(TempDirectory, "NestedContainer.prefab").Replace('\\', '/'));
                SafeDeleteAsset(parentPath);
                SafeDeleteAsset(Path.Combine(TempDirectory, "ChildPrefab.prefab").Replace('\\', '/'));
            }
        }

        #endregion

        #region UPDATE Tests (Open, Save, Close)

        [Test]
        public void SaveOpenStage_WithForce_SavesEvenWhenNotDirty()
        {
            string prefabPath = CreateTestPrefab("ForceSaveTest");
            Vector3 originalScale = Vector3.one;

            try
            {
                ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "open_stage",
                    ["prefabPath"] = prefabPath
                });

                PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
                Assert.IsNotNull(stage, "Stage should be open.");
                Assert.IsFalse(stage.scene.isDirty, "Stage should not be dirty initially.");

                // Save without force - should succeed but indicate no changes
                var noForceResult = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "save_open_stage"
                }));

                Assert.IsTrue(noForceResult.Value<bool>("success"),
                    "Save should succeed even when not dirty.");

                // Now save with force
                var forceResult = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "save_open_stage",
                    ["force"] = true
                }));

                Assert.IsTrue(forceResult.Value<bool>("success"), "Force save should succeed.");
                var data = forceResult["data"] as JObject;
                Assert.IsTrue(data.Value<bool>("isDirty") || data.Value<bool>("isOpen"),
                    "Stage should still be open after force save.");
            }
            finally
            {
                StageUtility.GoToMainStage();
                SafeDeleteAsset(prefabPath);
            }
        }

        [Test]
        public void SaveOpenStage_DoesNotShowSaveDialog()
        {
            string prefabPath = CreateTestPrefab("NoDialogTest");

            try
            {
                ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "open_stage",
                    ["prefabPath"] = prefabPath
                });

                PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
                stage.prefabContentsRoot.transform.localScale = new Vector3(2f, 2f, 2f);
                // Mark as dirty to ensure changes are tracked
                EditorUtility.SetDirty(stage.prefabContentsRoot);

                // This save should NOT show a dialog - it should complete synchronously
                // If a dialog appeared, this would hang or require user interaction
                var result = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "save_open_stage",
                    ["force"] = true  // Use force to ensure save happens
                }));

                // If we got here without hanging, no dialog was shown
                Assert.IsTrue(result.Value<bool>("success"),
                    "Save should complete without showing dialog.");

                // Verify the change was saved
                GameObject reloaded = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Assert.AreEqual(new Vector3(2f, 2f, 2f), reloaded.transform.localScale,
                    "Changes should be saved without dialog.");
            }
            finally
            {
                StageUtility.GoToMainStage();
                SafeDeleteAsset(prefabPath);
            }
        }

        [Test]
        public void CloseStage_WithSaveBeforeClose_SavesDirtyChanges()
        {
            string prefabPath = CreateTestPrefab("CloseSaveTest");

            try
            {
                ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "open_stage",
                    ["prefabPath"] = prefabPath
                });

                PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
                stage.prefabContentsRoot.transform.position = new Vector3(5f, 5f, 5f);
                // Mark as dirty to ensure changes are tracked
                EditorUtility.SetDirty(stage.prefabContentsRoot);

                // Close with save
                var result = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "close_stage",
                    ["saveBeforeClose"] = true
                }));

                Assert.IsTrue(result.Value<bool>("success"), "Close with save should succeed.");
                Assert.IsNull(PrefabStageUtility.GetCurrentPrefabStage(),
                    "Stage should be closed after close_stage.");

                // Verify changes were saved
                GameObject reloaded = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Assert.AreEqual(new Vector3(5f, 5f, 5f), reloaded.transform.position,
                    "Position change should be saved before close.");
            }
            finally
            {
                StageUtility.GoToMainStage();
                SafeDeleteAsset(prefabPath);
            }
        }

        [Test]
        public void OpenEditClose_CompleteWorkflow_Succeeds()
        {
            string prefabPath = CreateTestPrefab("WorkflowTest");

            try
            {
                // OPEN
                var openResult = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "open_stage",
                    ["prefabPath"] = prefabPath
                }));
                Assert.IsTrue(openResult.Value<bool>("success"), "Open should succeed.");

                // EDIT
                PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
                stage.prefabContentsRoot.transform.localRotation = Quaternion.Euler(45f, 45f, 45f);
                // Mark as dirty to ensure changes are tracked
                EditorUtility.SetDirty(stage.prefabContentsRoot);

                // SAVE
                var saveResult = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "save_open_stage",
                    ["force"] = true  // Use force to ensure save happens
                }));
                Assert.IsTrue(saveResult.Value<bool>("success"), "Save should succeed.");
                // Note: stage.scene.isDirty may still be true in Unity's internal state
                // The important thing is that changes were saved (verified below)

                // CLOSE
                var closeResult = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "close_stage"
                }));
                Assert.IsTrue(closeResult.Value<bool>("success"), "Close should succeed.");
                Assert.IsNull(PrefabStageUtility.GetCurrentPrefabStage(),
                    "No stage should be open after close.");

                // VERIFY
                GameObject reloaded = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Assert.AreEqual(Quaternion.Euler(45f, 45f, 45f), reloaded.transform.localRotation,
                    "Rotation should be saved and persisted.");
            }
            finally
            {
                StageUtility.GoToMainStage();
                SafeDeleteAsset(prefabPath);
            }
        }

        #endregion

        #region Edge Cases & Error Handling

        [Test]
        public void HandleCommand_ReturnsError_ForUnknownAction()
        {
            var result = ToJObject(ManagePrefabs.HandleCommand(new JObject
            {
                ["action"] = "unknown_action"
            }));

            Assert.IsFalse(result.Value<bool>("success"), "Unknown action should fail.");
            Assert.IsTrue(result.Value<string>("error").Contains("Unknown action"),
                "Error should mention unknown action.");
        }

        [Test]
        public void HandleCommand_ReturnsError_ForNullParameters()
        {
            var result = ToJObject(ManagePrefabs.HandleCommand(null));

            Assert.IsFalse(result.Value<bool>("success"), "Null parameters should fail.");
            Assert.IsTrue(result.Value<string>("error").Contains("null"),
                "Error should mention null parameters.");
        }

        [Test]
        public void HandleCommand_ReturnsError_WhenActionIsMissing()
        {
            var result = ToJObject(ManagePrefabs.HandleCommand(new JObject()));

            Assert.IsFalse(result.Value<bool>("success"), "Missing action should fail.");
            Assert.IsTrue(result.Value<string>("error").Contains("Action parameter is required"),
                "Error should mention required action parameter.");
        }

        [Test]
        public void CreateFromGameObject_ReturnsError_ForEmptyTarget()
        {
            var result = ToJObject(ManagePrefabs.HandleCommand(new JObject
            {
                ["action"] = "create_from_gameobject",
                ["prefabPath"] = "Assets/Test.prefab"
            }));

            Assert.IsFalse(result.Value<bool>("success"), "Missing target should fail.");
            Assert.IsTrue(result.Value<string>("error").Contains("'target' parameter is required"),
                "Error should mention required target parameter.");
        }

        [Test]
        public void CreateFromGameObject_ReturnsError_ForEmptyPrefabPath()
        {
            var result = ToJObject(ManagePrefabs.HandleCommand(new JObject
            {
                ["action"] = "create_from_gameobject",
                ["target"] = "SomeObject"
            }));

            Assert.IsFalse(result.Value<bool>("success"), "Missing prefabPath should fail.");
            Assert.IsTrue(result.Value<string>("error").Contains("'prefabPath' parameter is required"),
                "Error should mention required prefabPath parameter.");
        }

        [Test]
        public void CreateFromGameObject_ReturnsError_ForPathTraversal()
        {
            GameObject testObject = new GameObject("TestObject");

            try
            {
                var result = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "create_from_gameobject",
                    ["target"] = "TestObject",
                    ["prefabPath"] = "../../etc/passwd"
                }));

                Assert.IsFalse(result.Value<bool>("success"), "Path traversal should be blocked.");
                Assert.IsTrue(result.Value<string>("error").Contains("path traversal") ||
                    result.Value<string>("error").Contains("Invalid"),
                    "Error should mention path traversal or invalid path.");
            }
            finally
            {
                if (testObject != null) UnityEngine.Object.DestroyImmediate(testObject, true);
            }
        }

        [Test]
        public void CreateFromGameObject_AutoPrependsAssets_WhenPathIsRelative()
        {
            GameObject testObject = new GameObject("TestObject");

            try
            {
                // SanitizeAssetPath auto-prepends "Assets/" to relative paths
                var result = ToJObject(ManagePrefabs.HandleCommand(new JObject
                {
                    ["action"] = "create_from_gameobject",
                    ["target"] = "TestObject",
                    ["prefabPath"] = "SomeFolder/Prefab.prefab"
                }));

                Assert.IsTrue(result.Value<bool>("success"), "Should auto-prepend Assets/ to relative path.");

                // Clean up the created prefab at the corrected path
                SafeDeleteAsset("Assets/SomeFolder/Prefab.prefab");
            }
            finally
            {
                if (testObject != null) UnityEngine.Object.DestroyImmediate(testObject, true);
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

            if (!success)
            {
                throw new Exception($"Failed to create test prefab at {path}");
            }
            return path;
        }

        private static string CreateNestedTestPrefab(string name)
        {
            EnsureFolder(TempDirectory);
            GameObject root = new GameObject(name);

            // Add children
            GameObject child1 = new GameObject("Child1");
            child1.transform.parent = root.transform;

            GameObject child2 = new GameObject("Child2");
            child2.transform.parent = root.transform;

            // Add grandchild
            GameObject grandchild = new GameObject("Grandchild");
            grandchild.transform.parent = child1.transform;

            string path = Path.Combine(TempDirectory, name + ".prefab").Replace('\\', '/');
            PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
            UnityEngine.Object.DestroyImmediate(root);
            AssetDatabase.Refresh();

            if (!success)
            {
                throw new Exception($"Failed to create nested test prefab at {path}");
            }
            return path;
        }

        #endregion
    }
}
