using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MCPForUnityTests.PlayMode
{
    /// <summary>
    /// Basic PlayMode tests to verify the MCP test runner handles PlayMode correctly.
    /// These tests exercise coroutine-based testing which requires Play mode.
    /// </summary>
    public class PlayModeBasicTests
    {
        [UnityTest]
        public IEnumerator GameObjectCreation_InPlayMode_Succeeds()
        {
            var go = new GameObject("TestObject");
            Assert.IsNotNull(go);
            Assert.AreEqual("TestObject", go.name);

            yield return null; // Wait one frame

            Assert.IsTrue(go != null); // Still exists after frame
            Object.Destroy(go);
        }

        [UnityTest]
        public IEnumerator WaitForSeconds_CompletesAfterDelay()
        {
            float startTime = Time.time;

            yield return new WaitForSeconds(0.1f);

            float elapsed = Time.time - startTime;
            Assert.GreaterOrEqual(elapsed, 0.09f, "Should have waited at least 0.09 seconds");
        }

        [UnityTest]
        public IEnumerator MultipleFrames_ProgressCorrectly()
        {
            int frameCount = Time.frameCount;

            yield return null;
            yield return null;
            yield return null;

            int newFrameCount = Time.frameCount;
            Assert.Greater(newFrameCount, frameCount, "Frame count should have advanced");
        }

        [UnityTest]
        public IEnumerator Component_AddAndRemove_InPlayMode()
        {
            var go = new GameObject("ComponentTest");

            yield return null;

            var rb = go.AddComponent<Rigidbody>();
            Assert.IsNotNull(rb);
            Assert.IsTrue(go.GetComponent<Rigidbody>() != null);

            yield return null;

            Object.Destroy(rb);

            yield return null;

            Assert.IsTrue(go.GetComponent<Rigidbody>() == null);
            Object.Destroy(go);
        }

        [UnityTest]
        public IEnumerator Coroutine_CanYieldMultipleTimes()
        {
            int counter = 0;

            for (int i = 0; i < 5; i++)
            {
                counter++;
                yield return null;
            }

            Assert.AreEqual(5, counter);
        }
    }
}
