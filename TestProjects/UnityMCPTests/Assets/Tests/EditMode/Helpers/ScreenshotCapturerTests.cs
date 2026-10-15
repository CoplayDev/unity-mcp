using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using MCPForUnity.Runtime.Helpers;

namespace MCPForUnityTests.Editor.Helpers
{
    public class ScreenshotCapturerTests
    {
        [TearDown]
        public void TearDown()
        {
            foreach (var capturer in UnityEngine.Resources.FindObjectsOfTypeAll<ScreenshotCapturer>())
            {
                if (capturer != null)
                    Object.DestroyImmediate(capturer.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator Begin_DoesNotLeakCapturerWhenFrameNeverCompletes()
        {
            bool called = false;
            var capturer = ScreenshotCapturer.Begin(1, _ => called = true, timeoutSeconds: 0.15f);
            Assert.IsNotNull(capturer, "Begin should return the live capturer.");

            float deadline = Time.realtimeSinceStartup + 2f;
            while (!called && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.IsTrue(called, "Capturer must complete even if WaitForEndOfFrame never resumes.");
            yield return null;

            Assert.IsTrue(capturer == null, "Hidden __MCP_ScreenshotCapturer__ must destroy itself after completion.");
            Assert.AreEqual(0, UnityEngine.Resources.FindObjectsOfTypeAll<ScreenshotCapturer>().Length);
        }

        [UnityTest]
        public IEnumerator Destroy_StillCompletesPendingCallback()
        {
            // Outside play mode Unity sends this component no OnDestroy, so after an outside
            // destroy it is the editor-update timeout that completes the waiter, and it must
            // do so without touching the destroyed object.
            bool called = false;
            Texture2D received = null;

            var capturer = ScreenshotCapturer.Begin(1, tex =>
            {
                received = tex;
                called = true;
            }, timeoutSeconds: 0.15f);

            Assert.IsNotNull(capturer);
            Object.DestroyImmediate(capturer.gameObject);

            float deadline = Time.realtimeSinceStartup + 2f;
            while (!called && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.IsTrue(called, "Destroying the capturer must complete the waiter so MCP commands cannot hang.");
            Assert.IsNull(received);
            Assert.AreEqual(0, UnityEngine.Resources.FindObjectsOfTypeAll<ScreenshotCapturer>().Length);
        }

        [Test]
        public void CaptureCompositedAsync_InBatchMode_RendersACameraAtOnceAndSaysWhy()
        {
            if (!Application.isBatchMode)
                Assert.Ignore("Covers the batch-mode path; run the suite with -batchmode.");
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("Requires a graphics device for the camera render; unavailable under -nographics.");

            var cameraObject = new GameObject("__ScreenshotBatchModeTestCamera");
            cameraObject.AddComponent<Camera>();
            const string folder = "Temp/ScreenshotCapturerTests";
            try
            {
                var task = ScreenshotUtility.CaptureCompositedAsync(
                    "batch_mode", includeImage: true, maxResolution: 64, folderOverride: folder);

                // Batch mode renders no frames, so waiting for one would only sit out the timeout.
                Assert.IsTrue(task.IsCompleted, "the call must not wait for an end of frame in batch mode");
                var result = task.Result;
                StringAssert.StartsWith("Batch mode renders no frames", result.FallbackReason);
                // The camera the response reports must be the one that rendered, as the reason says.
                Assert.IsNotNull(result.FallbackCameraName);
                StringAssert.Contains($"render of camera '{result.FallbackCameraName}'", result.FallbackReason);
                Assert.IsNotNull(result.ImageBase64, "the caller still gets an image");
                Assert.AreEqual(0, UnityEngine.Resources.FindObjectsOfTypeAll<ScreenshotCapturer>().Length,
                    "no capturer may start when no frame can come");
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
                string absolute = ScreenshotUtility.ResolveFolderAbsolute(folder);
                if (Directory.Exists(absolute))
                    Directory.Delete(absolute, true);
            }
        }
    }
}
