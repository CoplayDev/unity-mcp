using System.IO;
using MCPForUnity.Editor.Clients;
using MCPForUnity.Editor.Clients.Configurators;
using NUnit.Framework;

namespace MCPForUnityTests.Editor.Clients
{
    [TestFixture]
    public class IsInstalledTests
    {
        [Test]
        public void IMcpClientConfigurator_ExposesIsInstalled()
        {
            var prop = typeof(IMcpClientConfigurator).GetProperty("IsInstalled");
            Assert.IsNotNull(prop, "IMcpClientConfigurator must expose an IsInstalled property");
            Assert.AreEqual(typeof(bool), prop.PropertyType);
        }

        [Test]
        public void JsonClient_NotInstalled_WhenParentDirMissing()
        {
            var cursor = new CursorConfigurator();
            string parent = Path.GetDirectoryName(cursor.GetConfigPath());
            if (parent == null || !Directory.Exists(parent))
            {
                Assert.IsFalse(cursor.IsInstalled,
                    "Cursor parent dir does not exist on this machine, IsInstalled must be false");
            }
            else
            {
                Assert.IsTrue(cursor.IsInstalled,
                    "Cursor parent dir exists, IsInstalled must be true");
            }
        }

        [Test]
        public void JsonClient_Installed_WhenParentDirExists()
        {
            var claude = new ClaudeDesktopConfigurator();
            string parent = Path.GetDirectoryName(claude.GetConfigPath());
            bool expected = parent != null && Directory.Exists(parent);
            Assert.AreEqual(expected, claude.IsInstalled);
        }

        /// <summary>
        /// Pi's presence check must key on Pi's OWN agent directory, honouring
        /// <c>PI_CODING_AGENT_DIR</c>. The file it writes is the tool-agnostic shared
        /// <c>~/.config/mcp/mcp.json</c>, so the inherited "parent directory of the config path
        /// exists" rule would answer a different question entirely.
        /// </summary>
        [Test]
        public void PiConfigurator_PresenceIsAgentDirBased()
        {
            var pi = new PiConfigurator();
            Assert.AreEqual(Directory.Exists(PiAgentDirectory()), pi.IsInstalled,
                "Pi detection must follow Pi's agent directory, honouring PI_CODING_AGENT_DIR");
        }

        /// <summary>
        /// The specific over-report this configurator guards against: a shared MCP config tree with
        /// no Pi install beside it must NOT read as "Pi detected". Cursor and Claude Code users have
        /// <c>~/.config/mcp</c> without ever installing Pi, so keying on it would offer Pi in
        /// "Configure All Detected Clients" on machines that do not have it. Skipped (Pass) when Pi
        /// is genuinely installed here or when no shared tree exists -- neither host can distinguish
        /// the two rules, so asserting there would prove nothing.
        /// </summary>
        [Test]
        public void PiConfigurator_SharedMcpConfigTreeAlone_IsNotInstalled()
        {
            var pi = new PiConfigurator();
            string sharedParent = Path.GetDirectoryName(pi.GetConfigPath());

            if (sharedParent == null || !Directory.Exists(sharedParent))
                Assert.Pass("no shared MCP config tree on this machine -- nothing to distinguish");
            if (Directory.Exists(PiAgentDirectory()))
                Assert.Pass("Pi is installed on this machine -- the over-report case needs a host without it");

            Assert.IsFalse(pi.IsInstalled,
                "the shared MCP config tree is not evidence of a Pi install; detection must key on Pi's agent dir");
        }

        private static string PiAgentDirectory()
        {
            string overrideDir = System.Environment.GetEnvironmentVariable("PI_CODING_AGENT_DIR");
            if (!string.IsNullOrWhiteSpace(overrideDir))
                return overrideDir.Trim();

            string home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".pi", "agent");
        }
    }
}
