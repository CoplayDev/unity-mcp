using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Models;

namespace MCPForUnity.Editor.Clients.Configurators
{
    /// <summary>
    /// Configures the CodeBuddy CLI (~/.codebuddy.json) MCP settings.
    /// </summary>
    public class CodeBuddyCliConfigurator : JsonFileMcpConfigurator
    {
        public CodeBuddyCliConfigurator() : base(new McpClient
        {
            name = "CodeBuddy CLI",
            windowsConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codebuddy.json"),
            macConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codebuddy.json"),
            linuxConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codebuddy.json"),
        })
        { }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "Install CodeBuddy CLI and ensure '~/.codebuddy.json' exists",
            "Click Configure to add the UnityMCP entry (or manually edit the file above)",
            "Restart your CLI session if needed"
        };
    }
}
