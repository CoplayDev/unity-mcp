using System;
using System.IO;
using MCPForUnity.Editor.Security;
using NUnit.Framework;

namespace MCPForUnityTests.Editor.AssetGen
{
    /// <summary>
    /// Targets the CI-safe EncryptedFileKeyStore directly against a throwaway temp dir
    /// (never the user's real store). Proves round-trip, deletion, overwrite, multi-secret
    /// blobs, and that the on-disk file is actually encrypted.
    /// </summary>
    public class SecureKeyStoreTests
    {
        private string _dir;
        private EncryptedFileKeyStore _store;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "mcp_assetgen_keys_" + Guid.NewGuid().ToString("N"));
            _store = new EncryptedFileKeyStore(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { /* ignore */ }
        }

        [Test]
        public void SetThenGet_RoundTrips()
        {
            _store.Set("tripo", "tsk_secret_value_123");
            Assert.IsTrue(_store.Has("tripo"));
            Assert.IsTrue(_store.TryGet("tripo", out string v));
            Assert.AreEqual("tsk_secret_value_123", v);
        }

        [Test]
        public void Get_Missing_ReturnsFalse()
        {
            Assert.IsFalse(_store.Has("meshy"));
            Assert.IsFalse(_store.TryGet("meshy", out string v));
            Assert.IsNull(v);
        }

        [Test]
        public void Delete_RemovesKey()
        {
            _store.Set("fal", "k-123456");
            _store.Delete("fal");
            Assert.IsFalse(_store.Has("fal"));
            Assert.IsFalse(_store.TryGet("fal", out _));
        }

        [Test]
        public void Overwrite_ReplacesValue()
        {
            _store.Set("tripo", "first_value");
            _store.Set("tripo", "second_value");
            Assert.IsTrue(_store.TryGet("tripo", out string v));
            Assert.AreEqual("second_value", v);
        }

        [Test]
        public void MultiSecret_JsonBlob_RoundTrips()
        {
            string blob = "{\"secretId\":\"AKID123\",\"secretKey\":\"SK456\"}";
            _store.Set("tripo", blob);
            Assert.IsTrue(_store.TryGet("tripo", out string v));
            Assert.AreEqual(blob, v);
        }

        [Test]
        public void Set_Empty_DeletesKey()
        {
            _store.Set("tripo", "abc");
            _store.Set("tripo", "");
            Assert.IsFalse(_store.Has("tripo"));
        }

        [Test]
        public void Ciphertext_OnDisk_DoesNotContainPlaintext()
        {
            _store.Set("tripo", "PLAINTEXT_SECRET_XYZ");
            string file = Path.Combine(_dir, "key_tripo.bin");
            Assert.IsTrue(File.Exists(file));
            StringAssert.DoesNotContain("PLAINTEXT_SECRET_XYZ", File.ReadAllText(file));
        }
    }
}
