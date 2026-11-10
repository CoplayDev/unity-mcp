using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using MCPForUnity.Editor.Services.AssetGen.Import;
using NUnit.Framework;

namespace MCPForUnityTests.Editor.AssetGen
{
    /// <summary>
    /// Verifies <see cref="SafeZipExtractor"/> unpacks normal entries and rejects Zip-Slip
    /// traversal entries. Builds tiny zips in memory (no fixtures, no network).
    /// </summary>
    public class SafeZipExtractorTests
    {
        private string _work;

        [SetUp]
        public void SetUp()
        {
            _work = Path.Combine(Path.GetTempPath(), "mcp_zip_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_work);
        }

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(_work)) Directory.Delete(_work, true); } catch { /* ignore */ }
        }

        private string MakeZip(string entryName, string content)
        {
            string zipPath = Path.Combine(_work, "in_" + Guid.NewGuid().ToString("N") + ".zip");
            using (var ms = new MemoryStream())
            {
                using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
                {
                    ZipArchiveEntry entry = archive.CreateEntry(entryName);
                    using (Stream s = entry.Open())
                    {
                        byte[] bytes = Encoding.UTF8.GetBytes(content);
                        s.Write(bytes, 0, bytes.Length);
                    }
                }
                File.WriteAllBytes(zipPath, ms.ToArray());
            }
            return zipPath;
        }

        private string MakeMultiZip(params (string name, string content)[] entries)
        {
            string zipPath = Path.Combine(_work, "in_" + Guid.NewGuid().ToString("N") + ".zip");
            using (var ms = new MemoryStream())
            {
                using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
                {
                    foreach (var (name, content) in entries)
                    {
                        ZipArchiveEntry entry = archive.CreateEntry(name);
                        using (Stream s = entry.Open())
                        {
                            byte[] bytes = Encoding.UTF8.GetBytes(content);
                            s.Write(bytes, 0, bytes.Length);
                        }
                    }
                }
                File.WriteAllBytes(zipPath, ms.ToArray());
            }
            return zipPath;
        }

        [Test]
        public void Allowlist_SkipsDisallowedEntries()
        {
            // A hostile marketplace archive: a valid model plus an editor script + a managed dll.
            string zip = MakeMultiZip(
                ("teapot.obj", "o teapot"),
                ("Editor/Hack.cs", "// [InitializeOnLoad] arbitrary code"),
                ("plugins/Evil.dll", "MZ..."));
            string dest = Path.Combine(_work, "out");

            SafeZipExtractor.ExtractTo(zip, dest, new System.Collections.Generic.HashSet<string> { ".obj" });

            Assert.IsTrue(File.Exists(Path.Combine(dest, "teapot.obj")), "allowed model must be written");
            Assert.IsFalse(File.Exists(Path.Combine(dest, "Editor", "Hack.cs")), "disallowed .cs must be skipped");
            Assert.IsFalse(File.Exists(Path.Combine(dest, "plugins", "Evil.dll")), "disallowed .dll must be skipped");
        }

        [Test]
        public void NormalEntry_Extracts()
        {
            string zip = MakeZip("models/teapot.obj", "o teapot");
            string dest = Path.Combine(_work, "out");

            SafeZipExtractor.ExtractTo(zip, dest);

            string extracted = Path.Combine(dest, "models", "teapot.obj");
            Assert.IsTrue(File.Exists(extracted), "expected extracted file to exist");
            Assert.AreEqual("o teapot", File.ReadAllText(extracted));
        }

        [Test]
        public void TraversalEntry_Throws()
        {
            string zip = MakeZip("../evil.txt", "pwned");
            string dest = Path.Combine(_work, "out");

            Assert.Throws<IOException>(() => SafeZipExtractor.ExtractTo(zip, dest));

            string escaped = Path.GetFullPath(Path.Combine(_work, "evil.txt"));
            Assert.IsFalse(File.Exists(escaped), "traversal target must not be written");
        }
    }
}
