using System;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using System.Threading;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using MCPForUnityTests.Editor.Tools.Fixtures;

namespace MCPForUnityTests.Editor.Tools
{
    public class ManageScriptableObjectTests
    {
        private const string TempRoot = "Assets/Temp/ManageScriptableObjectTests";
        private const string NestedFolder = TempRoot + "/Nested/Deeper";

        private string _createdAssetPath;
        private string _createdGuid;
        private string _matAPath;
        private string _matBPath;

        [SetUp]
        public void SetUp()
        {
            WaitForUnityReady();
            EnsureFolder("Assets/Temp");
            // Start from a clean slate every time (prevents intermittent setup failures).
            if (AssetDatabase.IsValidFolder(TempRoot))
            {
                AssetDatabase.DeleteAsset(TempRoot);
            }
            EnsureFolder(TempRoot);

            _createdAssetPath = null;
            _createdGuid = null;

            // Create two Materials we can reference by guid/path.
            _matAPath = $"{TempRoot}/MatA_{Guid.NewGuid():N}.mat";
            _matBPath = $"{TempRoot}/MatB_{Guid.NewGuid():N}.mat";
            var shader = Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("HDRP/Lit")
                ?? Shader.Find("Standard")
                ?? Shader.Find("Unlit/Color");
            Assert.IsNotNull(shader, "A fallback shader must be available for creating Material assets in tests.");
            AssetDatabase.CreateAsset(new Material(shader), _matAPath);
            AssetDatabase.CreateAsset(new Material(shader), _matBPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            WaitForUnityReady();
        }

        [TearDown]
        public void TearDown()
        {
            // Best-effort cleanup
            if (!string.IsNullOrEmpty(_createdAssetPath) && AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(_createdAssetPath) != null)
            {
                AssetDatabase.DeleteAsset(_createdAssetPath);
            }
            if (!string.IsNullOrEmpty(_matAPath) && AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(_matAPath) != null)
            {
                AssetDatabase.DeleteAsset(_matAPath);
            }
            if (!string.IsNullOrEmpty(_matBPath) && AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(_matBPath) != null)
            {
                AssetDatabase.DeleteAsset(_matBPath);
            }

            if (AssetDatabase.IsValidFolder(TempRoot))
            {
                AssetDatabase.DeleteAsset(TempRoot);
            }

            // Clean up parent Temp folder if empty
            if (AssetDatabase.IsValidFolder("Assets/Temp"))
            {
                var remainingDirs = System.IO.Directory.GetDirectories("Assets/Temp");
                var remainingFiles = System.IO.Directory.GetFiles("Assets/Temp");
                if (remainingDirs.Length == 0 && remainingFiles.Length == 0)
                {
                    AssetDatabase.DeleteAsset("Assets/Temp");
                }
            }

            AssetDatabase.Refresh();
        }

        [Test]
        public void Create_CreatesNestedFolders_PlacesAssetCorrectly_AndAppliesPatches()
        {
            var create = new JObject
            {
                ["action"] = "create",
                ["typeName"] = typeof(ManageScriptableObjectTestDefinition).FullName,
                ["folderPath"] = NestedFolder,
                ["assetName"] = "My_Test_Def",
                ["overwrite"] = true,
                ["patches"] = new JArray
                {
                    new JObject { ["propertyPath"] = "displayName", ["op"] = "set", ["value"] = "Hello" },
                    new JObject { ["propertyPath"] = "baseNumber", ["op"] = "set", ["value"] = 42 },
                    new JObject { ["propertyPath"] = "nested.note", ["op"] = "set", ["value"] = "note!" }
                }
            };

            var raw = ManageScriptableObject.HandleCommand(create);
            var result = raw as JObject ?? JObject.FromObject(raw);

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var data = result["data"] as JObject;
            Assert.IsNotNull(data, "Expected data payload");

            _createdGuid = data!["guid"]?.ToString();
            _createdAssetPath = data["path"]?.ToString();

            Assert.IsTrue(AssetDatabase.IsValidFolder(NestedFolder), "Nested folder should be created.");
            Assert.IsTrue(_createdAssetPath!.StartsWith(NestedFolder, StringComparison.Ordinal), $"Asset should be created under {NestedFolder}: {_createdAssetPath}");
            Assert.IsTrue(_createdAssetPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase), "Asset should have .asset extension.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(_createdGuid), "Expected guid in response.");

            var asset = AssetDatabase.LoadAssetAtPath<ManageScriptableObjectTestDefinition>(_createdAssetPath);
            Assert.IsNotNull(asset, "Created asset should load as TestDefinition.");
            Assert.AreEqual("Hello", asset!.DisplayName, "Private [SerializeField] string should be set via SerializedProperty.");
            Assert.AreEqual(42, asset.BaseNumber, "Inherited serialized field should be set via SerializedProperty.");
            Assert.AreEqual("note!", asset.NestedNote, "Nested struct field should be set via SerializedProperty path.");
        }

        [Test]
        public void Modify_ArrayResize_ThenAssignObjectRefs_ByGuidAndByPath()
        {
            // Create base asset first with no patches.
            var create = new JObject
            {
                ["action"] = "create",
                ["typeName"] = typeof(ManageScriptableObjectTestDefinition).FullName,
                ["folderPath"] = TempRoot,
                ["assetName"] = "Modify_Target",
                ["overwrite"] = true
            };
            var createRes = ToJObject(ManageScriptableObject.HandleCommand(create));
            Assert.IsTrue(createRes.Value<bool>("success"), createRes.ToString());
            _createdGuid = createRes["data"]?["guid"]?.ToString();
            _createdAssetPath = createRes["data"]?["path"]?.ToString();

            var matAGuid = AssetDatabase.AssetPathToGUID(_matAPath);

            var modify = new JObject
            {
                ["action"] = "modify",
                ["target"] = new JObject { ["guid"] = _createdGuid },
                ["patches"] = new JArray
                {
                    // Resize list to 2
                    new JObject { ["propertyPath"] = "materials.Array.size", ["op"] = "array_resize", ["value"] = 2 },
                    // Assign element 0 by guid
                    new JObject
                    {
                        ["propertyPath"] = "materials.Array.data[0]",
                        ["op"] = "set",
                        ["ref"] = new JObject { ["guid"] = matAGuid }
                    },
                    // Assign element 1 by path
                    new JObject
                    {
                        ["propertyPath"] = "materials.Array.data[1]",
                        ["op"] = "set",
                        ["ref"] = new JObject { ["path"] = _matBPath }
                    }
                }
            };

            var modRes = ToJObject(ManageScriptableObject.HandleCommand(modify));
            Assert.IsTrue(modRes.Value<bool>("success"), modRes.ToString());

            // Assert patch results are ok so failures are visible even if the tool returns success.
            var results = modRes["data"]?["results"] as JArray;
            Assert.IsNotNull(results, "Expected per-patch results in response.");
            foreach (var r in results!)
            {
                Assert.IsTrue(r.Value<bool>("ok"), $"Patch failed: {r}");
            }

            var asset = AssetDatabase.LoadAssetAtPath<ManageScriptableObjectTestDefinition>(_createdAssetPath);
            Assert.IsNotNull(asset);
            Assert.AreEqual(2, asset!.Materials.Count, "List should be resized to 2.");

            var matA = AssetDatabase.LoadAssetAtPath<Material>(_matAPath);
            var matB = AssetDatabase.LoadAssetAtPath<Material>(_matBPath);
            Assert.AreEqual(matA, asset.Materials[0], "Element 0 should be set by GUID ref.");
            Assert.AreEqual(matB, asset.Materials[1], "Element 1 should be set by path ref.");
        }

        [Test]
        public void Errors_InvalidAction_TypeNotFound_TargetNotFound()
        {
            // invalid action
            var badAction = ToJObject(ManageScriptableObject.HandleCommand(new JObject { ["action"] = "nope" }));
            Assert.IsFalse(badAction.Value<bool>("success"));
            Assert.AreEqual("invalid_params", badAction.Value<string>("error"));

            // type not found
            var badType = ToJObject(ManageScriptableObject.HandleCommand(new JObject
            {
                ["action"] = "create",
                ["typeName"] = "Nope.MissingType",
                ["folderPath"] = TempRoot,
                ["assetName"] = "X",
            }));
            Assert.IsFalse(badType.Value<bool>("success"));
            Assert.AreEqual("type_not_found", badType.Value<string>("error"));

            // target not found
            var badTarget = ToJObject(ManageScriptableObject.HandleCommand(new JObject
            {
                ["action"] = "modify",
                ["target"] = new JObject { ["guid"] = "00000000000000000000000000000000" },
                ["patches"] = new JArray(),
            }));
            Assert.IsFalse(badTarget.Value<bool>("success"));
            Assert.AreEqual("target_not_found", badTarget.Value<string>("error"));
        }

        [Test]
        public void Create_RejectsNonAssetsRootFolders()
        {
            var badPackages = ToJObject(ManageScriptableObject.HandleCommand(new JObject
            {
                ["action"] = "create",
                ["typeName"] = typeof(ManageScriptableObjectTestDefinition).FullName,
                ["folderPath"] = "Packages/NotAllowed",
                ["assetName"] = "BadFolder",
                ["overwrite"] = true,
            }));
            Assert.IsFalse(badPackages.Value<bool>("success"));
            Assert.AreEqual("invalid_folder_path", badPackages.Value<string>("error"));

            var badAbsolute = ToJObject(ManageScriptableObject.HandleCommand(new JObject
            {
                ["action"] = "create",
                ["typeName"] = typeof(ManageScriptableObjectTestDefinition).FullName,
                ["folderPath"] = "/tmp/not_allowed",
                ["assetName"] = "BadFolder2",
                ["overwrite"] = true,
            }));
            Assert.IsFalse(badAbsolute.Value<bool>("success"));
            Assert.AreEqual("invalid_folder_path", badAbsolute.Value<string>("error"));

            var badFileUri = ToJObject(ManageScriptableObject.HandleCommand(new JObject
            {
                ["action"] = "create",
                ["typeName"] = typeof(ManageScriptableObjectTestDefinition).FullName,
                ["folderPath"] = "file:///tmp/not_allowed",
                ["assetName"] = "BadFolder3",
                ["overwrite"] = true,
            }));
            Assert.IsFalse(badFileUri.Value<bool>("success"));
            Assert.AreEqual("invalid_folder_path", badFileUri.Value<string>("error"));
        }

        [Test]
        public void Create_NormalizesRelativeAndBackslashPaths_AndAvoidsDoubleSlashesInResult()
        {
            var create = new JObject
            {
                ["action"] = "create",
                ["typeName"] = typeof(ManageScriptableObjectTestDefinition).FullName,
                ["folderPath"] = @"Temp\ManageScriptableObjectTests\SlashProbe\\Deep",
                ["assetName"] = "SlashProbe",
                ["overwrite"] = true,
            };

            var res = ToJObject(ManageScriptableObject.HandleCommand(create));
            Assert.IsTrue(res.Value<bool>("success"), res.ToString());

            var path = res["data"]?["path"]?.ToString();
            Assert.IsNotNull(path, "Expected path in response.");
            Assert.IsTrue(path!.StartsWith("Assets/Temp/ManageScriptableObjectTests/SlashProbe/Deep", StringComparison.Ordinal),
                $"Expected sanitized Assets-rooted path, got: {path}");
            Assert.IsFalse(path.Contains("//", StringComparison.Ordinal), $"Path should not contain double slashes: {path}");
        }

        private static void EnsureFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
                return;

            // Only used for Assets/... paths in tests.
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

        private static void WaitForUnityReady(double timeoutSeconds = 30.0)
        {
            // Some EditMode tests trigger script compilation/domain reload. Tools like ManageScriptableObject
            // intentionally return "compiling_or_reloading" during these windows. Wait until Unity is stable
            // to make tests deterministic.
            double start = EditorApplication.timeSinceStartup;
            while (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                if (EditorApplication.timeSinceStartup - start > timeoutSeconds)
                {
                    Assert.Fail($"Timed out waiting for Unity to finish compiling/updating (>{timeoutSeconds:0.0}s).");
                }
                Thread.Sleep(50);
            }
        }
    }
}


