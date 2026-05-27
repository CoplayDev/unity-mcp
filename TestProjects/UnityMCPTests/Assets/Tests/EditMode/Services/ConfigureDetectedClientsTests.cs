using System.Linq;
using MCPForUnity.Editor.Services;
using NUnit.Framework;

namespace MCPForUnityTests.Editor.Services
{
    // ConfigureAllDetectedClients walks the real McpClientRegistry and Configure()s every
    // detected client, which would touch real user config files on a dev machine. These
    // tests pass on CI (no MCP clients installed there) but would mutate real state on
    // a developer's machine. Marked [Explicit] so they only run when invoked by name;
    // proper isolation requires DI of the configurator list and is tracked separately.
    [TestFixture]
    public class ConfigureDetectedClientsTests
    {
        [Test]
        [Explicit("Side-effect: writes real client configs on machines with MCP clients installed")]
        public void Summary_ContainsOnlyInstalledClients()
        {
            var svc = new ClientConfigurationService();
            var summary = svc.ConfigureAllDetectedClients();
            int installedCount = svc.GetAllClients().Count(c => c.IsInstalled);
            Assert.AreEqual(installedCount, summary.SuccessCount + summary.FailureCount,
                "Only installed clients should appear in success/failure totals");
        }

        [Test]
        [Explicit("Side-effect: writes real client configs on machines with MCP clients installed")]
        public void Summary_SkippedCountTracksUninstalled()
        {
            var svc = new ClientConfigurationService();
            var summary = svc.ConfigureAllDetectedClients();
            int uninstalledCount = svc.GetAllClients().Count(c => !c.IsInstalled);
            Assert.AreEqual(uninstalledCount, summary.SkippedCount);
        }
    }
}
