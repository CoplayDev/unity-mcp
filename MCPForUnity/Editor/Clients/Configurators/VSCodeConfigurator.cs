using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Models;

namespace MCPForUnity.Editor.Clients.Configurators
{
    public class VSCodeConfigurator : JsonFileMcpConfigurator
    {
        public VSCodeConfigurator() : base(new McpClient
        {
            name = "VSCode GitHub Copilot",
            windowsConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Code", "User", "mcp.json"),
            macConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Code", "User", "mcp.json"),
            linuxConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "Code", "User", "mcp.json"),
            IsVsCodeLayout = true
        })
        { }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "Install GitHub Copilot extension",
            "Open or create mcp.json at the path above",
            "Paste the configuration JSON",
            "Save and restart VSCode"
        };
    }
}
