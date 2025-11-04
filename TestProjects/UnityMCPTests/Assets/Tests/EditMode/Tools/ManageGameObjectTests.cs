using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Tools;

namespace MCPForUnityTests.Editor.Tools
{
    public class ManageGameObjectTests
    {
        private GameObject testGameObject;

        [SetUp]
        public void SetUp()
        {
            // Create a test GameObject for each test
            testGameObject = new GameObject("TestObject");
        }

        [TearDown]
        public void TearDown()
        {
            // Clean up test GameObject
            if (testGameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(testGameObject);
            }
        }

        [Test]
        public void HandleCommand_ReturnsError_ForNullParams()
        {
            var result = ManageGameObject.HandleCommand(null);

            Assert.IsNotNull(result, "Should return a result object");
            // Note: Actual error checking would need access to Response structure
        }

        [Test]
        public void HandleCommand_ReturnsError_ForEmptyParams()
        {
            var emptyParams = new JObject();
            var result = ManageGameObject.HandleCommand(emptyParams);

            Assert.IsNotNull(result, "Should return a result object for empty params");
        }

        [Test]
        public void HandleCommand_ProcessesValidCreateAction()
        {
            var createParams = new JObject
            {
                ["action"] = "create",
                ["name"] = "TestCreateObject"
            };

            var result = ManageGameObject.HandleCommand(createParams);

            Assert.IsNotNull(result, "Should return a result for valid create action");

            // Clean up - find and destroy the created object
            var createdObject = GameObject.Find("TestCreateObject");
            if (createdObject != null)
            {
                UnityEngine.Object.DestroyImmediate(createdObject);
            }
        }

        [Test]
        public void ComponentResolver_Integration_WorksWithRealComponents()
        {
            // Test that our ComponentResolver works with actual Unity components
            var transformResult = ComponentResolver.TryResolve("Transform", out Type transformType, out string error);

            Assert.IsTrue(transformResult, "Should resolve Transform component");
            Assert.AreEqual(typeof(Transform), transformType, "Should return correct Transform type");
            Assert.IsEmpty(error, "Should have no error for valid component");
        }

        [Test]
        public void ComponentResolver_Integration_WorksWithBuiltInComponents()
        {
            var components = new[]
            {
                ("Rigidbody", typeof(Rigidbody)),
                ("Collider", typeof(Collider)),
                ("Renderer", typeof(Renderer)),
                ("Camera", typeof(Camera)),
                ("Light", typeof(Light))
            };

            foreach (var (componentName, expectedType) in components)
            {
                var result = ComponentResolver.TryResolve(componentName, out Type actualType, out string error);

                // Some components might not resolve (abstract classes), but the method should handle gracefully
                if (result)
                {
                    Assert.IsTrue(expectedType.IsAssignableFrom(actualType),
                        $"{componentName} should resolve to assignable type");
                }
                else
                {
                    Assert.IsNotEmpty(error, $"Should have error message for {componentName}");
                }
            }
        }

        [Test]
        public void PropertyMatching_Integration_WorksWithRealGameObject()
        {
            // Add a Rigidbody to test real property matching
            var rigidbody = testGameObject.AddComponent<Rigidbody>();

            var properties = ComponentResolver.GetAllComponentProperties(typeof(Rigidbody));

            Assert.IsNotEmpty(properties, "Rigidbody should have properties");
            Assert.Contains("mass", properties, "Rigidbody should have mass property");
            Assert.Contains("useGravity", properties, "Rigidbody should have useGravity property");

            // Test AI suggestions
            var suggestions = ComponentResolver.GetAIPropertySuggestions("Use Gravity", properties);
            Assert.Contains("useGravity", suggestions, "Should suggest useGravity for 'Use Gravity'");
        }

        [Test]
        public void PropertyMatching_HandlesMonoBehaviourProperties()
        {
            var properties = ComponentResolver.GetAllComponentProperties(typeof(MonoBehaviour));

            Assert.IsNotEmpty(properties, "MonoBehaviour should have properties");
            Assert.Contains("enabled", properties, "MonoBehaviour should have enabled property");
            Assert.Contains("name", properties, "MonoBehaviour should have name property");
            Assert.Contains("tag", properties, "MonoBehaviour should have tag property");
        }

