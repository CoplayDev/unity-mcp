using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using System.Collections;
using UnityEditor;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Tools;
using System;
using System.Text.RegularExpressions;

namespace Tests.EditMode
{
    /// <summary>
    /// Tests specifically for MCP tool parameter handling issues.
    /// These tests focus on the actual problems we encountered:
    /// 1. JSON parameter parsing in manage_asset and manage_gameobject tools
    /// 2. Material creation with properties through MCP tools
    /// 3. Material assignment through MCP tools
    /// </summary>
    public class MCPToolParameterTests
    {
		private static void AssertShaderIsSupported(Shader s)
		{
			Assert.IsNotNull(s, "Shader should not be null");
			// Accept common defaults across pipelines
			var name = s.name;
			bool ok = name == "Universal Render Pipeline/Lit"
				|| name == "HDRP/Lit"
				|| name == "Standard"
				|| name == "Unlit/Color";
			Assert.IsTrue(ok, $"Unexpected shader: {name}");
		}
        [Test]
        public void Test_ManageAsset_ShouldAcceptJSONProperties()
        {
            // Arrange: create temp folder
            const string tempDir = "Assets/Temp/MCPToolParameterTests";
            if (!AssetDatabase.IsValidFolder("Assets/Temp"))
            {
                AssetDatabase.CreateFolder("Assets", "Temp");
            }
            if (!AssetDatabase.IsValidFolder(tempDir))
            {
                AssetDatabase.CreateFolder("Assets/Temp", "MCPToolParameterTests");
            }

            var matPath = $"{tempDir}/JsonMat_{Guid.NewGuid().ToString("N")}.mat";

            // Build params with properties as a JSON string (to be coerced)
            var p = new JObject
            {
                ["action"] = "create",
                ["path"] = matPath,
                ["assetType"] = "Material",
                ["properties"] = "{\"shader\": \"Universal Render Pipeline/Lit\", \"color\": [0,0,1,1]}"
            };

            try
            {
                var raw = ManageAsset.HandleCommand(p);
                var result = raw as JObject ?? JObject.FromObject(raw);
                Assert.IsNotNull(result, "Handler should return a JObject result");
                Assert.IsTrue(result!.Value<bool>("success"), result.ToString());

				var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
				Assert.IsNotNull(mat, "Material should be created at path");
				AssertShaderIsSupported(mat.shader);
                if (mat.HasProperty("_Color"))
                {
                    Assert.AreEqual(Color.blue, mat.GetColor("_Color"));
                }
            }
            finally
            {
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(matPath) != null)
                {
                    AssetDatabase.DeleteAsset(matPath);
                }
                AssetDatabase.Refresh();
            }
        }

