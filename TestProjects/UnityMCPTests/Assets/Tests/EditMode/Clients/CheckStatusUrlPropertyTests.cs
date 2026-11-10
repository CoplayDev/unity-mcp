using System;
using System.IO;
using MCPForUnity.Editor.Clients;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Models;
using NUnit.Framework;
using UnityEditor;

namespace MCPForUnityTests.Editor.Clients
{
    /// <summary>
    /// Covers the regression where JsonFileMcpConfigurator.CheckStatus only recognized
    /// the "url" property and missed "serverUrl" (Antigravity/Windsurf) and "httpUrl"
    /// (Gemini CLI), so clients configured via HTTP looked unconfigured and got rewritten
    /// every startup by the auto-rewrite path.
    /// </summary>
    [TestFixture]
    public class CheckStatusUrlPropertyTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "UnityMCPTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
        }

        [Test]
        public void CheckStatus_DetectsHttp_WhenUrlProperty()
            => AssertHttpDetected("url");

        [Test]
        public void CheckStatus_DetectsHttp_WhenServerUrlProperty()
            => AssertHttpDetected("serverUrl");

        [Test]
        public void CheckStatus_DetectsHttp_WhenHttpUrlProperty()
            => AssertHttpDetected("httpUrl");

        // Prerelease packages expect "mcpforunityserver>=0.0.0a0". On Windows, Mono's
        // Path.GetFullPath throws on '>', so a correct stdio config read as VersionMismatch
        // and the auto-rewrite path rewrote it every startup.
        [Test]
        public void CheckStatus_Configured_WhenStdioUsesPrereleasePackageRange()
        {
            const string source = "mcpforunityserver>=0.0.0a0";
            bool hadOverride = EditorPrefs.HasKey(EditorPrefKeys.GitUrlOverride);
            string originalOverride = EditorPrefs.GetString(EditorPrefKeys.GitUrlOverride, string.Empty);
            try
            {
                EditorPrefs.SetString(EditorPrefKeys.GitUrlOverride, source);
                string configPath = Path.Combine(_tempDir, "stdio.json");
                File.WriteAllText(configPath,
                    "{\"mcpServers\":{\"unityMCP\":{\"command\":\"uvx\",\"args\":[\"--prerelease\",\"explicit\",\"--from\",\""
                    + source + "\",\"mcp-for-unity\",\"--transport\",\"stdio\"]}}}");
                var client = new McpClient
                {
                    name = "Fake",
                    windowsConfigPath = configPath,
                    macConfigPath = configPath,
                    linuxConfigPath = configPath,
                };

                var status = new FakeJsonConfigurator(client).CheckStatus(attemptAutoRewrite: false);

                Assert.AreEqual(McpStatus.Configured, status, client.configStatus);
            }
            finally
            {
                if (hadOverride) EditorPrefs.SetString(EditorPrefKeys.GitUrlOverride, originalOverride);
                else EditorPrefs.DeleteKey(EditorPrefKeys.GitUrlOverride);
            }
        }

        private void AssertHttpDetected(string urlProperty)
        {
            string configPath = Path.Combine(_tempDir, $"{urlProperty}.json");
            File.WriteAllText(configPath,
                "{\"mcpServers\":{\"unityMCP\":{\"" + urlProperty + "\":\"http://localhost:65535/mcp\"}}}");

            var client = new McpClient
            {
                name = "Fake",
                windowsConfigPath = configPath,
                macConfigPath = configPath,
                linuxConfigPath = configPath,
                HttpUrlProperty = urlProperty,
            };
            var configurator = new FakeJsonConfigurator(client);

            configurator.CheckStatus(attemptAutoRewrite: false);

            Assert.AreNotEqual(ConfiguredTransport.Unknown, client.configuredTransport,
                $"CheckStatus must recognize the '{urlProperty}' property as an HTTP URL");
            Assert.That(
                client.configuredTransport == ConfiguredTransport.Http
                    || client.configuredTransport == ConfiguredTransport.HttpRemote,
                $"Expected HTTP/HttpRemote transport for '{urlProperty}', got {client.configuredTransport}");
        }

        private sealed class FakeJsonConfigurator : JsonFileMcpConfigurator
        {
            public FakeJsonConfigurator(McpClient client) : base(client) { }
        }
    }
}
