using System;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using MCPForUnity.Editor.Tools;

namespace MCPForUnityTests.Editor.Tools
{
    public class ManageMaterialReproTests
    {
        private const string TempRoot = "Assets/Temp/ManageMaterialReproTests";
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
                AssetDatabase.CreateFolder("Assets/Temp", "ManageMaterialReproTests");
            }

            string guid = Guid.NewGuid().ToString("N");
            _matPath = $"{TempRoot}/ReproMat_{guid}.mat";
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
        public void CreateMaterial_WithInvalidJsonString_ReturnsGenericError()
        {
            // Arrange
            // Malformed JSON string (missing closing brace)
            string invalidJson = "{\"_Color\": [1,0,0,1]"; 
            
            var paramsObj = new JObject
            {
                ["action"] = "create",
                ["materialPath"] = _matPath,
                ["shader"] = "Standard",
                ["properties"] = invalidJson
            };

            // Act
            var result = ToJObject(ManageMaterial.HandleCommand(paramsObj));

            // Assert
            Assert.AreEqual("error", result.Value<string>("status"));
            
            // We expect more detailed error message after fix
            var message = result.Value<string>("message");
            Assert.IsTrue(message.StartsWith("Invalid JSON in properties"), "Message should start with prefix");
            Assert.AreNotEqual("Invalid JSON in properties", message, "Message should contain exception details");
        }
    }
}

