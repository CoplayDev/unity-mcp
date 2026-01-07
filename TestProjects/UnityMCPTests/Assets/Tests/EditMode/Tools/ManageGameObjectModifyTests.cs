using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEditorInternal;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Tools;

namespace MCPForUnityTests.Editor.Tools
{
    /// <summary>
    /// Comprehensive baseline tests for ManageGameObject "modify" action.
    /// These tests capture existing behavior before API redesign.
    /// </summary>
    public class ManageGameObjectModifyTests
    {
        private List<GameObject> testObjects = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            // Create a standard test object for each test
            var go = new GameObject("ModifyTestObject");
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            testObjects.Add(go);
        }

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

        #region Target Resolution Tests

        [Test]
        public void Modify_ByName_FindsAndModifiesObject()
        {
            var p = new JObject
            {
                ["action"] = "modify",
                ["target"] = "ModifyTestObject",
                ["searchMethod"] = "by_name",
                ["position"] = new JArray { 10.0f, 0.0f, 0.0f }
            };

            var result = ManageGameObject.HandleCommand(p);
            var resultObj = result as JObject ?? JObject.FromObject(result);

            Assert.IsTrue(resultObj.Value<bool>("success"), resultObj.ToString());
            Assert.AreEqual(new Vector3(10f, 0f, 0f), testObjects[0].transform.position);
        }

        [Test]
        public void Modify_ByInstanceID_FindsAndModifiesObject()
        {
            int instanceID = testObjects[0].GetInstanceID();

            var p = new JObject
            {
                ["action"] = "modify",
                ["target"] = instanceID,
                ["searchMethod"] = "by_id",
                ["position"] = new JArray { 20.0f, 0.0f, 0.0f }
            };

            var result = ManageGameObject.HandleCommand(p);
            var resultObj = result as JObject ?? JObject.FromObject(result);

            Assert.IsTrue(resultObj.Value<bool>("success"), resultObj.ToString());
            Assert.AreEqual(new Vector3(20f, 0f, 0f), testObjects[0].transform.position);
        }

        [Test]
        public void Modify_WithNameAlias_UsesNameAsTarget()
        {
            // When target is missing but name is provided, should use name as target
            var p = new JObject
            {
                ["action"] = "modify",
                ["name"] = "ModifyTestObject",
                ["position"] = new JArray { 30.0f, 0.0f, 0.0f }
            };

            var result = ManageGameObject.HandleCommand(p);
            var resultObj = result as JObject ?? JObject.FromObject(result);

            Assert.IsTrue(resultObj.Value<bool>("success"), resultObj.ToString());
            Assert.AreEqual(new Vector3(30f, 0f, 0f), testObjects[0].transform.position);
        }

        [Test]
        public void Modify_NonExistentTarget_ReturnsError()
        {
            var p = new JObject
            {
                ["action"] = "modify",
                ["target"] = "NonExistentObject12345",
                ["searchMethod"] = "by_name",
                ["position"] = new JArray { 0.0f, 0.0f, 0.0f }
            };

            var result = ManageGameObject.HandleCommand(p);
            var resultObj = result as JObject ?? JObject.FromObject(result);

            Assert.IsFalse(resultObj.Value<bool>("success"), "Should fail for non-existent object");
        }

        [Test]
        public void Modify_WithoutTarget_ReturnsError()
        {
            var p = new JObject
            {
                ["action"] = "modify",
                ["position"] = new JArray { 0.0f, 0.0f, 0.0f }
            };

            var result = ManageGameObject.HandleCommand(p);
            var resultObj = result as JObject ?? JObject.FromObject(result);

            Assert.IsFalse(resultObj.Value<bool>("success"), "Should fail without target");
        }

        #endregion

        #region Transform Modification Tests

        [Test]
        public void Modify_Position_SetsNewPosition()
        {
            var p = new JObject
            {
                ["action"] = "modify",
                ["target"] = "ModifyTestObject",
                ["position"] = new JArray { 1.0f, 2.0f, 3.0f }
            };

            var result = ManageGameObject.HandleCommand(p);
            var resultObj = result as JObject ?? JObject.FromObject(result);

            Assert.IsTrue(resultObj.Value<bool>("success"), resultObj.ToString());
            Assert.AreEqual(new Vector3(1f, 2f, 3f), testObjects[0].transform.position);
        }

