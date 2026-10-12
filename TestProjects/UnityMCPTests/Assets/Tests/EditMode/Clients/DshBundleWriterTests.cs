using System;
using System.IO;
using MCPForUnity.Editor.Helpers;
using NUnit.Framework;

namespace MCPForUnityTests.Editor.Clients
{
    /// <summary>
    /// Covers <see cref="DshBundleWriter"/>: writing and deleting the configuration-only
    /// DSH bundle directory that holds the manifest and the managed patch block.
    /// </summary>
    [TestFixture]
    public class DshBundleWriterTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "UnityMCPTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
        }

        [Test]
        public void WriteBundle_CreatesBothFiles()
        {
            string block = DshConfigHelper.BuildUnityMcpBlock(
                useHttp: true, httpUrl: "http://127.0.0.1:8090/mcp",
                uvxCommand: null, uvxArgs: null,
                apiKeyHeader: null, apiKey: null, windowsSystemRoot: null);

            string dir = DshBundleWriter.WriteBundle(_tempDir, block);

            Assert.IsTrue(File.Exists(Path.Combine(dir, "package.json")));
            Assert.IsTrue(File.Exists(Path.Combine(dir, "cordis.patch.yml")));
            Assert.AreEqual(Path.Combine(_tempDir, "bundles", "unity-mcp"), dir);
        }

        [Test]
        public void WriteBundle_ExactManifestContent()
        {
            string dir = DshBundleWriter.WriteBundle(_tempDir, "# test");

            string manifest = File.ReadAllText(Path.Combine(dir, "package.json"));
            StringAssert.Contains("\"name\": \"@local/unity-mcp-dsh\"", manifest);
            StringAssert.Contains("\"version\": \"1.0.0\"", manifest);
            StringAssert.Contains("\"private\": true", manifest);
            StringAssert.Contains("\"type\": \"module\"", manifest);
            StringAssert.Contains("\"dsh\": { \"bundle\": { \"patch\": \"./cordis.patch.yml\" } }", manifest);
        }

        [Test]
        public void WriteBundle_PatchContentMatchesInput()
        {
            string block = DshConfigHelper.BuildUnityMcpBlock(
                useHttp: true, httpUrl: "http://127.0.0.1:8090/mcp",
                uvxCommand: null, uvxArgs: null,
                apiKeyHeader: null, apiKey: null, windowsSystemRoot: null);

            string dir = DshBundleWriter.WriteBundle(_tempDir, block);

            string written = File.ReadAllText(Path.Combine(dir, "cordis.patch.yml"));
            Assert.AreEqual(block, written, "patch file must contain the block verbatim");
        }

        [Test]
        public void WriteBundle_OverwritesExistingFiles()
        {
            DshBundleWriter.WriteBundle(_tempDir, "# first");
            string dir = DshBundleWriter.WriteBundle(_tempDir, "# second");

            string written = File.ReadAllText(Path.Combine(dir, "cordis.patch.yml"));
            Assert.AreEqual("# second", written, "second write must overwrite the first");
        }

        [Test]
        public void TryDeleteBundle_RemovesDir_ReturnsTrue()
        {
            DshBundleWriter.WriteBundle(_tempDir, "# test");
            string bundleDir = Path.Combine(_tempDir, "bundles", "unity-mcp");
            Assert.IsTrue(Directory.Exists(bundleDir));

            bool deleted = DshBundleWriter.TryDeleteBundle(_tempDir);

            Assert.IsTrue(deleted);
            Assert.IsFalse(Directory.Exists(bundleDir));
        }

        [Test]
        public void TryDeleteBundle_AbsentDir_ReturnsFalse()
        {
            bool deleted = DshBundleWriter.TryDeleteBundle(_tempDir);
            Assert.IsFalse(deleted);
        }
    }
}
