using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Models;

namespace MCPForUnity.Editor.Clients.Configurators
{
    public class ClineConfigurator : JsonFileMcpConfigurator
    {
        public ClineConfigurator() : base(new McpClient
        {
            name = "Cline",
            windowsConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Code", "User", "globalStorage", "saoudrizwan.claude-dev", "settings", "cline_mcp_settings.json"),
            macConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Code", "User", "globalStorage", "saoudrizwan.claude-dev", "settings", "cline_mcp_settings.json"),
            linuxConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "Code", "User", "globalStorage", "saoudrizwan.claude-dev", "settings", "cline_mcp_settings.json"),
            DefaultUnityFields = { { "disabled", false }, { "autoApprove", new object[] { } } }
        })
        { }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "Open Cline in VS Code",
            "Click the MCP Servers icon in the Cline pane",
            "Go to Configure tab and click 'Configure MCP Servers'\nOR open the config file at the path above",
            "Paste the configuration JSON into the mcpServers object",
            "Save and restart VS Code"
        };
    }
}
