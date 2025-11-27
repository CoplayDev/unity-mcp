using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Models;
using UnityEditor;

namespace MCPForUnity.Editor.Clients.Configurators
{
    public class AntigravityConfigurator : JsonFileMcpConfigurator
    {
        public AntigravityConfigurator() : base(new McpClient
        {
            name = "Antigravity",
            windowsConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity", "mcp_config.json"),
            macConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity", "mcp_config.json"),
            linuxConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity", "mcp_config.json"),
            HttpUrlProperty = "serverUrl",
            DefaultUnityFields = { { "disabled", false } },
            StripEnvWhenNotRequired = true
        })
        { }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "Open Antigravity",
            "Click the more_horiz menu in the Agent pane > MCP Servers",
            "Select 'Install' for Unity MCP or use the Configure button above",
            "Restart Antigravity if necessary"
        };
    }
}
