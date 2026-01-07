using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEditorInternal;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Tools.GameObjects;

namespace MCPForUnityTests.Editor.Tools
{
    /// <summary>
    /// Comprehensive baseline tests for ManageGameObject "create" action.
    /// These tests capture existing behavior before API redesign.
    /// </summary>
    public class ManageGameObjectCreateTests
    {
        private List<GameObject> createdObjects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in createdObjects)
            {
                if (go != null)
                {
                    Object.DestroyImmediate(go);
                }
            }
            createdObjects.Clear();
        }

        private GameObject FindAndTrack(string name)
        {
            var go = GameObject.Find(name);
            if (go != null && !createdObjects.Contains(go))
            {
                createdObjects.Add(go);
            }
            return go;
        }

        #region Basic Create Tests

        [Test]
        public void Create_WithNameOnly_CreatesEmptyGameObject()
        {
            var p = new JObject
            {
                ["action"] = "create",
                ["name"] = "TestEmptyObject"
            };

            var result = ManageGameObject.HandleCommand(p);
            var resultObj = result as JObject ?? JObject.FromObject(result);

            Assert.IsTrue(resultObj.Value<bool>("success"), resultObj.ToString());

            var created = FindAndTrack("TestEmptyObject");
            Assert.IsNotNull(created, "GameObject should be created");
            Assert.AreEqual("TestEmptyObject", created.name);
        }

        [Test]
        public void Create_WithoutName_ReturnsError()
        {
            var p = new JObject
            {
                ["action"] = "create"
            };

            var result = ManageGameObject.HandleCommand(p);
            var resultObj = result as JObject ?? JObject.FromObject(result);

            Assert.IsFalse(resultObj.Value<bool>("success"), "Should fail without name");
        }

        [Test]
        public void Create_WithEmptyName_ReturnsError()
        {
            var p = new JObject
            {
                ["action"] = "create",
                ["name"] = ""
            };

            var result = ManageGameObject.HandleCommand(p);
            var resultObj = result as JObject ?? JObject.FromObject(result);

            Assert.IsFalse(resultObj.Value<bool>("success"), "Should fail with empty name");
        }

        #endregion

        #region Primitive Type Tests

        [Test]
        public void Create_PrimitiveCube_CreatesCubeWithComponents()
        {
            var p = new JObject
            {
                ["action"] = "create",
                ["name"] = "TestCube",
                ["primitiveType"] = "Cube"
            };

            var result = ManageGameObject.HandleCommand(p);
            var resultObj = result as JObject ?? JObject.FromObject(result);

            Assert.IsTrue(resultObj.Value<bool>("success"), resultObj.ToString());

            var created = FindAndTrack("TestCube");
            Assert.IsNotNull(created, "Cube should be created");
            Assert.IsNotNull(created.GetComponent<MeshFilter>(), "Cube should have MeshFilter");
            Assert.IsNotNull(created.GetComponent<MeshRenderer>(), "Cube should have MeshRenderer");
            Assert.IsNotNull(created.GetComponent<BoxCollider>(), "Cube should have BoxCollider");
        }

        [Test]
        public void Create_PrimitiveSphere_CreatesSphereWithComponents()
        {
            var p = new JObject
            {
                ["action"] = "create",
                ["name"] = "TestSphere",
                ["primitiveType"] = "Sphere"
            };

            var result = ManageGameObject.HandleCommand(p);
            var resultObj = result as JObject ?? JObject.FromObject(result);

            Assert.IsTrue(resultObj.Value<bool>("success"), resultObj.ToString());

            var created = FindAndTrack("TestSphere");
            Assert.IsNotNull(created, "Sphere should be created");
            Assert.IsNotNull(created.GetComponent<SphereCollider>(), "Sphere should have SphereCollider");
        }

        [Test]
        public void Create_PrimitiveCapsule_CreatesCapsule()
        {
            var p = new JObject
            {
                ["action"] = "create",
                ["name"] = "TestCapsule",
                ["primitiveType"] = "Capsule"
            };

            var result = ManageGameObject.HandleCommand(p);
            var resultObj = result as JObject ?? JObject.FromObject(result);

            Assert.IsTrue(resultObj.Value<bool>("success"), resultObj.ToString());

            var created = FindAndTrack("TestCapsule");
            Assert.IsNotNull(created, "Capsule should be created");
            Assert.IsNotNull(created.GetComponent<CapsuleCollider>(), "Capsule should have CapsuleCollider");
        }

        [Test]
        public void Create_PrimitivePlane_CreatesPlane()
        {
            var p = new JObject
            {
                ["action"] = "create",
                ["name"] = "TestPlane",
                ["primitiveType"] = "Plane"
            };

            var result = ManageGameObject.HandleCommand(p);
            var resultObj = result as JObject ?? JObject.FromObject(result);

            Assert.IsTrue(resultObj.Value<bool>("success"), resultObj.ToString());

            var created = FindAndTrack("TestPlane");
            Assert.IsNotNull(created, "Plane should be created");
        }

        [Test]
        public void Create_PrimitiveCylinder_CreatesCylinder()
        {
            var p = new JObject
            {
                ["action"] = "create",
                ["name"] = "TestCylinder",
                ["primitiveType"] = "Cylinder"
            };

            var result = ManageGameObject.HandleCommand(p);
            var resultObj = result as JObject ?? JObject.FromObject(result);

            Assert.IsTrue(resultObj.Value<bool>("success"), resultObj.ToString());

            var created = FindAndTrack("TestCylinder");
            Assert.IsNotNull(created, "Cylinder should be created");
        }

        [Test]
        public void Create_PrimitiveQuad_CreatesQuad()
        {
            var p = new JObject
            {
                ["action"] = "create",
                ["name"] = "TestQuad",
                ["primitiveType"] = "Quad"
            };

            var result = ManageGameObject.HandleCommand(p);
            var resultObj = result as JObject ?? JObject.FromObject(result);

            Assert.IsTrue(resultObj.Value<bool>("success"), resultObj.ToString());

            var created = FindAndTrack("TestQuad");
            Assert.IsNotNull(created, "Quad should be created");
        }

        [Test]
        public void Create_InvalidPrimitiveType_HandlesGracefully()
        {
            var p = new JObject
            {
                ["action"] = "create",
                ["name"] = "TestInvalidPrimitive",
                ["primitiveType"] = "InvalidType"
            };

            var result = ManageGameObject.HandleCommand(p);
            // Should either fail or create empty object - capture current behavior
            Assert.IsNotNull(result, "Should return a result");
        }

        #endregion

        #region Transform Tests

        [Test]
        public void Create_WithPosition_SetsPosition()
        {
            var p = new JObject
            {
                ["action"] = "create",
                ["name"] = "TestPositioned",
                ["position"] = new JArray { 1.0f, 2.0f, 3.0f }
            };

            var result = ManageGameObject.HandleCommand(p);
            var resultObj = result as JObject ?? JObject.FromObject(result);

            Assert.IsTrue(resultObj.Value<bool>("success"), resultObj.ToString());

            var created = FindAndTrack("TestPositioned");
            Assert.IsNotNull(created);
            Assert.AreEqual(new Vector3(1f, 2f, 3f), created.transform.position);
        }

        [Test]
        public void Create_WithRotation_SetsRotation()
        {
            var p = new JObject
            {
                ["action"] = "create",
                ["name"] = "TestRotated",
                ["rotation"] = new JArray { 0.0f, 90.0f, 0.0f }
            };

            var result = ManageGameObject.HandleCommand(p);
            var resultObj = result as JObject ?? JObject.FromObject(result);

            Assert.IsTrue(resultObj.Value<bool>("success"), resultObj.ToString());

            var created = FindAndTrack("TestRotated");
            Assert.IsNotNull(created);
            // Check Y rotation is approximately 90 degrees
            Assert.AreEqual(90f, created.transform.eulerAngles.y, 0.1f);
        }

        [Test]
        public void Create_WithScale_SetsScale()
        {
            var p = new JObject
            {
                ["action"] = "create",
                ["name"] = "TestScaled",
                ["scale"] = new JArray { 2.0f, 3.0f, 4.0f }
            };

            var result = ManageGameObject.HandleCommand(p);
            var resultObj = result as JObject ?? JObject.FromObject(result);

            Assert.IsTrue(resultObj.Value<bool>("success"), resultObj.ToString());

            var created = FindAndTrack("TestScaled");
            Assert.IsNotNull(created);
            Assert.AreEqual(new Vector3(2f, 3f, 4f), created.transform.localScale);
        }

        [Test]
        public void Create_WithAllTransformProperties_SetsAll()
        {
            var p = new JObject
            {
                ["action"] = "create",
                ["name"] = "TestFullTransform",
                ["position"] = new JArray { 5.0f, 6.0f, 7.0f },
                ["rotation"] = new JArray { 45.0f, 90.0f, 0.0f },
                ["scale"] = new JArray { 1.5f, 1.5f, 1.5f }
            };

            var result = ManageGameObject.HandleCommand(p);
            var resultObj = result as JObject ?? JObject.FromObject(result);

            Assert.IsTrue(resultObj.Value<bool>("success"), resultObj.ToString());

            var created = FindAndTrack("TestFullTransform");
            Assert.IsNotNull(created);
            Assert.AreEqual(new Vector3(5f, 6f, 7f), created.transform.position);
            Assert.AreEqual(new Vector3(1.5f, 1.5f, 1.5f), created.transform.localScale);
        }

        #endregion

        #region Parenting Tests

        [Test]
        public void Create_WithParentByName_SetsParent()
        {
            // Create parent first
            var parent = new GameObject("TestParent");
            createdObjects.Add(parent);

            var p = new JObject
            {
                ["action"] = "create",
                ["name"] = "TestChild",
                ["parent"] = "TestParent"
            };

            var result = ManageGameObject.HandleCommand(p);
            var resultObj = result as JObject ?? JObject.FromObject(result);

            Assert.IsTrue(resultObj.Value<bool>("success"), resultObj.ToString());

            var child = FindAndTrack("TestChild");
            Assert.IsNotNull(child);
            Assert.AreEqual(parent.transform, child.transform.parent);
        }

        [Test]
        public void Create_WithNonExistentParent_HandlesGracefully()
        {
            var p = new JObject
            {
                ["action"] = "create",
                ["name"] = "TestOrphan",
                ["parent"] = "NonExistentParent"
            };

            var result = ManageGameObject.HandleCommand(p);
            // Should either fail or create without parent - capture current behavior
            Assert.IsNotNull(result, "Should return a result");
        }

        #endregion

        #region Tag and Layer Tests

        [Test]
        public void Create_WithTag_SetsTag()
        {
            var p = new JObject
            {
                ["action"] = "create",
                ["name"] = "TestTagged",
                ["tag"] = "MainCamera" // Use built-in tag
            };

            var result = ManageGameObject.HandleCommand(p);
            var resultObj = result as JObject ?? JObject.FromObject(result);

            Assert.IsTrue(resultObj.Value<bool>("success"), resultObj.ToString());

            var created = FindAndTrack("TestTagged");
            Assert.IsNotNull(created);
            Assert.AreEqual("MainCamera", created.tag);
        }

        [Test]
        public void Create_WithLayer_SetsLayer()
        {
            var p = new JObject
            {
                ["action"] = "create",
                ["name"] = "TestLayered",
                ["layer"] = "UI" // Use built-in layer
            };

            var result = ManageGameObject.HandleCommand(p);
            var resultObj = result as JObject ?? JObject.FromObject(result);

            Assert.IsTrue(resultObj.Value<bool>("success"), resultObj.ToString());

            var created = FindAndTrack("TestLayered");
            Assert.IsNotNull(created);
            Assert.AreEqual(LayerMask.NameToLayer("UI"), created.layer);
        }

        [Test]
        public void Create_WithNewTag_AutoCreatesTag()
        {
            const string testTag = "AutoCreatedTag12345";
            
            // Tags that don't exist are now auto-created
            var p = new JObject
            {
                ["action"] = "create",
                ["name"] = "TestAutoTag",
                ["tag"] = testTag
            };

            var result = ManageGameObject.HandleCommand(p);
            var resultObj = result as JObject ?? JObject.FromObject(result);
            
            Assert.IsTrue(resultObj.Value<bool>("success"), resultObj.ToString());
            
            var created = FindAndTrack("TestAutoTag");
            Assert.IsNotNull(created, "Object should be created");
            Assert.AreEqual(testTag, created.tag, "Tag should be auto-created and assigned");
            
            // Verify tag was actually added to the tag manager
            Assert.That(UnityEditorInternal.InternalEditorUtility.tags, Does.Contain(testTag), 
                "Tag should exist in Unity's tag manager");
            
            // Clean up the created tag
            try { UnityEditorInternal.InternalEditorUtility.RemoveTag(testTag); } catch { }
        }

        #endregion

        #region Response Structure Tests

        [Test]
        public void Create_Success_ReturnsInstanceID()
        {
            var p = new JObject
            {
                ["action"] = "create",
                ["name"] = "TestInstanceID"
            };

            var result = ManageGameObject.HandleCommand(p);
            var resultObj = result as JObject ?? JObject.FromObject(result);

            Assert.IsTrue(resultObj.Value<bool>("success"), resultObj.ToString());
            
            var data = resultObj["data"];
            Assert.IsNotNull(data, "Response should include data");
            
            // Check that instanceID is returned (case-insensitive check)
            var instanceID = data["instanceID"]?.Value<int>() ?? data["InstanceID"]?.Value<int>();
            Assert.IsTrue(instanceID.HasValue && instanceID.Value != 0, 
                $"Response should include a non-zero instanceID. Data: {data}");

            FindAndTrack("TestInstanceID");
        }

        [Test]
        public void Create_Success_ReturnsName()
        {
            var p = new JObject
            {
                ["action"] = "create",
                ["name"] = "TestReturnedName"
            };

            var result = ManageGameObject.HandleCommand(p);
            var resultObj = result as JObject ?? JObject.FromObject(result);

            Assert.IsTrue(resultObj.Value<bool>("success"), resultObj.ToString());
            
            var data = resultObj["data"];
            Assert.IsNotNull(data, "Response should include data");
            
            // Check name is in response
            var nameValue = data["name"]?.ToString() ?? data["Name"]?.ToString();
            Assert.IsTrue(!string.IsNullOrEmpty(nameValue) || data.ToString().Contains("TestReturnedName"),
                "Response should include name");

            FindAndTrack("TestReturnedName");
        }

        #endregion
    }
}

