using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MCPForUnity.Editor.Clients;
using MCPForUnity.Editor.Clients.Configurators;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Models;
using MCPForUnity.Editor.Migrations;
using MCPForUnity.Editor.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MCPForUnityTests.Editor.Clients
{
    [TestFixture]
    public class JsonManualStdioConfigurationTests
    {
        private const string ExpectedSource = "mcpforunityserver==99.0.0";
        private readonly Dictionary<string, (bool present, string value)> _stringPrefs = new();
        private readonly Dictionary<string, (bool present, bool value)> _boolPrefs = new();
        private IPathResolverService _originalPaths;
        private bool _originalHttp;
        private string _directory;
        private string _configPath;
        private string _uvxPath;
        private McpClient _client;
        private JsonFileMcpConfigurator _configurator;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Temp",
                "UnityMCPTests", "ManualStdio", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            _configPath = Path.Combine(_directory, "client.json");
            _uvxPath = Path.Combine(_directory, "uvx");
            _originalPaths = MCPServiceLocator.Paths;
            _originalHttp = EditorConfigurationCache.Instance.UseHttpTransport;
            SaveBool(EditorPrefKeys.UseHttpTransport);
            SaveBool(EditorPrefKeys.LockCursorConfig);
            SaveBool(EditorPrefKeys.DevModeForceServerRefresh);
            _stringPrefs[EditorPrefKeys.GitUrlOverride] = (EditorPrefs.HasKey(EditorPrefKeys.GitUrlOverride),
                EditorPrefs.GetString(EditorPrefKeys.GitUrlOverride, ""));
            MCPServiceLocator.Register<IPathResolverService>(new FakePaths(_uvxPath));
            EditorPrefs.SetBool(EditorPrefKeys.LockCursorConfig, false);
            EditorPrefs.SetBool(EditorPrefKeys.DevModeForceServerRefresh, true);
            EditorPrefs.SetString(EditorPrefKeys.GitUrlOverride, ExpectedSource);
            EditorConfigurationCache.Instance.SetUseHttpTransport(true);
            UseDesktop();
        }

        [TearDown]
        public void TearDown()
        {
            MCPServiceLocator.Register<IPathResolverService>(_originalPaths);
            EditorConfigurationCache.Instance.SetUseHttpTransport(_originalHttp);
            foreach (var pref in _boolPrefs)
                if (pref.Value.present) EditorPrefs.SetBool(pref.Key, pref.Value.value);
                else EditorPrefs.DeleteKey(pref.Key);
            foreach (var pref in _stringPrefs)
                if (pref.Value.present) EditorPrefs.SetString(pref.Key, pref.Value.value);
                else EditorPrefs.DeleteKey(pref.Key);
            _boolPrefs.Clear();
            _stringPrefs.Clear();
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }

        private void SaveBool(string key) => _boolPrefs[key] = (EditorPrefs.HasKey(key), EditorPrefs.GetBool(key));

        private void UseDesktop()
        {
            var desktop = new ClaudeDesktopConfigurator();
            _client = desktop.Client;
            _client.windowsConfigPath = _client.macConfigPath = _client.linuxConfigPath = _configPath;
            _configurator = desktop;
        }

        [Test]
        public void Status_DefaultRefresh_PreservesInstalledDesktopLauncher()
        {
            WriteEntry(new JObject { ["command"] = @"C:\Tools\mcp-for-unity.exe",
                ["args"] = new JArray("--transport", "stdio"), ["type"] = "stdio" });
            AssertCustomPreserved(true);
        }

        [TestCase("mcp-for-unity", "omitted", true)]
        [TestCase("mcp-for-unity", "empty", false)]
        [TestCase("/tools/mcp-for-unity", "empty", true)]
        [TestCase("python3", "script", false)]
        [TestCase(@"C:\Tools\python.exe", "script", true)]
        [TestCase("wrapper", "from", true)]
        [TestCase("uv", "run", true)]
        public void Status_CustomLauncher_PreservesAllBytes(string command, string argumentKind, bool rewrite)
        {
            var entry = new JObject { ["command"] = command, ["type"] = "stdio" };
            if (argumentKind == "empty") entry["args"] = new JArray();
            if (argumentKind == "script") entry["args"] = new JArray("-m", "main", "--transport", "stdio");
            if (argumentKind == "from") entry["args"] = new JArray("--from", "custom-input");
            if (argumentKind == "run") entry["args"] = new JArray("run", "server.py", "--from", "custom-input");
            WriteEntry(entry);
            AssertCustomPreserved(rewrite);
        }

        [Test]
        public void Status_CustomLauncherWithoutType_RemainsConfigured()
        {
            WriteEntry(new JObject { ["command"] = "custom-launcher" });
            AssertCustomPreserved(true);
        }

        [Test]
        public void Status_MismatchedUvxSource_StillAutoUpdates()
        {
            WriteEntry(new JObject { ["command"] = _uvxPath,
                ["args"] = new JArray("--from", "mcpforunityserver==98.0.0", "mcp-for-unity"), ["type"] = "stdio" });
            Assert.AreEqual(McpStatus.Configured, _configurator.CheckStatus(true));
            var entry = JObject.Parse(File.ReadAllText(_configPath))["mcpServers"]["unityMCP"];
            Assert.AreEqual(ExpectedSource, McpConfigurationHelper.ExtractUvxUrl(entry["args"].ToObject<string[]>()));
        }

        [TestCase("servers", true)]
        [TestCase("mcp", false)]
        public void Status_CustomLauncher_UsesClientContainer(string container, bool vsCode)
        {
            _client.IsVsCodeLayout = vsCode;
            _client.ServerContainerKey = container;
            _client.StdioTypeValue = vsCode ? null : "local";
            WriteEntry(new JObject { ["command"] = "custom-launcher", ["args"] = new JArray(),
                ["type"] = vsCode ? "stdio" : "local" }, container);
            AssertCustomPreserved(true);
        }

        [TestCase("number")]
        [TestCase("boolean")]
        [TestCase("null-item")]
        [TestCase("object")]
        [TestCase("string")]
        [TestCase("matching-source-with-number")]
        [TestCase("null-args")]
        public void Status_InvalidArguments_AreNotAcceptedAsCustomStdio(string kind)
        {
            JToken args = kind == "number" ? new JArray(42)
                : kind == "boolean" ? new JArray(true)
                : kind == "null-item" ? new JArray(JValue.CreateNull())
                : kind == "object" ? new JObject()
                : kind == "matching-source-with-number" ? new JArray("--from", ExpectedSource, 42)
                : kind == "null-args" ? JValue.CreateNull() : new JValue("--transport stdio");
            WriteEntry(new JObject { ["command"] = "custom-launcher", ["args"] = args, ["type"] = "stdio" });
            var before = File.ReadAllBytes(_configPath);
            Assert.AreEqual(McpStatus.Error, _configurator.CheckStatus(true));
            Assert.AreEqual(ConfiguredTransport.Unknown, _client.configuredTransport);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(_configPath));
        }

        [TestCase("uvx")]
        [TestCase(@"C:\Tools\UVX.EXE")]
        [TestCase("/tools/uv")]
        [TestCase(@"C:\Tools\uv.exe")]
        public void Status_PackageRunner_RetainsVersionMismatchValidation(string command)
        {
            bool uv = command.EndsWith("uv", StringComparison.OrdinalIgnoreCase)
                || command.EndsWith("uv.exe", StringComparison.OrdinalIgnoreCase);
            var args = new JArray();
            if (uv) { args.Add("tool"); args.Add("run"); }
            args.Add("--from"); args.Add("mcpforunityserver==98.0.0"); args.Add("mcp-for-unity");
            WriteEntry(new JObject { ["command"] = command, ["args"] = args, ["type"] = "stdio" });
            var before = File.ReadAllBytes(_configPath);
            Assert.AreEqual(McpStatus.VersionMismatch, _configurator.CheckStatus(false));
            CollectionAssert.AreEqual(before, File.ReadAllBytes(_configPath));
        }

        [TestCase("flag")]
        [TestCase("value")]
        [TestCase("equals")]
        [TestCase("between")]
        public void Status_UvToolRunWithGlobalOptions_RetainsVersionValidation(string options)
        {
            var args = options == "flag" ? new JArray("--offline", "-v", "tool", "run")
                : options == "value" ? new JArray("--project", "run", "--color", "never", "tool", "run")
                : options == "equals" ? new JArray("--project=run", "tool", "run")
                : new JArray("tool", "--offline", "run");
            args.Add("--from"); args.Add("mcpforunityserver==98.0.0"); args.Add("mcp-for-unity");
            WriteEntry(new JObject { ["command"] = "uv", ["args"] = args, ["type"] = "stdio" });
            var before = File.ReadAllBytes(_configPath);
            Assert.AreEqual(McpStatus.VersionMismatch, _configurator.CheckStatus(false));
            CollectionAssert.AreEqual(before, File.ReadAllBytes(_configPath));
        }

        [Test]
        public void Status_UvRunScriptWithToolRunArguments_RemainsCustom()
        {
            WriteEntry(new JObject { ["command"] = "uv", ["type"] = "stdio",
                ["args"] = new JArray("--offline", "run", "server.py", "tool", "run", "--from", "custom-input") });
            AssertCustomPreserved(true);
        }

        [Test]
        public void Status_MatchingUvxSource_RemainsConfigured()
        {
            WriteEntry(new JObject { ["command"] = _uvxPath,
                ["args"] = new JArray("--from", ExpectedSource, "mcp-for-unity"), ["type"] = "stdio" });
            AssertCustomPreserved(true);
        }

        [Test]
        public void Status_HttpUrlWithLeftoverCommand_DoesNotBecomeCustomStdio()
        {
            WriteEntry(new JObject { ["command"] = "custom-launcher", ["args"] = new JArray(),
                ["url"] = "http://127.0.0.1:65535/mcp", ["type"] = "http" });
            Assert.AreNotEqual(McpStatus.Configured, _configurator.CheckStatus(false));
            Assert.AreNotEqual(ConfiguredTransport.Stdio, _client.configuredTransport);
        }

        [TestCase("null", "serverUrl")]
        [TestCase("empty", "serverUrl")]
        [TestCase("null", "httpUrl")]
        [TestCase("empty", "httpUrl")]
        public void Status_EmptyFirstUrl_DoesNotHideHttpAlias(string firstUrl, string alias)
        {
            var entry = new JObject { ["command"] = "custom-launcher", ["args"] = new JArray(),
                ["url"] = firstUrl == "null" ? JValue.CreateNull() : new JValue(""),
                [alias] = "http://127.0.0.1:65535/mcp" };
            if (alias == "httpUrl") entry["serverUrl"] = JValue.CreateNull();
            WriteEntry(entry);
            var before = File.ReadAllBytes(_configPath);
            Assert.AreNotEqual(McpStatus.Configured, _configurator.CheckStatus(false));
            Assert.AreEqual(ConfiguredTransport.Http, _client.configuredTransport);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(_configPath));
        }

        [TestCase("mcpServers", false)]
        [TestCase("servers", true)]
        [TestCase("mcp", false)]
        public void VersionMigration_PreservesCustomLauncher(string container, bool vsCode)
        {
            _client.IsVsCodeLayout = vsCode;
            _client.ServerContainerKey = container;
            EditorConfigurationCache.Instance.SetUseHttpTransport(false);
            WriteEntry(new JObject { ["command"] = "mcp-for-unity",
                ["args"] = new JArray("--transport", "stdio"), ["type"] = "stdio" }, container);
            var before = File.ReadAllBytes(_configPath);
            var service = new IsolatedClientService(_configurator);
            Assert.IsFalse(StdIoVersionMigration.RefreshStdioConfiguration(_configurator, service));
            Assert.AreEqual(0, service.ConfigureCalls);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(_configPath));
        }

        [Test]
        public void VersionMigration_UpdatesGeneratedLauncher()
        {
            EditorConfigurationCache.Instance.SetUseHttpTransport(false);
            WriteEntry(new JObject { ["command"] = _uvxPath, ["type"] = "stdio",
                ["args"] = new JArray("--from", "mcpforunityserver==98.0.0", "mcp-for-unity") });
            var service = new IsolatedClientService(_configurator);
            Assert.IsTrue(StdIoVersionMigration.RefreshStdioConfiguration(_configurator, service));
            Assert.AreEqual(1, service.ConfigureCalls);
            AssertGeneratedSource();
        }

        [TestCase("mcp-for-unity")]
        [TestCase("python3")]
        public void LegacyMigration_PreservesCustomLauncher(string command)
        {
            WriteEntry(new JObject { ["command"] = command, ["type"] = "stdio",
                ["args"] = new JArray("--transport", "stdio") });
            var before = File.ReadAllBytes(_configPath);
            var service = new IsolatedClientService(_configurator);
            var summary = LegacyServerSrcMigration.MigrateDetectedClients(service);
            Assert.AreEqual(1, summary.SkippedCount);
            Assert.AreEqual(0, summary.SuccessCount);
            Assert.AreEqual(0, summary.FailureCount);
            Assert.AreEqual(0, service.ConfigureCalls);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(_configPath));
        }

        [Test]
        public void LegacyMigration_UpdatesGeneratedLauncherAndRestoresTransport()
        {
            WriteEntry(new JObject { ["command"] = _uvxPath, ["type"] = "stdio",
                ["args"] = new JArray("--from", "mcpforunityserver==98.0.0", "mcp-for-unity") });
            var service = new IsolatedClientService(_configurator);
            var summary = LegacyServerSrcMigration.MigrateDetectedClients(service);
            Assert.AreEqual(1, summary.SuccessCount);
            Assert.AreEqual(0, summary.FailureCount);
            Assert.AreEqual(0, summary.SkippedCount);
            Assert.AreEqual(1, service.ConfigureCalls);
            AssertGeneratedSource();
            Assert.IsTrue(EditorConfigurationCache.Instance.UseHttpTransport);
        }

        private void AssertGeneratedSource()
        {
            var entry = JObject.Parse(File.ReadAllText(_configPath))["mcpServers"]["unityMCP"];
            Assert.AreEqual(_uvxPath, entry.Value<string>("command"));
            Assert.AreEqual(ExpectedSource, McpConfigurationHelper.ExtractUvxUrl(entry["args"].ToObject<string[]>()));
        }

        [Test]
        public void Configure_ExplicitlyReplacesCustomLauncher()
        {
            WriteEntry(new JObject { ["command"] = "custom-launcher",
                ["args"] = new JArray("--transport", "stdio"), ["type"] = "stdio" });
            new ClientConfigurationService().ConfigureClient(_configurator);
            var root = JObject.Parse(File.ReadAllText(_configPath));
            var entry = root["mcpServers"]["unityMCP"];
            Assert.AreEqual(_uvxPath, entry.Value<string>("command"));
            Assert.AreEqual(ExpectedSource, McpConfigurationHelper.ExtractUvxUrl(entry["args"].ToObject<string[]>()));
            Assert.AreEqual("other-launcher", root["mcpServers"]["otherServer"].Value<string>("command"));
            Assert.IsTrue(EditorConfigurationCache.Instance.UseHttpTransport);
        }

        private void AssertCustomPreserved(bool rewrite)
        {
            var before = File.ReadAllBytes(_configPath);
            var status = _configurator.CheckStatus(rewrite);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(_configPath), "Status checks must preserve the complete configuration file");
            Assert.AreEqual(McpStatus.Configured, status);
            Assert.AreEqual(ConfiguredTransport.Stdio, _client.configuredTransport);
        }

        private void WriteEntry(JObject entry, string container = "mcpServers")
        {
            entry["env"] = new JObject { ["TEST_LAUNCHER_MODE"] = "manual" };
            entry["cwd"] = "custom-working-directory";
            var root = new JObject { [container] = new JObject { ["unityMCP"] = entry,
                ["otherServer"] = new JObject { ["command"] = "other-launcher" } },
                ["unrelatedSetting"] = "keep" };
            File.WriteAllText(_configPath, " \r\n" + root.ToString(Formatting.Indented) + "\r\n ", new UTF8Encoding(true));
        }

        private sealed class IsolatedClientService : IClientConfigurationService
        {
            private readonly IMcpClientConfigurator _configurator;
            private readonly ClientConfigurationService _service = new();
            public int ConfigureCalls { get; private set; }
            public IsolatedClientService(IMcpClientConfigurator configurator) => _configurator = configurator;
            public IReadOnlyList<IMcpClientConfigurator> GetAllClients() => new[] { _configurator };
            public void ConfigureClient(IMcpClientConfigurator configurator)
            {
                Assert.AreSame(_configurator, configurator);
                ConfigureCalls++;
                _service.ConfigureClient(configurator);
            }
            public ClientConfigurationSummary ConfigureAllDetectedClients()
                => throw new InvalidOperationException("Tests must never configure the real client registry.");
            public bool CheckClientStatus(IMcpClientConfigurator configurator, bool attemptAutoRewrite = true)
                => _service.CheckClientStatus(configurator, attemptAutoRewrite);
        }

        private sealed class FakePaths : IPathResolverService
        {
            private readonly string _path;
            public FakePaths(string path) { _path = path; }
            public string GetUvxPath() => _path;
            public string GetClaudeCliPath() => null;
            public bool IsPythonDetected() => false;
            public bool IsClaudeCliDetected() => false;
            public void SetUvxPathOverride(string path) { }
            public void SetClaudeCliPathOverride(string path) { }
            public void ClearUvxPathOverride() { }
            public void ClearClaudeCliPathOverride() { }
            public bool HasUvxPathOverride => false;
            public bool HasClaudeCliPathOverride => false;
            public bool HasUvxPathFallback => false;
            public bool TryValidateUvxExecutable(string path, out string version) { version = null; return false; }
        }
    }
}
