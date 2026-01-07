using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using MCPForUnityTests.Editor.Tools.Fixtures;
using Debug = UnityEngine.Debug;

namespace MCPForUnityTests.Editor.Tools
{
    /// <summary>
    /// Stress tests for ManageScriptableObject tool.
    /// Tests bulk data operations, auto-resizing, path normalization, and validation.
    /// These tests document current behavior and will verify fixes after hardening.
    /// </summary>
    [TestFixture]
    public class ManageScriptableObjectStressTests
    {
        private const string TempRoot = "Assets/Temp/SOStressTests";
        private const double UnityReadyTimeoutSeconds = 180.0;

        private string _runRoot;
        private readonly List<string> _createdAssets = new List<string>();
        private string _matPath;
        private string _texPath;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return WaitForUnityReady(UnityReadyTimeoutSeconds);
            EnsureFolder("Assets/Temp");
            EnsureFolder(TempRoot);
            _runRoot = $"{TempRoot}/Run_{Guid.NewGuid():N}";
            EnsureFolder(_runRoot);
            _createdAssets.Clear();

            // Create test assets for reference tests
            var shader = Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("HDRP/Lit")
                ?? Shader.Find("Standard")
                ?? Shader.Find("Unlit/Color");
            Assert.IsNotNull(shader, "A fallback shader must be available.");

            _matPath = $"{_runRoot}/TestMat.mat";
            AssetDatabase.CreateAsset(new Material(shader), _matPath);
            _createdAssets.Add(_matPath);

            // Create a simple texture for reference tests
            var tex = new Texture2D(4, 4);
            _texPath = $"{_runRoot}/TestTex.asset";
            AssetDatabase.CreateAsset(tex, _texPath);
            _createdAssets.Add(_texPath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            yield return WaitForUnityReady(UnityReadyTimeoutSeconds);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var path in _createdAssets)
            {
                if (!string.IsNullOrEmpty(path) && AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) != null)
                {
                    AssetDatabase.DeleteAsset(path);
                }
            }
            _createdAssets.Clear();

            if (!string.IsNullOrEmpty(_runRoot) && AssetDatabase.IsValidFolder(_runRoot))
            {
                AssetDatabase.DeleteAsset(_runRoot);
            }

