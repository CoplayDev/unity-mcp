using System;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using MCPForUnity.Editor.Tools;
using static MCPForUnityTests.Editor.TestUtilities;

namespace MCPForUnityTests.Editor.Tools
{
    public class ManageMaterialStressTests
    {
        private const string TempRoot = "Assets/Temp/ManageMaterialStressTests";
        private string _matPath;
        private GameObject _cube;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Temp"))
            {
                AssetDatabase.CreateFolder("Assets", "Temp");
            }
            if (!AssetDatabase.IsValidFolder(TempRoot))
            {
                AssetDatabase.CreateFolder("Assets/Temp", "ManageMaterialStressTests");
            }

            string guid = Guid.NewGuid().ToString("N");
            _matPath = $"{TempRoot}/StressMat_{guid}.mat";
            
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            material.color = Color.white;
            AssetDatabase.CreateAsset(material, _matPath);
            AssetDatabase.SaveAssets();

            _cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _cube.name = "StressCube";
        }

        [TearDown]
        public void TearDown()
        {
            if (_cube != null)
            {
                UnityEngine.Object.DestroyImmediate(_cube);
            }
            
            if (AssetDatabase.IsValidFolder(TempRoot))
            {
                AssetDatabase.DeleteAsset(TempRoot);
            }
            
            // Clean up empty parent folders to avoid debris
            CleanupEmptyParentFolders(TempRoot);
        }

        [Test]
        public void HandleInvalidInputs_ReturnsError_NotException()
        {
            // 1. Bad path
            var paramsBadPath = new JObject
            {
                ["action"] = "set_material_color",
                ["materialPath"] = "Assets/NonExistent/Ghost.mat",
                ["color"] = new JArray(1f, 0f, 0f, 1f)
            };
            var resultBadPath = ToJObject(ManageMaterial.HandleCommand(paramsBadPath));
            Assert.IsFalse(resultBadPath.Value<bool>("success"));
            StringAssert.Contains("Could not find material", resultBadPath.Value<string>("error"));

            // 2. Bad color array (too short)
            var paramsBadColor = new JObject
            {
                ["action"] = "set_material_color",
                ["materialPath"] = _matPath,
                ["color"] = new JArray(1f) // Invalid
            };
            var resultBadColor = ToJObject(ManageMaterial.HandleCommand(paramsBadColor));
            Assert.IsFalse(resultBadColor.Value<bool>("success"));
            StringAssert.Contains("Invalid color format", resultBadColor.Value<string>("error"));

             // 3. Bad slot index
             // Assign material first
            var renderer = _cube.GetComponent<Renderer>();
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(_matPath);

            var paramsBadSlot = new JObject
            {
                ["action"] = "assign_material_to_renderer",
                ["target"] = "StressCube",
                ["searchMethod"] = "by_name",
                ["materialPath"] = _matPath,
                ["slot"] = 99
            };
            var resultBadSlot = ToJObject(ManageMaterial.HandleCommand(paramsBadSlot));
            Assert.IsFalse(resultBadSlot.Value<bool>("success"));
            StringAssert.Contains("out of bounds", resultBadSlot.Value<string>("error"));
        }

        [Test]
        public void StateIsolation_PropertyBlockDoesNotLeakToSharedMaterial()
        {
            // Arrange
            var renderer = _cube.GetComponent<Renderer>();
            var sharedMat = AssetDatabase.LoadAssetAtPath<Material>(_matPath);
            renderer.sharedMaterial = sharedMat;
            
            // Initial color
            var initialColor = Color.white;
            if (sharedMat.HasProperty("_BaseColor")) sharedMat.SetColor("_BaseColor", initialColor);
            else if (sharedMat.HasProperty("_Color")) sharedMat.SetColor("_Color", initialColor);
            
            // Act - Set Property Block Color
            var blockColor = Color.red;
            var paramsObj = new JObject
            {
                ["action"] = "set_renderer_color",
                ["target"] = "StressCube",
                ["searchMethod"] = "by_name",
                ["color"] = new JArray(blockColor.r, blockColor.g, blockColor.b, blockColor.a),
                ["mode"] = "property_block"
            };
            
            var result = ToJObject(ManageMaterial.HandleCommand(paramsObj));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            // Assert
            // 1. Renderer has property block with Red
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block, 0);
            var propName = sharedMat.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
            Assert.AreEqual(blockColor, block.GetColor(propName));

            // 2. Shared material remains White
            var sharedColor = sharedMat.GetColor(propName);
            Assert.AreEqual(initialColor, sharedColor, "Shared material color should NOT change when using PropertyBlock");
        }

        [Test]
        public void Integration_PureManageMaterial_AssignsMaterialAndModifies()
        {
             // This simulates a workflow where we create a GO, assign a mat, then tweak it.
             
             // 1. Create GO (already done in Setup, but let's verify)
             Assert.IsNotNull(_cube);
             
             // 2. Assign Material using ManageMaterial
             var assignParams = new JObject
             {
                 ["action"] = "assign_material_to_renderer",
                 ["target"] = "StressCube",
                 ["searchMethod"] = "by_name",
                 ["materialPath"] = _matPath
             };
             var assignResult = ToJObject(ManageMaterial.HandleCommand(assignParams));
             Assert.IsTrue(assignResult.Value<bool>("success"), assignResult.ToString());
             
             // Verify assignment
             var renderer = _cube.GetComponent<Renderer>();
             Assert.AreEqual(Path.GetFileNameWithoutExtension(_matPath), renderer.sharedMaterial.name);
             
             // 3. Modify Shared Material Color using ManageMaterial
             var newColor = Color.blue;
             var colorParams = new JObject
             {
                 ["action"] = "set_material_color",
                 ["materialPath"] = _matPath,
                 ["color"] = new JArray(newColor.r, newColor.g, newColor.b, newColor.a)
             };
             var colorResult = ToJObject(ManageMaterial.HandleCommand(colorParams));
             Assert.IsTrue(colorResult.Value<bool>("success"), colorResult.ToString());
             
             // Verify color changed on renderer (because it's shared)
             var propName = renderer.sharedMaterial.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
             Assert.AreEqual(newColor, renderer.sharedMaterial.GetColor(propName));
        }
    }
}

