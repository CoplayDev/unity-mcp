using NUnit.Framework;
using MCPForUnity.Editor.Services;

namespace MCPForUnityTests.Editor.Services
{
    /// <summary>
    /// Parsing of the GET /api/instances response the quit handler uses for its last-one-out check.
    /// The transport itself is not exercised here; the decision on top of the count is covered by
    /// McpEditorShutdownCleanupTests.ShouldStopManagedServer.
    /// </summary>
    [TestFixture]
    public class ServerManagementServiceInstanceProbeTests
    {
        private const string OwnHash = "aaaa111122223333";

        [Test]
        public void TryCountOtherInstances_OnlyOurOwnSession_CountsZero()
        {
            // Our own session can still be listed while the hub processes the WebSocket close that
            // the quit handler issued a moment earlier; it must not count as "someone else".
            string json = "{\"success\": true, \"instances\": [" +
                          "{\"session_id\": \"s1\", \"project\": \"Ours\", \"hash\": \"" + OwnHash + "\", \"unity_version\": \"6000.0.1f1\", \"connected_at\": 1.0}" +
                          "]}";

            Assert.IsTrue(ServerManagementService.TryCountOtherInstances(json, OwnHash, out int others));
            Assert.AreEqual(0, others);
        }

        [Test]
        public void TryCountOtherInstances_AnotherProjectConnected_CountsIt()
        {
            string json = "{\"success\": true, \"instances\": [" +
                          "{\"session_id\": \"s1\", \"project\": \"Ours\", \"hash\": \"" + OwnHash + "\"}," +
                          "{\"session_id\": \"s2\", \"project\": \"Theirs\", \"hash\": \"bbbb444455556666\"}" +
                          "]}";

            Assert.IsTrue(ServerManagementService.TryCountOtherInstances(json, OwnHash, out int others));
            Assert.AreEqual(1, others);
        }

        [Test]
        public void TryCountOtherInstances_OwnHashCaseDiffers_StillFiltered()
        {
            string json = "{\"success\": true, \"instances\": [{\"hash\": \"" + OwnHash.ToUpperInvariant() + "\"}]}";

            Assert.IsTrue(ServerManagementService.TryCountOtherInstances(json, OwnHash, out int others));
            Assert.AreEqual(0, others);
        }

        [Test]
        public void TryCountOtherInstances_NoInstances_CountsZero()
        {
            Assert.IsTrue(ServerManagementService.TryCountOtherInstances("{\"success\": true, \"instances\": []}", OwnHash, out int others));
            Assert.AreEqual(0, others);
        }

        [Test]
        public void TryCountOtherInstances_InstanceWithoutHash_CountsAsOther()
        {
            // Unknown identity is treated as another editor: the caller then leaves the server running.
            string json = "{\"success\": true, \"instances\": [{\"session_id\": \"s9\"}]}";

            Assert.IsTrue(ServerManagementService.TryCountOtherInstances(json, OwnHash, out int others));
            Assert.AreEqual(1, others);
        }

        [Test]
        public void TryCountOtherInstances_ServerReportedFailure_ReturnsFalse()
        {
            Assert.IsFalse(ServerManagementService.TryCountOtherInstances("{\"success\": false, \"error\": \"boom\"}", OwnHash, out _));
        }

        [Test]
        public void TryCountOtherInstances_MissingInstancesArray_ReturnsFalse()
        {
            Assert.IsFalse(ServerManagementService.TryCountOtherInstances("{\"success\": true}", OwnHash, out _));
        }

        [Test]
        public void TryCountOtherInstances_MalformedJson_ReturnsFalse()
        {
            Assert.IsFalse(ServerManagementService.TryCountOtherInstances("<html>not json</html>", OwnHash, out int others));
            Assert.AreEqual(0, others);
            Assert.IsFalse(ServerManagementService.TryCountOtherInstances(string.Empty, OwnHash, out _));
            Assert.IsFalse(ServerManagementService.TryCountOtherInstances(null, OwnHash, out _));
        }
    }
}