            AssetDatabase.Refresh();
        }

        #region Big Bang Test - Large Nested Array

        [Test]
        public void BigBang_CreateWithLargeNestedArray()
        {
            // Create a ComplexStressSO with a large nestedDataList in one create call
            const int elementCount = 50; // Start moderate, can increase after hardening

            var patches = new JArray();

            // First resize the array
            patches.Add(new JObject
            {
                ["propertyPath"] = "nestedDataList.Array.size",
                ["op"] = "array_resize",
                ["value"] = elementCount
            });

            // Then set each element's fields
            for (int i = 0; i < elementCount; i++)
            {
                patches.Add(new JObject
                {
                    ["propertyPath"] = $"nestedDataList.Array.data[{i}].id",
                    ["op"] = "set",
                    ["value"] = $"item_{i:D4}"
                });
                patches.Add(new JObject
                {
                    ["propertyPath"] = $"nestedDataList.Array.data[{i}].value",
                    ["op"] = "set",
                    ["value"] = i * 1.5f
                });
                patches.Add(new JObject
                {
                    ["propertyPath"] = $"nestedDataList.Array.data[{i}].position",
                    ["op"] = "set",
                    ["value"] = new JArray(i, i * 2, i * 3)
                });
            }

            var sw = Stopwatch.StartNew();
            var result = ToJObject(ManageScriptableObject.HandleCommand(new JObject
            {
                ["action"] = "create",
                ["typeName"] = "ComplexStressSO",
                ["folderPath"] = _runRoot,
                ["assetName"] = "BigBang",
                ["overwrite"] = true,
                ["patches"] = patches
            }));
            sw.Stop();

            Debug.Log($"[BigBang] {elementCount} elements with {patches.Count} patches in {sw.ElapsedMilliseconds}ms");

            Assert.IsTrue(result.Value<bool>("success"), $"BigBang create failed: {result}");

            var path = result["data"]?["path"]?.ToString();
            Assert.IsNotNull(path);
            _createdAssets.Add(path);

            // Verify the asset
            var asset = AssetDatabase.LoadAssetAtPath<ComplexStressSO>(path);
            Assert.IsNotNull(asset, "Asset should load as ComplexStressSO");
            Assert.AreEqual(elementCount, asset.nestedDataList.Count, "List should have correct count");

            // Spot check a few elements
            Assert.AreEqual("item_0000", asset.nestedDataList[0].id);
            Assert.AreEqual(0f, asset.nestedDataList[0].value, 0.01f);

            int lastIdx = elementCount - 1;
            Assert.AreEqual($"item_{lastIdx:D4}", asset.nestedDataList[lastIdx].id);
            Assert.AreEqual(lastIdx * 1.5f, asset.nestedDataList[lastIdx].value, 0.01f);
        }

        #endregion

        #region Out of Bounds Test - Auto-Grow Arrays

        [Test]
        public void OutOfBounds_SetElementBeyondArraySize_ShouldFailWithoutAutoGrow()
        {
            // Create an ArrayStressSO first
            var createResult = ToJObject(ManageScriptableObject.HandleCommand(new JObject
            {
                ["action"] = "create",
                ["typeName"] = "ArrayStressSO",
                ["folderPath"] = _runRoot,
                ["assetName"] = "OutOfBounds",
                ["overwrite"] = true
            }));
            Assert.IsTrue(createResult.Value<bool>("success"), createResult.ToString());

            var path = createResult["data"]?["path"]?.ToString();
            var guid = createResult["data"]?["guid"]?.ToString();
            _createdAssets.Add(path);

            // Try to set element at index 99 (array starts with 3 elements)
            var modifyResult = ToJObject(ManageScriptableObject.HandleCommand(new JObject
            {
                ["action"] = "modify",
                ["target"] = new JObject { ["guid"] = guid },
                ["patches"] = new JArray
                {
                    new JObject
                    {
                        ["propertyPath"] = "floatArray.Array.data[99]",
                        ["op"] = "set",
                        ["value"] = 42.0f
                    }
                }
            }));

            // Document current behavior: this should fail without auto-grow
            // After hardening, this should succeed
            var patchResults = modifyResult["data"]?["results"] as JArray;
            Assert.IsNotNull(patchResults);
            
            bool patchOk = patchResults[0]?.Value<bool>("ok") ?? false;
            Debug.Log($"[OutOfBounds] Setting [99] on 3-element array: ok={patchOk}, message={patchResults[0]?["message"]}");
            
            // Current expected behavior: fails with "Property not found"
            // After Phase 1.2 (auto-resize): should succeed
        }

        #endregion

        #region Friendly Path Syntax Test

        [Test]
        public void FriendlySyntax_BracketNotation_ShouldBeNormalized()
        {
            // Create asset first
            var createResult = ToJObject(ManageScriptableObject.HandleCommand(new JObject
            {
                ["action"] = "create",
                ["typeName"] = "ArrayStressSO",
                ["folderPath"] = _runRoot,
                ["assetName"] = "FriendlySyntax",
                ["overwrite"] = true,
                ["patches"] = new JArray
                {
                    // Resize first using proper syntax
                    new JObject { ["propertyPath"] = "floatArray.Array.size", ["op"] = "array_resize", ["value"] = 5 }
                }
            }));
            Assert.IsTrue(createResult.Value<bool>("success"), createResult.ToString());

            var path = createResult["data"]?["path"]?.ToString();
            var guid = createResult["data"]?["guid"]?.ToString();
            _createdAssets.Add(path);

            // Try using friendly syntax: floatArray[2] instead of floatArray.Array.data[2]
            var modifyResult = ToJObject(ManageScriptableObject.HandleCommand(new JObject
            {
                ["action"] = "modify",
                ["target"] = new JObject { ["guid"] = guid },
                ["patches"] = new JArray
                {
                    new JObject
                    {
                        ["propertyPath"] = "floatArray[2]",  // Friendly syntax!
                        ["op"] = "set",
                        ["value"] = 123.456f
                    }
                }
            }));

            var patchResults = modifyResult["data"]?["results"] as JArray;
            Assert.IsNotNull(patchResults);

            bool patchOk = patchResults[0]?.Value<bool>("ok") ?? false;
            Debug.Log($"[FriendlySyntax] Using floatArray[2] syntax: ok={patchOk}, message={patchResults[0]?["message"]}");

            // Current expected behavior: fails with "Property not found: floatArray[2]"
            // After Phase 1.1 (path normalization): should succeed
        }

        #endregion

        #region Deep Nesting Test

        [Test]
        public void DeepNesting_SetVectorAtDepth3()
        {
            // Create DeepStressSO and set level1.mid.deep.pos
            var createResult = ToJObject(ManageScriptableObject.HandleCommand(new JObject
            {
                ["action"] = "create",
                ["typeName"] = "DeepStressSO",
                ["folderPath"] = _runRoot,
                ["assetName"] = "DeepNesting",
                ["overwrite"] = true,
                ["patches"] = new JArray
                {
                    new JObject
                    {
                        ["propertyPath"] = "level1.topName",
                        ["op"] = "set",
                        ["value"] = "TopLevel"
                    },
                    new JObject
                    {
                        ["propertyPath"] = "level1.mid.midName",
                        ["op"] = "set",
                        ["value"] = "MiddleLevel"
                    },
                    new JObject
                    {
                        ["propertyPath"] = "level1.mid.deep.detail",
                        ["op"] = "set",
                        ["value"] = "DeepDetail"
                    },
                    new JObject
                    {
                        ["propertyPath"] = "level1.mid.deep.pos",
                        ["op"] = "set",
                        ["value"] = new JArray(1.0f, 2.0f, 3.0f)
                    },
                    new JObject
                    {
                        ["propertyPath"] = "overtone",
                        ["op"] = "set",
                        ["value"] = new JArray(1.0f, 0.5f, 0.25f, 1.0f)
                    }
                }
            }));

            Assert.IsTrue(createResult.Value<bool>("success"), $"DeepNesting create failed: {createResult}");

            var path = createResult["data"]?["path"]?.ToString();
            _createdAssets.Add(path);

            // Verify the asset
            var asset = AssetDatabase.LoadAssetAtPath<DeepStressSO>(path);
            Assert.IsNotNull(asset, "Asset should load as DeepStressSO");
            Assert.AreEqual("TopLevel", asset.level1.topName);
            Assert.AreEqual("MiddleLevel", asset.level1.mid.midName);
            Assert.AreEqual("DeepDetail", asset.level1.mid.deep.detail);
            Assert.AreEqual(new Vector3(1, 2, 3), asset.level1.mid.deep.pos);
            Assert.AreEqual(new Color(1f, 0.5f, 0.25f, 1f), asset.overtone);

            Debug.Log("[DeepNesting] Successfully set values at depth 3");
        }

        #endregion

        #region Mixed References Test

        [Test]
        public void MixedReferences_SetMaterialAndIntInOneCall()
        {
            var matGuid = AssetDatabase.AssetPathToGUID(_matPath);

            var createResult = ToJObject(ManageScriptableObject.HandleCommand(new JObject
            {
                ["action"] = "create",
                ["typeName"] = "ComplexStressSO",
                ["folderPath"] = _runRoot,
                ["assetName"] = "MixedRefs",
                ["overwrite"] = true,
                ["patches"] = new JArray
                {
                    new JObject
                    {
                        ["propertyPath"] = "intValue",
                        ["op"] = "set",
                        ["value"] = 42
                    },
                    new JObject
                    {
                        ["propertyPath"] = "floatValue",
                        ["op"] = "set",
                        ["value"] = 3.14f
                    },
                    new JObject
                    {
                        ["propertyPath"] = "stringValue",
                        ["op"] = "set",
                        ["value"] = "TestString"
                    },
                    new JObject
                    {
                        ["propertyPath"] = "boolValue",
                        ["op"] = "set",
                        ["value"] = true
                    },
                    new JObject
                    {
                        ["propertyPath"] = "enumValue",
                        ["op"] = "set",
                        ["value"] = "Beta"
                    },
                    new JObject
                    {
                        ["propertyPath"] = "vectorValue",
                        ["op"] = "set",
                        ["value"] = new JArray(10, 20, 30)
                    },
                    new JObject
                    {
                        ["propertyPath"] = "colorValue",
                        ["op"] = "set",
                        ["value"] = new JArray(1.0f, 0.0f, 0.0f, 1.0f)
                    }
                }
            }));

            Assert.IsTrue(createResult.Value<bool>("success"), $"MixedRefs create failed: {createResult}");

            var path = createResult["data"]?["path"]?.ToString();
            _createdAssets.Add(path);

            var asset = AssetDatabase.LoadAssetAtPath<ComplexStressSO>(path);
            Assert.IsNotNull(asset);
            Assert.AreEqual(42, asset.intValue);
            Assert.AreEqual(3.14f, asset.floatValue, 0.01f);
            Assert.AreEqual("TestString", asset.stringValue);
            Assert.IsTrue(asset.boolValue);
            Assert.AreEqual(TestEnum.Beta, asset.enumValue);
            Assert.AreEqual(new Vector3(10, 20, 30), asset.vectorValue);
            Assert.AreEqual(new Color(1, 0, 0, 1), asset.colorValue);

            Debug.Log("[MixedReferences] Successfully set multiple types in one call");
        }

        #endregion

        #region Rapid Fire Test

        [Test]
        public void RapidFire_100SequentialModifies()
        {
            // Create initial asset
            var createResult = ToJObject(ManageScriptableObject.HandleCommand(new JObject
            {
                ["action"] = "create",
                ["typeName"] = "ComplexStressSO",
                ["folderPath"] = _runRoot,
                ["assetName"] = "RapidFire",
                ["overwrite"] = true
            }));
            Assert.IsTrue(createResult.Value<bool>("success"), createResult.ToString());

            var path = createResult["data"]?["path"]?.ToString();
            var guid = createResult["data"]?["guid"]?.ToString();
            _createdAssets.Add(path);

            const int iterations = 100;
            int successCount = 0;
            var sw = Stopwatch.StartNew();

            for (int i = 0; i < iterations; i++)
            {
                var modifyResult = ToJObject(ManageScriptableObject.HandleCommand(new JObject
                {
                    ["action"] = "modify",
                    ["target"] = new JObject { ["guid"] = guid },
                    ["patches"] = new JArray
                    {
                        new JObject
                        {
                            ["propertyPath"] = "intValue",
                            ["op"] = "set",
                            ["value"] = i
                        }
                    }
                }));

                if (modifyResult.Value<bool>("success"))
                {
                    var results = modifyResult["data"]?["results"] as JArray;
                    if (results != null && results.Count > 0 && results[0].Value<bool>("ok"))
                    {
                        successCount++;
                    }
                }
            }

            sw.Stop();
            Debug.Log($"[RapidFire] {successCount}/{iterations} successful in {sw.ElapsedMilliseconds}ms ({sw.ElapsedMilliseconds / (float)iterations:F2}ms/op)");

            Assert.AreEqual(iterations, successCount, "All rapid fire modifications should succeed");

            // Verify final state
            var asset = AssetDatabase.LoadAssetAtPath<ComplexStressSO>(path);
            Assert.IsNotNull(asset);
            Assert.AreEqual(iterations - 1, asset.intValue, "Final value should be last iteration value");
        }

        #endregion

        #region Type Mismatch Test

        [Test]
        public void TypeMismatch_InvalidValueForPropertyType()
        {
            var createResult = ToJObject(ManageScriptableObject.HandleCommand(new JObject
            {
                ["action"] = "create",
                ["typeName"] = "ComplexStressSO",
                ["folderPath"] = _runRoot,
                ["assetName"] = "TypeMismatch",
                ["overwrite"] = true
            }));
            Assert.IsTrue(createResult.Value<bool>("success"), createResult.ToString());

            var path = createResult["data"]?["path"]?.ToString();
            var guid = createResult["data"]?["guid"]?.ToString();
            _createdAssets.Add(path);

            // Try to set an int field to a non-integer string
            var modifyResult = ToJObject(ManageScriptableObject.HandleCommand(new JObject
            {
                ["action"] = "modify",
                ["target"] = new JObject { ["guid"] = guid },
                ["patches"] = new JArray
                {
                    new JObject
                    {
                        ["propertyPath"] = "intValue",
                        ["op"] = "set",
                        ["value"] = "not_an_integer"
                    }
                }
            }));

            var patchResults = modifyResult["data"]?["results"] as JArray;
            Assert.IsNotNull(patchResults);

            bool patchOk = patchResults[0]?.Value<bool>("ok") ?? true;
            string message = patchResults[0]?["message"]?.ToString() ?? "";
            Debug.Log($"[TypeMismatch] Setting int to 'not_an_integer': ok={patchOk}, message={message}");

            // Type mismatch should fail gracefully with a clear error
            Assert.IsFalse(patchOk, "Setting int to string should fail");
            Assert.IsTrue(message.Contains("int", StringComparison.OrdinalIgnoreCase) || 
                          message.Contains("Expected", StringComparison.OrdinalIgnoreCase),
                          $"Error message should indicate type issue: {message}");
        }

        [Test]
        public void TypeMismatch_WrongVectorFormat()
        {
            var createResult = ToJObject(ManageScriptableObject.HandleCommand(new JObject
            {
                ["action"] = "create",
                ["typeName"] = "ComplexStressSO",
                ["folderPath"] = _runRoot,
                ["assetName"] = "WrongVector",
                ["overwrite"] = true
            }));
            Assert.IsTrue(createResult.Value<bool>("success"), createResult.ToString());

            var path = createResult["data"]?["path"]?.ToString();
            var guid = createResult["data"]?["guid"]?.ToString();
            _createdAssets.Add(path);

            // Try to set a Vector3 field to a single number
            var modifyResult = ToJObject(ManageScriptableObject.HandleCommand(new JObject
            {
                ["action"] = "modify",
                ["target"] = new JObject { ["guid"] = guid },
                ["patches"] = new JArray
                {
                    new JObject
                    {
                        ["propertyPath"] = "vectorValue",
                        ["op"] = "set",
                        ["value"] = 123  // Wrong format for Vector3
                    }
                }
            }));

            var patchResults = modifyResult["data"]?["results"] as JArray;
            Assert.IsNotNull(patchResults);

            bool patchOk = patchResults[0]?.Value<bool>("ok") ?? true;
            string message = patchResults[0]?["message"]?.ToString() ?? "";
            Debug.Log($"[TypeMismatch] Setting Vector3 to 123: ok={patchOk}, message={message}");

            Assert.IsFalse(patchOk, "Setting Vector3 to single number should fail");
        }

        #endregion

        #region Bulk Array Mapping Test (Phase 3 feature)

        [Test]
        public void BulkArrayMapping_PassArrayAsValue()
        {
            var createResult = ToJObject(ManageScriptableObject.HandleCommand(new JObject
            {
                ["action"] = "create",
                ["typeName"] = "ComplexStressSO",
                ["folderPath"] = _runRoot,
                ["assetName"] = "BulkArray",
                ["overwrite"] = true
            }));
            Assert.IsTrue(createResult.Value<bool>("success"), createResult.ToString());

            var path = createResult["data"]?["path"]?.ToString();
            var guid = createResult["data"]?["guid"]?.ToString();
            _createdAssets.Add(path);

            // Try to set the entire intArray using a JArray value directly
            var modifyResult = ToJObject(ManageScriptableObject.HandleCommand(new JObject
            {
                ["action"] = "modify",
                ["target"] = new JObject { ["guid"] = guid },
                ["patches"] = new JArray
                {
                    new JObject
                    {
                        ["propertyPath"] = "intArray",
                        ["op"] = "set",
                        ["value"] = new JArray(1, 2, 3, 4, 5)  // Bulk array!
                    }
                }
            }));

            var patchResults = modifyResult["data"]?["results"] as JArray;
            Assert.IsNotNull(patchResults);

            bool patchOk = patchResults[0]?.Value<bool>("ok") ?? false;
            string message = patchResults[0]?["message"]?.ToString() ?? "";
            Debug.Log($"[BulkArrayMapping] Setting intArray to [1,2,3,4,5]: ok={patchOk}, message={message}");

            // Current expected behavior: likely fails with unsupported type
            // After Phase 3.1 (bulk array mapping): should succeed
        }

        #endregion

        #region GUID Shorthand Test (Phase 4 feature)

        [Test]
        public void GuidShorthand_PassPlainGuidString()
        {
            var matGuid = AssetDatabase.AssetPathToGUID(_matPath);
            Assert.IsFalse(string.IsNullOrEmpty(matGuid), "Material GUID should be resolvable");

            // Create a test SO that has an ObjectReference field
            // For this test, we'll create a ManageScriptableObjectTestDefinition and set a material
            var createResult = ToJObject(ManageScriptableObject.HandleCommand(new JObject
            {
                ["action"] = "create",
                ["typeName"] = "MCPForUnityTests.Editor.Tools.Fixtures.ManageScriptableObjectTestDefinition",
                ["folderPath"] = _runRoot,
                ["assetName"] = "GuidShorthand",
                ["overwrite"] = true,
                ["patches"] = new JArray
                {
                    // Resize materials list first
                    new JObject { ["propertyPath"] = "materials.Array.size", ["op"] = "array_resize", ["value"] = 1 },
                    // Use GUID shorthand - just the 32-char hex string as value
                    new JObject
                    {
                        ["propertyPath"] = "materials.Array.data[0]",
                        ["op"] = "set",
                        ["value"] = matGuid  // Plain GUID string!
                    }
                }
            }));

            Assert.IsTrue(createResult.Value<bool>("success"), $"Create with GUID shorthand failed: {createResult}");

            var path = createResult["data"]?["path"]?.ToString();
            _createdAssets.Add(path);

            // Load and verify
            var asset = AssetDatabase.LoadAssetAtPath<ManageScriptableObjectTestDefinition>(path);
            Assert.IsNotNull(asset, "Asset should load");

            var mat = AssetDatabase.LoadAssetAtPath<Material>(_matPath);
            Assert.AreEqual(1, asset.Materials.Count, "Should have 1 material");
            Assert.AreEqual(mat, asset.Materials[0], "Material should be set via GUID shorthand");

            Debug.Log($"[GuidShorthand] Successfully set material using plain GUID: {matGuid}");
        }

        #endregion

        #region Dry Run Test (Phase 5 feature)

        [Test]
        public void DryRun_ValidatePatchesWithoutApplying()
        {
            // Create a test asset first
            var createResult = ToJObject(ManageScriptableObject.HandleCommand(new JObject
            {
                ["action"] = "create",
                ["typeName"] = "ComplexStressSO",
                ["folderPath"] = _runRoot,
                ["assetName"] = "DryRunTest",
                ["overwrite"] = true
            }));
            Assert.IsTrue(createResult.Value<bool>("success"), createResult.ToString());

            var path = createResult["data"]?["path"]?.ToString();
            var guid = createResult["data"]?["guid"]?.ToString();
            _createdAssets.Add(path);

            // Get initial value
            var asset = AssetDatabase.LoadAssetAtPath<ComplexStressSO>(path);
            int originalValue = asset.intValue;

            // Try a dry-run modify with some valid and some invalid patches
            var dryRunResult = ToJObject(ManageScriptableObject.HandleCommand(new JObject
            {
                ["action"] = "modify",
                ["target"] = new JObject { ["guid"] = guid },
                ["dryRun"] = true,
                ["patches"] = new JArray
                {
                    new JObject { ["propertyPath"] = "intValue", ["op"] = "set", ["value"] = 999 },
                    new JObject { ["propertyPath"] = "nonExistentField", ["op"] = "set", ["value"] = "test" },
                    new JObject { ["propertyPath"] = "stringList[5]", ["op"] = "set", ["value"] = "auto-grow" }
                }
            }));

            Assert.IsTrue(dryRunResult.Value<bool>("success"), $"Dry-run should succeed: {dryRunResult}");
            
            var data = dryRunResult["data"] as JObject;
            Assert.IsNotNull(data);
            Assert.IsTrue(data["dryRun"]?.Value<bool>() ?? false, "Response should indicate dry-run mode");

            var validationResults = data["validationResults"] as JArray;
            Assert.IsNotNull(validationResults, "Should have validation results");
            Assert.AreEqual(3, validationResults.Count, "Should validate all 3 patches");

            // First patch should be valid
            Assert.IsTrue(validationResults[0].Value<bool>("ok"), $"intValue patch should be valid: {validationResults[0]}");
            
            // Second patch should be invalid (field doesn't exist)
            Assert.IsFalse(validationResults[1].Value<bool>("ok"), $"nonExistentField patch should be invalid: {validationResults[1]}");
            
            // Third patch should be valid (auto-growable)
            Assert.IsTrue(validationResults[2].Value<bool>("ok"), $"stringList[5] patch should be valid (auto-grow): {validationResults[2]}");

            // Most importantly: verify no changes were actually made
            AssetDatabase.ImportAsset(path);
            asset = AssetDatabase.LoadAssetAtPath<ComplexStressSO>(path);
            Assert.AreEqual(originalValue, asset.intValue, "Dry-run should NOT modify the asset");

            Debug.Log("[DryRun] Successfully validated patches without applying");
        }

        #endregion

        #region Helper Methods

        private static void EnsureFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
                return;

            var sanitized = AssetPathUtility.SanitizeAssetPath(folderPath);
            if (string.Equals(sanitized, "Assets", StringComparison.OrdinalIgnoreCase))
                return;

            var parts = sanitized.Split('/');
            string current = "Assets";
            for (int i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }

        private static JObject ToJObject(object result)
        {
            return result as JObject ?? JObject.FromObject(result);
        }

        private static IEnumerator WaitForUnityReady(double timeoutSeconds = 30.0)
        {
            double start = EditorApplication.timeSinceStartup;
            while (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                if (EditorApplication.timeSinceStartup - start > timeoutSeconds)
                {
                    Assert.Fail($"Timed out waiting for Unity to finish compiling/updating (>{timeoutSeconds:0.0}s).");
                }
                yield return null;
            }
        }

        #endregion
    }
}

