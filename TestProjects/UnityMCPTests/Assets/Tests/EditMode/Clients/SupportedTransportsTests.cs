using System.Linq;
using MCPForUnity.Editor.Clients;
using MCPForUnity.Editor.Clients.Configurators;
using MCPForUnity.Editor.Models;
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
        public void Cursor_SupportsBothTransports()
        {
            var cursor = new CursorConfigurator();
            var list = cursor.SupportedTransports.ToList();
            CollectionAssert.Contains(list, ConfiguredTransport.Stdio);
            CollectionAssert.Contains(list, ConfiguredTransport.Http);
        }
    }
}
