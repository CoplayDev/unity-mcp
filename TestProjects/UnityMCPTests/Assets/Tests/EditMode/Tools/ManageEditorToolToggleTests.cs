using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Services.Transport;
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
        private TransportManager _originalTransportManager;

        [SetUp]
        public void SetUp()
        {
            _targetToolPrefKey = EditorPrefKeys.ToolEnabledPrefix + TargetTool;
            _hadTargetToolPref = EditorPrefs.HasKey(_targetToolPrefKey);
            _previousTargetToolEnabled = EditorPrefs.GetBool(_targetToolPrefKey, true);
            _originalTransportManager = MCPServiceLocator.TransportManager;
        }

        [TearDown]
        public void TearDown()
        {
            if (_originalTransportManager != null)
            {
                MCPServiceLocator.Register(_originalTransportManager);
            }

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
        public void HandleCommand_SetMcpToolEnabled_SetActiveInstanceIsUnknownTool()
        {
            var result = ManageEditor.HandleCommand(new JObject
            {
                ["action"] = "set_mcp_tool_enabled",
                ["toolName"] = "set_active_instance",
                ["enabled"] = false,
            });

            var response = JObject.FromObject(result);
            Assert.AreEqual(false, response["success"]?.Value<bool>());
            StringAssert.Contains("Unknown tool", response["error"]?.ToString());
            StringAssert.Contains("set_active_instance", response["error"]?.ToString());
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

        [Test]
        public void HandleCommand_SetMcpToolEnabled_ReregistersHttpTools_WhenHttpClientConnected()
        {
            var httpClient = new FakeTransportClient(isConnected: true, "http");
            var stdioClient = new FakeTransportClient(isConnected: false, "stdio");
            var transportManager = new TransportManager();
            transportManager.Configure(
                () => httpClient,
                () => stdioClient);

            bool started = transportManager.StartAsync(TransportMode.Http).GetAwaiter().GetResult();
            Assert.IsTrue(started);

            MCPServiceLocator.Register(transportManager);

            var result = ManageEditor.HandleCommand(new JObject
            {
                ["action"] = "set_mcp_tool_enabled",
                ["toolName"] = TargetTool,
                ["enabled"] = false,
            });

            var response = JObject.FromObject(result);
            Assert.AreEqual(true, response["success"]?.Value<bool>());
            Assert.IsTrue(httpClient.WaitForReregister(TimeSpan.FromSeconds(1)), "Expected HTTP tools to be reregistered.");
            Assert.AreEqual(1, httpClient.ReregisterCalls);
        }

        [Test]
        public void HandleCommand_SetMcpToolEnabled_DoesNotFail_WhenHttpClientMissing()
        {
            var transportManager = new TransportManager();
            transportManager.Configure(
                () => new FakeTransportClient(isConnected: true, "http"),
                () => new FakeTransportClient(isConnected: false, "stdio"));

            MCPServiceLocator.Register(transportManager);

            var result = ManageEditor.HandleCommand(new JObject
            {
                ["action"] = "set_mcp_tool_enabled",
                ["toolName"] = TargetTool,
                ["enabled"] = false,
            });

            var response = JObject.FromObject(result);
            Assert.AreEqual(true, response["success"]?.Value<bool>());
        }

        [Test]
        public void HandleCommand_SetMcpToolEnabled_DoesNotReregister_WhenHttpClientDisconnected()
        {
            var httpClient = new FakeTransportClient(isConnected: false, "http");
            var stdioClient = new FakeTransportClient(isConnected: false, "stdio");
            var transportManager = new TransportManager();
            transportManager.Configure(
                () => httpClient,
                () => stdioClient);

            bool started = transportManager.StartAsync(TransportMode.Http).GetAwaiter().GetResult();
            Assert.IsFalse(started);

            MCPServiceLocator.Register(transportManager);

            var result = ManageEditor.HandleCommand(new JObject
            {
                ["action"] = "set_mcp_tool_enabled",
                ["toolName"] = TargetTool,
                ["enabled"] = false,
            });

            var response = JObject.FromObject(result);
            Assert.AreEqual(true, response["success"]?.Value<bool>());
            Assert.IsFalse(httpClient.WaitForReregister(TimeSpan.FromMilliseconds(100)));
            Assert.AreEqual(0, httpClient.ReregisterCalls);
        }

        [Test]
        public void HandleCommand_SetMcpToolEnabled_SwallowsReregisterErrors()
        {
            var httpClient = new FakeTransportClient(isConnected: true, "http", throwOnReregister: true);
            var stdioClient = new FakeTransportClient(isConnected: false, "stdio");
            var transportManager = new TransportManager();
            transportManager.Configure(
                () => httpClient,
                () => stdioClient);

            bool started = transportManager.StartAsync(TransportMode.Http).GetAwaiter().GetResult();
            Assert.IsTrue(started);

            MCPServiceLocator.Register(transportManager);

            var result = ManageEditor.HandleCommand(new JObject
            {
                ["action"] = "set_mcp_tool_enabled",
                ["toolName"] = TargetTool,
                ["enabled"] = false,
            });

            var response = JObject.FromObject(result);
            Assert.AreEqual(true, response["success"]?.Value<bool>());
            Assert.IsTrue(httpClient.WaitForReregister(TimeSpan.FromSeconds(1)), "Expected HTTP reregister to be attempted.");
            Assert.AreEqual(1, httpClient.ReregisterCalls);
        }

        [Test]
        public void HandleCommand_SetMcpToolEnabled_DoesNotReregister_WhenValidationFails()
        {
            var httpClient = new FakeTransportClient(isConnected: true, "http");
            var stdioClient = new FakeTransportClient(isConnected: false, "stdio");
            var transportManager = new TransportManager();
            transportManager.Configure(
                () => httpClient,
                () => stdioClient);

            bool started = transportManager.StartAsync(TransportMode.Http).GetAwaiter().GetResult();
            Assert.IsTrue(started);

            MCPServiceLocator.Register(transportManager);

            var result = ManageEditor.HandleCommand(new JObject
            {
                ["action"] = "set_mcp_tool_enabled",
                ["toolName"] = "manage_editor",
                ["enabled"] = false,
            });

            var response = JObject.FromObject(result);
            Assert.AreEqual(false, response["success"]?.Value<bool>());
            Assert.IsFalse(httpClient.WaitForReregister(TimeSpan.FromMilliseconds(100)));
            Assert.AreEqual(0, httpClient.ReregisterCalls);
        }

        private sealed class FakeTransportClient : IMcpTransportClient
        {
            private readonly bool _isConnected;
            private readonly bool _throwOnReregister;
            private readonly ManualResetEventSlim _reregisterSignal = new ManualResetEventSlim(false);
            private int _reregisterCalls;

            public FakeTransportClient(bool isConnected, string name, bool throwOnReregister = false)
            {
                _isConnected = isConnected;
                _throwOnReregister = throwOnReregister;
                TransportName = name;
            }

            public bool IsConnected => _isConnected;
            public string TransportName { get; }
            public TransportState State => _isConnected
                ? TransportState.Connected(TransportName)
                : TransportState.Disconnected(TransportName);
            public int ReregisterCalls => _reregisterCalls;

            public Task<bool> StartAsync() => Task.FromResult(_isConnected);

            public Task StopAsync() => Task.CompletedTask;

            public Task<bool> VerifyAsync() => Task.FromResult(_isConnected);

            public Task ReregisterToolsAsync()
            {
                Interlocked.Increment(ref _reregisterCalls);
                _reregisterSignal.Set();
                if (_throwOnReregister)
                {
                    throw new InvalidOperationException("simulated reregister failure");
                }
                return Task.CompletedTask;
            }

            public bool WaitForReregister(TimeSpan timeout) => _reregisterSignal.Wait(timeout);
        }
    }
}
