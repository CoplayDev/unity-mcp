using System.Collections.Generic;
using MCPForUnity.Editor.Models;

namespace MCPForUnity.Editor.Clients.Configurators
{
    /// <summary>
    /// Claude Code configurator using the CLI-based registration (claude mcp add/remove).
    /// This integrates with Claude Code's native MCP management.
    /// </summary>
    public class ClaudeCodeConfigurator : ClaudeCliMcpConfigurator
    {
        public ClaudeCodeConfigurator() : base(new McpClient
        {
            name = "Claude Code",
            SupportsHttpTransport = true,
        })
        { }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "Ensure Claude CLI is installed (comes with Claude Code)",
            "Click Register to add UnityMCP via 'claude mcp add'",
            "The server will be automatically available in Claude Code",
            "Use Unregister to remove via 'claude mcp remove'"
        };
    }
}
