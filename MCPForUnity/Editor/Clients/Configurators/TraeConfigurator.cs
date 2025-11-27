using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Models;

namespace MCPForUnity.Editor.Clients.Configurators
{
    public class TraeConfigurator : JsonFileMcpConfigurator
    {
        public TraeConfigurator() : base(new McpClient
        {
            name = "Trae",
            windowsConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Trae", "mcp.json"),
            macConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Trae", "mcp.json"),
            linuxConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "Trae", "mcp.json"),
        })
        { }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "Open Trae and go to Settings > MCP",
            "Select Add Server > Add Manually",
            "Paste the JSON or point to the mcp.json file\n"+
                "Windows: %AppData%\\Trae\\mcp.json\n" +
                "macOS: ~/Library/Application Support/Trae/mcp.json\n" +
                "Linux: ~/.config/Trae/mcp.json\n",
            "Save and restart Trae"
        };
    }
}
