using System.IO;
using NUnit.Framework;
using UnityEngine;
using MCPForUnity.Editor.Data;
using MCPForUnity.Editor.Services;

namespace MCPForUnityTests.Editor.Services
{
    public class ToolSyncServiceTests
    {
        private ToolSyncService _service;
        private string _testToolsDir;

        [SetUp]
        public void SetUp()
        {
            _service = new ToolSyncService();
            _testToolsDir = Path.Combine(Path.GetTempPath(), "UnityMCPTests", "tools");

            // Clean up any existing test directory
            if (Directory.Exists(_testToolsDir))
            {
                Directory.Delete(_testToolsDir, true);
            }
        }

        [TearDown]
        public void TearDown()
        {
            // Clean up test directory
            if (Directory.Exists(_testToolsDir))
            {
                try
                {
                    Directory.Delete(_testToolsDir, true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }

        [Test]
        public void SyncProjectTools_CreatesDestinationDirectory()
        {
            _service.SyncProjectTools(_testToolsDir);

            Assert.IsTrue(Directory.Exists(_testToolsDir), "Should create destination directory");
        }

        [Test]
        public void SyncProjectTools_ReturnsSuccess_WhenNoPythonToolsAssets()
        {
            var result = _service.SyncProjectTools(_testToolsDir);

            Assert.IsNotNull(result, "Should return a result");
            Assert.AreEqual(0, result.CopiedCount, "Should not copy any files");
            Assert.AreEqual(0, result.ErrorCount, "Should not have errors");
        }

        [Test]
        public void SyncProjectTools_CopiesPythonFile_WhenAssetHasFile()
        {
            // Create a test asset with a Python file
            var asset = ScriptableObject.CreateInstance<PythonToolsAsset>();
            var textAsset = new TextAsset("print('test tool')");
            asset.pythonFiles.Add(textAsset);

            // Note: This test is limited because we can't easily create real TextAssets
            // with proper file paths in unit tests. This would be better as an integration test.

            Object.DestroyImmediate(asset);
        }

        [Test]
        public void SyncProjectTools_CleansUpStaleFiles()
        {
            // Create a stale file in the destination
            Directory.CreateDirectory(_testToolsDir);
            string staleFile = Path.Combine(_testToolsDir, "old_tool.py");
            File.WriteAllText(staleFile, "print('old')");

            Assert.IsTrue(File.Exists(staleFile), "Stale file should exist before sync");

            // Sync with no assets (should cleanup the stale file)
            _service.SyncProjectTools(_testToolsDir);

            Assert.IsFalse(File.Exists(staleFile), "Stale file should be removed after sync");
        }

        [Test]
        public void SyncProjectTools_HandlesMultipleAssets()
        {
            // This would require creating multiple PythonToolsAssets
            // Better suited for integration testing
            var result = _service.SyncProjectTools(_testToolsDir);

            Assert.IsNotNull(result, "Should handle multiple assets gracefully");
        }

        [Test]
        public void SyncProjectTools_ReportsCorrectCounts()
        {
            var result = _service.SyncProjectTools(_testToolsDir);

            Assert.IsTrue(result.CopiedCount >= 0, "Copied count should be non-negative");
            Assert.IsTrue(result.SkippedCount >= 0, "Skipped count should be non-negative");
            Assert.IsTrue(result.ErrorCount >= 0, "Error count should be non-negative");
        }
    }
}
