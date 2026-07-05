using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Models;

namespace MCPForUnity.Editor.Clients.Configurators
{
    /// <summary>
    /// Factory Droid configurator. Droid stores MCP server config in
    /// ~/.factory/mcp.json using the standard `mcpServers` container, so it
    /// reuses the shared JSON-file configurator pipeline.
    /// </summary>
    public class DroidConfigurator : JsonFileMcpConfigurator
    {
        public DroidConfigurator() : base(new McpClient
        {
            name = "Droid",
            // ~/.factory/mcp.json on every OS (UserProfile resolves to C:\Users\<user> on Windows).
            windowsConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".factory", "mcp.json"),
            macConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".factory", "mcp.json"),
            linuxConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".factory", "mcp.json"),
        })
        { }

        public override bool SupportsSkills => true;

        public override string GetSkillInstallPath()
        {
            var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(userHome, ".factory", "skills", "unity-mcp-skill");
        }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "Install Factory Droid (https://factory.ai)",
            "Click Configure to add UnityMCP to ~/.factory/mcp.json\nOR open the config file at the path above",
            "Paste the configuration JSON into the mcpServers object",
            "Save and restart Droid"
        };
    }
}
