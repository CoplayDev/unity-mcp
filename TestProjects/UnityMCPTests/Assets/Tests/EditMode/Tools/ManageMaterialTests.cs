using System;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using MCPForUnity.Editor.Tools;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnityTests.Editor.Tools
{
    public class ManageMaterialTests
    {
        private const string TempRoot = "Assets/Temp/ManageMaterialTests";
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
                AssetDatabase.CreateFolder("Assets/Temp", "ManageMaterialTests");
            }

            string guid = Guid.NewGuid().ToString("N");
            _matPath = $"{TempRoot}/TestMat_{guid}.mat";
            
            // Create a basic material
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            AssetDatabase.CreateAsset(material, _matPath);
            AssetDatabase.SaveAssets();
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TempRoot))
            {
                AssetDatabase.DeleteAsset(TempRoot);
            }
            
            // Clean up parent Temp folder if it's empty
            if (AssetDatabase.IsValidFolder("Assets/Temp"))
            {
                // Only delete if empty
                var subFolders = AssetDatabase.GetSubFolders("Assets/Temp");
                if (subFolders.Length == 0)
                {
                    AssetDatabase.DeleteAsset("Assets/Temp");
                }
            }
        }

        private static JObject ToJObject(object result)
        {
            return result as JObject ?? JObject.FromObject(result);
        }

        [Test]
        public void SetMaterialShaderProperty_SetsColor()
        {
            // Arrange
            var color = new Color(1f, 1f, 0f, 1f); // Yellow
            var paramsObj = new JObject
            {
                ["action"] = "set_material_shader_property",
                ["materialPath"] = _matPath,
                ["property"] = "_BaseColor", // URP
                ["value"] = new JArray(color.r, color.g, color.b, color.a)
            };
            
            // Check if using Standard shader (fallback)
            var mat = AssetDatabase.LoadAssetAtPath<Material>(_matPath);
            if (mat.shader.name == "Standard")
            {
                paramsObj["property"] = "_Color";
            }

            // Act
            var result = ToJObject(ManageMaterial.HandleCommand(paramsObj));

            // Assert
            Assert.AreEqual("success", result.Value<string>("status"), result.ToString());
            
            mat = AssetDatabase.LoadAssetAtPath<Material>(_matPath); // Reload
            var prop = mat.shader.name == "Standard" ? "_Color" : "_BaseColor";
            
            Assert.IsTrue(mat.HasProperty(prop), $"Material should have property {prop}");
            Assert.AreEqual(color, mat.GetColor(prop));
        }

        [Test]
        public void SetMaterialColor_SetsColorWithFallback()
        {
            // Arrange
            var color = new Color(0f, 1f, 0f, 1f); // Green
            var paramsObj = new JObject
            {
                ["action"] = "set_material_color",
                ["materialPath"] = _matPath,
                ["color"] = new JArray(color.r, color.g, color.b, color.a)
            };

            // Act
            var result = ToJObject(ManageMaterial.HandleCommand(paramsObj));

            // Assert
            Assert.AreEqual("success", result.Value<string>("status"), result.ToString());
            
            var mat = AssetDatabase.LoadAssetAtPath<Material>(_matPath);
            var prop = mat.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
            
            Assert.IsTrue(mat.HasProperty(prop), $"Material should have property {prop}");
            Assert.AreEqual(color, mat.GetColor(prop));
        }

        [Test]
        public void AssignMaterialToRenderer_Works()
        {
            // Arrange
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "AssignTestCube";
            
            try
            {
                var paramsObj = new JObject
                {
                    ["action"] = "assign_material_to_renderer",
                    ["target"] = "AssignTestCube",
                    ["searchMethod"] = "by_name",
                    ["materialPath"] = _matPath,
                    ["slot"] = 0
                };

                // Act
                var result = ToJObject(ManageMaterial.HandleCommand(paramsObj));

                // Assert
                Assert.AreEqual("success", result.Value<string>("status"), result.ToString());
                
                var renderer = go.GetComponent<Renderer>();
                Assert.IsNotNull(renderer.sharedMaterial);
                // Compare names because objects might be different instances (loaded vs scene)
                var matName = Path.GetFileNameWithoutExtension(_matPath);
                Assert.AreEqual(matName, renderer.sharedMaterial.name);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
        
        [Test]
        public void SetRendererColor_PropertyBlock_Works()
        {
             // Arrange
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "BlockTestCube";
            
            // Assign the material first so we have something valid
            var mat = AssetDatabase.LoadAssetAtPath<Material>(_matPath);
            go.GetComponent<Renderer>().sharedMaterial = mat;

            try
            {
                var color = new Color(1f, 0f, 0f, 1f); // Red
                var paramsObj = new JObject
                {
                    ["action"] = "set_renderer_color",
                    ["target"] = "BlockTestCube",
                    ["searchMethod"] = "by_name",
                    ["color"] = new JArray(color.r, color.g, color.b, color.a),
                    ["mode"] = "property_block"
                };

                // Act
                var result = ToJObject(ManageMaterial.HandleCommand(paramsObj));

                // Assert
                Assert.AreEqual("success", result.Value<string>("status"), result.ToString());
                
                var renderer = go.GetComponent<Renderer>();
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block, 0);
                
                var prop = mat.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
                Assert.AreEqual(color, block.GetColor(prop));
                
                // Verify material asset didn't change (it was originally white/gray from setup?)
                // We didn't check original color, but property block shouldn't affect shared material
                // We can check that sharedMaterial color is NOT red if we set it to something else first
                // But assuming test isolation, we can just verify the block is set.
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void GetMaterialInfo_ReturnsProperties()
        {
             // Arrange
            var paramsObj = new JObject
            {
                ["action"] = "get_material_info",
                ["materialPath"] = _matPath
            };

            // Act
            var result = ToJObject(ManageMaterial.HandleCommand(paramsObj));

            // Assert
            Assert.AreEqual("success", result.Value<string>("status"), result.ToString());
            Assert.IsNotNull(result["properties"]);
            Assert.IsInstanceOf<JArray>(result["properties"]);
            var props = result["properties"] as JArray;
            Assert.IsTrue(props.Count > 0);
            
            // Check for standard properties
            bool foundColor = false;
            foreach(var p in props)
            {
                var name = p["name"]?.ToString();
                if (name == "_Color" || name == "_BaseColor") foundColor = true;
            }
            Assert.IsTrue(foundColor, "Should find color property");
        }
    }
}
