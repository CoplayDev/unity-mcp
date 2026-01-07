using System;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using MCPForUnity.Editor.Tools;
using static MCPForUnityTests.Editor.TestUtilities;

namespace MCPForUnityTests.Editor.Tools
{
    public class ManageMaterialPropertiesTests
    {
        private const string TempRoot = "Assets/Temp/ManageMaterialPropertiesTests";
        private string _matPath;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Temp"))
            {
                AssetDatabase.CreateFolder("Assets", "Temp");
            }
            if (!AssetDatabase.IsValidFolder(TempRoot))
            {
                AssetDatabase.CreateFolder("Assets/Temp", "ManageMaterialPropertiesTests");
            }
            _matPath = $"{TempRoot}/PropTest.mat";
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TempRoot))
            {
                AssetDatabase.DeleteAsset(TempRoot);
            }

            // Clean up empty parent folders to avoid debris
            CleanupEmptyParentFolders(TempRoot);
        }

        [Test]
        public void CreateMaterial_WithValidJsonStringArray_SetsProperty()
        {
            string jsonProps = "{\"_Color\": [1.0, 0.0, 0.0, 1.0]}";
            var paramsObj = new JObject
            {
                ["action"] = "create",
                ["materialPath"] = _matPath,
                ["shader"] = "Standard",
                ["properties"] = jsonProps
            };

            var result = ToJObject(ManageMaterial.HandleCommand(paramsObj));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var mat = AssetDatabase.LoadAssetAtPath<Material>(_matPath);
            Assert.AreEqual(Color.red, mat.color);
        }

        [Test]
        public void CreateMaterial_WithJObjectArray_SetsProperty()
        {
            var props = new JObject();
            props["_Color"] = new JArray(0.0f, 1.0f, 0.0f, 1.0f);

            var paramsObj = new JObject
            {
                ["action"] = "create",
                ["materialPath"] = _matPath,
                ["shader"] = "Standard",
                ["properties"] = props
            };

            var result = ToJObject(ManageMaterial.HandleCommand(paramsObj));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var mat = AssetDatabase.LoadAssetAtPath<Material>(_matPath);
            Assert.AreEqual(Color.green, mat.color);
        }

        [Test]
        public void CreateMaterial_WithEmptyProperties_Succeeds()
        {
            var paramsObj = new JObject
            {
                ["action"] = "create",
                ["materialPath"] = _matPath,
                ["shader"] = "Standard",
                ["properties"] = new JObject()
            };

            var result = ToJObject(ManageMaterial.HandleCommand(paramsObj));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
        }

        [Test]
        public void CreateMaterial_WithInvalidJsonSyntax_ReturnsDetailedError()
        {
            // Missing closing brace
            string invalidJson = "{\"_Color\": [1,0,0,1]"; 
            
            var paramsObj = new JObject
            {
                ["action"] = "create",
                ["materialPath"] = _matPath,
                ["shader"] = "Standard",
                ["properties"] = invalidJson
            };

            var result = ToJObject(ManageMaterial.HandleCommand(paramsObj));

            Assert.IsFalse(result.Value<bool>("success"));
            string msg = result.Value<string>("error");
            
            // Verify we get exception details
            Assert.IsTrue(msg.Contains("Invalid JSON"), "Should mention Invalid JSON");
            // Verify the message contains more than just the prefix (has exception details)
            Assert.IsTrue(msg.Length > "Invalid JSON".Length, 
                $"Message should contain exception details. Got: {msg}");
        }

        [Test]
        public void CreateMaterial_WithNullProperty_HandlesGracefully()
        {
             var props = new JObject();
            props["_Color"] = null;

            var paramsObj = new JObject
            {
                ["action"] = "create",
                ["materialPath"] = _matPath,
                ["shader"] = "Standard",
                ["properties"] = props
            };

            // Should probably succeed but warn or ignore, or fail gracefully
            var result = ToJObject(ManageMaterial.HandleCommand(paramsObj));
            
            // We accept either success (ignored) or specific error, but not crash
            // The new response format uses a bool "success" field
            var success = result.Value<bool?>("success");
            Assert.IsNotNull(success, "Response should have success field"); 
        }
    }
}