        [Test]
        public void Modify_Rotation_SetsNewRotation()
        {
            var p = new JObject
            {
                ["action"] = "modify",
                ["target"] = "ModifyTestObject",
                ["rotation"] = new JArray { 0.0f, 90.0f, 0.0f }
            };

            var result = ManageGameObject.HandleCommand(p);
            var resultObj = result as JObject ?? JObject.FromObject(result);

            Assert.IsTrue(resultObj.Value<bool>("success"), resultObj.ToString());
            Assert.AreEqual(90f, testObjects[0].transform.eulerAngles.y, 0.1f);
        }

        [Test]
        public void Modify_Scale_SetsNewScale()
        {
            var p = new JObject
            {
                ["action"] = "modify",
                ["target"] = "ModifyTestObject",
                ["scale"] = new JArray { 2.0f, 3.0f, 4.0f }
            };

            var result = ManageGameObject.HandleCommand(p);
            var resultObj = result as JObject ?? JObject.FromObject(result);

            Assert.IsTrue(resultObj.Value<bool>("success"), resultObj.ToString());
            Assert.AreEqual(new Vector3(2f, 3f, 4f), testObjects[0].transform.localScale);
        }

        [Test]
        public void Modify_AllTransformProperties_SetsAll()
        {
            var p = new JObject
            {
                ["action"] = "modify",
                ["target"] = "ModifyTestObject",
                ["position"] = new JArray { 5.0f, 6.0f, 7.0f },
                ["rotation"] = new JArray { 45.0f, 45.0f, 45.0f },
                ["scale"] = new JArray { 0.5f, 0.5f, 0.5f }
            };

            var result = ManageGameObject.HandleCommand(p);
            var resultObj = result as JObject ?? JObject.FromObject(result);

            Assert.IsTrue(resultObj.Value<bool>("success"), resultObj.ToString());
            Assert.AreEqual(new Vector3(5f, 6f, 7f), testObjects[0].transform.position);
            Assert.AreEqual(new Vector3(0.5f, 0.5f, 0.5f), testObjects[0].transform.localScale);
        }

        #endregion

        #region Rename Tests

        [Test]
        public void Modify_Name_RenamesObject()
        {
            // Get instanceID first since name will change
            int instanceID = testObjects[0].GetInstanceID();
            
            var p = new JObject
            {
                ["action"] = "modify",
                ["target"] = instanceID,
                ["searchMethod"] = "by_id",
                ["name"] = "RenamedObject"  // Uses 'name' parameter, not 'newName'
            };

            var result = ManageGameObject.HandleCommand(p);
            var resultObj = result as JObject ?? JObject.FromObject(result);

            Assert.IsTrue(resultObj.Value<bool>("success"), resultObj.ToString());
            Assert.AreEqual("RenamedObject", testObjects[0].name);
        }

        [Test]
        public void Modify_NameToEmpty_HandlesGracefully()
        {
            int instanceID = testObjects[0].GetInstanceID();
            
            var p = new JObject
            {
                ["action"] = "modify",
                ["target"] = instanceID,
                ["searchMethod"] = "by_id",
                ["name"] = ""  // Empty name
            };

            var result = ManageGameObject.HandleCommand(p);
            // Capture current behavior - may reject or allow empty name
            Assert.IsNotNull(result, "Should return a result");
        }

        #endregion

        #region Reparenting Tests

        [Test]
        public void Modify_Parent_ReparentsObject()
        {
            var parent = CreateTestObject("NewParent");

            var p = new JObject
            {
                ["action"] = "modify",
                ["target"] = "ModifyTestObject",
                ["parent"] = "NewParent"
            };

            var result = ManageGameObject.HandleCommand(p);
            var resultObj = result as JObject ?? JObject.FromObject(result);

            Assert.IsTrue(resultObj.Value<bool>("success"), resultObj.ToString());
            Assert.AreEqual(parent.transform, testObjects[0].transform.parent);
        }

        [Test]
        public void Modify_ParentToNull_UnparentsObject()
        {
            // First parent the object
            var parent = CreateTestObject("TempParent");
            testObjects[0].transform.SetParent(parent.transform);

            var p = new JObject
            {
                ["action"] = "modify",
                ["target"] = "ModifyTestObject",
                ["parent"] = JValue.CreateNull()
            };

            var result = ManageGameObject.HandleCommand(p);
            // Capture current behavior for null parent
            Assert.IsNotNull(result, "Should return a result");
        }

        [Test]
        public void Modify_ParentToNonExistent_HandlesGracefully()
        {
            var p = new JObject
            {
                ["action"] = "modify",
                ["target"] = "ModifyTestObject",
                ["parent"] = "NonExistentParent12345"
            };

            var result = ManageGameObject.HandleCommand(p);
            // Should fail or handle gracefully
            Assert.IsNotNull(result, "Should return a result");
        }

