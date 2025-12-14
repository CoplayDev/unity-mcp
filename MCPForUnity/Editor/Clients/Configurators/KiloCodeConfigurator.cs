using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Models;

namespace MCPForUnity.Editor.Clients.Configurators
{
    public class KiloCodeConfigurator : JsonFileMcpConfigurator
    {
        public KiloCodeConfigurator() : base(new McpClient
        {
            name = "Kilo Code",
            windowsConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Code", "User", "globalStorage", "kilocode.kilo-code", "settings", "mcp_settings.json"),
            macConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Code", "User", "globalStorage", "kilocode.kilo-code", "settings", "mcp_settings.json"),
            linuxConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "Code", "User", "globalStorage", "kilocode.kilo-code", "settings", "mcp_settings.json"),
            IsVsCodeLayout = true
        })
        { }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "Install Kilo Code extension in VS Code",
            "Open Kilo Code settings (gear icon in sidebar)",
            "Navigate to MCP Servers section and click 'Edit Global MCP Settings'\nOR open the config file at the path above",
            "Paste the configuration JSON into the mcpServers object",
            "Save and restart VS Code"
        };
    }
}
