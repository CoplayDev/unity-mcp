using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Models;

namespace MCPForUnity.Editor.Clients.Configurators
{
    public class ZCodeConfigurator : JsonFileMcpConfigurator
    {
        public ZCodeConfigurator() : base(new McpClient
        {
            name = "ZCode",
            windowsConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".zcode", "cli", "config.json"),
            macConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".zcode", "cli", "config.json"),
            linuxConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".zcode", "cli", "config.json"),
            ServerContainerKey = "mcp.servers",
        })
        { }

        public override bool IsInstalled => ParentDirectoryExists(Path.GetDirectoryName(GetConfigPath()));

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "Install and launch ZCode to create its '~/.zcode' directory",
            "Click Configure to add UnityMCP to '~/.zcode/cli/config.json'",
            "Open ZCode Settings > MCP Servers and enable UnityMCP",
            "Restart the ZCode session or reconnect the server if needed"
        };
    }
}
