using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MCPForUnity.Runtime.Helpers;

namespace MCPForUnityTests.Editor.Helpers
{
    public class ScreenshotCapturerTests
    {
        [TearDown]
        public void TearDown()
        {
            foreach (var capturer in Resources.FindObjectsOfTypeAll<ScreenshotCapturer>())
            {
                if (capturer != null)
                    Object.DestroyImmediate(capturer.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator Begin_DoesNotLeakCapturerWhenFrameNeverCompletes()
        {
            LogAssert.ignoreFailingMessages = true;

            bool called = false;
            var capturer = ScreenshotCapturer.Begin(1, _ => called = true, timeoutSeconds: 0.15f);
            Assert.IsNotNull(capturer, "Begin should return the live capturer.");

            float deadline = Time.realtimeSinceStartup + 2f;
            while (!called && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.IsTrue(called, "Capturer must complete even if WaitForEndOfFrame never resumes.");
            yield return null;

            Assert.IsTrue(capturer == null, "Hidden __MCP_ScreenshotCapturer__ must destroy itself after completion.");
            Assert.AreEqual(0, Resources.FindObjectsOfTypeAll<ScreenshotCapturer>().Length);
        }

        [Test]
        public void Destroy_CompletesPendingCallback()
        {
            bool called = false;
            Texture2D received = null;

            var capturer = ScreenshotCapturer.Begin(1, tex =>
            {
                received = tex;
                called = true;
            }, timeoutSeconds: 5f);

            Assert.IsNotNull(capturer);
            Object.DestroyImmediate(capturer.gameObject);

            Assert.IsTrue(called, "Destroying the capturer must complete the waiter so MCP commands cannot hang.");
            Assert.IsNull(received);
            Assert.AreEqual(0, Resources.FindObjectsOfTypeAll<ScreenshotCapturer>().Length);
        }
    }
}
