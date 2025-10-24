using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using System.Collections;
using UnityEditor;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Tools;
using System;

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
            }
            finally
            {
                var goObj2 = GameObject.Find("MCPParamJSONSphere");
                if (goObj2 != null) UnityEngine.Object.DestroyImmediate(goObj2);
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(matPath) != null) AssetDatabase.DeleteAsset(matPath);
                AssetDatabase.Refresh();
            }
        }

    }
}