using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnityTests.Editor.Tools
{
    /// <summary>
    /// Tests specifically for the material and mesh instantiation warnings fix.
    /// These tests verify that the GameObjectSerializer uses sharedMaterial/sharedMesh
    /// in edit mode to prevent Unity's instantiation warnings.
    /// </summary>
    public class MaterialMeshInstantiationTests
    {
        private GameObject testGameObject;
        private Material testMaterial;
        private Mesh testMesh;

        [SetUp]
        public void SetUp()
        {
            // Create a test GameObject for each test
            testGameObject = new GameObject("MaterialMeshTestObject");
            
            // Create test material and mesh
            testMaterial = new Material(Shader.Find("Standard"));
            testMaterial.name = "TestMaterial";
            
            var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            testMesh = temp.GetComponent<MeshFilter>().sharedMesh;
            UnityEngine.Object.DestroyImmediate(temp);
            testMesh.name = "TestMesh";
        }

        [TearDown]
        public void TearDown()
        {
            // Clean up test objects
            if (testMaterial != null)
            {
                UnityEngine.Object.DestroyImmediate(testMaterial);
            }
            
            if (testGameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(testGameObject);
            }
        }

        [Test]
        public void GetComponentData_UsesSharedMaterialInsteadOfMaterial()
        {
            var meshRenderer = testGameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = testMaterial;
            int beforeId = meshRenderer.sharedMaterial != null ? meshRenderer.sharedMaterial.GetInstanceID() : 0;
            var result = GameObjectSerializer.GetComponentData(meshRenderer);
            int afterId = meshRenderer.sharedMaterial != null ? meshRenderer.sharedMaterial.GetInstanceID() : 0;
            Assert.AreEqual(beforeId, afterId, "sharedMaterial instanceID must not change during edit-mode serialization (no instantiation)");
            Assert.IsNotNull(result, "GetComponentData should return a result");
            var propsObj = (result as Dictionary<string, object>) != null && ((Dictionary<string, object>)result).TryGetValue("properties", out var p)
                ? p as Dictionary<string, object>
                : null;
            if (propsObj != null)
            {
                long? foundInstanceId = null;
                if (propsObj.TryGetValue("material", out var materialObj) && materialObj is Dictionary<string, object> matDict && matDict.TryGetValue("instanceID", out var idObj1) && idObj1 is long id1)
                {
                    foundInstanceId = id1;
                }
                else if (propsObj.TryGetValue("sharedMaterial", out var sharedMatObj) && sharedMatObj is Dictionary<string, object> sharedMatDict && sharedMatDict.TryGetValue("instanceID", out var idObj2) && idObj2 is long id2)
                {
                    foundInstanceId = id2;
                }
                else if (propsObj.TryGetValue("materials", out var materialsObj) && materialsObj is List<object> mats && mats.Count > 0 && mats[0] is Dictionary<string, object> firstMat && firstMat.TryGetValue("instanceID", out var idObj3) && idObj3 is long id3)
                {
                    foundInstanceId = id3;
                }
                else if (propsObj.TryGetValue("sharedMaterials", out var sharedMaterialsObj) && sharedMaterialsObj is List<object> smats && smats.Count > 0 && smats[0] is Dictionary<string, object> firstSMat && firstSMat.TryGetValue("instanceID", out var idObj4) && idObj4 is long id4)
                {
                    foundInstanceId = id4;
                }
                if (foundInstanceId.HasValue)
                {
                    Assert.AreEqual(beforeId, (int)foundInstanceId.Value, "Serialized material must reference the sharedMaterial instance");
                }
            }
        }
 
        [Test]
        public void GetComponentData_UsesSharedMeshInsteadOfMesh()
        {
            var meshFilter = testGameObject.AddComponent<MeshFilter>();
            var uniqueMesh = UnityEngine.Object.Instantiate(testMesh);
            meshFilter.sharedMesh = uniqueMesh;
            int beforeId = meshFilter.sharedMesh != null ? meshFilter.sharedMesh.GetInstanceID() : 0;
            var result = GameObjectSerializer.GetComponentData(meshFilter);
            int afterId = meshFilter.sharedMesh != null ? meshFilter.sharedMesh.GetInstanceID() : 0;
            Assert.AreEqual(beforeId, afterId, "sharedMesh instanceID must not change during edit-mode serialization (no instantiation)");
            Assert.IsNotNull(result, "GetComponentData should return a result");
            var propsObj = (result as Dictionary<string, object>) != null && ((Dictionary<string, object>)result).TryGetValue("properties", out var p)
                ? p as Dictionary<string, object>
                : null;
            if (propsObj != null)
            {
                long? foundInstanceId = null;
                if (propsObj.TryGetValue("mesh", out var meshObj) && meshObj is Dictionary<string, object> meshDict && meshDict.TryGetValue("instanceID", out var idObj1) && idObj1 is long id1)
                {
                    foundInstanceId = id1;
                }
                else if (propsObj.TryGetValue("sharedMesh", out var sharedMeshObj) && sharedMeshObj is Dictionary<string, object> sharedMeshDict && sharedMeshDict.TryGetValue("instanceID", out var idObj2) && idObj2 is long id2)
                {
                    foundInstanceId = id2;
                }
                if (foundInstanceId.HasValue)
                {
                    Assert.AreEqual(beforeId, (int)foundInstanceId.Value, "Serialized mesh must reference the sharedMesh instance");
                }
            }
            
            // Clean up the instantiated mesh
            UnityEngine.Object.DestroyImmediate(uniqueMesh);
        }
 
        // (The two strong tests above replace the prior lighter-weight versions.)
 
        [Test]
        public void GetComponentData_HandlesNullSharedMaterial()
        {
            // Arrange - Create MeshRenderer without setting shared material
            var meshRenderer = testGameObject.AddComponent<MeshRenderer>();
            // Don't set sharedMaterial - it should be null
            
            // Act - Get component data
            var result = GameObjectSerializer.GetComponentData(meshRenderer);
            
            // Assert - Should handle null shared material gracefully
            Assert.IsNotNull(result, "GetComponentData should handle null shared material");
        }
 
        [Test]
        public void GetComponentData_HandlesNullSharedMesh()
        {
            // Arrange - Create MeshFilter without setting shared mesh
            var meshFilter = testGameObject.AddComponent<MeshFilter>();
            // Don't set sharedMesh - it should be null
            
            // Act - Get component data
            var result = GameObjectSerializer.GetComponentData(meshFilter);
            
            // Assert - Should handle null shared mesh gracefully
            Assert.IsNotNull(result, "GetComponentData should handle null shared mesh");
        }
 
        [Test]
        public void GetComponentData_WorksWithMultipleSharedMaterials()
        {
            // Arrange - Create MeshRenderer with multiple shared materials
            var meshRenderer = testGameObject.AddComponent<MeshRenderer>();
            
            var material1 = new Material(Shader.Find("Standard"));
            material1.name = "TestMaterial1";
            var material2 = new Material(Shader.Find("Standard"));
            material2.name = "TestMaterial2";
            
            meshRenderer.sharedMaterials = new Material[] { material1, material2 };
            
            // Act - Get component data
            var result = GameObjectSerializer.GetComponentData(meshRenderer);
            
            // Assert - Should handle multiple shared materials
            Assert.IsNotNull(result, "GetComponentData should handle multiple shared materials");
            
            // Clean up additional materials
            UnityEngine.Object.DestroyImmediate(material1);
            UnityEngine.Object.DestroyImmediate(material2);
        }
 
        [Test]
        public void GetComponentData_EditModeDetectionWorks()
        {
            // This test verifies that our edit mode detection is working
            // We can't easily test Application.isPlaying directly, but we can verify
            // that the behavior is consistent with edit mode expectations
            
            // Arrange - Create components that would trigger warnings in edit mode
            var meshRenderer = testGameObject.AddComponent<MeshRenderer>();
            var meshFilter = testGameObject.AddComponent<MeshFilter>();
            
            meshRenderer.sharedMaterial = testMaterial;
            meshFilter.sharedMesh = testMesh;
            
            // Act - Get component data multiple times
            var rendererResult = GameObjectSerializer.GetComponentData(meshRenderer);
            var meshFilterResult = GameObjectSerializer.GetComponentData(meshFilter);
            
            // Assert - Both operations should succeed without warnings
            Assert.IsNotNull(rendererResult, "MeshRenderer serialization should work in edit mode");
            Assert.IsNotNull(meshFilterResult, "MeshFilter serialization should work in edit mode");
        }
        // Removed low-value property-presence tests; the instanceID tests are the authoritative guardrails.
    }
}
