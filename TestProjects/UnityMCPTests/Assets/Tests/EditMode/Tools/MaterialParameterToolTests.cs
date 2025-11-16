using System;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using MCPForUnity.Editor.Tools;

namespace MCPForUnityTests.Editor.Tools
{
    public class MaterialParameterToolTests
    {
        private const string TempRoot = "Assets/Temp/MaterialParameterToolTests";
        private string _matPath; // unique per test run
        private GameObject _sphere;

        [SetUp]
        public void SetUp()
        {
            _matPath = $"{TempRoot}/BlueURP_{Guid.NewGuid().ToString("N")}.mat";
            if (!AssetDatabase.IsValidFolder("Assets/Temp"))
            {
                AssetDatabase.CreateFolder("Assets", "Temp");
            }
            if (!AssetDatabase.IsValidFolder(TempRoot))
            {
                AssetDatabase.CreateFolder("Assets/Temp", "MaterialParameterToolTests");
            }
            // Ensure any leftover material from previous runs is removed
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(_matPath) != null)
            {
                AssetDatabase.DeleteAsset(_matPath);
                AssetDatabase.Refresh();
            }
            // Hard-delete any stray files on disk (in case GUID lookup fails)
            var abs = Path.Combine(Directory.GetCurrentDirectory(), _matPath);
            try
            {
                if (File.Exists(abs)) File.Delete(abs);
                if (File.Exists(abs + ".meta")) File.Delete(abs + ".meta");
            }
            catch { /* best-effort cleanup */ }
            AssetDatabase.Refresh();
        }

        [TearDown]
        public void TearDown()
        {
            if (_sphere != null)
            {
                UnityEngine.Object.DestroyImmediate(_sphere);
                _sphere = null;
            }
            if (AssetDatabase.LoadAssetAtPath<Material>(_matPath) != null)
            {
                AssetDatabase.DeleteAsset(_matPath);
            }
            
            // Clean up temp directory after each test
            if (AssetDatabase.IsValidFolder(TempRoot))
            {
                AssetDatabase.DeleteAsset(TempRoot);
            }
            
            // Clean up parent Temp folder if it's empty
            if (AssetDatabase.IsValidFolder("Assets/Temp"))
            {
                var remainingDirs = Directory.GetDirectories("Assets/Temp");
                var remainingFiles = Directory.GetFiles("Assets/Temp");
                if (remainingDirs.Length == 0 && remainingFiles.Length == 0)
                {
                    AssetDatabase.DeleteAsset("Assets/Temp");
                }
            }
            
            AssetDatabase.Refresh();
        }

        private static JObject ToJObject(object result)
        {
            return result as JObject ?? JObject.FromObject(result);
        }

        [Test]
        public void CreateMaterial_WithObjectProperties_SucceedsAndSetsColor()
        {
            // Ensure a clean state if a previous run left the asset behind (uses _matPath now)
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(_matPath) != null)
            {
                AssetDatabase.DeleteAsset(_matPath);
                AssetDatabase.Refresh();
            }
            var createParams = new JObject
            {
                ["action"] = "create",
                ["path"] = _matPath,
                ["assetType"] = "Material",
                ["properties"] = new JObject
                {
                    ["shader"] = "Universal Render Pipeline/Lit",
                    ["color"] = new JArray(0f, 0f, 1f, 1f)
                }
            };

            var result = ToJObject(ManageAsset.HandleCommand(createParams));
            Assert.IsTrue(result.Value<bool>("success"), result.Value<string>("error"));

            var mat = AssetDatabase.LoadAssetAtPath<Material>(_matPath);
            Assert.IsNotNull(mat, "Material should exist at path.");
            // Verify color if shader exposes _Color
            if (mat.HasProperty("_Color"))
            {
                Assert.AreEqual(Color.blue, mat.GetColor("_Color"));
            }
        }

        [Test]
        public void AssignMaterial_ToSphere_UsingComponentPropertiesObject_Succeeds()
        {
            // Ensure material exists first
            CreateMaterial_WithObjectProperties_SucceedsAndSetsColor();

            // Create a sphere via handler
            var createGo = new JObject
            {
                ["action"] = "create",
                ["name"] = "ToolTestSphere",
                ["primitiveType"] = "Sphere"
            };
            var createGoResult = ToJObject(ManageGameObject.HandleCommand(createGo));
            Assert.IsTrue(createGoResult.Value<bool>("success"), createGoResult.Value<string>("error"));

            _sphere = GameObject.Find("ToolTestSphere");
            Assert.IsNotNull(_sphere, "Sphere should be created.");

            // Assign material via object-typed componentProperties
            var modifyParams = new JObject
            {
                ["action"] = "modify",
                ["target"] = "ToolTestSphere",
                ["searchMethod"] = "by_name",
                ["componentProperties"] = new JObject
                {
                    ["MeshRenderer"] = new JObject
                    {
                        ["sharedMaterial"] = _matPath
                    }
                }
            };

            var modifyResult = ToJObject(ManageGameObject.HandleCommand(modifyParams));
            Assert.IsTrue(modifyResult.Value<bool>("success"), modifyResult.Value<string>("error"));

            var renderer = _sphere.GetComponent<MeshRenderer>();
            Assert.IsNotNull(renderer, "Sphere should have MeshRenderer.");
            Assert.IsNotNull(renderer.sharedMaterial, "sharedMaterial should be assigned.");
            StringAssert.StartsWith("BlueURP_", renderer.sharedMaterial.name);
        }

        [Test]
        public void ReadRendererData_DoesNotInstantiateMaterial_AndIncludesSharedMaterial()
        {
            // Prepare object and assignment
            AssignMaterial_ToSphere_UsingComponentPropertiesObject_Succeeds();

            var renderer = _sphere.GetComponent<MeshRenderer>();
            int beforeId = renderer.sharedMaterial != null ? renderer.sharedMaterial.GetInstanceID() : 0;

            var data = MCPForUnity.Editor.Helpers.GameObjectSerializer.GetComponentData(renderer) as System.Collections.Generic.Dictionary<string, object>;
            Assert.IsNotNull(data, "Serializer should return data.");

            int afterId = renderer.sharedMaterial != null ? renderer.sharedMaterial.GetInstanceID() : 0;
            Assert.AreEqual(beforeId, afterId, "sharedMaterial instance must not change (no instantiation in EditMode).");

            if (data.TryGetValue("properties", out var propsObj) && propsObj is System.Collections.Generic.Dictionary<string, object> props)
            {
                Assert.IsTrue(
                    props.ContainsKey("sharedMaterial") || props.ContainsKey("material") || props.ContainsKey("sharedMaterials") || props.ContainsKey("materials"),
                    "Serialized data should include material info.");
            }
        }
    }
}