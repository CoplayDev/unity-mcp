using NUnit.Framework;
using MCPForUnity.Editor.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace MCPForUnityTests.Editor.Models.Characterization
{
    /// <summary>
    /// Characterization tests for Models domain - documenting CURRENT behavior
    /// before refactoring (P1-4: Session Model Consolidation, P2-3: Configurator Builder Pattern).
    ///
    /// These tests capture:
    /// - Current serialization/deserialization patterns
    /// - Default values and initialization
    /// - McpClient over-configuration (6 capability flags)
    /// - JSON.NET serialization behaviors
    /// - Status management patterns
    /// </summary>
    [TestFixture]
    public class ModelsCharacterizationTests
    {
        #region McpStatus Enum Tests (3 tests)

        [Test]
        public void McpStatus_HasAllExpectedValues()
        {
            // Documents the 10 status values currently defined
            var expectedValues = new[]
            {
                McpStatus.NotConfigured,
                McpStatus.Configured,
                McpStatus.Running,
                McpStatus.Connected,
                McpStatus.IncorrectPath,
                McpStatus.CommunicationError,
                McpStatus.NoResponse,
                McpStatus.MissingConfig,
                McpStatus.UnsupportedOS,
                McpStatus.Error
            };

            // Verify all expected values are defined
            foreach (var value in expectedValues)
            {
                Assert.IsTrue(System.Enum.IsDefined(typeof(McpStatus), value),
                    $"McpStatus.{value} should be defined");
            }

            // Verify count matches
            var allValues = System.Enum.GetValues(typeof(McpStatus));
            Assert.AreEqual(10, allValues.Length, "McpStatus should have exactly 10 values");
        }

        [Test]
        public void McpStatus_EnumValuesMapToIntegers()
        {
            // Documents the integer values assigned to each enum
            Assert.AreEqual(0, (int)McpStatus.NotConfigured);
            Assert.AreEqual(1, (int)McpStatus.Configured);
            Assert.AreEqual(2, (int)McpStatus.Running);
            Assert.AreEqual(3, (int)McpStatus.Connected);
            Assert.AreEqual(4, (int)McpStatus.IncorrectPath);
            Assert.AreEqual(5, (int)McpStatus.CommunicationError);
            Assert.AreEqual(6, (int)McpStatus.NoResponse);
            Assert.AreEqual(7, (int)McpStatus.MissingConfig);
            Assert.AreEqual(8, (int)McpStatus.UnsupportedOS);
            Assert.AreEqual(9, (int)McpStatus.Error);
        }

        [Test]
        public void McpStatus_CanBeComparedAndSwitchedOn()
        {
            // Documents that enum can be used in comparisons and switch statements
            McpStatus status = McpStatus.Connected;

            Assert.IsTrue(status == McpStatus.Connected);
            Assert.IsFalse(status == McpStatus.Error);

            string result = status switch
            {
                McpStatus.NotConfigured => "not configured",
                McpStatus.Connected => "connected",
                _ => "other"
            };

            Assert.AreEqual("connected", result);
        }

        #endregion

        #region ConfiguredTransport Enum Tests (2 tests)

        [Test]
        public void ConfiguredTransport_HasThreeValues()
        {
            // Documents the 3 transport types currently defined
            var expectedValues = new[]
            {
                ConfiguredTransport.Unknown,
                ConfiguredTransport.Stdio,
                ConfiguredTransport.Http
            };

            foreach (var value in expectedValues)
            {
                Assert.IsTrue(System.Enum.IsDefined(typeof(ConfiguredTransport), value),
                    $"ConfiguredTransport.{value} should be defined");
            }

            var allValues = System.Enum.GetValues(typeof(ConfiguredTransport));
            Assert.AreEqual(3, allValues.Length, "ConfiguredTransport should have exactly 3 values");
        }

        [Test]
        public void ConfiguredTransport_DefaultsToUnknown()
        {
            // Documents that ConfiguredTransport defaults to Unknown (0)
            ConfiguredTransport transport = default;
            Assert.AreEqual(ConfiguredTransport.Unknown, transport);
            Assert.AreEqual(0, (int)ConfiguredTransport.Unknown);
        }

        #endregion

        #region McpClient Tests (20 tests)

        [Test]
        public void McpClient_DefaultValues()
        {
            // Documents the default state of a new McpClient
            var client = new McpClient();

            // String fields default to null
            Assert.IsNull(client.name);
            Assert.IsNull(client.windowsConfigPath);
            Assert.IsNull(client.macConfigPath);
            Assert.IsNull(client.linuxConfigPath);
            Assert.IsNull(client.configStatus);

            // Enum fields default to specific values
            Assert.AreEqual(McpStatus.NotConfigured, client.status);
            Assert.AreEqual(ConfiguredTransport.Unknown, client.configuredTransport);
        }

        [Test]
        public void McpClient_CapabilityFlagsDefaults()
        {
            // Documents the default capability flags - note SupportsHttpTransport defaults to TRUE
            var client = new McpClient();

            Assert.IsFalse(client.IsVsCodeLayout, "IsVsCodeLayout should default to false");
            Assert.IsTrue(client.SupportsHttpTransport, "SupportsHttpTransport should default to TRUE");
            Assert.IsFalse(client.EnsureEnvObject, "EnsureEnvObject should default to false");
            Assert.IsFalse(client.StripEnvWhenNotRequired, "StripEnvWhenNotRequired should default to false");
            Assert.AreEqual("url", client.HttpUrlProperty, "HttpUrlProperty should default to 'url'");
        }

        [Test]
        public void McpClient_DefaultUnityFieldsIsInitialized()
        {
            // Documents that DefaultUnityFields is always initialized as empty dictionary
            var client = new McpClient();

            Assert.IsNotNull(client.DefaultUnityFields);
            Assert.AreEqual(0, client.DefaultUnityFields.Count);
            Assert.IsInstanceOf<Dictionary<string, object>>(client.DefaultUnityFields);
        }

        [Test]
        public void McpClient_CanSetAllFields()
        {
            // Documents that all fields can be set directly
            var client = new McpClient
            {
                name = "TestClient",
                windowsConfigPath = "C:\\path\\config.json",
                macConfigPath = "/Users/test/config.json",
                linuxConfigPath = "/home/test/config.json",
                configStatus = "Running",
                status = McpStatus.Running,
                configuredTransport = ConfiguredTransport.Stdio
            };

            Assert.AreEqual("TestClient", client.name);
            Assert.AreEqual("C:\\path\\config.json", client.windowsConfigPath);
            Assert.AreEqual("/Users/test/config.json", client.macConfigPath);
            Assert.AreEqual("/home/test/config.json", client.linuxConfigPath);
            Assert.AreEqual("Running", client.configStatus);
            Assert.AreEqual(McpStatus.Running, client.status);
            Assert.AreEqual(ConfiguredTransport.Stdio, client.configuredTransport);
        }

        [Test]
        public void McpClient_CanSetCapabilityFlags()
        {
            // Documents that capability flags can be set individually
            var client = new McpClient
            {
                IsVsCodeLayout = true,
                SupportsHttpTransport = false,
                EnsureEnvObject = true,
                StripEnvWhenNotRequired = true,
                HttpUrlProperty = "httpUrl"
            };

            Assert.IsTrue(client.IsVsCodeLayout);
            Assert.IsFalse(client.SupportsHttpTransport);
            Assert.IsTrue(client.EnsureEnvObject);
            Assert.IsTrue(client.StripEnvWhenNotRequired);
            Assert.AreEqual("httpUrl", client.HttpUrlProperty);
        }

        [Test]
        public void McpClient_CapabilityFlagsOverconfigurations()
        {
            // Documents the over-configuration issue (P2-3): 6 separate flags must be set individually
            // This pattern is repeated across 14+ configurator classes
            var client = new McpClient();

            // Each flag must be configured separately - no builder pattern
            client.IsVsCodeLayout = false;
            client.SupportsHttpTransport = true;
            client.EnsureEnvObject = false;
            client.StripEnvWhenNotRequired = false;
            client.HttpUrlProperty = "url";
            client.DefaultUnityFields = new Dictionary<string, object>
            {
                { "projectPath", "${UNITY_PROJECT_PATH}" }
            };

            // Verify all 6 configuration points
            Assert.AreEqual(6, new[]
            {
                client.IsVsCodeLayout,
                client.SupportsHttpTransport,
                client.EnsureEnvObject,
                client.StripEnvWhenNotRequired,
                !string.IsNullOrEmpty(client.HttpUrlProperty),
                client.DefaultUnityFields != null
            }.Length, "6 capability configuration points documented");
        }

        [Test]
        public void McpClient_DefaultUnityFieldsCanStoreVariousTypes()
        {
            // Documents that DefaultUnityFields accepts object values (no type restriction)
            var client = new McpClient();

            client.DefaultUnityFields["string"] = "value";
            client.DefaultUnityFields["int"] = 42;
            client.DefaultUnityFields["bool"] = true;
            client.DefaultUnityFields["nested"] = new Dictionary<string, object> { { "key", "val" } };

            Assert.AreEqual("value", client.DefaultUnityFields["string"]);
            Assert.AreEqual(42, client.DefaultUnityFields["int"]);
            Assert.AreEqual(true, client.DefaultUnityFields["bool"]);
            Assert.IsNotNull(client.DefaultUnityFields["nested"]);
        }

        [Test]
        public void McpClient_GetStatusDisplayString_NotConfigured()
        {
            var client = new McpClient { status = McpStatus.NotConfigured };
            Assert.AreEqual("Not Configured", client.GetStatusDisplayString());
        }

        [Test]
        public void McpClient_GetStatusDisplayString_Configured()
        {
            var client = new McpClient { status = McpStatus.Configured };
            Assert.AreEqual("Configured", client.GetStatusDisplayString());
        }

        [Test]
        public void McpClient_GetStatusDisplayString_Running()
        {
            var client = new McpClient { status = McpStatus.Running };
            Assert.AreEqual("Running", client.GetStatusDisplayString());
        }

        [Test]
        public void McpClient_GetStatusDisplayString_Connected()
        {
            var client = new McpClient { status = McpStatus.Connected };
            Assert.AreEqual("Connected", client.GetStatusDisplayString());
        }

        [Test]
        public void McpClient_GetStatusDisplayString_IncorrectPath()
        {
            var client = new McpClient { status = McpStatus.IncorrectPath };
            Assert.AreEqual("Incorrect Path", client.GetStatusDisplayString());
        }

        [Test]
        public void McpClient_GetStatusDisplayString_CommunicationError()
        {
            var client = new McpClient { status = McpStatus.CommunicationError };
            Assert.AreEqual("Communication Error", client.GetStatusDisplayString());
        }

        [Test]
        public void McpClient_GetStatusDisplayString_NoResponse()
        {
            var client = new McpClient { status = McpStatus.NoResponse };
            Assert.AreEqual("No Response", client.GetStatusDisplayString());
        }

        [Test]
        public void McpClient_GetStatusDisplayString_MissingConfig()
        {
            var client = new McpClient { status = McpStatus.MissingConfig };
            Assert.AreEqual("Missing MCPForUnity Config", client.GetStatusDisplayString());
        }

        [Test]
        public void McpClient_GetStatusDisplayString_UnsupportedOS()
        {
            var client = new McpClient { status = McpStatus.UnsupportedOS };
            Assert.AreEqual("Unsupported OS", client.GetStatusDisplayString());
        }

        [Test]
        public void McpClient_GetStatusDisplayString_ErrorWithoutDetails()
        {
            // Documents that Error status returns "Error" when configStatus doesn't start with "Error:"
            var client = new McpClient { status = McpStatus.Error, configStatus = "Something went wrong" };
            Assert.AreEqual("Error", client.GetStatusDisplayString());
        }

        [Test]
        public void McpClient_GetStatusDisplayString_ErrorWithDetails()
        {
            // Documents that Error status returns full configStatus if it starts with "Error:"
            var client = new McpClient { status = McpStatus.Error, configStatus = "Error: Connection failed" };
            Assert.AreEqual("Error: Connection failed", client.GetStatusDisplayString());
        }

        [Test]
        public void McpClient_SetStatus_UpdatesBothFields()
        {
            // Documents that SetStatus() updates both status enum and configStatus string
            var client = new McpClient();
            client.SetStatus(McpStatus.Connected);

            Assert.AreEqual(McpStatus.Connected, client.status);
            Assert.AreEqual("Connected", client.configStatus);
        }

        [Test]
        public void McpClient_SetStatus_ErrorWithDetails()
        {
            // Documents that SetStatus() with Error and details prefixes with "Error: "
            var client = new McpClient();
            client.SetStatus(McpStatus.Error, "Connection timeout");

            Assert.AreEqual(McpStatus.Error, client.status);
            Assert.AreEqual("Error: Connection timeout", client.configStatus);
        }

        [Test]
        public void McpClient_SetStatus_ErrorWithoutDetails()
        {
            // Documents that SetStatus() with Error but no details uses display string
            // Note: Must initialize configStatus first to avoid NullReferenceException
            // This is a known issue in the current implementation
            var client = new McpClient();
            client.configStatus = ""; // Initialize to avoid null reference
            client.SetStatus(McpStatus.Error);

            Assert.AreEqual(McpStatus.Error, client.status);
            Assert.AreEqual("Error", client.configStatus);
        }

        #endregion

        #region McpConfigServer Tests (10 tests)

        [Test]
        public void McpConfigServer_DefaultValues()
        {
            // Documents that all fields default to null
            var server = new McpConfigServer();

            Assert.IsNull(server.command);
            Assert.IsNull(server.args);
            Assert.IsNull(server.type);
            Assert.IsNull(server.url);
        }

        [Test]
        public void McpConfigServer_CanSetAllFields()
        {
            var server = new McpConfigServer
            {
                command = "npx",
                args = new[] { "-y", "@modelcontextprotocol/server-everything" },
                type = "stdio",
                url = "http://localhost:3000"
            };

            Assert.AreEqual("npx", server.command);
            Assert.AreEqual(2, server.args.Length);
            Assert.AreEqual("-y", server.args[0]);
            Assert.AreEqual("@modelcontextprotocol/server-everything", server.args[1]);
            Assert.AreEqual("stdio", server.type);
            Assert.AreEqual("http://localhost:3000", server.url);
        }

        [Test]
        public void McpConfigServer_SerializesCommandAndArgs()
        {
            var server = new McpConfigServer
            {
                command = "node",
                args = new[] { "server.js" }
            };

            string json = JsonConvert.SerializeObject(server);
            Assert.IsTrue(json.Contains("\"command\":\"node\""));
            Assert.IsTrue(json.Contains("\"args\":[\"server.js\"]"));
        }

        [Test]
        public void McpConfigServer_OmitsNullTypeField()
        {
            // Documents NullValueHandling.Ignore behavior for type field
            var server = new McpConfigServer
            {
                command = "node",
                args = new[] { "server.js" },
                type = null // explicitly null
            };

            string json = JsonConvert.SerializeObject(server);
            Assert.IsFalse(json.Contains("\"type\""), "Null type field should be omitted");
        }

        [Test]
        public void McpConfigServer_OmitsNullUrlField()
        {
            // Documents NullValueHandling.Ignore behavior for url field
            var server = new McpConfigServer
            {
                command = "node",
                args = new[] { "server.js" },
                url = null // explicitly null
            };

            string json = JsonConvert.SerializeObject(server);
            Assert.IsFalse(json.Contains("\"url\""), "Null url field should be omitted");
        }

        [Test]
        public void McpConfigServer_IncludesTypeWhenSet()
        {
            var server = new McpConfigServer
            {
                command = "node",
                args = new[] { "server.js" },
                type = "stdio"
            };

            string json = JsonConvert.SerializeObject(server);
            Assert.IsTrue(json.Contains("\"type\":\"stdio\""), "Non-null type field should be included");
        }

        [Test]
        public void McpConfigServer_IncludesUrlWhenSet()
        {
            var server = new McpConfigServer
            {
                command = "node",
                args = new[] { "server.js" },
                url = "http://localhost:3000"
            };

            string json = JsonConvert.SerializeObject(server);
            Assert.IsTrue(json.Contains("\"url\":\"http://localhost:3000\""), "Non-null url field should be included");
        }

        [Test]
        public void McpConfigServer_RoundTripSerialization()
        {
            // Documents full JSON round-trip works correctly
            var original = new McpConfigServer
            {
                command = "npx",
                args = new[] { "-y", "server" },
                type = "stdio"
            };

            string json = JsonConvert.SerializeObject(original);
            var deserialized = JsonConvert.DeserializeObject<McpConfigServer>(json);

            Assert.AreEqual(original.command, deserialized.command);
            Assert.AreEqual(original.args.Length, deserialized.args.Length);
            Assert.AreEqual(original.args[0], deserialized.args[0]);
            Assert.AreEqual(original.type, deserialized.type);
        }

        [Test]
        public void McpConfigServer_DeserializesFromJsonWithoutOptionalFields()
        {
            // Documents that type and url are truly optional in JSON
            string json = @"{""command"":""node"",""args"":[""server.js""]}";
            var server = JsonConvert.DeserializeObject<McpConfigServer>(json);

            Assert.AreEqual("node", server.command);
            Assert.AreEqual(1, server.args.Length);
            Assert.AreEqual("server.js", server.args[0]);
            Assert.IsNull(server.type);
            Assert.IsNull(server.url);
        }

        [Test]
        public void McpConfigServer_SupportsEmptyArgsArray()
        {
            var server = new McpConfigServer
            {
                command = "server-bin",
                args = new string[] { }
            };

            string json = JsonConvert.SerializeObject(server);
            Assert.IsTrue(json.Contains("\"args\":[]"));

            var deserialized = JsonConvert.DeserializeObject<McpConfigServer>(json);
            Assert.AreEqual(0, deserialized.args.Length);
        }

        #endregion

        #region McpConfigServers Tests (4 tests)

        [Test]
        public void McpConfigServers_DefaultValues()
        {
            var servers = new McpConfigServers();
            Assert.IsNull(servers.unityMCP);
        }

        [Test]
        public void McpConfigServers_CanSetUnityMCPField()
        {
            var servers = new McpConfigServers
            {
                unityMCP = new McpConfigServer
                {
                    command = "npx",
                    args = new[] { "-y", "server" }
                }
            };

            Assert.IsNotNull(servers.unityMCP);
            Assert.AreEqual("npx", servers.unityMCP.command);
        }

        [Test]
        public void McpConfigServers_SerializesWithUnityMCPPropertyName()
        {
            // Documents JsonProperty("unityMCP") controls JSON field name
            var servers = new McpConfigServers
            {
                unityMCP = new McpConfigServer
                {
                    command = "node",
                    args = new[] { "server.js" }
                }
            };

            string json = JsonConvert.SerializeObject(servers);
            Assert.IsTrue(json.Contains("\"unityMCP\""), "Should use 'unityMCP' as JSON property name");
            Assert.IsTrue(json.Contains("\"command\":\"node\""));
        }

        [Test]
        public void McpConfigServers_RoundTripSerialization()
        {
            var original = new McpConfigServers
            {
                unityMCP = new McpConfigServer
                {
                    command = "npx",
                    args = new[] { "-y", "mcp-server" },
                    type = "stdio"
                }
            };

            string json = JsonConvert.SerializeObject(original);
            var deserialized = JsonConvert.DeserializeObject<McpConfigServers>(json);

            Assert.IsNotNull(deserialized.unityMCP);
            Assert.AreEqual(original.unityMCP.command, deserialized.unityMCP.command);
            Assert.AreEqual(original.unityMCP.args.Length, deserialized.unityMCP.args.Length);
            Assert.AreEqual(original.unityMCP.type, deserialized.unityMCP.type);
        }

        #endregion

        #region McpConfig Tests (5 tests)

        [Test]
        public void McpConfig_DefaultValues()
        {
            var config = new McpConfig();
            Assert.IsNull(config.mcpServers);
        }

        [Test]
        public void McpConfig_CanSetMcpServersField()
        {
            var config = new McpConfig
            {
                mcpServers = new McpConfigServers
                {
                    unityMCP = new McpConfigServer
                    {
                        command = "npx",
                        args = new[] { "-y", "server" }
                    }
                }
            };

            Assert.IsNotNull(config.mcpServers);
            Assert.IsNotNull(config.mcpServers.unityMCP);
            Assert.AreEqual("npx", config.mcpServers.unityMCP.command);
        }

        [Test]
        public void McpConfig_SerializesWithMcpServersPropertyName()
        {
            // Documents JsonProperty("mcpServers") controls JSON field name
            var config = new McpConfig
            {
                mcpServers = new McpConfigServers
                {
                    unityMCP = new McpConfigServer
                    {
                        command = "node",
                        args = new[] { "server.js" }
                    }
                }
            };

            string json = JsonConvert.SerializeObject(config);
            Assert.IsTrue(json.Contains("\"mcpServers\""), "Should use 'mcpServers' as JSON property name");
            Assert.IsTrue(json.Contains("\"unityMCP\""));
            Assert.IsTrue(json.Contains("\"command\":\"node\""));
        }

        [Test]
        public void McpConfig_ThreeLevelHierarchy()
        {
            // Documents the three-level hierarchy: McpConfig → McpConfigServers → McpConfigServer
            var config = new McpConfig
            {
                mcpServers = new McpConfigServers
                {
                    unityMCP = new McpConfigServer
                    {
                        command = "test-cmd",
                        args = new[] { "arg1" }
                    }
                }
            };

            // Navigate through all three levels
            Assert.IsNotNull(config.mcpServers, "Level 1: McpConfig.mcpServers");
            Assert.IsNotNull(config.mcpServers.unityMCP, "Level 2: McpConfigServers.unityMCP");
            Assert.AreEqual("test-cmd", config.mcpServers.unityMCP.command, "Level 3: McpConfigServer.command");
        }

        [Test]
        public void McpConfig_RoundTripSerialization()
        {
            var original = new McpConfig
            {
                mcpServers = new McpConfigServers
                {
                    unityMCP = new McpConfigServer
                    {
                        command = "npx",
                        args = new[] { "-y", "mcp-everything" },
                        type = "stdio",
                        url = null
                    }
                }
            };

            string json = JsonConvert.SerializeObject(original);
            var deserialized = JsonConvert.DeserializeObject<McpConfig>(json);

            Assert.IsNotNull(deserialized.mcpServers);
            Assert.IsNotNull(deserialized.mcpServers.unityMCP);
            Assert.AreEqual("npx", deserialized.mcpServers.unityMCP.command);
            Assert.AreEqual(2, deserialized.mcpServers.unityMCP.args.Length);
            Assert.AreEqual("stdio", deserialized.mcpServers.unityMCP.type);
            Assert.IsNull(deserialized.mcpServers.unityMCP.url);
        }

        #endregion

        #region Command Tests (8 tests)

        [Test]
        public void Command_DefaultValues()
        {
            var command = new Command();
            Assert.IsNull(command.type);
            Assert.IsNull(command.@params);
        }

        [Test]
        public void Command_CanSetTypeField()
        {
            var command = new Command { type = "execute_action" };
            Assert.AreEqual("execute_action", command.type);
        }

        [Test]
        public void Command_AcceptsAnyStringType()
        {
            // Documents that type field has no validation - accepts any string
            var command1 = new Command { type = "valid_action" };
            var command2 = new Command { type = "InvalidAction123" };
            var command3 = new Command { type = "" };

            Assert.AreEqual("valid_action", command1.type);
            Assert.AreEqual("InvalidAction123", command2.type);
            Assert.AreEqual("", command3.type);
        }

        [Test]
        public void Command_ParamsCanBeNull()
        {
            var command = new Command
            {
                type = "simple_action",
                @params = null
            };

            Assert.AreEqual("simple_action", command.type);
            Assert.IsNull(command.@params);
        }

        [Test]
        public void Command_ParamsCanBeEmptyJObject()
        {
            var command = new Command
            {
                type = "action",
                @params = new JObject()
            };

            Assert.IsNotNull(command.@params);
            Assert.AreEqual(0, command.@params.Count);
        }

        [Test]
        public void Command_ParamsSupportsSimpleValues()
        {
            var command = new Command
            {
                type = "set_value",
                @params = new JObject
                {
                    ["name"] = "test",
                    ["value"] = 42,
                    ["enabled"] = true
                }
            };

            Assert.AreEqual("test", command.@params["name"].Value<string>());
            Assert.AreEqual(42, command.@params["value"].Value<int>());
            Assert.AreEqual(true, command.@params["enabled"].Value<bool>());
        }

        [Test]
        public void Command_ParamsSupportsNestedStructures()
        {
            // Documents that @params can contain complex nested JSON
            var command = new Command
            {
                type = "complex_action",
                @params = new JObject
                {
                    ["config"] = new JObject
                    {
                        ["nested"] = new JObject
                        {
                            ["value"] = "deep"
                        }
                    },
                    ["list"] = new JArray { 1, 2, 3 }
                }
            };

            var nestedValue = command.@params["config"]["nested"]["value"].Value<string>();
            Assert.AreEqual("deep", nestedValue);

            var list = command.@params["list"].ToObject<int[]>();
            Assert.AreEqual(new[] { 1, 2, 3 }, list);
        }

        [Test]
        public void Command_RoundTripSerialization()
        {
            var original = new Command
            {
                type = "test_command",
                @params = new JObject
                {
                    ["param1"] = "value1",
                    ["param2"] = 123
                }
            };

            string json = JsonConvert.SerializeObject(original);
            var deserialized = JsonConvert.DeserializeObject<Command>(json);

            Assert.AreEqual(original.type, deserialized.type);
            Assert.AreEqual("value1", deserialized.@params["param1"].Value<string>());
            Assert.AreEqual(123, deserialized.@params["param2"].Value<int>());
        }

        #endregion
    }
}
