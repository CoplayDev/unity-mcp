using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Models;

namespace MCPForUnity.Editor.Clients.Configurators
{
    /// <summary>
    /// Qwen Code MCP client configurator.
    /// Qwen Code uses a JSON-based configuration file with mcpServers section.
    /// Config path: ~/.qwen/settings.json
    ///
    /// Qwen Code supports both stdio (uvx) and HTTP transport modes.
    /// Default: stdio mode (works without Unity Editor for basic operations)
    /// HTTP mode: requires Unity Editor running with MCP HTTP server started
    /// </summary>
    public class QwenCodeConfigurator : JsonFileMcpConfigurator
    {
        public QwenCodeConfigurator() : base(new McpClient
        {
            name = "Qwen Code",
            windowsConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".qwen", "settings.json"),
            macConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".qwen", "settings.json"),
            linuxConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".qwen", "settings.json"),
            SupportsHttpTransport = true,
            // Default to stdio transport for Qwen Code (like Cursor)
            // User can switch to HTTP in Unity: Window > MCP for Unity > Settings
        })
        { }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "Ensure Qwen Code is installed (npm install -g @qwen-code/qwen-code or download from https://github.com/QwenLM/qwen-code)",
            "Open Qwen Code",
            "Click 'Auto Configure' to automatically add UnityMCP to settings.json",
            "OR click 'Manual Setup' to copy the configuration JSON",
            "Open ~/.qwen/settings.json and paste the configuration",
            "Save and restart Qwen Code",
            "Use /mcp command in Qwen Code to verify Unity MCP is connected",
            "Note: For full functionality, open Unity Editor and start HTTP server"
        };
    }
}
