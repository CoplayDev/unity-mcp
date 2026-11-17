using System.Collections;
using MCPForUnity.Editor.Services;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.TestTools;

namespace MCPForUnityTests.Editor.Services
{
    [TestFixture]
    public class EditorStateCacheFreshnessTests
    {
        [UnityTest]
        public IEnumerator IdleEditor_KeepsSnapshotTimestampCurrent()
        {
            long first = (long)EditorStateCache.GetSnapshot()["observed_at_unix_ms"];

            // Let the editor tick for longer than the server's 2 s staleness window with nothing
            // changing: the snapshot must still be re-stamped by the update loop.
            double until = EditorApplication.timeSinceStartup + 2.5;
            while (EditorApplication.timeSinceStartup < until)
            {
                yield return null;
            }

            long second = (long)EditorStateCache.GetSnapshot()["observed_at_unix_ms"];
            Assert.GreaterOrEqual(second - first, 1000, "observed_at did not advance while the editor was idle");
        }
    }
}
