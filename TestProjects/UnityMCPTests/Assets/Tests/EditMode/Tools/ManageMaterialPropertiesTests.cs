using System;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using MCPForUnity.Editor.Tools;

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
        }

        private static JObject ToJObject(object result)
        {
            return result as JObject ?? JObject.FromObject(result);
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

            Assert.AreEqual("success", result.Value<string>("status"), result.ToString());
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

            Assert.AreEqual("success", result.Value<string>("status"), result.ToString());
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

            Assert.AreEqual("success", result.Value<string>("status"));
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

            Assert.AreEqual("error", result.Value<string>("status"));
            string msg = result.Value<string>("message");
            
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
            // Assert.AreNotEqual("internal_error", result.Value<string>("status")); 
            var status = result.Value<string>("status");
            Assert.IsTrue(status == "success" || status == "error", $"Status should be success or error, got {status}"); 
        }
    }
}


