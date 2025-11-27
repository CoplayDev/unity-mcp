using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Models;

namespace MCPForUnity.Editor.Clients.Configurators
{
    public class KiroConfigurator : JsonFileMcpConfigurator
    {
        public KiroConfigurator() : base(new McpClient
        {
            name = "Kiro",
            windowsConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kiro", "settings", "mcp.json"),
            macConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kiro", "settings", "mcp.json"),
            linuxConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kiro", "settings", "mcp.json"),
            EnsureEnvObject = true,
            DefaultUnityFields = { { "disabled", false } }
        })
        { }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "Open Kiro",
            "Go to File > Settings > Settings > Search for \"MCP\" > Open Workspace MCP Config\nOR open the config file at the path above",
            "Paste the configuration JSON",
            "Save and restart Kiro"
        };
    }
}