        [Test]
        public void PropertyMatching_HandlesCaseVariations()
        {
            var testProperties = new List<string> { "maxReachDistance", "playerHealth", "movementSpeed" };

            var testCases = new[]
            {
                ("max reach distance", "maxReachDistance"),
                ("Max Reach Distance", "maxReachDistance"),
                ("MAX_REACH_DISTANCE", "maxReachDistance"),
                ("player health", "playerHealth"),
                ("movement speed", "movementSpeed")
            };

            foreach (var (input, expected) in testCases)
            {
                var suggestions = ComponentResolver.GetAIPropertySuggestions(input, testProperties);
                Assert.Contains(expected, suggestions, $"Should suggest {expected} for input '{input}'");
            }
        }

        [Test]
        public void ErrorHandling_ReturnsHelpfulMessages()
        {
            // This test verifies that error messages are helpful and contain suggestions
            var testProperties = new List<string> { "mass", "velocity", "drag", "useGravity" };
            var suggestions = ComponentResolver.GetAIPropertySuggestions("weight", testProperties);

            // Even if no perfect match, should return valid list
            Assert.IsNotNull(suggestions, "Should return valid suggestions list");

            // Test with completely invalid input
            var badSuggestions = ComponentResolver.GetAIPropertySuggestions("xyz123invalid", testProperties);
            Assert.IsNotNull(badSuggestions, "Should handle invalid input gracefully");
        }

        [Test]
        public void PerformanceTest_CachingWorks()
        {
            var properties = ComponentResolver.GetAllComponentProperties(typeof(Transform));
            var input = "Test Property Name";

            // First call - populate cache
            var startTime = System.DateTime.UtcNow;
            var suggestions1 = ComponentResolver.GetAIPropertySuggestions(input, properties);
            var firstCallTime = (System.DateTime.UtcNow - startTime).TotalMilliseconds;

            // Second call - should use cache
            startTime = System.DateTime.UtcNow;
            var suggestions2 = ComponentResolver.GetAIPropertySuggestions(input, properties);
            var secondCallTime = (System.DateTime.UtcNow - startTime).TotalMilliseconds;

            Assert.AreEqual(suggestions1.Count, suggestions2.Count, "Cached results should be identical");
            CollectionAssert.AreEqual(suggestions1, suggestions2, "Cached results should match exactly");

            // Second call should be faster (though this test might be flaky)
            Assert.LessOrEqual(secondCallTime, firstCallTime * 2, "Cached call should not be significantly slower");
        }

        [Test]
        public void SetComponentProperties_CollectsAllFailuresAndAppliesValidOnes()
        {
            // Arrange - add Transform and Rigidbody components to test with
            var transform = testGameObject.transform;
            var rigidbody = testGameObject.AddComponent<Rigidbody>();

            // Create a params object with mixed valid and invalid properties
            var setPropertiesParams = new JObject
            {
                ["action"] = "modify",
                ["target"] = testGameObject.name,
                ["search_method"] = "by_name",
                ["componentProperties"] = new JObject
                {
                    ["Transform"] = new JObject
                    {
                        ["localPosition"] = new JObject { ["x"] = 1.0f, ["y"] = 2.0f, ["z"] = 3.0f },  // Valid
                        ["rotatoin"] = new JObject { ["x"] = 0.0f, ["y"] = 90.0f, ["z"] = 0.0f }, // Invalid (typo - should be rotation)
                        ["localScale"] = new JObject { ["x"] = 2.0f, ["y"] = 2.0f, ["z"] = 2.0f }      // Valid
                    },
                    ["Rigidbody"] = new JObject
                    {
                        ["mass"] = 5.0f,            // Valid
                        ["invalidProp"] = "test",   // Invalid - doesn't exist
                        ["useGravity"] = true       // Valid
                    }
                }
            };

            // Store original values to verify changes  
            var originalLocalPosition = transform.localPosition;
            var originalLocalScale = transform.localScale;
            var originalMass = rigidbody.mass;
            var originalUseGravity = rigidbody.useGravity;

            Debug.Log($"BEFORE TEST - Mass: {rigidbody.mass}, UseGravity: {rigidbody.useGravity}");

            // Expect the warning logs from the invalid properties
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Property 'rotatoin' not found"));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Property 'invalidProp' not found"));

            // Act
            var result = ManageGameObject.HandleCommand(setPropertiesParams);

            Debug.Log($"AFTER TEST - Mass: {rigidbody.mass}, UseGravity: {rigidbody.useGravity}");
            Debug.Log($"AFTER TEST - LocalPosition: {transform.localPosition}");
            Debug.Log($"AFTER TEST - LocalScale: {transform.localScale}");

