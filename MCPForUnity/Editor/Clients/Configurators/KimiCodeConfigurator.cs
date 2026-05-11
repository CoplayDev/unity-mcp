using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Models;

namespace MCPForUnity.Editor.Clients.Configurators
{
    /// <summary>
    /// Kimi Code CLI MCP client configurator.
    /// Kimi Code uses a JSON-based configuration file with mcpServers section.
    /// Config path: ~/.kimi/mcp.json
    ///
    /// Kimi Code supports both stdio (uvx) and HTTP transport modes.
    /// Default: stdio mode (works without Unity Editor for basic operations)
    /// HTTP mode: requires Unity Editor running with MCP HTTP server started
    /// </summary>
    public class KimiCodeConfigurator : JsonFileMcpConfigurator
    {
        public KimiCodeConfigurator() : base(new McpClient
        {
            name = "Kimi Code",
            windowsConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kimi", "mcp.json"),
            macConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kimi", "mcp.json"),
            linuxConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kimi", "mcp.json"),
            SupportsHttpTransport = true,
        })
        { }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "Ensure Kimi Code CLI is installed (pip install kimi-cli or see https://github.com/MoonshotAI/kimi-cli)",
            "Click 'Auto Configure' to automatically add UnityMCP to ~/.kimi/mcp.json",
            "OR click 'Manual Setup' to copy the configuration JSON",
            "Open ~/.kimi/mcp.json and paste the configuration",
            "Save and restart Kimi Code CLI",
            "Use 'kimi mcp list' to verify Unity MCP is connected",
            "Note: For full functionality, open Unity Editor and start HTTP server"
        };
    }
}
