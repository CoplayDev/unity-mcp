using System;
using System.Linq;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;

namespace MCPForUnityTests.Editor.Tools
{
    public class ManageEditorToolToggleTests
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
        public void HandleCommand_SetMcpToolEnabled_UpdatesStoredToolPreference()
        {
            var result = ManageEditor.HandleCommand(new JObject
            {
                ["action"] = "set_mcp_tool_enabled",
                ["toolName"] = TargetTool,
                ["enabled"] = false,
            });

            var response = JObject.FromObject(result);
            Assert.AreEqual(true, response["success"]?.Value<bool>());
            Assert.AreEqual(false, EditorPrefs.GetBool(_targetToolPrefKey, true));
        }

        [Test]
        public void HandleCommand_SetMcpToolEnabled_RejectsDisablingManageEditor()
        {
            var result = ManageEditor.HandleCommand(new JObject
            {
                ["action"] = "set_mcp_tool_enabled",
                ["toolName"] = "manage_editor",
                ["enabled"] = false,
            });

            var response = JObject.FromObject(result);
            Assert.AreEqual(false, response["success"]?.Value<bool>());
            StringAssert.Contains("cannot be disabled", response["error"]?.ToString());
        }

        [Test]
        public void HandleCommand_GetMcpToolEnabled_ReturnsCurrentState()
        {
            EditorPrefs.SetBool(_targetToolPrefKey, false);

            var result = ManageEditor.HandleCommand(new JObject
            {
                ["action"] = "get_mcp_tool_enabled",
                ["toolName"] = TargetTool,
            });

            var response = JObject.FromObject(result);
            Assert.AreEqual(true, response["success"]?.Value<bool>());

            var data = response["data"] as JObject;
            Assert.IsNotNull(data);
            Assert.AreEqual(TargetTool, data["toolName"]?.ToString());
            Assert.AreEqual(false, data["enabled"]?.Value<bool>());
        }

        [Test]
        public void HandleCommand_ListMcpTools_ReturnsToolStateShape()
        {
            var result = ManageEditor.HandleCommand(new JObject
            {
                ["action"] = "list_mcp_tools",
            });

            var response = JObject.FromObject(result);
            Assert.AreEqual(true, response["success"]?.Value<bool>());

            var data = response["data"] as JObject;
            Assert.IsNotNull(data);
            var tools = data["tools"] as JArray;
            Assert.IsNotNull(tools);
            Assert.Greater(tools.Count, 0);

            var sceneTool = tools
                .OfType<JObject>()
                .FirstOrDefault(tool => string.Equals(tool["name"]?.ToString(), TargetTool, StringComparison.Ordinal));

            Assert.IsNotNull(sceneTool);
            Assert.IsNotNull(sceneTool["enabled"]);
            Assert.IsNotNull(sceneTool["autoRegister"]);
            Assert.IsNotNull(sceneTool["isBuiltIn"]);
        }
    }
}
