using MCPForUnity.Editor.Services;
using NUnit.Framework;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace MCPForUnityTests.Editor.Services
{
    /// <summary>
    /// TestRunnerApi instances are HideAndDontSave, so they survive domain reloads while the
    /// references to them do not. The reload hook must destroy the instance it owns.
    /// </summary>
    public class TestRunnerApiLeakTests
    {
        private static int CountInstances() => Resources.FindObjectsOfTypeAll<TestRunnerApi>().Length;

        [Test]
        public void ReleaseTestRunnerApi_DestroysOwnedInstance()
        {
            int before = CountInstances();
            var service = new TestRunnerService();
            Assert.AreEqual(before + 1, CountInstances(), "service should create one TestRunnerApi");

            service.ReleaseTestRunnerApi();
            Assert.AreEqual(before, CountInstances(), "reload hook should destroy the service's TestRunnerApi");

            service.Dispose();
            Assert.AreEqual(before, CountInstances(), "Dispose after release must not throw or destroy anything else");
        }

        [Test]
        public void Dispose_DestroysOwnedInstance()
        {
            int before = CountInstances();
            var service = new TestRunnerService();
            service.Dispose();
            Assert.AreEqual(before, CountInstances());
        }
    }
}
