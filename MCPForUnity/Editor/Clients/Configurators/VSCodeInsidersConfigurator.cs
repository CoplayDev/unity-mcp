using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Models;

namespace MCPForUnity.Editor.Clients.Configurators
{
    public class VSCodeInsidersConfigurator : JsonFileMcpConfigurator
    {
        public VSCodeInsidersConfigurator() : base(new McpClient
        {
            name = "VSCode Insiders GitHub Copilot",
            windowsConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Code - Insiders", "User", "mcp.json"),
            macConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Code - Insiders", "User", "mcp.json"),
            linuxConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "Code - Insiders", "User", "mcp.json"),
            IsVsCodeLayout = true
        })
        { }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "Install GitHub Copilot extension in VS Code Insiders",
            "Open or create mcp.json at the path above",
            "Paste the configuration JSON",
            "Save and restart VS Code Insiders"
        };
    }
}
