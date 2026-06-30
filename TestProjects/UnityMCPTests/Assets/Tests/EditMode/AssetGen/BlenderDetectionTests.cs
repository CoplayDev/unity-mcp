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
    }
}
