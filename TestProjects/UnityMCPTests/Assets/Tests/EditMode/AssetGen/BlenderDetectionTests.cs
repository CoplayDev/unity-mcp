using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using NUnit.Framework;

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
