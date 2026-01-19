using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnityTests.Editor.Tools
{
    /// <summary>
    /// Tests for UIDocument component serialization.
    /// Reproduces issue #585: UIDocument component causes infinite loop when serializing components
    /// due to circular parent/child references in rootVisualElement.
    /// </summary>
    public class UIDocumentSerializationTests
    {
        private GameObject testGameObject;
        private PanelSettings testPanelSettings;
        private VisualTreeAsset testVisualTreeAsset;

        [SetUp]
        public void SetUp()
        {
            // Create a test GameObject
            testGameObject = new GameObject("UIDocumentTestObject");
            
            // Create PanelSettings asset (required for UIDocument to have a rootVisualElement)
            testPanelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            
            // Create a minimal VisualTreeAsset
            // Note: VisualTreeAsset cannot be created via CreateInstance, we need to use AssetDatabase
            // For the test, we'll create a temporary UXML file
            CreateTestVisualTreeAsset();
        }

        [TearDown]
        public void TearDown()
        {
            // Clean up test GameObject
            if (testGameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(testGameObject);
            }
            
            // Clean up ScriptableObject instances
            if (testPanelSettings != null)
            {
                UnityEngine.Object.DestroyImmediate(testPanelSettings);
            }
            
            // Clean up temporary UXML file
            CleanupTestVisualTreeAsset();
        }

        private void CreateTestVisualTreeAsset()
        {
            // Create a minimal UXML file for testing
            string uxmlPath = "Assets/Tests/EditMode/Tools/TestUIDocument.uxml";
            string uxmlContent = @"<ui:UXML xmlns:ui=""UnityEngine.UIElements"">
    <ui:VisualElement name=""root"">
        <ui:Label text=""Test Label"" />
    </ui:VisualElement>
</ui:UXML>";
            
            // Ensure directory exists
            string directory = System.IO.Path.GetDirectoryName(uxmlPath);
            if (!System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }
            
            System.IO.File.WriteAllText(uxmlPath, uxmlContent);
            AssetDatabase.Refresh();
            
            testVisualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);
        }

        private void CleanupTestVisualTreeAsset()
        {
            string uxmlPath = "Assets/Tests/EditMode/Tools/TestUIDocument.uxml";
            if (System.IO.File.Exists(uxmlPath))
            {
                AssetDatabase.DeleteAsset(uxmlPath);
            }
        }

        /// <summary>
        /// Test that UIDocument component can be serialized without infinite loops.
        /// This test reproduces issue #585 where UIDocument causes infinite loop 
        /// when both visualTreeAsset and panelSettings are assigned.
        /// 
        /// The bug: UIDocument.rootVisualElement returns a VisualElement with circular
        /// parent/child references (children[] → child elements → parent → back to parent).
        /// 
        /// Note: NUnit [Timeout] will fail this test if serialization hangs.
        /// </summary>
        [Test]
        [Timeout(10000)] // 10 second timeout - if serialization hangs, test fails
        public void GetComponentData_UIDocument_WithBothAssetsAssigned_DoesNotHang()
        {
            // Skip test if we couldn't create the VisualTreeAsset
            if (testVisualTreeAsset == null)
            {
                Assert.Inconclusive("Could not create test VisualTreeAsset - test cannot run");
            }

            // Arrange - Add UIDocument component with both assets assigned
            var uiDocument = testGameObject.AddComponent<UIDocument>();
            uiDocument.panelSettings = testPanelSettings;
            uiDocument.visualTreeAsset = testVisualTreeAsset;

            // Act - This should NOT hang or cause infinite loop
            // The [Timeout] attribute will fail the test if it takes too long
            object result = GameObjectSerializer.GetComponentData(uiDocument);

            // Assert
            Assert.IsNotNull(result, "Should return serialized component data");
            
            var resultDict = result as Dictionary<string, object>;
            Assert.IsNotNull(resultDict, "Result should be a dictionary");
            Assert.AreEqual("UnityEngine.UIElements.UIDocument", resultDict["typeName"]);
        }

        /// <summary>
        /// Test that UIDocument serialization includes expected properties.
        /// Verifies the structure matches Camera special handling (typeName, instanceID, properties).
        /// </summary>
        [Test]
        [Timeout(10000)]
        public void GetComponentData_UIDocument_ReturnsExpectedProperties()
        {
            // Skip test if we couldn't create the VisualTreeAsset
            if (testVisualTreeAsset == null)
            {
                Assert.Inconclusive("Could not create test VisualTreeAsset - test cannot run");
            }

            // Arrange
            var uiDocument = testGameObject.AddComponent<UIDocument>();
            uiDocument.panelSettings = testPanelSettings;
            uiDocument.visualTreeAsset = testVisualTreeAsset;
            uiDocument.sortingOrder = 42;

            // Act
            var result = GameObjectSerializer.GetComponentData(uiDocument);

            // Assert
            Assert.IsNotNull(result, "Should return serialized component data");
            
            var resultDict = result as Dictionary<string, object>;
            Assert.IsNotNull(resultDict, "Result should be a dictionary");
            
            // Check for expected top-level keys (matches Camera special handling structure)
            Assert.IsTrue(resultDict.ContainsKey("typeName"), "Should contain typeName");
            Assert.IsTrue(resultDict.ContainsKey("instanceID"), "Should contain instanceID");
            Assert.IsTrue(resultDict.ContainsKey("properties"), "Should contain properties");
            
            // Verify type name
            Assert.AreEqual("UnityEngine.UIElements.UIDocument", resultDict["typeName"], 
                "typeName should be UIDocument");
            
            // Verify properties dict contains expected keys
            var properties = resultDict["properties"] as Dictionary<string, object>;
            Assert.IsNotNull(properties, "properties should be a dictionary");
            Assert.IsTrue(properties.ContainsKey("panelSettings"), "Should have panelSettings");
            Assert.IsTrue(properties.ContainsKey("visualTreeAsset"), "Should have visualTreeAsset");
            Assert.IsTrue(properties.ContainsKey("sortingOrder"), "Should have sortingOrder");
            Assert.IsTrue(properties.ContainsKey("enabled"), "Should have enabled");
            Assert.IsTrue(properties.ContainsKey("_note"), "Should have _note about skipped rootVisualElement");
            
            // CRITICAL: Verify rootVisualElement is NOT included (this is the fix for Issue #585)
            Assert.IsFalse(properties.ContainsKey("rootVisualElement"), 
                "Should NOT include rootVisualElement - it causes circular reference loops");
            
            // Verify asset references use consistent structure (name, instanceID, assetPath)
            var panelSettingsRef = properties["panelSettings"] as Dictionary<string, object>;
            Assert.IsNotNull(panelSettingsRef, "panelSettings should be serialized as dictionary");
            Assert.IsTrue(panelSettingsRef.ContainsKey("name"), "panelSettings should have name");
            Assert.IsTrue(panelSettingsRef.ContainsKey("instanceID"), "panelSettings should have instanceID");
        }

        /// <summary>
        /// Test that UIDocument WITHOUT assets assigned doesn't cause issues.
        /// This is a baseline test - the bug only occurs with both assets assigned.
        /// </summary>
        [Test]
        public void GetComponentData_UIDocument_WithoutAssets_Succeeds()
        {
            // Arrange - Add UIDocument component WITHOUT assets
            var uiDocument = testGameObject.AddComponent<UIDocument>();
            // Don't assign panelSettings or visualTreeAsset

            // Act
            var result = GameObjectSerializer.GetComponentData(uiDocument);

            // Assert
            Assert.IsNotNull(result, "Should return serialized component data");
            
            var resultDict = result as Dictionary<string, object>;
            Assert.IsNotNull(resultDict, "Result should be a dictionary");
            Assert.AreEqual("UnityEngine.UIElements.UIDocument", resultDict["typeName"]);
        }

        /// <summary>
        /// Test that UIDocument with only panelSettings assigned doesn't cause issues.
        /// </summary>
        [Test]
        public void GetComponentData_UIDocument_WithOnlyPanelSettings_Succeeds()
        {
            // Arrange
            var uiDocument = testGameObject.AddComponent<UIDocument>();
            uiDocument.panelSettings = testPanelSettings;
            // Don't assign visualTreeAsset

            // Act
            var result = GameObjectSerializer.GetComponentData(uiDocument);

            // Assert
            Assert.IsNotNull(result, "Should return serialized component data");
        }

        /// <summary>
        /// Test that UIDocument with only visualTreeAsset assigned doesn't cause issues.
        /// </summary>
        [Test]
        public void GetComponentData_UIDocument_WithOnlyVisualTreeAsset_Succeeds()
        {
            // Skip test if we couldn't create the VisualTreeAsset
            if (testVisualTreeAsset == null)
            {
                Assert.Inconclusive("Could not create test VisualTreeAsset - test cannot run");
            }

            // Arrange
            var uiDocument = testGameObject.AddComponent<UIDocument>();
            uiDocument.visualTreeAsset = testVisualTreeAsset;
            // Don't assign panelSettings

            // Act
            var result = GameObjectSerializer.GetComponentData(uiDocument);

            // Assert
            Assert.IsNotNull(result, "Should return serialized component data");
        }
    }
}
