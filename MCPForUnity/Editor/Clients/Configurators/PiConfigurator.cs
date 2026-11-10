using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Models;

namespace MCPForUnity.Editor.Clients.Configurators
{
    /// <summary>
    /// Pi (pi.dev) configurator.
    ///
    /// Two things make Pi different from every other JSON client in this folder, and both are
    /// load-bearing rather than cosmetic:
    ///
    /// 1. <b>Pi has no built-in MCP client.</b> As of pi 0.84.x there is no <c>mcp</c> subcommand,
    ///    no <c>--mcp-config</c> equivalent, and no <c>mcp.json</c> reader anywhere in the
    ///    distributed package; its own README states "No MCP. Build CLI tools with READMEs, or
    ///    build an extension that adds MCP support." Reaching this server from Pi therefore
    ///    requires an MCP extension, and the <c>unityMCP</c> entry written below is what that
    ///    extension reads. <see cref="GetInstallationSteps"/> names the extension explicitly,
    ///    because on Pi a config file alone is a silent no-op — the exact "configured, enabled, and
    ///    silently providing nothing" shape this window exists to prevent.
    ///
    /// 2. <b>The file is shared, so presence cannot be inferred from it.</b> Pi MCP extensions read
    ///    the tool-agnostic <c>~/.config/mcp/mcp.json</c>, not a Pi-owned path. That makes the
    ///    inherited <c>IsInstalled</c> ("does the config path's parent directory exist") wrong in
    ///    both directions — see <see cref="IsInstalled"/>.
    /// </summary>
    public class PiConfigurator : JsonFileMcpConfigurator
    {
        public PiConfigurator() : base(new McpClient
        {
            name = "Pi",
            windowsConfigPath = GetSharedMcpConfigPath(),
            macConfigPath = GetSharedMcpConfigPath(),
            linuxConfigPath = GetSharedMcpConfigPath()
        })
        { }

        /// <summary>
        /// Pi-exclusive presence check, deliberately NOT the inherited
        /// <c>ParentDirectoryExists(GetConfigPath())</c>.
        ///
        /// <para>
        /// The config path here is a SHARED, tool-agnostic file. Its parent directory therefore says
        /// nothing about whether Pi exists: a Cursor or Claude Code user has a
        /// <c>~/.config/mcp</c> tree without ever installing Pi, and a Pi user who has not yet
        /// configured any MCP server has no <c>~/.config/mcp</c> at all. Keying detection on that
        /// path would make "Configure All Detected Clients" offer Pi on machines that do not have
        /// it while skipping the exact machines this configurator exists to fix.
        /// </para>
        ///
        /// <para>
        /// Pi's own agent directory is the correct marker: pi creates it on first run for settings,
        /// sessions and auth, nothing else writes there, and <c>PI_CODING_AGENT_DIR</c> relocates
        /// it — so an explicitly relocated install is detected too rather than hardcoding
        /// <c>~/.pi</c>.
        /// </para>
        /// </summary>
        public override bool IsInstalled
        {
            get
            {
                string agentDir = GetPiAgentDirectory();
                return !string.IsNullOrEmpty(agentDir) && Directory.Exists(agentDir);
            }
        }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "Install an MCP extension for Pi — Pi ships no MCP client of its own:\npi install npm:pi-mcp-adapter",
            "Restart Pi so the extension loads (extensions are read at startup)",
            "Paste the configuration JSON into the file at the path above, or use Configure",
            "Start Pi and ask it to search MCP — e.g. \"search MCP for manage_gameobject\""
        };

        /// <summary>The tool-agnostic shared MCP config Pi MCP extensions read, in the user's home.</summary>
        private static string GetSharedMcpConfigPath()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".config", "mcp", "mcp.json");
        }

        /// <summary>
        /// Pi's agent directory: <c>$PI_CODING_AGENT_DIR</c> when set (Pi's own documented
        /// relocation override), else <c>~/.pi/agent</c>. Null when the home directory cannot be
        /// resolved, which callers must treat as "not installed" rather than as a match.
        /// </summary>
        private static string GetPiAgentDirectory()
        {
            string overrideDir = Environment.GetEnvironmentVariable("PI_CODING_AGENT_DIR");
            if (!string.IsNullOrWhiteSpace(overrideDir))
                return overrideDir.Trim();

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return string.IsNullOrEmpty(home) ? null : Path.Combine(home, ".pi", "agent");
        }
    }
}
