using MCPForUnity.Editor.Windows.Components.Branding;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace MCPForUnityTests.Editor
{
    /// <summary>
    /// Guards the geometry/color transcription and construction of the Painter2D-drawn
    /// Ocean brand mark. The pixel rendering itself is verified manually in-Editor; these
    /// tests lock the pure math and the coordinate mapping that feed the drawing.
    /// </summary>
    public class OceanMarkTests
    {
        [Test]
        public void FromHex_ParsesBrandColors()
        {
            Color c = OceanMark.FromHex(0x2563EB);
            Assert.AreEqual(0x25 / 255f, c.r, 1e-4f);
            Assert.AreEqual(0x63 / 255f, c.g, 1e-4f);
            Assert.AreEqual(0xEB / 255f, c.b, 1e-4f);
            Assert.AreEqual(1f, c.a, 1e-4f);
        }

        [Test]
        public void MapSvgPoint_MapsViewBoxCornersToSquareContentRect()
        {
            var rect = new Rect(0, 0, 140, 140);
            // Source viewBox is "30 30 140 140": origin (30,30) -> (0,0), far corner (170,170) -> (140,140).
            AssertV2(new Vector2(0, 0), OceanMark.MapSvgPoint(30, 30, rect));
            AssertV2(new Vector2(140, 140), OceanMark.MapSvgPoint(170, 170, rect));
            AssertV2(new Vector2(70, 70), OceanMark.MapSvgPoint(100, 100, rect));
        }

        [Test]
        public void MapSvgPoint_ScalesAndCentersWithinNonSquareRect()
        {
            // 40 wide, 20 tall -> box = min = 20, scale = 20/140, horizontally centered by (40-20)/2 = 10.
            var rect = new Rect(0, 0, 40, 20);
            AssertV2(new Vector2(10, 0), OceanMark.MapSvgPoint(30, 30, rect));
            AssertV2(new Vector2(30, 20), OceanMark.MapSvgPoint(170, 170, rect));
        }

        [Test]
        public void Construct_DoesNotThrow_AndIsDecorative()
        {
            var mark = new OceanMark();
            Assert.IsNotNull(mark);
            Assert.AreEqual(PickingMode.Ignore, mark.pickingMode);
        }

        private static void AssertV2(Vector2 expected, Vector2 actual)
        {
            Assert.AreEqual(expected.x, actual.x, 1e-4f, "x");
            Assert.AreEqual(expected.y, actual.y, 1e-4f, "y");
        }
    }
}
