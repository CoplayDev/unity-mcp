using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Models;

namespace MCPForUnity.Editor.Clients.Configurators
{
    public class ClaudeCodeConfigurator : JsonFileMcpConfigurator
    {
        public ClaudeCodeConfigurator() : base(new McpClient
        {
            name = "Claude Code",
            windowsConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json"),
            macConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json"),
            linuxConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json"),
            SupportsHttpTransport = true,
            HttpUrlProperty = "url", // Claude Code uses "url" for HTTP servers
            IsVsCodeLayout = false,  // Claude Code uses standard mcpServers layout
        })
        { }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "Open your project in Claude Code",
            "Click Configure in MCP for Unity (or manually edit ~/.claude.json)",
            "The MCP server will be added to the global mcpServers section",
            "Restart Claude Code to apply changes"
        };
    }
}
