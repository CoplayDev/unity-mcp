using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MCPForUnity.Editor.Data;

namespace MCPForUnityTests.Editor.Data
{
    public class PythonToolsAssetTests
    {
        private PythonToolsAsset _asset;

        [SetUp]
        public void SetUp()
        {
            _asset = ScriptableObject.CreateInstance<PythonToolsAsset>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_asset != null)
            {
                UnityEngine.Object.DestroyImmediate(_asset, true);
            }
        }

        [Test]
        public void GetValidFiles_ReturnsEmptyList_WhenNoFilesAdded()
        {
            var validFiles = _asset.GetValidFiles().ToList();

            Assert.IsEmpty(validFiles, "Should return empty list when no files added");
        }

        [Test]
        public void GetValidFiles_FiltersOutNullReferences()
        {
            _asset.pythonFiles.Add(null);
            _asset.pythonFiles.Add(new TextAsset("print('test')"));
            _asset.pythonFiles.Add(null);

            var validFiles = _asset.GetValidFiles().ToList();

            Assert.AreEqual(1, validFiles.Count, "Should filter out null references");
        }

        [Test]
        public void GetValidFiles_ReturnsAllNonNullFiles()
        {
            var file1 = new TextAsset("print('test1')");
            var file2 = new TextAsset("print('test2')");

            _asset.pythonFiles.Add(file1);
            _asset.pythonFiles.Add(file2);

            var validFiles = _asset.GetValidFiles().ToList();

            Assert.AreEqual(2, validFiles.Count, "Should return all non-null files");
            CollectionAssert.Contains(validFiles, file1);
            CollectionAssert.Contains(validFiles, file2);
        }

        [Test]
        public void NeedsSync_ReturnsTrue_WhenHashingDisabled()
        {
            _asset.useContentHashing = false;
            var textAsset = new TextAsset("print('test')");

            bool needsSync = _asset.NeedsSync(textAsset, "any_hash");

            Assert.IsTrue(needsSync, "Should always need sync when hashing disabled");
        }

        [Test]
        public void NeedsSync_ReturnsTrue_WhenFileNotInStates()
        {
            _asset.useContentHashing = true;
            var textAsset = new TextAsset("print('test')");

            bool needsSync = _asset.NeedsSync(textAsset, "new_hash");

            Assert.IsTrue(needsSync, "Should need sync for new file");
        }

        [Test]
        public void NeedsSync_ReturnsFalse_WhenHashMatches()
        {
            _asset.useContentHashing = true;
            var textAsset = new TextAsset("print('test')");
            string hash = "test_hash_123";

            // Record the file with a hash
            _asset.RecordSync(textAsset, hash);

            // Check if needs sync with same hash
            bool needsSync = _asset.NeedsSync(textAsset, hash);

            Assert.IsFalse(needsSync, "Should not need sync when hash matches");
        }

        [Test]
        public void NeedsSync_ReturnsTrue_WhenHashDiffers()
        {
            _asset.useContentHashing = true;
            var textAsset = new TextAsset("print('test')");

            // Record with one hash
            _asset.RecordSync(textAsset, "old_hash");

            // Check with different hash
            bool needsSync = _asset.NeedsSync(textAsset, "new_hash");

            Assert.IsTrue(needsSync, "Should need sync when hash differs");
        }

        [Test]
        public void RecordSync_AddsNewFileState()
        {
            var textAsset = new TextAsset("print('test')");
            string hash = "test_hash";

            _asset.RecordSync(textAsset, hash);

            Assert.AreEqual(1, _asset.fileStates.Count, "Should add one file state");
            Assert.AreEqual(hash, _asset.fileStates[0].contentHash, "Should store the hash");
            Assert.IsNotNull(_asset.fileStates[0].assetGuid, "Should store the GUID");
        }

        [Test]
        public void RecordSync_UpdatesExistingFileState()
        {
            var textAsset = new TextAsset("print('test')");

            // Record first time
            _asset.RecordSync(textAsset, "hash1");
            var firstTime = _asset.fileStates[0].lastSyncTime;

            // Wait a tiny bit to ensure time difference
            System.Threading.Thread.Sleep(10);

            // Record second time with different hash
            _asset.RecordSync(textAsset, "hash2");

            Assert.AreEqual(1, _asset.fileStates.Count, "Should still have only one state");
            Assert.AreEqual("hash2", _asset.fileStates[0].contentHash, "Should update the hash");
            Assert.Greater(_asset.fileStates[0].lastSyncTime, firstTime, "Should update sync time");
        }

        [Test]
        public void CleanupStaleStates_RemovesStatesForRemovedFiles()
        {
            var file1 = new TextAsset("print('test1')");
            var file2 = new TextAsset("print('test2')");

            // Add both files
            _asset.pythonFiles.Add(file1);
            _asset.pythonFiles.Add(file2);

            // Record sync for both
            _asset.RecordSync(file1, "hash1");
            _asset.RecordSync(file2, "hash2");

            Assert.AreEqual(2, _asset.fileStates.Count, "Should have two states");

            // Remove one file
            _asset.pythonFiles.Remove(file1);

            // Cleanup
            _asset.CleanupStaleStates();

            Assert.AreEqual(1, _asset.fileStates.Count, "Should have one state after cleanup");
        }

        [Test]
        public void CleanupStaleStates_KeepsStatesForCurrentFiles()
        {
            var file1 = new TextAsset("print('test1')");

            _asset.pythonFiles.Add(file1);
            _asset.RecordSync(file1, "hash1");

            _asset.CleanupStaleStates();

            Assert.AreEqual(1, _asset.fileStates.Count, "Should keep state for current file");
        }

        [Test]
        public void CleanupStaleStates_HandlesEmptyFilesList()
        {
            // Add some states without corresponding files
            _asset.fileStates.Add(new PythonFileState
            {
                assetGuid = "fake_guid_1",
                contentHash = "hash1",
                fileName = "test1.py",
                lastSyncTime = DateTime.UtcNow
            });

            _asset.CleanupStaleStates();

            Assert.IsEmpty(_asset.fileStates, "Should remove all states when no files exist");
        }
    }
}
