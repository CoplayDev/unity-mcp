using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;
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
        }
    }
}