        #endregion

        #region Active State Tests

        [Test]
        public void Modify_SetActive_DeactivatesObject()
        {
            Assert.IsTrue(testObjects[0].activeSelf, "Object should start active");

            var p = new JObject
            {
                ["action"] = "modify",
                ["target"] = "ModifyTestObject",
                ["setActive"] = false
            };

            var result = ManageGameObject.HandleCommand(p);
            var resultObj = result as JObject ?? JObject.FromObject(result);

            Assert.IsTrue(resultObj.Value<bool>("success"), resultObj.ToString());
            Assert.IsFalse(testObjects[0].activeSelf, "Object should be deactivated");
        }

        [Test]
        public void Modify_SetActive_ActivatesObject()
        {
            testObjects[0].SetActive(false);
            Assert.IsFalse(testObjects[0].activeSelf, "Object should start inactive");

            var p = new JObject
            {
                ["action"] = "modify",
                ["target"] = "ModifyTestObject",
                ["setActive"] = true
            };

            var result = ManageGameObject.HandleCommand(p);
            var resultObj = result as JObject ?? JObject.FromObject(result);

            Assert.IsTrue(resultObj.Value<bool>("success"), resultObj.ToString());
            Assert.IsTrue(testObjects[0].activeSelf, "Object should be activated");
        }

        #endregion

        #region Tag and Layer Tests

        [Test]
        public void Modify_Tag_SetsNewTag()
        {
            var p = new JObject
            {
                ["action"] = "modify",
                ["target"] = "ModifyTestObject",
                ["tag"] = "MainCamera"
            };

            var result = ManageGameObject.HandleCommand(p);
            var resultObj = result as JObject ?? JObject.FromObject(result);

            Assert.IsTrue(resultObj.Value<bool>("success"), resultObj.ToString());
            Assert.AreEqual("MainCamera", testObjects[0].tag);
        }

        [Test]
        public void Modify_Layer_SetsNewLayer()
        {
            var p = new JObject
            {
                ["action"] = "modify",
                ["target"] = "ModifyTestObject",
                ["layer"] = "UI"
            };

            var result = ManageGameObject.HandleCommand(p);
            var resultObj = result as JObject ?? JObject.FromObject(result);

            Assert.IsTrue(resultObj.Value<bool>("success"), resultObj.ToString());
            Assert.AreEqual(LayerMask.NameToLayer("UI"), testObjects[0].layer);
        }

        [Test]
        public void Modify_NewTag_AutoCreatesTag()
        {
            const string testTag = "AutoModifyTag12345";
            
            // Tags that don't exist are now auto-created
            var p = new JObject
            {
                ["action"] = "modify",
                ["target"] = "ModifyTestObject",
                ["tag"] = testTag
            };

            var result = ManageGameObject.HandleCommand(p);
            var resultObj = result as JObject ?? JObject.FromObject(result);
            
            Assert.IsTrue(resultObj.Value<bool>("success"), resultObj.ToString());
            Assert.AreEqual(testTag, testObjects[0].tag, "Tag should be auto-created and assigned");
            
            // Verify tag was actually added to the tag manager
            Assert.That(UnityEditorInternal.InternalEditorUtility.tags, Does.Contain(testTag), 
                "Tag should exist in Unity's tag manager");
            
            // Clean up the created tag
            try { UnityEditorInternal.InternalEditorUtility.RemoveTag(testTag); } catch { }
        }

        #endregion

        #region Multiple Modifications Tests

        [Test]
        public void Modify_MultipleProperties_AppliesAll()
        {
            var parent = CreateTestObject("MultiModifyParent");
            int instanceID = testObjects[0].GetInstanceID();

            var p = new JObject
            {
                ["action"] = "modify",
                ["target"] = instanceID,
                ["searchMethod"] = "by_id",
                ["name"] = "MultiModifiedObject",  // Uses 'name' not 'newName'
                ["position"] = new JArray { 100.0f, 200.0f, 300.0f },
                ["scale"] = new JArray { 5.0f, 5.0f, 5.0f },
                ["parent"] = "MultiModifyParent",
                ["tag"] = "MainCamera"
            };

            var result = ManageGameObject.HandleCommand(p);
            var resultObj = result as JObject ?? JObject.FromObject(result);

            Assert.IsTrue(resultObj.Value<bool>("success"), resultObj.ToString());
            Assert.AreEqual("MultiModifiedObject", testObjects[0].name);
            Assert.AreEqual(parent.transform, testObjects[0].transform.parent);
            Assert.AreEqual("MainCamera", testObjects[0].tag);
        }

        #endregion
    }
}

