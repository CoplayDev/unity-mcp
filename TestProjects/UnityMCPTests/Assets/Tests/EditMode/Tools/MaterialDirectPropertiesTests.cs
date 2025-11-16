using System;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using MCPForUnity.Editor.Tools;

namespace MCPForUnityTests.Editor.Tools
{
    public class MaterialDirectPropertiesTests
    {
        private const string TempRoot = "Assets/Temp/MaterialDirectPropertiesTests";
        private string _matPath;
        private string _baseMapPath;
        private string _normalMapPath;
        private string _occlusionMapPath;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Temp"))
            {
                AssetDatabase.CreateFolder("Assets", "Temp");
            }
            if (!AssetDatabase.IsValidFolder(TempRoot))
            {
                AssetDatabase.CreateFolder("Assets/Temp", "MaterialDirectPropertiesTests");
            }

            string guid = Guid.NewGuid().ToString("N");
            _matPath = $"{TempRoot}/DirectProps_{guid}.mat";
            _baseMapPath = $"{TempRoot}/TexBase_{guid}.asset";
            _normalMapPath = $"{TempRoot}/TexNormal_{guid}.asset";
            _occlusionMapPath = $"{TempRoot}/TexOcc_{guid}.asset";

            // Clean any leftovers just in case
            TryDeleteAsset(_matPath);
            TryDeleteAsset(_baseMapPath);
            TryDeleteAsset(_normalMapPath);
            TryDeleteAsset(_occlusionMapPath);

            AssetDatabase.Refresh();
        }

        [TearDown]
        public void TearDown()
        {
            TryDeleteAsset(_matPath);
            TryDeleteAsset(_baseMapPath);
            TryDeleteAsset(_normalMapPath);
            TryDeleteAsset(_occlusionMapPath);
            
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

        private static void TryDeleteAsset(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) != null)
            {
                AssetDatabase.DeleteAsset(path);
            }
            var abs = Path.Combine(Directory.GetCurrentDirectory(), path);
            try
            {
                if (File.Exists(abs)) File.Delete(abs);
                if (File.Exists(abs + ".meta")) File.Delete(abs + ".meta");
            }
            catch { }
        }

        private static Texture2D CreateSolidTextureAsset(string path, Color color)
        {
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var pixels = new Color[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            tex.SetPixels(pixels);
            tex.Apply();
            AssetDatabase.CreateAsset(tex, path);
            AssetDatabase.SaveAssets();
            return tex;
        }

        private static JObject ToJObject(object result)
        {
            return result as JObject ?? JObject.FromObject(result);
        }

        [Test]
        public void CreateAndModifyMaterial_WithDirectPropertyKeys_Works()
        {
            // Arrange: create textures as assets
            CreateSolidTextureAsset(_baseMapPath, Color.white);
            CreateSolidTextureAsset(_normalMapPath, new Color(0.5f, 0.5f, 1f));
            CreateSolidTextureAsset(_occlusionMapPath, Color.gray);

            // Create material using direct keys via JSON string
            var createParams = new JObject
            {
                ["action"] = "create",
                ["path"] = _matPath,
                ["assetType"] = "Material",
                ["properties"] = new JObject
                {
                    ["shader"] = "Universal Render Pipeline/Lit",
                    ["_Color"] = new JArray(0f, 1f, 0f, 1f),
                    ["_Glossiness"] = 0.25f
                }
            };
            var createRes = ToJObject(ManageAsset.HandleCommand(createParams));
            Assert.IsTrue(createRes.Value<bool>("success"), createRes.ToString());

            // Modify with aliases and textures
            var modifyParams = new JObject
            {
                ["action"] = "modify",
                ["path"] = _matPath,
                ["properties"] = new JObject
                {
                    ["_BaseColor"] = new JArray(0f, 0f, 1f, 1f),
                    ["_Smoothness"] = 0.5f,
                    ["_BaseMap"] = _baseMapPath,
                    ["_BumpMap"] = _normalMapPath,
                    ["_OcclusionMap"] = _occlusionMapPath
                }
            };
            var modifyRes = ToJObject(ManageAsset.HandleCommand(modifyParams));
            Assert.IsTrue(modifyRes.Value<bool>("success"), modifyRes.ToString());

            var mat = AssetDatabase.LoadAssetAtPath<Material>(_matPath);
            Assert.IsNotNull(mat, "Material should exist at path.");

            // Verify color alias applied
            if (mat.HasProperty("_BaseColor"))
            {
                Assert.AreEqual(Color.blue, mat.GetColor("_BaseColor"));
            }
            else if (mat.HasProperty("_Color"))
            {
                Assert.AreEqual(Color.blue, mat.GetColor("_Color"));
            }

            // Verify float
            string smoothProp = mat.HasProperty("_Smoothness") ? "_Smoothness" : (mat.HasProperty("_Glossiness") ? "_Glossiness" : null);
            Assert.IsNotNull(smoothProp, "Material should expose Smoothness/Glossiness.");
            Assert.That(Mathf.Abs(mat.GetFloat(smoothProp) - 0.5f) < 1e-4f);

            // Verify textures
            string baseMapProp = mat.HasProperty("_BaseMap") ? "_BaseMap" : (mat.HasProperty("_MainTex") ? "_MainTex" : null);
            Assert.IsNotNull(baseMapProp, "Material should expose BaseMap/MainTex.");
            Assert.IsNotNull(mat.GetTexture(baseMapProp), "BaseMap/MainTex should be assigned.");
            if (mat.HasProperty("_BumpMap")) Assert.IsNotNull(mat.GetTexture("_BumpMap"));
            if (mat.HasProperty("_OcclusionMap")) Assert.IsNotNull(mat.GetTexture("_OcclusionMap"));
        }
    }
}


