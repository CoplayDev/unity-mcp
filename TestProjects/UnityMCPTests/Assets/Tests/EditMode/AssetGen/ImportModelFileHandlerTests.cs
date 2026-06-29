using System;
using System.IO;
using MCPForUnity.Editor.Tools.AssetGen;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MCPForUnityTests.Editor.AssetGen
{
    /// <summary>
    /// Drives the import_model_file handler with on-disk fixture files (no network, no provider).
    /// Covers missing source, unsupported extension, and a real OBJ import that yields an asset GUID.
    /// </summary>
    public class ImportModelFileHandlerTests
    {
        private string _tempDir;
        private const string TestFolder = "Assets/__import_model_file_test";

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "mcp_imf_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TestFolder))
                AssetDatabase.DeleteAsset(TestFolder);
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { /* ignore */ }
        }

        private static JObject Call(JObject p)
            => JObject.Parse(JsonConvert.SerializeObject(ImportModelFile.HandleCommand(p)));

        private string WriteCubeObj()
        {
            string path = Path.Combine(_tempDir, "cube.obj");
            File.WriteAllText(path,
                "o Cube\n" +
                "v 0 0 0\nv 1 0 0\nv 1 1 0\nv 0 1 0\nv 0 0 1\nv 1 0 1\nv 1 1 1\nv 0 1 1\n" +
                "f 1 2 3 4\nf 5 6 7 8\nf 1 2 6 5\nf 2 3 7 6\nf 3 4 8 7\nf 4 1 5 8\n");
            return path;
        }

        [Test]
        public void MissingSource_ReturnsError()
        {
            JObject resp = Call(new JObject { ["sourcePath"] = Path.Combine(_tempDir, "nope.obj") });
            Assert.AreEqual(false, (bool)resp["success"]);
            StringAssert.Contains("not found", ((string)resp["error"]).ToLowerInvariant());
        }

        [Test]
        public void UnsupportedExtension_ReturnsError()
        {
            string txt = Path.Combine(_tempDir, "readme.txt");
            File.WriteAllText(txt, "hi");
            JObject resp = Call(new JObject { ["sourcePath"] = txt });
            Assert.AreEqual(false, (bool)resp["success"]);
            StringAssert.Contains("unsupported", ((string)resp["error"]).ToLowerInvariant());
        }

        [Test]
        public void ImportsObj_ReturnsAssetPathAndGuid()
        {
            string obj = WriteCubeObj();
            JObject resp = Call(new JObject
            {
                ["sourcePath"] = obj,
                ["name"] = "TestCube",
                ["outputFolder"] = TestFolder,
            });
            Assert.AreEqual(true, (bool)resp["success"], resp.ToString());
            string assetPath = (string)resp["data"]["asset_path"];
            StringAssert.StartsWith(TestFolder, assetPath);
            Assert.IsFalse(string.IsNullOrEmpty((string)resp["data"]["asset_guid"]));
            Assert.IsTrue(File.Exists(assetPath), "imported file should exist under Assets");
        }

        [Test]
        public void OutputFolderTraversal_ReturnsError_AndDoesNotStageOutsideAssets()
        {
            string obj = WriteCubeObj();
            string escapeFolder = "Library/__import_model_file_escape_" + Guid.NewGuid().ToString("N");
            string escapedAbs = Path.Combine(Path.GetDirectoryName(Application.dataPath), escapeFolder, "Escaped.obj");
            try
            {
                JObject resp = Call(new JObject
                {
                    ["sourcePath"] = obj,
                    ["name"] = "Escaped",
                    ["outputFolder"] = "Assets/../" + escapeFolder,
                });

                Assert.AreEqual(false, (bool)resp["success"]);
                StringAssert.Contains("output_folder", ((string)resp["error"]).ToLowerInvariant());
                Assert.IsFalse(File.Exists(escapedAbs), "handler must not stage files outside Assets");
            }
            finally
            {
                string dir = Path.GetDirectoryName(escapedAbs);
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { /* ignore */ }
            }
        }
    }
}
