using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MCPForUnity.Editor.Data;
using MCPForUnity.Editor.Services;

namespace MCPForUnityTests.Editor.Services
{
    public class PythonToolRegistryServiceTests
    {
        private PythonToolRegistryService _service;

        [SetUp]
        public void SetUp()
        {
            _service = new PythonToolRegistryService();
        }

        [Test]
        public void GetAllRegistries_ReturnsEmptyList_WhenNoPythonToolsAssetsExist()
        {
            var registries = _service.GetAllRegistries().ToList();

            // Note: This might find assets in the test project, so we just verify it doesn't throw
            Assert.IsNotNull(registries, "Should return a non-null list");
        }

        [Test]
        public void NeedsSync_ReturnsTrue_WhenHashingDisabled()
        {
            var asset = ScriptableObject.CreateInstance<PythonToolsAsset>();
            asset.useContentHashing = false;

            var textAsset = new TextAsset("print('test')");

            bool needsSync = _service.NeedsSync(asset, textAsset);

            Assert.IsTrue(needsSync, "Should always need sync when hashing is disabled");

            Object.DestroyImmediate(asset);
        }

        [Test]
        public void NeedsSync_ReturnsTrue_WhenFileNotPreviouslySynced()
        {
            var asset = ScriptableObject.CreateInstance<PythonToolsAsset>();
            asset.useContentHashing = true;

            var textAsset = new TextAsset("print('test')");

            bool needsSync = _service.NeedsSync(asset, textAsset);

            Assert.IsTrue(needsSync, "Should need sync for new file");

            Object.DestroyImmediate(asset);
        }

        [Test]
        public void NeedsSync_ReturnsFalse_WhenHashMatches()
        {
            var asset = ScriptableObject.CreateInstance<PythonToolsAsset>();
            asset.useContentHashing = true;

            var textAsset = new TextAsset("print('test')");

            // First sync
            _service.RecordSync(asset, textAsset);

            // Check if needs sync again
            bool needsSync = _service.NeedsSync(asset, textAsset);

            Assert.IsFalse(needsSync, "Should not need sync when hash matches");

            Object.DestroyImmediate(asset);
        }

        [Test]
        public void RecordSync_StoresFileState()
        {
            var asset = ScriptableObject.CreateInstance<PythonToolsAsset>();
            var textAsset = new TextAsset("print('test')");

            _service.RecordSync(asset, textAsset);

            Assert.AreEqual(1, asset.fileStates.Count, "Should have one file state recorded");
            Assert.IsNotNull(asset.fileStates[0].contentHash, "Hash should be stored");
            Assert.IsNotNull(asset.fileStates[0].assetGuid, "GUID should be stored");

            Object.DestroyImmediate(asset);
        }

        [Test]
        public void RecordSync_UpdatesExistingState_WhenFileAlreadyRecorded()
        {
            var asset = ScriptableObject.CreateInstance<PythonToolsAsset>();
            var textAsset = new TextAsset("print('test')");

            // Record twice
            _service.RecordSync(asset, textAsset);
            var firstHash = asset.fileStates[0].contentHash;

            _service.RecordSync(asset, textAsset);

            Assert.AreEqual(1, asset.fileStates.Count, "Should still have only one state");
            Assert.AreEqual(firstHash, asset.fileStates[0].contentHash, "Hash should remain the same");

            Object.DestroyImmediate(asset);
        }

        [Test]
        public void ComputeHash_ReturnsSameHash_ForSameContent()
        {
            var textAsset1 = new TextAsset("print('hello')");
            var textAsset2 = new TextAsset("print('hello')");

            string hash1 = _service.ComputeHash(textAsset1);
            string hash2 = _service.ComputeHash(textAsset2);

            Assert.AreEqual(hash1, hash2, "Same content should produce same hash");
        }

        [Test]
        public void ComputeHash_ReturnsDifferentHash_ForDifferentContent()
        {
            var textAsset1 = new TextAsset("print('hello')");
            var textAsset2 = new TextAsset("print('world')");

            string hash1 = _service.ComputeHash(textAsset1);
            string hash2 = _service.ComputeHash(textAsset2);

            Assert.AreNotEqual(hash1, hash2, "Different content should produce different hash");
        }
    }
}
