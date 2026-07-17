using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using MCPForUnity.Editor.Clients.Configurators;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Models;
using MCPForUnity.Editor.Constants;
using EditorConfigCache = MCPForUnity.Editor.Services.EditorConfigurationCache;

namespace MCPForUnityTests.Editor.Helpers
{
    // Per-client MCP config-format coverage.
    //
    // Issue #1120: Kilo Code v7.0.33+ moved its MCP config from the VS Code extension's
    // globalStorage/mcp_settings.json (mcpServers / type:"http" or "streamableHttp" / disabled)
    // to a CLI-style kilo.jsonc under ~/.config/kilo whose schema (https://app.kilo.ai/config.json)
    // uses an "mcp" container, type:"remote" for HTTP servers, and an "enabled" flag. Writing the
    // old format left the server showing as "stdio" + disabled. These tests pin the new Kilo format
    // while guarding that Cline keeps "streamableHttp" and generic clients keep plain "http".
    public class ClientConfigFormatTests
    {
        private const string UseHttpTransportPrefKey = EditorPrefKeys.UseHttpTransport;

        private bool _hadHttpTransport;
        private bool _originalHttpTransport;

        [SetUp]
        public void SetUp()
        {
            _hadHttpTransport = EditorPrefs.HasKey(UseHttpTransportPrefKey);
            _originalHttpTransport = EditorPrefs.GetBool(UseHttpTransportPrefKey, true);

            // Force HTTP transport so the remote/streamableHttp branch is exercised.
            EditorPrefs.SetBool(UseHttpTransportPrefKey, true);
            EditorConfigCache.Instance.Refresh();
        }

        [TearDown]
        public void TearDown()
        {
            if (_hadHttpTransport)
                EditorPrefs.SetBool(UseHttpTransportPrefKey, _originalHttpTransport);
            else
                EditorPrefs.DeleteKey(UseHttpTransportPrefKey);
            EditorConfigCache.Instance.Refresh();
        }

        [Test]
        public void KiloCodeConfigurator_DeclaresNewKiloFormat()
        {
            var client = new KiloCodeConfigurator().Client;

            Assert.AreEqual("mcp", client.ServerContainerKey,
                "Kilo Code's new schema nests servers under an \"mcp\" container, not \"mcpServers\"");
            Assert.AreEqual("remote", client.HttpTypeValue,
                "Kilo Code's new schema uses type:\"remote\" for HTTP servers");
            Assert.AreEqual("local", client.StdioTypeValue,
                "Kilo Code's new schema uses type:\"local\" for stdio servers");
            Assert.AreEqual("https://app.kilo.ai/config.json", client.SchemaUrl,
                "Kilo Code config should declare the kilo.jsonc $schema");
            Assert.IsTrue(client.DefaultUnityFields.ContainsKey("enabled"),
                "Kilo Code uses an \"enabled\" flag instead of \"disabled\"");
            Assert.AreEqual(true, client.DefaultUnityFields["enabled"],
                "Kilo Code must default enabled:true so the server is active");
            Assert.IsFalse(client.DefaultUnityFields.ContainsKey("disabled"),
                "Kilo Code's new schema must not write the legacy \"disabled\" field");

            foreach (var path in new[] { client.windowsConfigPath, client.macConfigPath, client.linuxConfigPath })
            {
                Assert.AreEqual("kilo.jsonc", System.IO.Path.GetFileName(path),
                    "Kilo Code config must target kilo.jsonc, not mcp_settings.json");
            }
        }

        [Test]
        public void BuildManualConfigJson_ForKiloCode_UsesMcpContainerRemoteTypeAndEnabled()
        {
            var client = new KiloCodeConfigurator().Client;

            var root = JObject.Parse(ConfigJsonBuilder.BuildManualConfigJson(uvPath: null, client));

            Assert.AreEqual("https://app.kilo.ai/config.json", (string)root["$schema"],
                "Kilo Code config should include the kilo.jsonc $schema at the root");
            Assert.IsNull(root["mcpServers"], "Kilo Code must not use the legacy \"mcpServers\" container");

            var unity = (JObject)root.SelectToken("mcp.unityMCP");
            Assert.NotNull(unity, "Expected mcp.unityMCP node");
            Assert.AreEqual("remote", (string)unity["type"],
                "Kilo Code HTTP config must use type:remote, not type:http/streamableHttp");
            Assert.AreEqual(true, (bool)unity["enabled"],
                "Kilo Code config must set enabled:true");
            Assert.IsNull(unity["disabled"], "Kilo Code must not write the legacy disabled field");
            Assert.IsNotNull(unity["url"], "HTTP transport should set a url");
            Assert.IsNull(unity["command"], "HTTP transport should not include a command");
        }

        [Test]
        public void ApplyUnityServerToExistingConfig_ForKiloCode_RewritesStaleStdioToRemote()
        {
            var client = new KiloCodeConfigurator().Client;

            // Simulate a stale local (stdio) entry that Kilo would have shown as the wrong transport.
            var root = new JObject
            {
                ["mcp"] = new JObject
                {
                    ["unityMCP"] = new JObject
                    {
                        ["command"] = "uvx",
                        ["args"] = new JArray("unity-mcp-server"),
                        ["type"] = "local"
                    }
                }
            };

            var result = ConfigJsonBuilder.ApplyUnityServerToExistingConfig(root, uvPath: null, client);

            Assert.AreEqual("https://app.kilo.ai/config.json", (string)result["$schema"],
                "Rewrite should add the kilo.jsonc $schema when missing");
            var unity = (JObject)result.SelectToken("mcp.unityMCP");
            Assert.NotNull(unity, "Expected mcp.unityMCP node");
            Assert.AreEqual("remote", (string)unity["type"],
                "Existing config should be rewritten to type:remote for Kilo Code");
            Assert.AreEqual(true, (bool)unity["enabled"], "Existing config should gain enabled:true");
            Assert.IsNull(unity["command"], "HTTP transport should remove the stale stdio command");
        }

        [Test]
        public void BuildManualConfigJson_ForCline_StillUsesStreamableHttpInMcpServers()
        {
            var client = new ClineConfigurator().Client;

            var root = JObject.Parse(ConfigJsonBuilder.BuildManualConfigJson(uvPath: null, client));

            Assert.IsNull(root["$schema"], "Cline must not write a $schema");
            var unity = (JObject)root.SelectToken("mcpServers.unityMCP");
            Assert.NotNull(unity, "Expected mcpServers.unityMCP node");
            Assert.AreEqual("streamableHttp", (string)unity["type"],
                "Cline must keep type:streamableHttp after the name-check was replaced with a flag");
        }

        [Test]
        public void BuildManualConfigJson_ForGenericHttpClient_UsesPlainHttpType()
        {
            // A client without a type override must continue to receive the generic type:http.
            var client = new McpClient { name = "Cursor" };

            var root = JObject.Parse(ConfigJsonBuilder.BuildManualConfigJson(uvPath: null, client));
            var unity = (JObject)root.SelectToken("mcpServers.unityMCP");

            Assert.NotNull(unity, "Expected mcpServers.unityMCP node");
            Assert.AreEqual("http", (string)unity["type"],
                "Clients without HttpTypeValue should keep the generic type:http");
        }
    }
}
