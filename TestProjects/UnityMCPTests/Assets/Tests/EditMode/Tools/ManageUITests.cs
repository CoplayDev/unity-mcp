using System;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using MCPForUnity.Editor.Tools;
using static MCPForUnityTests.Editor.TestUtilities;

namespace MCPForUnityTests.Editor.Tools
{
    public class ManageUITests
    {
        private const string TempRoot = "Assets/Temp/ManageUITests";

        [SetUp]
        public void SetUp()
        {
            EnsureFolder(TempRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TempRoot))
            {
                AssetDatabase.DeleteAsset(TempRoot);
            }
            CleanupEmptyParentFolders(TempRoot);
        }

        // ---- Action validation ----

        [Test]
        public void HandleCommand_MissingAction_ReturnsError()
        {
            var result = ToJObject(ManageUI.HandleCommand(new JObject()));
            Assert.IsFalse(result.Value<bool>("success"));
        }

        [Test]
        public void HandleCommand_UnknownAction_ReturnsError()
        {
            var result = ToJObject(ManageUI.HandleCommand(new JObject
            {
                ["action"] = "explode"
            }));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["error"].ToString(), Does.Contain("Unknown action"));
        }

        [Test]
        public void Ping_ReturnsPong()
        {
            var result = ToJObject(ManageUI.HandleCommand(new JObject
            {
                ["action"] = "ping"
            }));
            Assert.IsTrue(result.Value<bool>("success"));
            Assert.AreEqual("pong", result.Value<string>("message"));
        }

        // ---- Create file ----

        [Test]
        public void Create_Uxml_CreatesFile()
        {
            string path = $"{TempRoot}/Test_{Guid.NewGuid():N}.uxml";
            string content = "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\"><ui:Label text=\"Hi\" /></ui:UXML>";

            var result = ToJObject(ManageUI.HandleCommand(new JObject
            {
                ["action"] = "create",
                ["path"] = path,
                ["contents"] = content,
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            // Verify file was created on disk
            string fullPath = Path.Combine(Application.dataPath,
                path.Substring("Assets/".Length)).Replace('/', Path.DirectorySeparatorChar);
            Assert.IsTrue(File.Exists(fullPath), $"File should exist at {fullPath}");

            string actual = File.ReadAllText(fullPath);
            Assert.AreEqual(content, actual);
        }

        [Test]
        public void Create_Uss_CreatesFile()
        {
            string path = $"{TempRoot}/Test_{Guid.NewGuid():N}.uss";
            string content = ".root { background-color: red; }";

            var result = ToJObject(ManageUI.HandleCommand(new JObject
            {
                ["action"] = "create",
                ["path"] = path,
                ["contents"] = content,
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
        }

        [Test]
        public void Create_InvalidExtension_ReturnsError()
        {
            var result = ToJObject(ManageUI.HandleCommand(new JObject
            {
                ["action"] = "create",
                ["path"] = $"{TempRoot}/Test.txt",
                ["contents"] = "hello",
            }));

            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["error"].ToString(), Does.Contain(".uxml or .uss"));
        }

        [Test]
        public void Create_MissingContents_ReturnsError()
        {
            var result = ToJObject(ManageUI.HandleCommand(new JObject
            {
                ["action"] = "create",
                ["path"] = $"{TempRoot}/Test.uxml",
            }));

            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["error"].ToString(), Does.Contain("contents"));
        }

        [Test]
        public void Create_AlreadyExists_ReturnsError()
        {
            string path = $"{TempRoot}/Exists_{Guid.NewGuid():N}.uxml";

            // Create first time
            ManageUI.HandleCommand(new JObject
            {
                ["action"] = "create",
                ["path"] = path,
                ["contents"] = "<ui:UXML />",
            });

            // Try to create again
            var result = ToJObject(ManageUI.HandleCommand(new JObject
            {
                ["action"] = "create",
                ["path"] = path,
                ["contents"] = "<ui:UXML />",
            }));

            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["error"].ToString(), Does.Contain("already exists"));
        }

        [Test]
        public void Create_WithBase64EncodedContents_Decodes()
        {
            string path = $"{TempRoot}/Encoded_{Guid.NewGuid():N}.uxml";
            string content = "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\" />";
            string encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(content));

            var result = ToJObject(ManageUI.HandleCommand(new JObject
            {
                ["action"] = "create",
                ["path"] = path,
                ["encodedContents"] = encoded,
                ["contentsEncoded"] = true,
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            string fullPath = Path.Combine(Application.dataPath,
                path.Substring("Assets/".Length)).Replace('/', Path.DirectorySeparatorChar);
            string actual = File.ReadAllText(fullPath);
            Assert.AreEqual(content, actual);
        }

        // ---- Read file ----

        [Test]
        public void Read_ExistingFile_ReturnsContents()
        {
            string path = $"{TempRoot}/ReadTest_{Guid.NewGuid():N}.uxml";
            string content = "<ui:UXML />";

            ManageUI.HandleCommand(new JObject
            {
                ["action"] = "create",
                ["path"] = path,
                ["contents"] = content,
            });

            var result = ToJObject(ManageUI.HandleCommand(new JObject
            {
                ["action"] = "read",
                ["path"] = path,
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var data = result["data"] as JObject;
            Assert.IsNotNull(data);
            Assert.AreEqual(content, data.Value<string>("contents"));
        }

        [Test]
        public void Read_NonExistentFile_ReturnsError()
        {
            var result = ToJObject(ManageUI.HandleCommand(new JObject
            {
                ["action"] = "read",
                ["path"] = $"{TempRoot}/DoesNotExist.uxml",
            }));

            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["error"].ToString(), Does.Contain("not found"));
        }

        // ---- Update file ----

        [Test]
        public void Update_ExistingFile_OverwritesContents()
        {
            string path = $"{TempRoot}/UpdateTest_{Guid.NewGuid():N}.uss";
            string original = ".root { color: red; }";
            string updated = ".root { color: blue; font-size: 20px; }";

            ManageUI.HandleCommand(new JObject
            {
                ["action"] = "create",
                ["path"] = path,
                ["contents"] = original,
            });

            var result = ToJObject(ManageUI.HandleCommand(new JObject
            {
                ["action"] = "update",
                ["path"] = path,
                ["contents"] = updated,
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            // Verify content was updated
            var readResult = ToJObject(ManageUI.HandleCommand(new JObject
            {
                ["action"] = "read",
                ["path"] = path,
            }));
            Assert.AreEqual(updated, readResult["data"].Value<string>("contents"));
        }

        [Test]
        public void Update_NonExistentFile_ReturnsError()
        {
            var result = ToJObject(ManageUI.HandleCommand(new JObject
            {
                ["action"] = "update",
                ["path"] = $"{TempRoot}/Missing.uxml",
                ["contents"] = "<ui:UXML />",
            }));

            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["error"].ToString(), Does.Contain("not found"));
        }

        // ---- Create PanelSettings ----

        [Test]
        public void CreatePanelSettings_CreatesAsset()
        {
            string path = $"{TempRoot}/TestPanel_{Guid.NewGuid():N}.asset";

            var result = ToJObject(ManageUI.HandleCommand(new JObject
            {
                ["action"] = "create_panel_settings",
                ["path"] = path,
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
            Assert.IsNotNull(ps, "PanelSettings should exist at the path");
        }

        [Test]
        public void CreatePanelSettings_AlreadyExists_ReturnsError()
        {
            string path = $"{TempRoot}/ExistingPanel_{Guid.NewGuid():N}.asset";

            ManageUI.HandleCommand(new JObject
            {
                ["action"] = "create_panel_settings",
                ["path"] = path,
            });

            var result = ToJObject(ManageUI.HandleCommand(new JObject
            {
                ["action"] = "create_panel_settings",
                ["path"] = path,
            }));

            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["error"].ToString(), Does.Contain("already exists"));
        }

        // ---- Attach UIDocument ----

        [Test]
        public void AttachUIDocument_AddsComponent()
        {
            // Create a UXML file first
            string uxmlPath = $"{TempRoot}/Attach_{Guid.NewGuid():N}.uxml";
            ManageUI.HandleCommand(new JObject
            {
                ["action"] = "create",
                ["path"] = uxmlPath,
                ["contents"] = "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\"><ui:Label text=\"Test\" /></ui:UXML>",
            });
            AssetDatabase.Refresh();

            // Create a test GameObject
            var go = new GameObject("UITestObject_Attach");
            try
            {
                var result = ToJObject(ManageUI.HandleCommand(new JObject
                {
                    ["action"] = "attach_ui_document",
                    ["target"] = go.name,
                    ["source_asset"] = uxmlPath,
                }));

                Assert.IsTrue(result.Value<bool>("success"), result.ToString());

                var uiDoc = go.GetComponent<UIDocument>();
                Assert.IsNotNull(uiDoc, "UIDocument component should be attached");
                Assert.IsNotNull(uiDoc.visualTreeAsset, "VisualTreeAsset should be assigned");
                Assert.IsNotNull(uiDoc.panelSettings, "PanelSettings should be assigned (auto-created)");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void AttachUIDocument_MissingTarget_ReturnsError()
        {
            var result = ToJObject(ManageUI.HandleCommand(new JObject
            {
                ["action"] = "attach_ui_document",
                ["source_asset"] = "Assets/UI/Test.uxml",
            }));

            Assert.IsFalse(result.Value<bool>("success"));
        }

        [Test]
        public void AttachUIDocument_MissingSourceAsset_ReturnsError()
        {
            var result = ToJObject(ManageUI.HandleCommand(new JObject
            {
                ["action"] = "attach_ui_document",
                ["target"] = "SomeObject",
            }));

            Assert.IsFalse(result.Value<bool>("success"));
        }

        // ---- Get Visual Tree ----

        [Test]
        public void GetVisualTree_MissingTarget_ReturnsError()
        {
            var result = ToJObject(ManageUI.HandleCommand(new JObject
            {
                ["action"] = "get_visual_tree",
            }));

            Assert.IsFalse(result.Value<bool>("success"));
        }

        [Test]
        public void GetVisualTree_NoUIDocument_ReturnsError()
        {
            var go = new GameObject("UITestObject_NoDoc");
            try
            {
                var result = ToJObject(ManageUI.HandleCommand(new JObject
                {
                    ["action"] = "get_visual_tree",
                    ["target"] = go.name,
                }));

                Assert.IsFalse(result.Value<bool>("success"));
                Assert.That(result["error"].ToString(), Does.Contain("UIDocument"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
