using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using MCPForUnity.Runtime.Helpers;

namespace MCPForUnityTests.Editor.Tools
{
    /// <summary>
    /// DownscaleTexture blitted through a RenderTexture created with the project-default
    /// read/write mode. In a Linear-colorspace project that is sRGB, so a linear-flagged
    /// capture picked up an sRGB encode on store and the inline preview came back washed
    /// out while the on-disk PNG stayed correct (issue #1328).
    /// </summary>
    [TestFixture]
    public class ScreenshotUtilityDownscaleTests
    {
        [SetUp]
        public void SkipWithoutGraphics()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("Requires a graphics device; Graphics.Blit is unavailable under -nographics.");
        }

        [Test]
        public void DownscaleTexture_LinearSource_PreservesPixelValues()
        {
            // The regression only reproduces in a Linear-colorspace project; in Gamma this
            // asserts the (already correct) identity round-trip.
            var source = new Texture2D(64, 64, TextureFormat.RGBA32, false, linear: true);
            var fill = new Color32(11, 11, 11, 255); // the dark value reported in #1328
            var pixels = new Color32[64 * 64];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = fill;
            source.SetPixels32(pixels);
            source.Apply();

            Texture2D result = null;
            try
            {
                result = ScreenshotUtility.DownscaleTexture(source, 32);

                Assert.AreEqual(32, result.width);
                Assert.AreEqual(32, result.height);

                Color32 got = result.GetPixels32()[result.width * result.height / 2];
                Assert.AreEqual(fill.r, got.r, 2, $"Red shifted {fill.r} -> {got.r} (sRGB re-encode?)");
                Assert.AreEqual(fill.g, got.g, 2, $"Green shifted {fill.g} -> {got.g} (sRGB re-encode?)");
                Assert.AreEqual(fill.b, got.b, 2, $"Blue shifted {fill.b} -> {got.b} (sRGB re-encode?)");
            }
            finally
            {
                if (result != null) Object.DestroyImmediate(result);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void DownscaleTexture_SrgbSource_PreservesPixelValues()
        {
            var source = new Texture2D(64, 64, TextureFormat.RGBA32, false, linear: false);
            var fill = new Color32(128, 64, 32, 255);
            var pixels = new Color32[64 * 64];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = fill;
            source.SetPixels32(pixels);
            source.Apply();

            Texture2D result = null;
            try
            {
                result = ScreenshotUtility.DownscaleTexture(source, 16);

                Color32 got = result.GetPixels32()[result.width * result.height / 2];
                Assert.AreEqual(fill.r, got.r, 2);
                Assert.AreEqual(fill.g, got.g, 2);
                Assert.AreEqual(fill.b, got.b, 2);
            }
            finally
            {
                if (result != null) Object.DestroyImmediate(result);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void DownscaleTexture_NeverUpscales()
        {
            var source = new Texture2D(8, 8, TextureFormat.RGBA32, false, linear: true);
            source.Apply();

            Texture2D result = null;
            try
            {
                result = ScreenshotUtility.DownscaleTexture(source, 512);
                Assert.AreEqual(8, result.width);
                Assert.AreEqual(8, result.height);
            }
            finally
            {
                if (result != null) Object.DestroyImmediate(result);
                Object.DestroyImmediate(source);
            }
        }
    }
}