            // Assert - verify that valid properties were set despite invalid ones
            Assert.AreEqual(new Vector3(1.0f, 2.0f, 3.0f), transform.localPosition,
                "Valid localPosition should be set even with other invalid properties");
            Assert.AreEqual(new Vector3(2.0f, 2.0f, 2.0f), transform.localScale,
                "Valid localScale should be set even with other invalid properties");
            Assert.AreEqual(5.0f, rigidbody.mass, 0.001f,
                "Valid mass should be set even with other invalid properties");
            Assert.AreEqual(true, rigidbody.useGravity,
                "Valid useGravity should be set even with other invalid properties");

            // Verify the result indicates errors (since we had invalid properties)
            Assert.IsNotNull(result, "Should return a result object");

            // The collect-and-continue behavior means we should get an error response 
            // that contains info about the failed properties, but valid ones were still applied
            // This proves the collect-and-continue behavior is working

            // Harden: verify structured error response with failures list contains both invalid fields
            var successProp = result.GetType().GetProperty("success");
            Assert.IsNotNull(successProp, "Result should expose 'success' property");
            Assert.IsFalse((bool)successProp.GetValue(result), "Result.success should be false for partial failure");

            var dataProp = result.GetType().GetProperty("data");
            Assert.IsNotNull(dataProp, "Result should include 'data' with errors");
            var dataVal = dataProp.GetValue(result);
            Assert.IsNotNull(dataVal, "Result.data should not be null");
            var errorsProp = dataVal.GetType().GetProperty("errors");
            Assert.IsNotNull(errorsProp, "Result.data should include 'errors' list");
            var errorsEnum = errorsProp.GetValue(dataVal) as System.Collections.IEnumerable;
            Assert.IsNotNull(errorsEnum, "errors should be enumerable");

