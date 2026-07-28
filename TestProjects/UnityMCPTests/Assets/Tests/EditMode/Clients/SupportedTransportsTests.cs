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
        public void Codex_SupportsStdioOnly()
        {
            // Regression guard for #1193: Codex does not expose tools over the HTTP block, so it
            // must advertise stdio only and let CoerceTransportFor pick stdio before Configure().
            var codex = new CodexConfigurator();
            CollectionAssert.AreEqual(
                new[] { ConfiguredTransport.Stdio },
                codex.SupportedTransports.ToList(),
                "Codex must advertise stdio and nothing else");
            Assert.IsFalse(codex.Client.SupportsHttpTransport, "Codex must not be treated as HTTP-capable");
        }

        [Test]
        public void Codex_ManualSnippet_IsStdio_EvenWhenHttpPreferred()
        {
            // The snippet path does not go through ConfigureWithTransportCoercion, so with the
            // global HTTP pref on it used to render a url block for a client that cannot use one.
            var cache = EditorConfigurationCache.Instance;
            bool original = cache.UseHttpTransport;
            try
            {
                cache.SetUseHttpTransport(true);
                string snippet = new CodexConfigurator().GetManualSnippet();

                StringAssert.Contains("command", snippet, "Codex snippet must configure stdio");
                Assert.IsFalse(snippet.Contains("url ="), "Codex snippet must not configure an HTTP url");
                Assert.IsTrue(cache.UseHttpTransport, "The global transport pref must be restored");
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
