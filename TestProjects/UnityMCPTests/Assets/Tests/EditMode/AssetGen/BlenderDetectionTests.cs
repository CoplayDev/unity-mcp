using System.Collections.Generic;
using System.IO;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using NUnit.Framework;
using UnityEngine;

namespace MCPForUnityTests.Editor.AssetGen
{
    /// <summary>
    /// Covers the pure detection core (path list + an exists predicate) deterministically, without
    /// depending on whether the test machine actually has Blender installed.
    /// </summary>
    public class BlenderDetectionTests
    {
        [Test]
        public void DetectIn_ReturnsTrue_WhenACandidateExists()
        {
            var candidates = new List<string> { "/x/blender", "/y/blender" };
            Assert.IsTrue(BlenderDetection.DetectIn(candidates, p => p == "/y/blender"));
        }

        [Test]
        public void DetectIn_ReturnsFalse_WhenNoCandidateExists()
        {
            var candidates = new List<string> { "/x/blender", "/y/blender" };
            Assert.IsFalse(BlenderDetection.DetectIn(candidates, _ => false));
        }

        [Test]
        public void DetectIn_IgnoresNullOrEmptyCandidates()
        {
            var candidates = new List<string> { null, "", "/real/blender" };
            Assert.IsTrue(BlenderDetection.DetectIn(candidates, p => p == "/real/blender"));
        }

        [Test]
        public void CandidatePaths_AreNonEmpty()
        {
            CollectionAssert.IsNotEmpty(new List<string>(BlenderDetection.CandidatePaths()));
        }

        [Test]
        public void CandidatePaths_IncludeTheDefaultSteamLibrary()
        {
            var paths = BlenderDetection.CandidatePaths().Select(p => p.Replace('\\', '/')).ToList();
            string exe = Application.platform == RuntimePlatform.WindowsEditor ? "blender.exe"
                : Application.platform == RuntimePlatform.OSXEditor ? "Blender.app/Contents/MacOS/Blender"
                : "blender";
            Assert.IsTrue(paths.Any(p => p.EndsWith("steamapps/common/Blender/" + exe)), string.Join("\n", paths));
            if (Application.platform == RuntimePlatform.LinuxEditor)
                Assert.IsTrue(paths.Any(p => p.EndsWith(".local/share/flatpak/exports/bin/org.blender.Blender")),
                    "per-user flatpak export should be a candidate on Linux");
        }

        [Test]
        public void HasStorePackage_MatchesTheBlenderPackageFamily()
        {
            const string root = "C:/Users/u/AppData/Local/Packages";
            var dirs = new List<string> { root + "/Microsoft.WindowsCalculator_8wekyb3d8bbwe", root + "/BlenderFoundation.Blender_ppwjx1n5r4v9t" };
            Assert.IsTrue(BlenderDetection.HasStorePackage(root, r => r == root ? dirs : new List<string>()));
        }

        [Test]
        public void HasStorePackage_IgnoresOtherPackages()
        {
            const string root = "C:/Users/u/AppData/Local/Packages";
            var dirs = new List<string> { root + "/BlenderFoundation.Other_x", root + "/Microsoft.Foo_8wekyb3d8bbwe", root + "/Blender_ppwjx1n5r4v9t" };
            Assert.IsFalse(BlenderDetection.HasStorePackage(root, _ => dirs));
        }

        [Test]
        public void HasStorePackage_ReturnsFalse_ForMissingOrUnreadableRoot()
        {
            Assert.IsFalse(BlenderDetection.HasStorePackage(null, _ => new List<string> { "BlenderFoundation.Blender_x" }));
            Assert.IsFalse(BlenderDetection.HasStorePackage("", _ => new List<string> { "BlenderFoundation.Blender_x" }));
            Assert.IsFalse(BlenderDetection.HasStorePackage("C:/missing/Packages", _ => throw new DirectoryNotFoundException()));
            Assert.IsFalse(BlenderDetection.HasStorePackage("C:/Packages", null));
            Assert.IsFalse(BlenderDetection.HasStorePackage("C:/Packages", _ => null));
        }

        [Test]
        public void PickAddonsDir_PrefersNewestDirThatHasTheFile()
        {
            var dirs = new List<string> { "/cfg/5.2/scripts/addons", "/cfg/4.2/scripts/addons" };
            string picked = BlenderDetection.PickAddonsDir(dirs, p => p == "/cfg/4.2/scripts/addons/addon.py", "addon.py");
            Assert.AreEqual("/cfg/4.2/scripts/addons", picked);
        }

        [Test]
        public void PickAddonsDir_FallsBackToNewest_WhenFileIsNowhere()
        {
            var dirs = new List<string> { "/cfg/5.2/scripts/addons", "/cfg/4.2/scripts/addons" };
            Assert.AreEqual("/cfg/5.2/scripts/addons", BlenderDetection.PickAddonsDir(dirs, _ => false, "addon.py"));
        }

        [Test]
        public void PickAddonsDir_ReturnsNull_WhenNoDirs()
        {
            Assert.IsNull(BlenderDetection.PickAddonsDir(new List<string>(), _ => true, "addon.py"));
            Assert.IsNull(BlenderDetection.PickAddonsDir(null, _ => true, "addon.py"));
        }

        [Test]
        public void ParseVersion_AcceptsBlenderFolderNames()
        {
            Assert.AreEqual(new System.Version(5, 2), BlenderDetection.ParseVersion("5.2"));
            Assert.AreEqual(new System.Version(4, 0), BlenderDetection.ParseVersion("4"));
            Assert.IsNull(BlenderDetection.ParseVersion("config"));
            Assert.IsNull(BlenderDetection.ParseVersion(""));
        }
    }
}