        [Test]
        public void Test_ManageGameObject_ShouldAcceptJSONComponentProperties()
        {
            const string tempDir = "Assets/Temp/MCPToolParameterTests";
            if (!AssetDatabase.IsValidFolder("Assets/Temp")) AssetDatabase.CreateFolder("Assets", "Temp");
            if (!AssetDatabase.IsValidFolder(tempDir)) AssetDatabase.CreateFolder("Assets/Temp", "MCPToolParameterTests");
            var matPath = $"{tempDir}/JsonMat_{Guid.NewGuid().ToString("N")}.mat";

            // Create a material first (object-typed properties)
            var createMat = new JObject
            {
                ["action"] = "create",
                ["path"] = matPath,
                ["assetType"] = "Material",
                ["properties"] = new JObject { ["shader"] = "Universal Render Pipeline/Lit", ["color"] = new JArray(0,0,1,1) }
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

                // Verify material assignment and shader on the GameObject's MeshRenderer
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
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(matPath) != null) AssetDatabase.DeleteAsset(matPath);
                AssetDatabase.Refresh();
            }
        }

        [Test]
        public void Test_JSONParsing_ShouldWorkInMCPTools()
        {
            const string tempDir = "Assets/Temp/MCPToolParameterTests";
            if (!AssetDatabase.IsValidFolder("Assets/Temp")) AssetDatabase.CreateFolder("Assets", "Temp");
            if (!AssetDatabase.IsValidFolder(tempDir)) AssetDatabase.CreateFolder("Assets/Temp", "MCPToolParameterTests");
            var matPath = $"{tempDir}/JsonMat_{Guid.NewGuid().ToString("N")}.mat";

            // manage_asset with JSON string properties (coercion path)
            var createMat = new JObject
            {
                ["action"] = "create",
                ["path"] = matPath,
                ["assetType"] = "Material",
                ["properties"] = "{\"shader\": \"Universal Render Pipeline/Lit\", \"color\": [0,0,1,1]}"
            };
            var createResRaw = ManageAsset.HandleCommand(createMat);
            var createRes = createResRaw as JObject ?? JObject.FromObject(createResRaw);
            Assert.IsTrue(createRes.Value<bool>("success"), createRes.ToString());

            // Create sphere and assign material (object-typed componentProperties)
            var go = new JObject { ["action"] = "create", ["name"] = "MCPParamJSONSphere", ["primitiveType"] = "Sphere" };
            var goRes = ManageGameObject.HandleCommand(go);
            var goObj = goRes as JObject ?? JObject.FromObject(goRes);
            Assert.IsTrue(goObj.Value<bool>("success"), goObj.ToString());

            try
            {
                var compJsonObj = new JObject { ["MeshRenderer"] = new JObject { ["sharedMaterial"] = matPath } };
                var compJson = compJsonObj.ToString(Newtonsoft.Json.Formatting.None);
                var modify = new JObject
                {
                    ["action"] = "modify",
                    ["target"] = "MCPParamJSONSphere",
                    ["searchMethod"] = "by_name",
                    ["componentProperties"] = compJson
                };
                var modResRaw = ManageGameObject.HandleCommand(modify);
                var modRes = modResRaw as JObject ?? JObject.FromObject(modResRaw);
                Assert.IsTrue(modRes.Value<bool>("success"), modRes.ToString());

                // Verify shader on created material
                var createdMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                Assert.IsNotNull(createdMat);
				AssertShaderIsSupported(createdMat.shader);
            }
            finally
            {
                var goObj2 = GameObject.Find("MCPParamJSONSphere");
                if (goObj2 != null) UnityEngine.Object.DestroyImmediate(goObj2);
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(matPath) != null) AssetDatabase.DeleteAsset(matPath);
                AssetDatabase.Refresh();
            }
        }

        [Test]
        public void Test_ManageAsset_JSONStringParsing_CreateMaterial()
        {
            // Test that JSON string properties are correctly parsed and applied
            const string tempDir = "Assets/Temp/MCPToolParameterTests";
            var matPath = $"{tempDir}/JsonStringTest_{Guid.NewGuid().ToString("N")}.mat";

            var p = new JObject
            {
                ["action"] = "create",
                ["path"] = matPath,
                ["assetType"] = "Material",
                ["properties"] = "{\"shader\": \"Universal Render Pipeline/Lit\", \"color\": [1,0,0,1]}"
            };

            try
            {
                var raw = ManageAsset.HandleCommand(p);
                var result = raw as JObject ?? JObject.FromObject(raw);
                Assert.IsNotNull(result, "Handler should return a JObject result");
                Assert.IsTrue(result!.Value<bool>("success"), $"Create failed: {result}");

				var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
				Assert.IsNotNull(mat, "Material should be created at path");
				AssertShaderIsSupported(mat.shader);
                if (mat.HasProperty("_Color"))
                {
                    Assert.AreEqual(Color.red, mat.GetColor("_Color"), "Material should have red color");
                }
            }
            finally
            {
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(matPath) != null)
                {
                    AssetDatabase.DeleteAsset(matPath);
                }
                AssetDatabase.Refresh();
            }
        }

        [Test]
        public void Test_ManageAsset_JSONStringParsing_ModifyMaterial()
        {
            // Test that JSON string properties work for modify operations
            const string tempDir = "Assets/Temp/MCPToolParameterTests";
            var matPath = $"{tempDir}/JsonStringModifyTest_{Guid.NewGuid().ToString("N")}.mat";

            // First create a material
            var createParams = new JObject
            {
                ["action"] = "create",
                ["path"] = matPath,
                ["assetType"] = "Material",
                ["properties"] = "{\"shader\": \"Universal Render Pipeline/Lit\", \"color\": [0,1,0,1]}"
            };

            try
            {
                var createRaw = ManageAsset.HandleCommand(createParams);
                var createResult = createRaw as JObject ?? JObject.FromObject(createRaw);
                Assert.IsTrue(createResult!.Value<bool>("success"), "Create should succeed");

                // Now modify with JSON string
                var modifyParams = new JObject
                {
                    ["action"] = "modify",
                    ["path"] = matPath,
                    ["properties"] = "{\"color\": [0,0,1,1]}"
                };

                var modifyRaw = ManageAsset.HandleCommand(modifyParams);
                var modifyResult = modifyRaw as JObject ?? JObject.FromObject(modifyRaw);
                Assert.IsNotNull(modifyResult, "Modify should return a result");
                Assert.IsTrue(modifyResult!.Value<bool>("success"), $"Modify failed: {modifyResult}");

				var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
				Assert.IsNotNull(mat, "Material should exist");
				AssertShaderIsSupported(mat.shader);
                if (mat.HasProperty("_Color"))
                {
                    Assert.AreEqual(Color.blue, mat.GetColor("_Color"), "Material should have blue color after modify");
                }
            }
            finally
            {
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(matPath) != null)
                {
                    AssetDatabase.DeleteAsset(matPath);
                }
                AssetDatabase.Refresh();
            }
        }

        [Test]
        public void Test_ManageAsset_InvalidJSONString_HandledGracefully()
        {
            // Test that invalid JSON strings are handled gracefully
            const string tempDir = "Assets/Temp/MCPToolParameterTests";
            var matPath = $"{tempDir}/InvalidJsonTest_{Guid.NewGuid().ToString("N")}.mat";

            var p = new JObject
            {
                ["action"] = "create",
                ["path"] = matPath,
                ["assetType"] = "Material",
                ["properties"] = "{\"invalid\": json, \"missing\": quotes}" // Invalid JSON
            };

			try
			{
				LogAssert.Expect(LogType.Warning, new Regex("(failed to parse)|(Could not parse 'properties' JSON string)", RegexOptions.IgnoreCase));
				var raw = ManageAsset.HandleCommand(p);
				var result = raw as JObject ?? JObject.FromObject(raw);
				// Should either succeed with defaults or fail gracefully
				Assert.IsNotNull(result, "Handler should return a result");
				// The result might be success (with defaults) or failure, both are acceptable
			}
			finally
			{
				if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(matPath) != null)
				{
					AssetDatabase.DeleteAsset(matPath);
				}
				AssetDatabase.Refresh();
			}
		}

		[Test]
		public void Test_ManageAsset_JSONStringParsing_FloatProperty_Metallic_CreateAndModify()
		{
			// Validate float property handling via JSON string for create and modify
			const string tempDir = "Assets/Temp/MCPToolParameterTests";
			if (!AssetDatabase.IsValidFolder("Assets/Temp")) AssetDatabase.CreateFolder("Assets", "Temp");
			if (!AssetDatabase.IsValidFolder(tempDir)) AssetDatabase.CreateFolder("Assets/Temp", "MCPToolParameterTests");
			var matPath = $"{tempDir}/JsonFloatTest_{Guid.NewGuid().ToString("N")}.mat";

			var createParams = new JObject
			{
				["action"] = "create",
				["path"] = matPath,
				["assetType"] = "Material",
				["properties"] = "{\"shader\": \"Universal Render Pipeline/Lit\", \"metallic\": 0.75}"
			};

			try
			{
				var createRaw = ManageAsset.HandleCommand(createParams);
				var createResult = createRaw as JObject ?? JObject.FromObject(createRaw);
				Assert.IsTrue(createResult!.Value<bool>("success"), createResult.ToString());

				var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
				Assert.IsNotNull(mat, "Material should be created at path");
				AssertShaderIsSupported(mat.shader);
				if (mat.HasProperty("_Metallic"))
				{
					Assert.AreEqual(0.75f, mat.GetFloat("_Metallic"), 1e-3f, "Metallic should be ~0.75 after create");
				}

				var modifyParams = new JObject
				{
					["action"] = "modify",
					["path"] = matPath,
					["properties"] = "{\"metallic\": 0.1}"
				};

				var modifyRaw = ManageAsset.HandleCommand(modifyParams);
				var modifyResult = modifyRaw as JObject ?? JObject.FromObject(modifyRaw);
				Assert.IsTrue(modifyResult!.Value<bool>("success"), modifyResult.ToString());

				var mat2 = AssetDatabase.LoadAssetAtPath<Material>(matPath);
				Assert.IsNotNull(mat2, "Material should still exist");
				if (mat2.HasProperty("_Metallic"))
				{
					Assert.AreEqual(0.1f, mat2.GetFloat("_Metallic"), 1e-3f, "Metallic should be ~0.1 after modify");
				}
			}
			finally
			{
				if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(matPath) != null)
				{
					AssetDatabase.DeleteAsset(matPath);
				}
				AssetDatabase.Refresh();
			}
		}

		[Test]
		public void Test_ManageAsset_JSONStringParsing_TextureAssignment_CreateAndModify()
		{
			// Uses flexible direct property assignment to set _BaseMap/_MainTex by path
			const string tempDir = "Assets/Temp/MCPToolParameterTests";
			if (!AssetDatabase.IsValidFolder("Assets/Temp")) AssetDatabase.CreateFolder("Assets", "Temp");
			if (!AssetDatabase.IsValidFolder(tempDir)) AssetDatabase.CreateFolder("Assets/Temp", "MCPToolParameterTests");
			var matPath = $"{tempDir}/JsonTexTest_{Guid.NewGuid().ToString("N")}.mat";
			var texPath = "Assets/Temp/LiveTests/TempBaseTex.asset"; // created by GenTempTex

			// Ensure the texture exists BEFORE creating the material so assignment succeeds during create
			var preTex = AssetDatabase.LoadAssetAtPath<Texture>(texPath);
			if (preTex == null)
			{
				if (!AssetDatabase.IsValidFolder("Assets/Temp")) AssetDatabase.CreateFolder("Assets", "Temp");
				if (!AssetDatabase.IsValidFolder("Assets/Temp/LiveTests")) AssetDatabase.CreateFolder("Assets/Temp", "LiveTests");
				var tex2D = new Texture2D(4, 4, TextureFormat.RGBA32, false);
				var pixels = new Color[16];
				for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
				tex2D.SetPixels(pixels);
				tex2D.Apply();
				AssetDatabase.CreateAsset(tex2D, texPath);
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();
			}

			var createParams = new JObject
			{
				["action"] = "create",
				["path"] = matPath,
				["assetType"] = "Material",
				["properties"] = new JObject
				{
					["shader"] = "Universal Render Pipeline/Lit",
					["_BaseMap"] = texPath // resolves to _BaseMap or _MainTex internally
				}
			};

			try
			{
				var createRaw = ManageAsset.HandleCommand(createParams);
				var createResult = createRaw as JObject ?? JObject.FromObject(createRaw);
				Assert.IsTrue(createResult!.Value<bool>("success"), createResult.ToString());

				var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
				Assert.IsNotNull(mat, "Material should be created at path");
				AssertShaderIsSupported(mat.shader);
				var tex = AssetDatabase.LoadAssetAtPath<Texture>(texPath);
				if (tex == null)
				{
					// Create a tiny white texture if missing to make the test self-sufficient
					if (!AssetDatabase.IsValidFolder("Assets/Temp")) AssetDatabase.CreateFolder("Assets", "Temp");
					if (!AssetDatabase.IsValidFolder("Assets/Temp/LiveTests")) AssetDatabase.CreateFolder("Assets/Temp", "LiveTests");
					var tex2D = new Texture2D(4, 4, TextureFormat.RGBA32, false);
					var pixels = new Color[16];
					for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
					tex2D.SetPixels(pixels);
					tex2D.Apply();
					AssetDatabase.CreateAsset(tex2D, texPath);
					AssetDatabase.SaveAssets();
					AssetDatabase.Refresh();
					tex = AssetDatabase.LoadAssetAtPath<Texture>(texPath);
				}
				Assert.IsNotNull(tex, "Test texture should exist");
				// Verify either _BaseMap or _MainTex holds the texture
				bool hasTexture = (mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") == tex)
					|| (mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") == tex);
				Assert.IsTrue(hasTexture, "Material should reference the assigned texture");

				// Modify by changing to same texture via alternate alias key
				var modifyParams = new JObject
				{
					["action"] = "modify",
					["path"] = matPath,
					["properties"] = new JObject { ["_MainTex"] = texPath }
				};
				var modifyRaw = ManageAsset.HandleCommand(modifyParams);
				var modifyResult = modifyRaw as JObject ?? JObject.FromObject(modifyRaw);
				Assert.IsTrue(modifyResult!.Value<bool>("success"), modifyResult.ToString());

				var mat2 = AssetDatabase.LoadAssetAtPath<Material>(matPath);
				Assert.IsNotNull(mat2);
				bool hasTexture2 = (mat2.HasProperty("_BaseMap") && mat2.GetTexture("_BaseMap") == tex)
					|| (mat2.HasProperty("_MainTex") && mat2.GetTexture("_MainTex") == tex);
				Assert.IsTrue(hasTexture2, "Material should keep the assigned texture after modify");
			}
			finally
			{
				if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(matPath) != null)
				{
					AssetDatabase.DeleteAsset(matPath);
				}
				AssetDatabase.Refresh();
			}
		}

		[Test]
		public void Test_EndToEnd_PropertyHandling_AllScenarios()
		{
			// Comprehensive end-to-end test of all 10 property handling scenarios
			const string tempDir = "Assets/Temp/LiveTests";
			if (!AssetDatabase.IsValidFolder("Assets/Temp")) AssetDatabase.CreateFolder("Assets", "Temp");
			if (!AssetDatabase.IsValidFolder(tempDir)) AssetDatabase.CreateFolder("Assets/Temp", "LiveTests");

			string guidSuffix = Guid.NewGuid().ToString("N").Substring(0, 8);
			string matPath = $"{tempDir}/Mat_{guidSuffix}.mat";
			string texPath = $"{tempDir}/TempBaseTex.asset";
			string sphereName = $"LiveSphere_{guidSuffix}";
			string badJsonPath = $"{tempDir}/BadJson_{guidSuffix}.mat";

			// Ensure clean state from previous runs
			var preSphere = GameObject.Find(sphereName);
			if (preSphere != null) UnityEngine.Object.DestroyImmediate(preSphere);
			if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(matPath) != null) AssetDatabase.DeleteAsset(matPath);
			if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(badJsonPath) != null) AssetDatabase.DeleteAsset(badJsonPath);
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
				var expectedRed = Color.red;
				if (mat.HasProperty("_BaseColor"))
					Assert.AreEqual(expectedRed, mat.GetColor("_BaseColor"), "Test 1: _BaseColor should be red");
				else if (mat.HasProperty("_Color"))
					Assert.AreEqual(expectedRed, mat.GetColor("_Color"), "Test 1: _Color should be red");
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
				else
					Assert.Inconclusive("Material has neither _BaseColor nor _Color");
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

				// 4. Assign texture via direct prop alias (skip if texture doesn't exist)
				if (AssetDatabase.LoadAssetAtPath<Texture>(texPath) != null)
				{
					var modify3 = new JObject
					{
						["action"] = "modify",
						["path"] = matPath,
						["properties"] = "{\"_BaseMap\":\"" + texPath + "\"}"
					};
					var modifyRaw3 = ManageAsset.HandleCommand(modify3);
					var modifyResult3 = modifyRaw3 as JObject ?? JObject.FromObject(modifyRaw3);
					Assert.IsTrue(modifyResult3.Value<bool>("success"), $"Test 4 failed: {modifyResult3}");
					Debug.Log("Test 4: Texture assignment successful");
				}
				else
				{
					Debug.LogWarning("Test 4: Skipped - texture not found at " + texPath);
				}

				// 5. Assign texture via structured block (skip if texture doesn't exist)
				if (AssetDatabase.LoadAssetAtPath<Texture>(texPath) != null)
				{
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
					Debug.Log("Test 5: Structured texture assignment successful");
				}
				else
				{
					Debug.LogWarning("Test 5: Skipped - texture not found at " + texPath);
				}

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
				{
					Assert.AreEqual(expectedColor, mat.GetColor("_BaseColor"), "Test 7: _BaseColor should be set");
				}
				else if (mat.HasProperty("_Color"))
				{
					Assert.AreEqual(expectedColor, mat.GetColor("_Color"), "Test 7: Fallback _Color should be set");
				}

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
				// Should either succeed with defaults or fail gracefully
				Assert.IsNotNull(invalidResult, "Test 8: Should return a result");
				Debug.Log($"Test 8: Invalid JSON handled - {invalidResult!["success"]}");

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
				Assert.IsTrue(Mathf.Abs(c9.r - 1f) < 0.02f && Mathf.Abs(c9.g - 1f) < 0.02f && Mathf.Abs(c9.b - 0f) < 0.02f, "Test 9: Color should be near yellow");

				// 10. Mixed friendly and alias keys in one go
				var modify7 = new JObject
				{
					["action"] = "modify",
					["path"] = matPath,
					["properties"] = new JObject
					{
						["metallic"] = 0.8,
						["smoothness"] = 0.3,
						["albedo"] = texPath // Texture path if exists
					}
				};
				var modifyRaw7 = ManageAsset.HandleCommand(modify7);
				var modifyResult7 = modifyRaw7 as JObject ?? JObject.FromObject(modifyRaw7);
				Assert.IsTrue(modifyResult7.Value<bool>("success"), $"Test 10 failed: {modifyResult7}");
				mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
				Assert.AreEqual(0.8f, mat.GetFloat("_Metallic"), 0.001f, "Test 10: Metallic should be 0.8");
				Assert.AreEqual(0.3f, mat.GetFloat("_Glossiness"), 0.001f, "Test 10: Smoothness should be 0.3");

				Debug.Log("All 10 end-to-end property handling tests completed successfully!");
			}
			finally
			{
				// Cleanup
				var sphere = GameObject.Find(sphereName);
				if (sphere != null) UnityEngine.Object.DestroyImmediate(sphere);
				if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(matPath) != null) AssetDatabase.DeleteAsset(matPath);
				if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(badJsonPath) != null) AssetDatabase.DeleteAsset(badJsonPath);
				AssetDatabase.Refresh();
			}
		}

    }
}