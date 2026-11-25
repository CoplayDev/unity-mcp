using System;
using System.IO;
using MCPForUnity.Editor.Clients.Configurators;
using MCPForUnity.Editor.Models;
using MCPForUnity.Editor.Services;
using NUnit.Framework;

namespace MCPForUnityTests.Editor.Clients
{
    [TestFixture]
    public class DeepSeekHarnessConfiguratorTests
    {
        /// <summary>
        /// DSH presence keys on the DSH home directory, honouring <c>DSH_HOME</c>. Mirrors the
        /// Pi agent-dir test in <c>IsInstalledTests</c>: detection must follow the home dir, not
        /// the bundle patch file's parent directory.
        /// </summary>
        [Test]
        public void DeepSeekHarnessConfigurator_PresenceIsDshHomeBased()
        {
            var dsh = new DeepSeekHarnessConfigurator();
            Assert.AreEqual(Directory.Exists(DshHome()), dsh.IsInstalled,
                "DSH detection must follow DSH_HOME or ~/.dsh");
        }

        /// <summary>
        /// DSH_HOME pointing at a path that does not exist must read as not installed
        /// (fail-closed), even on machines where DSH genuinely is installed.
        /// </summary>
        [Test]
        public void DeepSeekHarnessConfigurator_IsInstalledFalse_WhenDshHomeMissing()
        {
            string original = Environment.GetEnvironmentVariable("DSH_HOME");
            string missing = Path.Combine(Path.GetTempPath(), "dsh-missing-" + Guid.NewGuid().ToString("N"));
            try
            {
                Environment.SetEnvironmentVariable("DSH_HOME", missing);
                Assert.IsFalse(Directory.Exists(missing), "test precondition: temp path must not exist");
                Assert.IsFalse(new DeepSeekHarnessConfigurator().IsInstalled,
                    "DSH_HOME pointing at a missing directory must read as not installed");
            }
            finally
            {
                Environment.SetEnvironmentVariable("DSH_HOME", original);
            }
        }

        /// <summary>
        /// Status reflects bundle-file presence: no snippet patch file => NotConfigured,
        /// patch file present => Configured regardless of the attemptAutoRewrite flag.
        /// Uses a temp DSH_HOME so the real ~/.dsh is never read or written.
        /// </summary>
        [Test]
        public void DeepSeekHarnessConfigurator_CheckStatusFollowsBundlePresence()
        {
            string originalHome = Environment.GetEnvironmentVariable("DSH_HOME");
            string originalProfile = Environment.GetEnvironmentVariable("DSH_PROFILE");
            string root = Path.Combine(Path.GetTempPath(), "dsh-status-" + Guid.NewGuid().ToString("N"));
            try
            {
                Environment.SetEnvironmentVariable("DSH_HOME", root);
                Environment.SetEnvironmentVariable("DSH_PROFILE", "test-profile");

                var dsh = new DeepSeekHarnessConfigurator();
                Assert.AreEqual(McpStatus.NotConfigured, dsh.CheckStatus(true),
                    "missing bundle patch file must read as NotConfigured");
                Assert.AreEqual(McpStatus.NotConfigured, dsh.CheckStatus(false),
                    "missing bundle patch file must read as NotConfigured (attemptAutoRewrite ignored)");

                string patchPath = Path.Combine(root, "bundles", "test-profile", "unity-mcp", "cordis.patch.yml");
                Directory.CreateDirectory(Path.GetDirectoryName(patchPath));
                File.WriteAllText(patchPath, "# test fixture\n");

                Assert.AreEqual(McpStatus.Configured, dsh.CheckStatus(true),
                    "present bundle patch file must read as Configured");
                Assert.AreEqual(McpStatus.Configured, dsh.CheckStatus(false),
                    "present bundle patch file must read as Configured (attemptAutoRewrite ignored)");
            }
            finally
            {
                Environment.SetEnvironmentVariable("DSH_HOME", originalHome);
                Environment.SetEnvironmentVariable("DSH_PROFILE", originalProfile);
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        }

        [Test]
        public void DeepSeekHarnessConfigurator_ConfigureThrows()
        {
            Assert.Throws<InvalidOperationException>(() => new DeepSeekHarnessConfigurator().Configure(),
                "DSH Configure() must throw; setup goes through the manual snippet");
        }

        /// <summary>
        /// The manual snippet must hold the two bundle files, the plugin add/remove commands,
        /// and the managed-block markers around the Cordis row. The tool-name prefix
        /// (mcp__unityMCP__&lt;tool&gt;) is documented in the installation steps, not the
        /// snippet, so it is asserted there. HTTP is forced for determinism: the stdio row
        /// needs uvx on the host and GetManualSnippet would return an error string instead.
        /// Save/restore follows the SupportedTransportsTests pattern, which also calls
        /// GetManualSnippet in EditMode.
        /// </summary>
        [Test]
        public void DeepSeekHarnessConfigurator_ManualSnippetContainsBundle()
        {
            var cache = EditorConfigurationCache.Instance;
            bool original = cache.UseHttpTransport;
            try
            {
                cache.SetUseHttpTransport(true);
                var dsh = new DeepSeekHarnessConfigurator();
                string snippet = dsh.GetManualSnippet();

                StringAssert.Contains("@local/unity-mcp-dsh", snippet, "snippet must name the DSH bundle");
                StringAssert.Contains("cordis.patch.yml", snippet, "snippet must show the patch file");
                StringAssert.Contains("dsh plugin", snippet, "snippet must show the plugin commands");
                StringAssert.Contains("# >>> MCP for Unity (unityMCP) managed block >>>", snippet, "snippet must open the managed block");
                StringAssert.Contains("# <<< MCP for Unity (unityMCP) <<<", snippet, "snippet must close the managed block");
                StringAssert.Contains("serverName: unityMCP", snippet, "snippet must name the MCP server");
                StringAssert.Contains("transport: streamable-http", snippet, "snippet must use the streamable-http transport");
                StringAssert.Contains("url:", snippet, "snippet must carry the MCP endpoint URL");
                StringAssert.Contains("failOnStartupError: true", snippet, "snippet must fail on startup errors");

                var steps = dsh.GetInstallationSteps();
                bool hasToolPrefix = false;
                foreach (string step in steps)
                {
                    if (step.Contains("mcp__unityMCP__"))
                        hasToolPrefix = true;
                }
                Assert.IsTrue(hasToolPrefix, "installation steps must document the mcp__unityMCP__<tool> tool names");
            }
            finally
            {
                cache.SetUseHttpTransport(original);
            }
        }

        // Mirrors DeepSeekHarnessConfigurator.DshHome(): DSH_HOME when set and non-blank,
        // else ~/.dsh, else empty string (fail-closed).
        private static string DshHome()
        {
            string envHome = Environment.GetEnvironmentVariable("DSH_HOME");
            if (!string.IsNullOrWhiteSpace(envHome)) return envHome.Trim();
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return string.IsNullOrWhiteSpace(home) ? string.Empty : Path.Combine(home, ".dsh");
        }
    }
}
