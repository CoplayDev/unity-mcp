using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Models;

namespace MCPForUnity.Editor.Clients.Configurators
{
    public class TraeCnConfigurator : JsonFileMcpConfigurator
    {
        public TraeCnConfigurator() : base(new McpClient
        {
            name = "Trae CN",
            windowsConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Trae CN", "User", "mcp.json"),
            macConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Trae CN", "User", "mcp.json"),
            linuxConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "Trae CN", "User", "mcp.json"),
        })
        { }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "Open Trae CN and go to Settings > MCP",
            "Select Add Server > Add Manually",
            "Paste the JSON or point to the mcp.json file\n"+
                "Windows: %AppData%\\Trae CN\\User\\mcp.json\n" +
                "macOS: ~/Library/Application Support/Trae CN/User/mcp.json\n" +
                "Linux: ~/.config/Trae CN/User/mcp.json\n",
            "Save and restart Trae CN"
        };
    }
}
