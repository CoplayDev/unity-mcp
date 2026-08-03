using System.Linq;
using MCPForUnity.Editor.Clients;
using MCPForUnity.Editor.Clients.Configurators;
using MCPForUnity.Editor.Models;
using MCPForUnity.Editor.Services;
using NUnit.Framework;

namespace MCPForUnityTests.Editor.Clients
{
    [TestFixture]
    public class SupportedTransportsTests
    {
        [Test]
        public void IMcpClientConfigurator_ExposesSupportedTransports()
        {
            var prop = typeof(IMcpClientConfigurator).GetProperty("SupportedTransports");
            Assert.IsNotNull(prop, "Must expose SupportedTransports");
        }

        [Test]
        public void ClaudeDesktop_SupportsStdioOnly()
        {
            var claude = new ClaudeDesktopConfigurator();
            CollectionAssert.Contains(claude.SupportedTransports.ToList(), ConfiguredTransport.Stdio);
            CollectionAssert.DoesNotContain(claude.SupportedTransports.ToList(), ConfiguredTransport.Http);
        }

        [Test]
        public void Codex_SupportsBothTransports()
        {
            // Verified against Codex CLI 0.47.0: a bare `[mcp_servers.x] url = "..."` entry reports
            // `transport: streamable_http` from `codex mcp get` and completes a full MCP handshake
            // against a live server, with no feature flag set. #1193 was not Codex lacking HTTP.
            var codex = new CodexConfigurator();
            var list = codex.SupportedTransports.ToList();
            CollectionAssert.Contains(list, ConfiguredTransport.Stdio);
            CollectionAssert.Contains(list, ConfiguredTransport.Http);
            Assert.IsTrue(codex.Client.SupportsHttpTransport, "Codex is HTTP-capable");
        }

        [Test]
        public void Codex_ManualSnippet_RendersUrl_WhenHttpPreferred()
        {
            var cache = EditorConfigurationCache.Instance;
            bool original = cache.UseHttpTransport;
            try
            {
                cache.SetUseHttpTransport(true);
                string snippet = new CodexConfigurator().GetManualSnippet();

                StringAssert.Contains("url =", snippet, "Codex snippet must configure the HTTP url");
                Assert.IsTrue(cache.UseHttpTransport, "The global transport pref must be restored");
            }
            finally
            {
                cache.SetUseHttpTransport(original);
            }
        }

        [Test]
        public void Codex_ManualSnippet_RendersCommand_WhenStdioPreferred()
        {
            var cache = EditorConfigurationCache.Instance;
            bool original = cache.UseHttpTransport;
            try
            {
                cache.SetUseHttpTransport(false);
                string snippet = new CodexConfigurator().GetManualSnippet();

                StringAssert.Contains("command", snippet, "Codex snippet must configure stdio");
                Assert.IsFalse(snippet.Contains("url ="), "Stdio snippet must not carry an HTTP url");
            }
            finally
            {
                cache.SetUseHttpTransport(original);
            }
        }

        [Test]
        public void Cursor_SupportsBothTransports()
        {
            var cursor = new CursorConfigurator();
            var list = cursor.SupportedTransports.ToList();
            CollectionAssert.Contains(list, ConfiguredTransport.Stdio);
            CollectionAssert.Contains(list, ConfiguredTransport.Http);
        }
    }
}