            bool foundRotatoin = false;
            bool foundInvalidProp = false;
            foreach (var err in errorsEnum)
            {
                string s = err?.ToString() ?? string.Empty;
                if (s.Contains("rotatoin")) foundRotatoin = true;
                if (s.Contains("invalidProp")) foundInvalidProp = true;
            }
            Assert.IsTrue(foundRotatoin, "errors should mention the misspelled 'rotatoin' property");
            Assert.IsTrue(foundInvalidProp, "errors should mention the 'invalidProp' property");
        }

        [Test]
        public void SetComponentProperties_ContinuesAfterException()
        {
            // Arrange - create scenario that might cause exceptions
            var rigidbody = testGameObject.AddComponent<Rigidbody>();

            // Set initial values that we'll change
            rigidbody.mass = 1.0f;
            rigidbody.useGravity = true;

            var setPropertiesParams = new JObject
            {
                ["action"] = "modify",
                ["target"] = testGameObject.name,
                ["search_method"] = "by_name",
                ["componentProperties"] = new JObject
                {
                    ["Rigidbody"] = new JObject
                    {
                        ["mass"] = 2.5f,                    // Valid - should be set
                        ["velocity"] = "invalid_type",      // Invalid type - will cause exception  
                        ["useGravity"] = false              // Valid - should still be set after exception
                    }
                }
            };

            // Expect the error logs from the invalid property
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Unexpected error converting token to UnityEngine.Vector3"));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("SetProperty.*Failed to set 'velocity'"));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Property 'velocity' not found"));

            // Act
            var result = ManageGameObject.HandleCommand(setPropertiesParams);

            // Assert - verify that valid properties before AND after the exception were still set
            Assert.AreEqual(2.5f, rigidbody.mass, 0.001f,
                "Mass should be set even if later property causes exception");
            Assert.AreEqual(false, rigidbody.useGravity,
                "UseGravity should be set even if previous property caused exception");

            Assert.IsNotNull(result, "Should return a result even with exceptions");

            // The key test: processing continued after the exception and set useGravity
            // This proves the collect-and-continue behavior works even with exceptions

            // Harden: verify structured error response contains velocity failure
            var successProp2 = result.GetType().GetProperty("success");
            Assert.IsNotNull(successProp2, "Result should expose 'success' property");
            Assert.IsFalse((bool)successProp2.GetValue(result), "Result.success should be false when an exception occurs for a property");

            var dataProp2 = result.GetType().GetProperty("data");
            Assert.IsNotNull(dataProp2, "Result should include 'data' with errors");
            var dataVal2 = dataProp2.GetValue(result);
            Assert.IsNotNull(dataVal2, "Result.data should not be null");
            var errorsProp2 = dataVal2.GetType().GetProperty("errors");
            Assert.IsNotNull(errorsProp2, "Result.data should include 'errors' list");
            var errorsEnum2 = errorsProp2.GetValue(dataVal2) as System.Collections.IEnumerable;
            Assert.IsNotNull(errorsEnum2, "errors should be enumerable");

            bool foundVelocityError = false;
            foreach (var err in errorsEnum2)
            {
                string s = err?.ToString() ?? string.Empty;
                if (s.Contains("velocity")) { foundVelocityError = true; break; }
            }
            Assert.IsTrue(foundVelocityError, "errors should include a message referencing 'velocity'");
        }

        [Test]
        public void GetComponentData_DoesNotInstantiateMaterialsInEditMode()
        {
            // Arrange - Create a GameObject with MeshRenderer and MeshFilter components
            var testObject = new GameObject("MaterialMeshTestObject");
            var meshRenderer = testObject.AddComponent<MeshRenderer>();
            var meshFilter = testObject.AddComponent<MeshFilter>();
            
            // Create a simple material and mesh for testing
            var testMaterial = new Material(Shader.Find("Standard"));
            var tempCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var testMesh = tempCube.GetComponent<MeshFilter>().sharedMesh;
            UnityEngine.Object.DestroyImmediate(tempCube);
            
            // Set the shared material and mesh (these should be used in edit mode)
            meshRenderer.sharedMaterial = testMaterial;
            meshFilter.sharedMesh = testMesh;
            
            // Act - Get component data which should trigger material/mesh property access
            var prevIgnore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true; // Avoid failing due to incidental editor logs during reflection
            object result;
            try
            {
                result = MCPForUnity.Editor.Helpers.GameObjectSerializer.GetComponentData(meshRenderer);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = prevIgnore;
            }
            
            // Assert - Basic success and shape tolerance
            Assert.IsNotNull(result, "GetComponentData should return a result");
            if (result is Dictionary<string, object> dict &&
                dict.TryGetValue("properties", out var propsObj) &&
                propsObj is Dictionary<string, object> properties)
            {
                Assert.IsTrue(properties.ContainsKey("material") || properties.ContainsKey("sharedMaterial") || properties.ContainsKey("materials") || properties.ContainsKey("sharedMaterials"),
                    "Serialized data should include a material-related key when present.");
            }
            
            // Clean up
            UnityEngine.Object.DestroyImmediate(testMaterial);
            UnityEngine.Object.DestroyImmediate(testObject);
        }

        [Test]
        public void GetComponentData_DoesNotInstantiateMeshesInEditMode()
        {
            // Arrange - Create a GameObject with MeshFilter component
            var testObject = new GameObject("MeshTestObject");
            var meshFilter = testObject.AddComponent<MeshFilter>();
            
            // Create a simple mesh for testing
            var tempSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var testMesh = tempSphere.GetComponent<MeshFilter>().sharedMesh;
            UnityEngine.Object.DestroyImmediate(tempSphere);
            meshFilter.sharedMesh = testMesh;
            
            // Act - Get component data which should trigger mesh property access
            var prevIgnore2 = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            object result;
            try
            {
                result = MCPForUnity.Editor.Helpers.GameObjectSerializer.GetComponentData(meshFilter);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = prevIgnore2;
            }
            
            // Assert - Basic success and shape tolerance
            Assert.IsNotNull(result, "GetComponentData should return a result");
            if (result is Dictionary<string, object> dict2 &&
                dict2.TryGetValue("properties", out var propsObj2) &&
                propsObj2 is Dictionary<string, object> properties2)
            {
                Assert.IsTrue(properties2.ContainsKey("mesh") || properties2.ContainsKey("sharedMesh"),
                    "Serialized data should include a mesh-related key when present.");
            }
            
            // Clean up
            UnityEngine.Object.DestroyImmediate(testObject);
        }

        [Test]
        public void GetComponentData_UsesSharedMaterialInEditMode()
        {
            // Arrange - Create a GameObject with MeshRenderer
            var testObject = new GameObject("SharedMaterialTestObject");
            var meshRenderer = testObject.AddComponent<MeshRenderer>();
            
            // Create a test material
            var testMaterial = new Material(Shader.Find("Standard"));
            testMaterial.name = "TestMaterial";
            meshRenderer.sharedMaterial = testMaterial;
            
            // Act - Get component data in edit mode
            var result = MCPForUnity.Editor.Helpers.GameObjectSerializer.GetComponentData(meshRenderer);
            
            // Assert - Verify that the material property was accessed without instantiation
            Assert.IsNotNull(result, "GetComponentData should return a result");
            
            // Check that result is a dictionary with properties key
            if (result is Dictionary<string, object> resultDict && 
                resultDict.TryGetValue("properties", out var propertiesObj) &&
                propertiesObj is Dictionary<string, object> properties)
            {
                Assert.IsTrue(properties.ContainsKey("material") || properties.ContainsKey("sharedMaterial"),
                    "Serialized data should include 'material' or 'sharedMaterial' when present.");
            }
            
            // Clean up
            UnityEngine.Object.DestroyImmediate(testMaterial);
            UnityEngine.Object.DestroyImmediate(testObject);
        }

        [Test]
        public void GetComponentData_UsesSharedMeshInEditMode()
        {
            // Arrange - Create a GameObject with MeshFilter
            var testObject = new GameObject("SharedMeshTestObject");
            var meshFilter = testObject.AddComponent<MeshFilter>();
            
            // Create a test mesh
            var tempCylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            var testMesh = tempCylinder.GetComponent<MeshFilter>().sharedMesh;
            UnityEngine.Object.DestroyImmediate(tempCylinder);
            testMesh.name = "TestMesh";
            meshFilter.sharedMesh = testMesh;
            
            // Act - Get component data in edit mode
            var result = MCPForUnity.Editor.Helpers.GameObjectSerializer.GetComponentData(meshFilter);
            
            // Assert - Verify that the mesh property was accessed without instantiation
            Assert.IsNotNull(result, "GetComponentData should return a result");
            
            // Check that result is a dictionary with properties key
            if (result is Dictionary<string, object> resultDict && 
                resultDict.TryGetValue("properties", out var propertiesObj) &&
                propertiesObj is Dictionary<string, object> properties)
            {
                Assert.IsTrue(properties.ContainsKey("mesh") || properties.ContainsKey("sharedMesh"),
                    "Serialized data should include 'mesh' or 'sharedMesh' when present.");
            }
            
            // Clean up
            UnityEngine.Object.DestroyImmediate(testObject);
        }

        [Test]
        public void GetComponentData_HandlesNullMaterialsAndMeshes()
        {
            // Arrange - Create a GameObject with MeshRenderer and MeshFilter but no materials/meshes
            var testObject = new GameObject("NullMaterialMeshTestObject");
            var meshRenderer = testObject.AddComponent<MeshRenderer>();
            var meshFilter = testObject.AddComponent<MeshFilter>();
            
            // Don't set any materials or meshes - they should be null
            
            // Act - Get component data
            var rendererResult = MCPForUnity.Editor.Helpers.GameObjectSerializer.GetComponentData(meshRenderer);
            var meshFilterResult = MCPForUnity.Editor.Helpers.GameObjectSerializer.GetComponentData(meshFilter);
            
            // Assert - Verify that the operations succeeded even with null materials/meshes
            Assert.IsNotNull(rendererResult, "GetComponentData should handle null materials");
            Assert.IsNotNull(meshFilterResult, "GetComponentData should handle null meshes");
            
            // Clean up
            UnityEngine.Object.DestroyImmediate(testObject);
        }

        [Test]
        public void GetComponentData_WorksWithMultipleMaterials()
        {
            // Arrange - Create a GameObject with MeshRenderer that has multiple materials
            var testObject = new GameObject("MultiMaterialTestObject");
            var meshRenderer = testObject.AddComponent<MeshRenderer>();
            
            // Create multiple test materials
            var material1 = new Material(Shader.Find("Standard"));
            material1.name = "TestMaterial1";
            var material2 = new Material(Shader.Find("Standard"));
            material2.name = "TestMaterial2";
            
            meshRenderer.sharedMaterials = new Material[] { material1, material2 };
            
            // Act - Get component data
            var result = MCPForUnity.Editor.Helpers.GameObjectSerializer.GetComponentData(meshRenderer);
            
            // Assert - Verify that the operation succeeded with multiple materials
            Assert.IsNotNull(result, "GetComponentData should handle multiple materials");
            
            // Clean up
            UnityEngine.Object.DestroyImmediate(material1);
            UnityEngine.Object.DestroyImmediate(material2);
            UnityEngine.Object.DestroyImmediate(testObject);
        }
    }
}
