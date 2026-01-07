using System;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnityTests.Editor.Tools
{
    /// <summary>
    /// Tests for RunTests tool functionality.
    /// Note: We cannot easily test the full HandleCommand because it would create
    /// recursive test runner calls.
    /// </summary>
    public class RunTestsTests
    {
        [Test]
        public void HandleCommand_WhenTestsAlreadyRunning_ReturnsBusyError()
        {
            // Arrange: Force TestJobManager into a "busy" state without starting a real run.
            // We do this via reflection because TestJobManager is internal.
            var asm = typeof(MCPForUnity.Editor.Services.MCPServiceLocator).Assembly;
            var testJobManagerType = asm.GetType("MCPForUnity.Editor.Services.TestJobManager");
            Assert.NotNull(testJobManagerType, "Could not locate TestJobManager type via reflection");

            var currentJobIdField = testJobManagerType.GetField("_currentJobId", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(currentJobIdField, "Could not locate TestJobManager._currentJobId field");

            var originalJobId = currentJobIdField.GetValue(null) as string;
            currentJobIdField.SetValue(null, "busy-test-job-id");

            try
            {
                var resultObj = MCPForUnity.Editor.Tools.RunTests.HandleCommand(new JObject()).GetAwaiter().GetResult();

                Assert.IsInstanceOf<ErrorResponse>(resultObj);
                var err = (ErrorResponse)resultObj;
                Assert.AreEqual(false, err.Success);
                Assert.AreEqual("tests_running", err.Code);

                var data = err.Data != null ? JObject.FromObject(err.Data) : null;
                Assert.NotNull(data, "Expected data payload on tests_running error");
                Assert.AreEqual("tests_running", data["reason"]?.ToString());
                Assert.GreaterOrEqual(data["retry_after_ms"]?.Value<int>() ?? 0, 500);
            }
            finally
            {
                currentJobIdField.SetValue(null, originalJobId);
            }
        }

        [Test]
        public void HandleCommand_WithInvalidMode_ReturnsError()
        {
            var resultObj = MCPForUnity.Editor.Tools.RunTests.HandleCommand(new JObject
            {
                ["mode"] = "NotARealMode"
            }).GetAwaiter().GetResult();

            Assert.IsInstanceOf<ErrorResponse>(resultObj);
            var err = (ErrorResponse)resultObj;
            Assert.AreEqual(false, err.Success);
            Assert.IsTrue(err.Error.Contains("Unknown test mode", StringComparison.OrdinalIgnoreCase));
        }
    }
}
