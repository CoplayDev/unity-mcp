using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MCPForUnity.Editor.Constants;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;

namespace MCPForUnityTests.Editor.Services
{
    public class TransportCommandDispatcherToolToggleTests
    {
        private const string TargetTool = "manage_scene";
        private string _targetToolPrefKey;
        private bool _hadTargetToolPref;
        private bool _previousTargetToolEnabled;

        [SetUp]
        public void SetUp()
        {
            _targetToolPrefKey = EditorPrefKeys.ToolEnabledPrefix + TargetTool;
            _hadTargetToolPref = EditorPrefs.HasKey(_targetToolPrefKey);
            _previousTargetToolEnabled = EditorPrefs.GetBool(_targetToolPrefKey, true);
        }

        [TearDown]
        public void TearDown()
        {
            if (_hadTargetToolPref)
            {
                EditorPrefs.SetBool(_targetToolPrefKey, _previousTargetToolEnabled);
            }
            else
            {
                EditorPrefs.DeleteKey(_targetToolPrefKey);
            }
        }

        [Test]
        public void ExecuteCommandJsonAsync_WhenToolDisabled_ReturnsDisabledError()
        {
            EditorPrefs.SetBool(_targetToolPrefKey, false);

            string payload = new JObject
            {
                ["type"] = TargetTool,
                ["params"] = new JObject
                {
                    ["action"] = "ping",
                },
            }.ToString();

            string responseJson = ExecuteCommandJson(payload);
            var response = JObject.Parse(responseJson);
            string error = response["error"]?.ToString() ?? string.Empty;

            Assert.AreEqual("error", response["status"]?.ToString());
            StringAssert.Contains("disabled in the Unity Editor", error);
        }

        [Test]
        public void ExecuteCommandJsonAsync_WhenToolEnabled_DoesNotReturnDisabledError()
        {
            EditorPrefs.SetBool(_targetToolPrefKey, true);

            string payload = new JObject
            {
                ["type"] = TargetTool,
                ["params"] = new JObject
                {
                    ["action"] = "ping",
                },
            }.ToString();

            string responseJson = ExecuteCommandJson(payload);
            var response = JObject.Parse(responseJson);
            string error = response["error"]?.ToString() ?? string.Empty;

            Assert.Less(error.IndexOf("disabled in the Unity Editor", StringComparison.OrdinalIgnoreCase), 0);
        }

        private static string ExecuteCommandJson(string commandJson)
        {
            Type dispatcherType = Type.GetType(
                "MCPForUnity.Editor.Services.Transport.TransportCommandDispatcher, MCPForUnity.Editor");
            Assert.IsNotNull(dispatcherType, "Failed to resolve TransportCommandDispatcher type.");

            MethodInfo executeMethod = dispatcherType.GetMethod(
                "ExecuteCommandJsonAsync",
                BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(executeMethod, "Failed to resolve ExecuteCommandJsonAsync.");

            var task = executeMethod.Invoke(
                null,
                new object[] { commandJson, CancellationToken.None }) as Task<string>;
            Assert.IsNotNull(task, "ExecuteCommandJsonAsync did not return Task<string>.");

            return task.GetAwaiter().GetResult();
        }
    }
}
