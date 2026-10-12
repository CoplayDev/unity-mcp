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

        /// <summary>Creates a fresh temp home directory per test.</summary>
        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "UnityMCPTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        /// <summary>Removes the temp home directory.</summary>
        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
        }

        /// <summary>WriteBundle creates both manifest and patch at the profile-qualified path.</summary>
        [Test]
        public void WriteBundle_CreatesBothFiles()
        {
            string block = DshConfigHelper.BuildUnityMcpBlock(
                useHttp: true, httpUrl: "http://127.0.0.1:8090/mcp",
                uvxCommand: null, uvxArgs: null,
                apiKeyHeader: null, apiKey: null, windowsSystemRoot: null);

            string dir = DshBundleWriter.WriteBundle(_tempDir, "desktop", block);

            Assert.IsTrue(File.Exists(Path.Combine(dir, "package.json")));
            Assert.IsTrue(File.Exists(Path.Combine(dir, "cordis.patch.yml")));
            Assert.AreEqual(Path.Combine(_tempDir, "bundles", "desktop", "unity-mcp"), dir);
        }

        /// <summary>The manifest matches the exact content DSH's plugin manager expects.</summary>
        [Test]
        public void WriteBundle_ExactManifestContent()
        {
            string dir = DshBundleWriter.WriteBundle(_tempDir, "desktop", "# test");

            string manifest = File.ReadAllText(Path.Combine(dir, "package.json"));
            StringAssert.Contains("\"name\": \"@local/unity-mcp-dsh\"", manifest);
            StringAssert.Contains("\"version\": \"1.0.0\"", manifest);
            StringAssert.Contains("\"private\": true", manifest);
            StringAssert.Contains("\"type\": \"module\"", manifest);
            StringAssert.Contains("\"dsh\": { \"bundle\": { \"patch\": \"./cordis.patch.yml\" } }", manifest);
        }

        /// <summary>The patch file receives the block byte-for-byte.</summary>
        [Test]
        public void WriteBundle_PatchContentMatchesInput()
        {
            string block = DshConfigHelper.BuildUnityMcpBlock(
                useHttp: true, httpUrl: "http://127.0.0.1:8090/mcp",
                uvxCommand: null, uvxArgs: null,
                apiKeyHeader: null, apiKey: null, windowsSystemRoot: null);

            string dir = DshBundleWriter.WriteBundle(_tempDir, "desktop", block);

            string written = File.ReadAllText(Path.Combine(dir, "cordis.patch.yml"));
            Assert.AreEqual(block, written, "patch file must contain the block verbatim");
        }

        /// <summary>Reconfiguring overwrites the previous bundle contents.</summary>
        [Test]
        public void WriteBundle_OverwritesExistingFiles()
        {
            DshBundleWriter.WriteBundle(_tempDir, "desktop", "# first");
            string dir = DshBundleWriter.WriteBundle(_tempDir, "desktop", "# second");

            string written = File.ReadAllText(Path.Combine(dir, "cordis.patch.yml"));
            Assert.AreEqual("# second", written, "second write must overwrite the first");
        }

        /// <summary>TryDeleteBundle removes the directory and reports having done so.</summary>
        [Test]
        public void TryDeleteBundle_RemovesDir_ReturnsTrue()
        {
            DshBundleWriter.WriteBundle(_tempDir, "desktop", "# test");
            string bundleDir = Path.Combine(_tempDir, "bundles", "desktop", "unity-mcp");
            Assert.IsTrue(Directory.Exists(bundleDir));

            bool deleted = DshBundleWriter.TryDeleteBundle(_tempDir, "desktop");

            Assert.IsTrue(deleted);
            Assert.IsFalse(Directory.Exists(bundleDir));
        }

        /// <summary>TryDeleteBundle reports false when the bundle is already absent.</summary>
        [Test]
        public void TryDeleteBundle_AbsentDir_ReturnsFalse()
        {
            bool deleted = DshBundleWriter.TryDeleteBundle(_tempDir, "desktop");
            Assert.IsFalse(deleted);
        }

        /// <summary>
        /// Regression: bundle paths are profile-qualified because dsh plugin add symlinks
        /// the directory — a shared path would let one profile's delete dangle another's link.
        /// </summary>
        [Test]
        public void Bundles_AreIsolatedPerProfile()
        {
            DshBundleWriter.WriteBundle(_tempDir, "desktop", "# desktop-block");
            DshBundleWriter.WriteBundle(_tempDir, "web", "# web-block");

            string desktopDir = Path.Combine(_tempDir, "bundles", "desktop", "unity-mcp");
            string webDir = Path.Combine(_tempDir, "bundles", "web", "unity-mcp");
            Assert.AreNotEqual(desktopDir, webDir);

            Assert.IsTrue(DshBundleWriter.TryDeleteBundle(_tempDir, "desktop"));

            Assert.IsFalse(Directory.Exists(desktopDir), "target profile bundle must be deleted");
            Assert.IsTrue(Directory.Exists(webDir), "other profile bundle must survive");
            Assert.AreEqual("# web-block",
                File.ReadAllText(Path.Combine(webDir, "cordis.patch.yml")),
                "other profile bundle content must be intact");
        }

        /// <summary>GetBundleDir rejects profile names that could escape the bundles directory.</summary>
        [Test]
        public void GetBundleDir_RejectsUnsafeProfileNames()
        {
            Assert.Throws<ArgumentException>(() => DshBundleWriter.GetBundleDir(_tempDir, null));
            Assert.Throws<ArgumentException>(() => DshBundleWriter.GetBundleDir(_tempDir, " "));
            Assert.Throws<ArgumentException>(() => DshBundleWriter.GetBundleDir(_tempDir, ".."));
            Assert.Throws<ArgumentException>(() => DshBundleWriter.GetBundleDir(_tempDir, "a/b"));
            Assert.Throws<ArgumentException>(() => DshBundleWriter.GetBundleDir(_tempDir, "a\\b"));
            Assert.Throws<ArgumentException>(() => DshBundleWriter.GetBundleDir(_tempDir, "a%b"),
                "cmd.exe expands %NAME% patterns even inside quotes");
            Assert.Throws<ArgumentException>(() => DshBundleWriter.GetBundleDir(_tempDir, "a\"b"),
                "double quotes break argument quoting");
            // cmd.exe metacharacters are safe: arguments are always double-quoted.
            StringAssert.Contains("R&D", DshBundleWriter.GetBundleDir(_tempDir, "R&D"));
        }
    }
}
