using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using MCPForUnity.Editor.Clients;
using MCPForUnity.Editor.Clients.Configurators;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Models;
using MCPForUnity.Editor.Services;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace MCPForUnityTests.Editor.Clients
{
    [TestFixture]
    public class ZCodeConfiguratorTests : TransportPreferenceTestBase
    {
        private string _tempDir;
        private readonly Dictionary<string, string> _savedPrefs = new();
        private bool _hadLock;
        private bool _locked;

        [SetUp]
        public void SetUp()
        {
            foreach (string key in new[] { EditorPrefKeys.HttpTransportScope, EditorPrefKeys.HttpBaseUrl })
                _savedPrefs[key] = EditorPrefs.HasKey(key) ? EditorPrefs.GetString(key) : null;
            _hadLock = EditorPrefs.HasKey(EditorPrefKeys.LockCursorConfig);
            _locked = EditorPrefs.GetBool(EditorPrefKeys.LockCursorConfig, false);
            EditorPrefs.SetString(EditorPrefKeys.HttpTransportScope, "local");
            EditorPrefs.SetString(EditorPrefKeys.HttpBaseUrl, "http://127.0.0.1:8080");
            EditorPrefs.SetBool(EditorPrefKeys.LockCursorConfig, false);
            EditorConfigurationCache.Instance.Refresh();
            _tempDir = Path.Combine(Path.GetTempPath(), "UnityMCPTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var pref in _savedPrefs)
            {
                if (pref.Value == null) EditorPrefs.DeleteKey(pref.Key);
                else EditorPrefs.SetString(pref.Key, pref.Value);
            }
            _savedPrefs.Clear();
            if (_hadLock) EditorPrefs.SetBool(EditorPrefKeys.LockCursorConfig, _locked);
            else EditorPrefs.DeleteKey(EditorPrefKeys.LockCursorConfig);
            EditorConfigurationCache.Instance.Refresh();
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
        }

        [Test]
        public void Registry_DiscoversZCode()
        {
            var zcode = McpClientRegistry.All.Single(c => c.Id == "zcode");
            Assert.IsInstanceOf<ZCodeConfigurator>(zcode);
            Assert.AreEqual("ZCode", zcode.DisplayName);
        }

        [Test]
        public void ConfigPath_UsesNativeUserScopeOnEveryPlatform()
        {
            var configurator = new ZCodeConfigurator();
            string expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".zcode", "cli", "config.json");
            Assert.AreEqual(expected, configurator.Client.windowsConfigPath);
            Assert.AreEqual(expected, configurator.Client.macConfigPath);
            Assert.AreEqual(expected, configurator.Client.linuxConfigPath);
            Assert.AreEqual(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".agents", "mcp.json"), configurator.AgentsConfigPath);
        }

        [Test]
        public void IsInstalled_DetectsZCodeBeforeCliConfigHasBeenCreated()
        {
            var configurator = new TempZCodeConfigurator(Path.Combine(_tempDir, ".zcode", "cli", "config.json"));
            Assert.IsFalse(configurator.IsInstalled);
            Directory.CreateDirectory(Path.Combine(_tempDir, ".zcode"));
            Assert.IsTrue(configurator.IsInstalled);
            Assert.IsFalse(Directory.Exists(Path.Combine(_tempDir, ".zcode", "cli")));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ManualSnippet_UsesNativeNestedContainer(bool http)
        {
            EditorConfigurationCache.Instance.SetUseHttpTransport(http);
            var root = JObject.Parse(new ZCodeConfigurator().GetManualSnippet());
            Assert.IsNotNull(root["mcp"]?["servers"]?["unityMCP"]);
            Assert.IsNull(root["mcpServers"]);
            Assert.IsNull(root["mcp.servers"]);
            Assert.AreEqual(http ? "http" : "stdio", (string)root["mcp"]?["servers"]?["unityMCP"]?["type"]);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Configure_RoundTripsNativeConfigAndPreservesOtherSettings(bool http)
        {
            EditorConfigurationCache.Instance.SetUseHttpTransport(http);
            var configurator = new TempZCodeConfigurator(Path.Combine(_tempDir, "config.json"));
            File.WriteAllText(configurator.GetConfigPath(), "{\"theme\":\"dark\",\"mcp\":{\"setting\":true,\"servers\":{\"other\":{\"command\":\"other-server\"}}}}");

            configurator.Configure();
            string firstWrite = File.ReadAllText(configurator.GetConfigPath());
            configurator.Configure();
            Assert.AreEqual(firstWrite, File.ReadAllText(configurator.GetConfigPath()));
            Assert.AreEqual(McpStatus.Configured, configurator.CheckStatus(attemptAutoRewrite: false));
            Assert.AreEqual(http ? ConfiguredTransport.Http : ConfiguredTransport.Stdio, configurator.ConfiguredTransport);

            var root = JObject.Parse(firstWrite);
            var unity = (JObject)root["mcp"]?["servers"]?["unityMCP"];
            Assert.IsNotNull(unity);
            Assert.IsNull(root["mcpServers"]);
            Assert.IsNull(root["mcp.servers"]);
            Assert.AreEqual("dark", (string)root["theme"]);
            Assert.IsTrue((bool)root["mcp"]["setting"]);
            Assert.AreEqual("other-server", (string)root["mcp"]["servers"]["other"]["command"]);
            if (http)
            {
                Assert.AreEqual("http://127.0.0.1:8080/mcp", (string)unity["url"]);
                Assert.IsNull(unity["command"]);
            }
            else
            {
                Assert.IsNotEmpty((string)unity["command"]);
                CollectionAssert.Contains(unity["args"].ToObject<string[]>(), "stdio");
                Assert.IsNull(unity["url"]);
            }
            Assert.AreEqual(!http, ConfigUsesStdio(configurator.Client));

            configurator.Unregister();
            root = JObject.Parse(File.ReadAllText(configurator.GetConfigPath()));
            Assert.IsNull(root["mcp"]?["servers"]?["unityMCP"]);
            Assert.AreEqual("other-server", (string)root["mcp"]["servers"]["other"]["command"]);
            Assert.AreEqual("dark", (string)root["theme"]);
            Assert.IsTrue((bool)root["mcp"]["setting"]);
        }

        [TestCase("Cursor", "mcpServers")]
        [TestCase("Kilo", "mcp")]
        [TestCase("VSCode", "servers")]
        public void ExistingClients_KeepTheirContainerAndStdioMigration(string name, string container)
        {
            EditorConfigurationCache.Instance.SetUseHttpTransport(false);
            McpClient client;
            switch (name)
            {
                case "Kilo": client = new KiloCodeConfigurator().Client; break;
                case "VSCode": client = new VSCodeConfigurator().Client; break;
                default: client = new CursorConfigurator().Client; break;
            }
            string path = Path.Combine(_tempDir, "mcp.json");
            client.windowsConfigPath = client.macConfigPath = client.linuxConfigPath = path;
            var configurator = new TempJsonConfigurator(client);
            configurator.Configure();

            var root = JObject.Parse(File.ReadAllText(path));
            Assert.IsNotNull(root[container]?["unityMCP"]);
            Assert.AreEqual(McpStatus.Configured, configurator.CheckStatus(attemptAutoRewrite: false));
            Assert.IsTrue(ConfigUsesStdio(client));
            configurator.Unregister();
            root = JObject.Parse(File.ReadAllText(path));
            Assert.IsNull(root[container]?["unityMCP"]);
        }

        [Test]
        public void CheckStatus_DoesNotMistakeFallbackLayoutForNativeConfig()
        {
            var configurator = new TempZCodeConfigurator(Path.Combine(_tempDir, "config.json"));
            const string content = "{\"mcpServers\":{\"unityMCP\":{\"type\":\"http\",\"url\":\"http://127.0.0.1:8080/mcp\"}}}";
            File.WriteAllText(configurator.GetConfigPath(), content);
            Assert.AreEqual(McpStatus.MissingConfig, configurator.CheckStatus(attemptAutoRewrite: false));
            Assert.AreEqual(content, File.ReadAllText(configurator.GetConfigPath()));
        }

        [TestCase(null)]
        [TestCase("  \n")]
        [TestCase("{}")]
        [TestCase("{\"mcp\":{}}")]
        [TestCase("{\"mcp\":{\"servers\":{}}}")]
        [TestCase("{\"mcp\":{\"servers\":null}}")]
        // Configure() rewrites the next three as a fresh object, so they hide the .agents servers too.
        [TestCase("invalid JSON")]
        [TestCase("{\"mcp\":[]}")]
        [TestCase("{\"mcp\":{\"servers\":[]}}")]
        public void ShadowedServers_WhenNativeHasNoServers_ReturnsOnlyOtherServerNames(string nativeJson)
        {
            string nativePath = Path.Combine(_tempDir, "config.json");
            string agentsPath = Path.Combine(_tempDir, "mcp.json");
            if (nativeJson != null) File.WriteAllText(nativePath, nativeJson);
            File.WriteAllText(agentsPath, "{\"mcpServers\":{\"local\":{\"env\":{\"TOKEN\":\"secret-env-value\"}},\"remote\":{\"headers\":{\"Authorization\":\"secret-header-value\"}},\"unityMCP\":{}}}");

            var names = ZCodeConfigurator.GetShadowedServerNames(nativePath, agentsPath);

            CollectionAssert.AreEquivalent(new[] { "local", "remote" }, names);
            StringAssert.DoesNotContain("secret-env-value", string.Join(",", names));
            StringAssert.DoesNotContain("secret-header-value", string.Join(",", names));
        }

        [TestCase("{\"mcp\":{\"servers\":{\"other\":{}}}}")]
        [TestCase("{\"mcp\":{\"servers\":{\"unityMCP\":{}}}}")]
        public void ShadowedServers_WhenNativeAlreadyHasServers_ReturnsEmpty(string nativeJson)
        {
            string nativePath = Path.Combine(_tempDir, "config.json");
            string agentsPath = Path.Combine(_tempDir, "mcp.json");
            File.WriteAllText(nativePath, nativeJson);
            File.WriteAllText(agentsPath, "{\"mcpServers\":{\"other\":{}}}");

            Assert.IsEmpty(ZCodeConfigurator.GetShadowedServerNames(nativePath, agentsPath));
        }

        [TestCase(null)]
        [TestCase("invalid JSON")]
        [TestCase("[]")]
        [TestCase("{}")]
        [TestCase("{\"mcpServers\":{}}")]
        [TestCase("{\"mcpServers\":[]}")]
        [TestCase("{\"mcpServers\":null}")]
        [TestCase("{\"mcpServers\":\"wrong shape\"}")]
        [TestCase("{\"mcpServers\":{\"unityMCP\":{}}}")]
        public void ShadowedServers_WhenAgentsHasNoOtherServers_ReturnsEmpty(string agentsJson)
        {
            string nativePath = Path.Combine(_tempDir, "config.json");
            string agentsPath = Path.Combine(_tempDir, "mcp.json");
            if (agentsJson != null) File.WriteAllText(agentsPath, agentsJson);

            Assert.IsEmpty(ZCodeConfigurator.GetShadowedServerNames(nativePath, agentsPath));
        }

        [Test]
        public void Configure_WarnsAboutShadowedServersAndLeavesAgentsBytesUnchanged()
        {
            EditorConfigurationCache.Instance.SetUseHttpTransport(true);
            var configurator = new TempZCodeConfigurator(Path.Combine(_tempDir, "config.json"));
            File.WriteAllText(configurator.AgentsConfigPath, "{\n  \"mcpServers\": {\"other\": {\"env\": {\"TOKEN\": \"secret-env-value\"}}}\n}\n");
            byte[] original = File.ReadAllBytes(configurator.AgentsConfigPath);
            LogAssert.Expect(LogType.Warning, new Regex("ZCode now skips.*these servers will stop loading: other\\. .*Settings > MCP Servers.*mcp.servers.*reads.*again if the native file defines no servers"));

            configurator.Configure();

            Assert.AreEqual(McpStatus.Configured, configurator.Status);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(configurator.AgentsConfigPath));
            Assert.IsEmpty(ZCodeConfigurator.GetShadowedServerNames(configurator.GetConfigPath(), configurator.AgentsConfigPath));
        }

        private static bool ConfigUsesStdio(McpClient client)
        {
            var migration = typeof(McpClient).Assembly.GetType("MCPForUnity.Editor.Migrations.StdIoVersionMigration");
            var method = migration.GetMethod("JsonConfigUsesStdIo", BindingFlags.NonPublic | BindingFlags.Static);
            return (bool)method.Invoke(null, new object[] { client });
        }

        private sealed class TempZCodeConfigurator : ZCodeConfigurator
        {
            public TempZCodeConfigurator(string path)
            {
                Client.windowsConfigPath = Client.macConfigPath = Client.linuxConfigPath = path;
                AgentsConfigPath = Path.Combine(Path.GetDirectoryName(path), "agents-mcp.json");
            }
        }

        private sealed class TempJsonConfigurator : JsonFileMcpConfigurator
        {
            public TempJsonConfigurator(McpClient client) : base(client) { }
        }
    }
}
