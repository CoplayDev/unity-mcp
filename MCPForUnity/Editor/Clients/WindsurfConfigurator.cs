using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Models;

namespace MCPForUnity.Editor.Clients
{
    public class WindsurfConfigurator : JsonFileMcpConfigurator
    {
        public WindsurfConfigurator() : base(new McpClient
        {
            name = "Windsurf",
            windowsConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codeium", "windsurf", "mcp_config.json"),
            macConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codeium", "windsurf", "mcp_config.json"),
            linuxConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codeium", "windsurf", "mcp_config.json"),
            HttpUrlProperty = "serverUrl",
            DefaultUnityFields = { { "disabled", false } },
            StripEnvWhenNotRequired = true
        })
        { }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "Open Windsurf",
            "Settings > MCP > Manage MCPs > View raw config",
            "Paste JSON",
            "Save and restart"
        };

        public override string GetManualSnippet()
        {
            // Force consistent handling for Windsurf; reuse base behavior
            return base.GetManualSnippet();
        }
    }
}
