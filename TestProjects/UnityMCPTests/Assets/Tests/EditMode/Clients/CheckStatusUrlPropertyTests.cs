using System;
using System.IO;
using MCPForUnity.Editor.Clients;
using MCPForUnity.Editor.Models;
using NUnit.Framework;

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
