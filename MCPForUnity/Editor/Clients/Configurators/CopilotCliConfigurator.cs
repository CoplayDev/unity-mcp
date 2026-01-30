using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Models;

namespace MCPForUnity.Editor.Clients.Configurators
{
    public class CopilotCliConfigurator : JsonFileMcpConfigurator
    {
        public CopilotCliConfigurator() : base(new McpClient
        {
            name = "GitHub Copilot CLI",
            windowsConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".copilot", "mcp-config.json"),
            macConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".copilot", "mcp-config.json"),
            linuxConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".copilot", "mcp-config.json")
        })
        { }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "Install GitHub Copilot CLI (https://docs.github.com/en/copilot/concepts/agents/about-copilot-cli)",
            "Open or create mcp-config.json at the path above",
            "Paste the configuration JSON (or use /mcp add in the CLI)",
            "Restart your Copilot CLI session"
        };
    }
}
