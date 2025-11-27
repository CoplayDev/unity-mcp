using System.Collections.Generic;
using MCPForUnity.Editor.Models;

namespace MCPForUnity.Editor.Clients.Configurators
{
    public class ClaudeCodeConfigurator : ClaudeCliMcpConfigurator
    {
        public ClaudeCodeConfigurator() : base(new McpClient
        {
            name = "Claude Code",
            windowsConfigPath = string.Empty,
            macConfigPath = string.Empty,
            linuxConfigPath = string.Empty,
        })
        { }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "Ensure Claude CLI is installed",
            "Use the Register button to register automatically\nOR manually run: claude mcp add UnityMCP",
            "Restart Claude Code"
        };
    }
}
