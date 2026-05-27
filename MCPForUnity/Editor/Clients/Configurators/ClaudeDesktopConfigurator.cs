using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Models;
using UnityEditor;

namespace MCPForUnity.Editor.Clients.Configurators
{
    public class ClaudeDesktopConfigurator : JsonFileMcpConfigurator
    {
        public const string ClientName = "Claude Desktop";

        public ClaudeDesktopConfigurator() : base(new McpClient
        {
            name = ClientName,
            windowsConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude", "claude_desktop_config.json"),
            macConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Claude", "claude_desktop_config.json"),
            linuxConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "Claude", "claude_desktop_config.json"),
            SupportsHttpTransport = false,
            StripEnvWhenNotRequired = true
        })
        { }

        public override bool SupportsSkills => true;

        public override string GetSkillInstallPath()
        {
            var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(userHome, ".claude", "skills", "unity-mcp-skill");
        }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "Open Claude Desktop",
            "Go to Settings > Developer > Edit Config\nOR open the config path",
            "Paste the configuration JSON",
            "Save and restart Claude Desktop"
        };

        private static readonly ConfiguredTransport[] StdioOnly = { ConfiguredTransport.Stdio };
        public override IReadOnlyList<ConfiguredTransport> SupportedTransports => StdioOnly;
    }
}
