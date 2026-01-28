using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Models;
using MCPForUnity.Editor.Services;
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

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "Open Claude Desktop",
            "Go to Settings > Developer > Edit Config\nOR open the config path",
            "Paste the configuration JSON",
            "Save and restart Claude Desktop"
        };

        public override void Configure()
        {
            bool useHttp = EditorConfigurationCache.Instance.UseHttpTransport;
            if (useHttp)
            {
                throw new InvalidOperationException("Claude Desktop does not support HTTP transport. Switch to stdio in settings before configuring.");
            }

            base.Configure();
        }

        public override string GetManualSnippet()
        {
            bool useHttp = EditorConfigurationCache.Instance.UseHttpTransport;
            if (useHttp)
            {
                return "# Claude Desktop does not support HTTP transport.\n" +
                       "# Open Advanced Settings and disable HTTP transport to use stdio, then regenerate.";
            }

            return base.GetManualSnippet();
        }
    }
}
