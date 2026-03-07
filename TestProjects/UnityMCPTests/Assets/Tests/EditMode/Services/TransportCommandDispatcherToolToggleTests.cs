using System.Threading;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Services.Transport;
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

            string responseJson = TransportCommandDispatcher.ExecuteCommandJsonAsync(payload, CancellationToken.None).GetAwaiter().GetResult();
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

            string responseJson = TransportCommandDispatcher.ExecuteCommandJsonAsync(payload, CancellationToken.None).GetAwaiter().GetResult();
            var response = JObject.Parse(responseJson);
            string error = response["error"]?.ToString() ?? string.Empty;

            StringAssert.DoesNotContain("disabled in the Unity Editor", error);
        }
    }
}
