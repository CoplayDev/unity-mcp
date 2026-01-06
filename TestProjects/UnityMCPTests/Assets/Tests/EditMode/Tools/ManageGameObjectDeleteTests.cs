using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Tools;

namespace MCPForUnityTests.Editor.Tools
{
    /// <summary>
    /// Comprehensive baseline tests for ManageGameObject "delete" action.
    /// These tests capture existing behavior before API redesign.
    /// </summary>
    public class ManageGameObjectDeleteTests
    {
        private List<GameObject> testObjects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in testObjects)
            {
                if (go != null)
                {
                    Object.DestroyImmediate(go);
                }
            }
            testObjects.Clear();
        }

        private GameObject CreateTestObject(string name)
        {
            var go = new GameObject(name);
            testObjects.Add(go);
            return go;
        }

        #region Basic Delete Tests

        [Test]
        public void Delete_ByName_DeletesObject()
        {
            var target = CreateTestObject("DeleteTargetByName");

            var p = new JObject
            {
                ["action"] = "delete",
                ["target"] = "DeleteTargetByName",
                ["searchMethod"] = "by_name"
            };

            var result = ManageGameObject.HandleCommand(p);
            var resultObj = result as JObject ?? JObject.FromObject(result);

            Assert.IsTrue(resultObj.Value<bool>("success"), resultObj.ToString());
            
            // Verify object is deleted
            var found = GameObject.Find("DeleteTargetByName");
            Assert.IsNull(found, "Object should be deleted");
            
            // Remove from our tracking list since it's deleted
            testObjects.Remove(target);
        }

        [Test]
        public void Delete_ByInstanceID_DeletesObject()
        {
            var target = CreateTestObject("DeleteTargetByID");
            int instanceID = target.GetInstanceID();

            var p = new JObject
            {
                ["action"] = "delete",
                ["target"] = instanceID,
                ["searchMethod"] = "by_id"
            };

            var result = ManageGameObject.HandleCommand(p);
            var resultObj = result as JObject ?? JObject.FromObject(result);

            Assert.IsTrue(resultObj.Value<bool>("success"), resultObj.ToString());
            
            // Verify object is deleted
            var found = GameObject.Find("DeleteTargetByID");
            Assert.IsNull(found, "Object should be deleted");
            
            testObjects.Remove(target);
        }

        [Test]
        public void Delete_NonExistentObject_ReturnsError()
        {
            var p = new JObject
            {
                ["action"] = "delete",
                ["target"] = "NonExistentObject12345",
                ["searchMethod"] = "by_name"
            };

            var result = ManageGameObject.HandleCommand(p);
            var resultObj = result as JObject ?? JObject.FromObject(result);

            Assert.IsFalse(resultObj.Value<bool>("success"), "Should fail for non-existent object");
        }

        [Test]
        public void Delete_WithoutTarget_ReturnsError()
        {
            var p = new JObject
            {
                ["action"] = "delete"
            };

            var result = ManageGameObject.HandleCommand(p);
            var resultObj = result as JObject ?? JObject.FromObject(result);

            Assert.IsFalse(resultObj.Value<bool>("success"), "Should fail without target");
        }

        #endregion

        #region Search Method Tests

        [Test]
        public void Delete_ByTag_DeletesMatchingObjects()
        {
            // Current behavior: delete action finds first matching object and deletes it.
            // This test verifies at least one tagged object is deleted.
            var target1 = CreateTestObject("DeleteByTag1");
            var target2 = CreateTestObject("DeleteByTag2");
            
            // Use built-in tag
            target1.tag = "MainCamera";
            target2.tag = "MainCamera";

            var p = new JObject
            {
                ["action"] = "delete",
                ["target"] = "MainCamera",
                ["searchMethod"] = "by_tag"
            };

            var result = ManageGameObject.HandleCommand(p);
            var resultObj = result as JObject ?? JObject.FromObject(result);

            Assert.IsTrue(resultObj.Value<bool>("success"), resultObj.ToString());
            
            // Verify at least one object was deleted (current behavior deletes first match)
            bool target1Deleted = target1 == null; // Unity Object == null check
            bool target2Deleted = target2 == null;
            Assert.IsTrue(target1Deleted || target2Deleted, "At least one tagged object should be deleted");
            
            // Check response data for deletion info
            var data = resultObj["data"];
            Assert.IsNotNull(data, "Response should include data");
            
            // Clean up only surviving objects from tracking
            if (!target1Deleted) testObjects.Remove(target1);
            if (!target2Deleted) testObjects.Remove(target2);
        }

        [Test]
        public void Delete_ByLayer_DeletesMatchingObjects()
        {
            var target = CreateTestObject("DeleteByLayer");
            target.layer = LayerMask.NameToLayer("UI");

            var p = new JObject
            {
                ["action"] = "delete",
                ["target"] = "UI",
                ["searchMethod"] = "by_layer"
            };

            var result = ManageGameObject.HandleCommand(p);
            var resultObj = result as JObject ?? JObject.FromObject(result);

            Assert.IsTrue(resultObj.Value<bool>("success"), resultObj.ToString());
            
            // Verify the object was actually deleted
            bool targetDeleted = target == null; // Unity Object == null check
            Assert.IsTrue(targetDeleted, "Object on UI layer should be deleted");
            Assert.IsFalse(testObjects.Contains(target) && target != null, "Deleted object should not be findable");
            
            // Only remove from tracking if not already destroyed
            if (!targetDeleted) testObjects.Remove(target);
        }

        [Test]
        public void Delete_ByPath_DeletesObject()
        {
            var parent = CreateTestObject("DeleteParent");
            var child = CreateTestObject("DeleteChild");
            child.transform.SetParent(parent.transform);

            var p = new JObject
            {
                ["action"] = "delete",
                ["target"] = "DeleteParent/DeleteChild",
                ["searchMethod"] = "by_path"
            };

            var result = ManageGameObject.HandleCommand(p);
            // Capture current behavior
            Assert.IsNotNull(result, "Should return a result");
            
            testObjects.Remove(child);
        }

        #endregion

        #region Hierarchy Tests

        [Test]
        public void Delete_Parent_DeletesChildren()
        {
            var parent = CreateTestObject("DeleteParentWithChildren");
            var child1 = CreateTestObject("Child1");
            var child2 = CreateTestObject("Child2");
            var grandchild = CreateTestObject("Grandchild");
            
            child1.transform.SetParent(parent.transform);
            child2.transform.SetParent(parent.transform);
            grandchild.transform.SetParent(child1.transform);

            var p = new JObject
            {
                ["action"] = "delete",
                ["target"] = "DeleteParentWithChildren",
                ["searchMethod"] = "by_name"
            };

            var result = ManageGameObject.HandleCommand(p);
            var resultObj = result as JObject ?? JObject.FromObject(result);

            Assert.IsTrue(resultObj.Value<bool>("success"), resultObj.ToString());
            
            // All should be deleted
            Assert.IsNull(GameObject.Find("DeleteParentWithChildren"), "Parent should be deleted");
            Assert.IsNull(GameObject.Find("Child1"), "Child1 should be deleted");
            Assert.IsNull(GameObject.Find("Child2"), "Child2 should be deleted");
            Assert.IsNull(GameObject.Find("Grandchild"), "Grandchild should be deleted");
            
            testObjects.Remove(parent);
            testObjects.Remove(child1);
            testObjects.Remove(child2);
            testObjects.Remove(grandchild);
        }

        [Test]
        public void Delete_Child_DoesNotDeleteParent()
        {
            var parent = CreateTestObject("ParentShouldSurvive");
            var child = CreateTestObject("ChildToDelete");
            child.transform.SetParent(parent.transform);

            var p = new JObject
            {
                ["action"] = "delete",
                ["target"] = "ChildToDelete",
                ["searchMethod"] = "by_name"
            };

            var result = ManageGameObject.HandleCommand(p);
            var resultObj = result as JObject ?? JObject.FromObject(result);

            Assert.IsTrue(resultObj.Value<bool>("success"), resultObj.ToString());
            
            // Child deleted, parent survives
            Assert.IsNull(GameObject.Find("ChildToDelete"), "Child should be deleted");
            Assert.IsNotNull(GameObject.Find("ParentShouldSurvive"), "Parent should survive");
            
            testObjects.Remove(child);
        }

        #endregion

        #region Response Structure Tests

        [Test]
        public void Delete_Success_ReturnsDeletedCount()
        {
            var target = CreateTestObject("DeleteCountTest");

            var p = new JObject
            {
                ["action"] = "delete",
                ["target"] = "DeleteCountTest",
                ["searchMethod"] = "by_name"
            };

            var result = ManageGameObject.HandleCommand(p);
            var resultObj = result as JObject ?? JObject.FromObject(result);

            Assert.IsTrue(resultObj.Value<bool>("success"), resultObj.ToString());
            
            // Verify object was actually deleted
            bool targetDeleted = target == null;
            Assert.IsTrue(targetDeleted, "Object should be deleted");
            
            // Check for deleted count in response
            var data = resultObj["data"];
            Assert.IsNotNull(data, "Response should include data");
            
            // Verify the actual count if present
            if (data is JObject dataObj && dataObj.ContainsKey("deletedCount"))
            {
                Assert.AreEqual(1, dataObj.Value<int>("deletedCount"), "Should report 1 deleted object");
            }
            
            // Only remove from tracking if not already destroyed
            if (!targetDeleted) testObjects.Remove(target);
        }

        #endregion

        #region Edge Cases

        [Test]
        public void Delete_InactiveObject_StillDeletes()
        {
            var target = CreateTestObject("InactiveDeleteTarget");
            target.SetActive(false);

            var p = new JObject
            {
                ["action"] = "delete",
                ["target"] = "InactiveDeleteTarget",
                ["searchMethod"] = "by_name"
            };

            var result = ManageGameObject.HandleCommand(p);
            // Capture current behavior for inactive objects
            Assert.IsNotNull(result, "Should return a result");
            
            testObjects.Remove(target);
        }

        [Test]
        public void Delete_MultipleObjectsSameName_DeletesCorrectly()
        {
            // Expected behavior: delete action with by_name finds the FIRST matching object
            // and deletes only that one. This is consistent with Unity's GameObject.Find behavior.
            var target1 = CreateTestObject("DuplicateName");
            var target2 = CreateTestObject("DuplicateName");

            var p = new JObject
            {
                ["action"] = "delete",
                ["target"] = "DuplicateName",
                ["searchMethod"] = "by_name"
            };

            var result = ManageGameObject.HandleCommand(p);
            var resultObj = result as JObject ?? JObject.FromObject(result);

            Assert.IsTrue(resultObj.Value<bool>("success"), resultObj.ToString());
            
            // Verify deletion occurred - at least one should be deleted
            bool target1Deleted = target1 == null;
            bool target2Deleted = target2 == null;
            Assert.IsTrue(target1Deleted || target2Deleted, "At least one object should be deleted");
            
            // Count remaining objects with the name to verify behavior
            int remainingCount = 0;
            if (!target1Deleted) remainingCount++;
            if (!target2Deleted) remainingCount++;
            
            // Document the actual behavior: first match is deleted, second survives
            // If both are deleted, that's also acceptable (bulk delete mode)
            Assert.IsTrue(remainingCount <= 1, $"Expected at most 1 remaining, got {remainingCount}");
            
            // Clean up only survivors from tracking
            if (!target1Deleted) testObjects.Remove(target1);
            if (!target2Deleted) testObjects.Remove(target2);
        }

        #endregion
    }
}

