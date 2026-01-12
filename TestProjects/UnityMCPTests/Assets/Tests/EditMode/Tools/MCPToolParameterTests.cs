using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEditor;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Tools;
using MCPForUnity.Editor.Tools.GameObjects;
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace MCPForUnityTests.Editor.Tools
{
    /// <summary>
    /// Tests for MCP tool parameter handling - JSON parsing in manage_asset and manage_gameobject tools.
    /// Consolidated from multiple redundant tests into focused, non-overlapping test cases.
    /// </summary>
    public class MCPToolParameterTests
    {
        private const string TempDir = "Assets/Temp/MCPToolParameterTests";
        private const string TempLiveDir = "Assets/Temp/LiveTests";

        private static void AssertColorsEqual(Color expected, Color actual, string message)
        {
            const float tolerance = 0.001f;
            Assert.AreEqual(expected.r, actual.r, tolerance, $"{message} - Red component mismatch");
            Assert.AreEqual(expected.g, actual.g, tolerance, $"{message} - Green component mismatch");
            Assert.AreEqual(expected.b, actual.b, tolerance, $"{message} - Blue component mismatch");
            Assert.AreEqual(expected.a, actual.a, tolerance, $"{message} - Alpha component mismatch");
        }

        private static void AssertShaderIsSupported(Shader s)
        {
            Assert.IsNotNull(s, "Shader should not be null");
            var name = s.name;
            bool ok = name == "Universal Render Pipeline/Lit"
                || name == "HDRP/Lit"
                || name == "Standard"
                || name == "Unlit/Color";
            Assert.IsTrue(ok, $"Unexpected shader: {name}");
        }

        private static void EnsureTempFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Temp"))
                AssetDatabase.CreateFolder("Assets", "Temp");
            if (!AssetDatabase.IsValidFolder(TempDir))
                AssetDatabase.CreateFolder("Assets/Temp", "MCPToolParameterTests");
            if (!AssetDatabase.IsValidFolder(TempLiveDir))
                AssetDatabase.CreateFolder("Assets/Temp", "LiveTests");
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TempDir))
                AssetDatabase.DeleteAsset(TempDir);
            if (AssetDatabase.IsValidFolder(TempLiveDir))
                AssetDatabase.DeleteAsset(TempLiveDir);

            if (AssetDatabase.IsValidFolder("Assets/Temp"))
            {
                var remainingDirs = Directory.GetDirectories("Assets/Temp");
                var remainingFiles = Directory.GetFiles("Assets/Temp");
                if (remainingDirs.Length == 0 && remainingFiles.Length == 0)
                    AssetDatabase.DeleteAsset("Assets/Temp");
            }
        }

        /// <summary>
        /// Tests GameObject componentProperties JSON string coercion path.
        /// Verifies material assignment via JSON string works correctly.
        /// </summary>
        [Test]
        public void ManageGameObject_JSONComponentProperties_AssignsMaterial()
        {
            EnsureTempFolders();
            var matPath = $"{TempDir}/Mat_{Guid.NewGuid():N}.mat";

            // Create material with object-typed properties
            var createMat = new JObject
            {
                ["action"] = "create",
                ["path"] = matPath,
                ["assetType"] = "Material",
                ["properties"] = new JObject { ["shader"] = "Universal Render Pipeline/Lit", ["color"] = new JArray(0, 0, 1, 1) }
            };
            var createMatRes = ManageAsset.HandleCommand(createMat);
            var createMatObj = createMatRes as JObject ?? JObject.FromObject(createMatRes);
            Assert.IsTrue(createMatObj.Value<bool>("success"), createMatObj.ToString());

            // Create a sphere
            var createGo = new JObject { ["action"] = "create", ["name"] = "MCPParamTestSphere", ["primitiveType"] = "Sphere" };
            var createGoRes = ManageGameObject.HandleCommand(createGo);
            var createGoObj = createGoRes as JObject ?? JObject.FromObject(createGoRes);
            Assert.IsTrue(createGoObj.Value<bool>("success"), createGoObj.ToString());

            try
            {
                // Assign material via JSON string componentProperties (coercion path)
                var compJsonObj = new JObject { ["MeshRenderer"] = new JObject { ["sharedMaterial"] = matPath } };
                var compJson = compJsonObj.ToString(Newtonsoft.Json.Formatting.None);
                var modify = new JObject
                {
                    ["action"] = "modify",
                    ["target"] = "MCPParamTestSphere",
                    ["searchMethod"] = "by_name",
                    ["componentProperties"] = compJson
                };
                var raw = ManageGameObject.HandleCommand(modify);
                var result = raw as JObject ?? JObject.FromObject(raw);
                Assert.IsTrue(result.Value<bool>("success"), result.ToString());

                // Verify material assignment
                var goVerify = GameObject.Find("MCPParamTestSphere");
                Assert.IsNotNull(goVerify, "GameObject should exist");
                var renderer = goVerify.GetComponent<MeshRenderer>();
                Assert.IsNotNull(renderer, "MeshRenderer should exist");
                var assignedMat = renderer.sharedMaterial;
                Assert.IsNotNull(assignedMat, "sharedMaterial should be assigned");
                AssertShaderIsSupported(assignedMat.shader);
                var createdMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                Assert.AreEqual(createdMat, assignedMat, "Assigned material should match created material");
            }
            finally
            {
                var go = GameObject.Find("MCPParamTestSphere");
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(matPath) != null)
                    AssetDatabase.DeleteAsset(matPath);
                AssetDatabase.Refresh();
            }
        }

        /// <summary>
        /// Comprehensive end-to-end test covering all 10 property handling scenarios:
        /// 1. Create material via JSON string
        /// 2. Modify color and metallic (friendly names)
        /// 3. Modify using structured float block
        /// 4. Assign texture via direct prop alias
        /// 5. Assign texture via structured block
        /// 6. Create sphere and assign material via componentProperties JSON
        /// 7. Use URP color alias key
        /// 8. Invalid JSON handling (graceful degradation)
        /// 9. Switch shader pipeline dynamically
        /// 10. Mixed friendly and alias keys
        /// </summary>
        [Test]
        public void EndToEnd_PropertyHandling_AllScenarios()
        {
            EnsureTempFolders();

            string guidSuffix = Guid.NewGuid().ToString("N").Substring(0, 8);
            string matPath = $"{TempLiveDir}/Mat_{guidSuffix}.mat";
            string texPath = $"{TempLiveDir}/TempBaseTex_{guidSuffix}.asset";
            string sphereName = $"LiveSphere_{guidSuffix}";
            string badJsonPath = $"{TempLiveDir}/BadJson_{guidSuffix}.mat";

            // Ensure clean state
            var preSphere = GameObject.Find(sphereName);
            if (preSphere != null) UnityEngine.Object.DestroyImmediate(preSphere);
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(matPath) != null)
                AssetDatabase.DeleteAsset(matPath);
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(badJsonPath) != null)
                AssetDatabase.DeleteAsset(badJsonPath);
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(texPath) != null)
                AssetDatabase.DeleteAsset(texPath);

            // Create test texture for texture-dependent scenarios (4, 5, 10)
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var pixels = new Color[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
            tex.SetPixels(pixels);
            tex.Apply();
            AssetDatabase.CreateAsset(tex, texPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            try
            {
                // 1. Create material via JSON string
                var createParams = new JObject
                {
                    ["action"] = "create",
                    ["path"] = matPath,
                    ["assetType"] = "Material",
                    ["properties"] = "{\"shader\":\"Universal Render Pipeline/Lit\",\"color\":[1,0,0,1]}"
                };
                var createRaw = ManageAsset.HandleCommand(createParams);
                var createResult = createRaw as JObject ?? JObject.FromObject(createRaw);
                Assert.IsTrue(createResult.Value<bool>("success"), $"Test 1 failed: {createResult}");
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                Assert.IsNotNull(mat, "Material should be created");
                if (mat.HasProperty("_BaseColor"))
                    Assert.AreEqual(Color.red, mat.GetColor("_BaseColor"), "Test 1: _BaseColor should be red");
                else if (mat.HasProperty("_Color"))
                    Assert.AreEqual(Color.red, mat.GetColor("_Color"), "Test 1: _Color should be red");
                else
                    Assert.Inconclusive("Material has neither _BaseColor nor _Color");

                // 2. Modify color and metallic (friendly names)
                var modify1 = new JObject
                {
                    ["action"] = "modify",
                    ["path"] = matPath,
                    ["properties"] = "{\"color\":[0,0.5,1,1],\"metallic\":0.6}"
                };
                var modifyRaw1 = ManageAsset.HandleCommand(modify1);
                var modifyResult1 = modifyRaw1 as JObject ?? JObject.FromObject(modifyRaw1);
                Assert.IsTrue(modifyResult1.Value<bool>("success"), $"Test 2 failed: {modifyResult1}");
                mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                var expectedCyan = new Color(0, 0.5f, 1, 1);
                if (mat.HasProperty("_BaseColor"))
                    Assert.AreEqual(expectedCyan, mat.GetColor("_BaseColor"), "Test 2: _BaseColor should be cyan");
                else if (mat.HasProperty("_Color"))
                    Assert.AreEqual(expectedCyan, mat.GetColor("_Color"), "Test 2: _Color should be cyan");
                Assert.AreEqual(0.6f, mat.GetFloat("_Metallic"), 0.001f, "Test 2: Metallic should be 0.6");

                // 3. Modify using structured float block
                var modify2 = new JObject
                {
                    ["action"] = "modify",
                    ["path"] = matPath,
                    ["properties"] = new JObject
                    {
                        ["float"] = new JObject { ["name"] = "_Metallic", ["value"] = 0.1 }
                    }
                };
                var modifyRaw2 = ManageAsset.HandleCommand(modify2);
                var modifyResult2 = modifyRaw2 as JObject ?? JObject.FromObject(modifyRaw2);
                Assert.IsTrue(modifyResult2.Value<bool>("success"), $"Test 3 failed: {modifyResult2}");
                mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                Assert.AreEqual(0.1f, mat.GetFloat("_Metallic"), 0.001f, "Test 3: Metallic should be 0.1");

                // 4. Assign texture via direct prop alias
                var modify3 = new JObject
                {
                    ["action"] = "modify",
                    ["path"] = matPath,
                    ["properties"] = "{\"_BaseMap\":\"" + texPath + "\"}"
                };
                var modifyRaw3 = ManageAsset.HandleCommand(modify3);
                var modifyResult3 = modifyRaw3 as JObject ?? JObject.FromObject(modifyRaw3);
                Assert.IsTrue(modifyResult3.Value<bool>("success"), $"Test 4 failed: {modifyResult3}");

                // 5. Assign texture via structured block
                var modify4 = new JObject
                {
                    ["action"] = "modify",
                    ["path"] = matPath,
                    ["properties"] = new JObject
                    {
                        ["texture"] = new JObject { ["name"] = "_MainTex", ["path"] = texPath }
                    }
                };
                var modifyRaw4 = ManageAsset.HandleCommand(modify4);
                var modifyResult4 = modifyRaw4 as JObject ?? JObject.FromObject(modifyRaw4);
                Assert.IsTrue(modifyResult4.Value<bool>("success"), $"Test 5 failed: {modifyResult4}");

                // 6. Create sphere and assign material via componentProperties JSON string
                var createSphere = new JObject
                {
                    ["action"] = "create",
                    ["name"] = sphereName,
                    ["primitiveType"] = "Sphere"
                };
                var sphereRaw = ManageGameObject.HandleCommand(createSphere);
                var sphereResult = sphereRaw as JObject ?? JObject.FromObject(sphereRaw);
                Assert.IsTrue(sphereResult.Value<bool>("success"), $"Test 6 - Create sphere failed: {sphereResult}");

                var modifySphere = new JObject
                {
                    ["action"] = "modify",
                    ["target"] = sphereName,
                    ["searchMethod"] = "by_name",
                    ["componentProperties"] = "{\"MeshRenderer\":{\"sharedMaterial\":\"" + matPath + "\"}}"
                };
                var sphereModifyRaw = ManageGameObject.HandleCommand(modifySphere);
                var sphereModifyResult = sphereModifyRaw as JObject ?? JObject.FromObject(sphereModifyRaw);
                Assert.IsTrue(sphereModifyResult.Value<bool>("success"), $"Test 6 - Assign material failed: {sphereModifyResult}");
                var sphere = GameObject.Find(sphereName);
                Assert.IsNotNull(sphere, "Test 6: Sphere should exist");
                var renderer = sphere.GetComponent<MeshRenderer>();
                Assert.IsNotNull(renderer.sharedMaterial, "Test 6: Material should be assigned");

                // 7. Use URP color alias key
                var modify5 = new JObject
                {
                    ["action"] = "modify",
                    ["path"] = matPath,
                    ["properties"] = new JObject
                    {
                        ["_BaseColor"] = new JArray(0.2, 0.8, 0.3, 1)
                    }
                };
                var modifyRaw5 = ManageAsset.HandleCommand(modify5);
                var modifyResult5 = modifyRaw5 as JObject ?? JObject.FromObject(modifyRaw5);
                Assert.IsTrue(modifyResult5.Value<bool>("success"), $"Test 7 failed: {modifyResult5}");
                mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                Color expectedColor = new Color(0.2f, 0.8f, 0.3f, 1f);
                if (mat.HasProperty("_BaseColor"))
                    AssertColorsEqual(expectedColor, mat.GetColor("_BaseColor"), "Test 7: _BaseColor should be set");
                else if (mat.HasProperty("_Color"))
                    AssertColorsEqual(expectedColor, mat.GetColor("_Color"), "Test 7: Fallback _Color should be set");

                // 8. Invalid JSON should warn (don't fail)
                var invalidJson = new JObject
                {
                    ["action"] = "create",
                    ["path"] = badJsonPath,
                    ["assetType"] = "Material",
                    ["properties"] = "{\"invalid\": json, \"missing\": quotes}"
                };
                LogAssert.Expect(LogType.Warning, new Regex("(failed to parse)|(Could not parse 'properties' JSON string)", RegexOptions.IgnoreCase));
                var invalidRaw = ManageAsset.HandleCommand(invalidJson);
                var invalidResult = invalidRaw as JObject ?? JObject.FromObject(invalidRaw);
                Assert.IsNotNull(invalidResult, "Test 8: Should return a result");

                // 9. Switch shader pipeline dynamically
                var modify6 = new JObject
                {
                    ["action"] = "modify",
                    ["path"] = matPath,
                    ["properties"] = "{\"shader\":\"Standard\",\"color\":[1,1,0,1]}"
                };
                var modifyRaw6 = ManageAsset.HandleCommand(modify6);
                var modifyResult6 = modifyRaw6 as JObject ?? JObject.FromObject(modifyRaw6);
                Assert.IsTrue(modifyResult6.Value<bool>("success"), $"Test 9 failed: {modifyResult6}");
                mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                Assert.AreEqual("Standard", mat.shader.name, "Test 9: Shader should be Standard");
                var c9 = mat.GetColor("_Color");
                // Looser tolerance (0.02) for shader-switched colors due to color space conversion differences
                Assert.IsTrue(Mathf.Abs(c9.r - 1f) < 0.02f && Mathf.Abs(c9.g - 1f) < 0.02f && Mathf.Abs(c9.b - 0f) < 0.02f,
                    "Test 9: Color should be near yellow");

                // 10. Mixed friendly and alias keys in one go
                var modify7 = new JObject
                {
                    ["action"] = "modify",
                    ["path"] = matPath,
                    ["properties"] = new JObject
                    {
                        ["metallic"] = 0.8,
                        ["smoothness"] = 0.3,
                        ["albedo"] = texPath
                    }
                };
                var modifyRaw7 = ManageAsset.HandleCommand(modify7);
                var modifyResult7 = modifyRaw7 as JObject ?? JObject.FromObject(modifyRaw7);
                Assert.IsTrue(modifyResult7.Value<bool>("success"), $"Test 10 failed: {modifyResult7}");
                mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                Assert.AreEqual(0.8f, mat.GetFloat("_Metallic"), 0.001f, "Test 10: Metallic should be 0.8");
                Assert.AreEqual(0.3f, mat.GetFloat("_Glossiness"), 0.001f, "Test 10: Smoothness should be 0.3");
            }
            finally
            {
                var sphere = GameObject.Find(sphereName);
                if (sphere != null) UnityEngine.Object.DestroyImmediate(sphere);
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(matPath) != null)
                    AssetDatabase.DeleteAsset(matPath);
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(badJsonPath) != null)
                    AssetDatabase.DeleteAsset(badJsonPath);
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(texPath) != null)
                    AssetDatabase.DeleteAsset(texPath);
                AssetDatabase.Refresh();
            }
        }
    }
}
