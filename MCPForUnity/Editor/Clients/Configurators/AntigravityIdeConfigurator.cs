using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Models;
using UnityEditor;

namespace MCPForUnity.Editor.Clients.Configurators
{
    /// <summary>
    /// Antigravity IDE — the separate IDE build that ships its own ~/.gemini/antigravity-ide/
    /// runtime dir and reads its MCP config from that same folder. It did NOT migrate to
    /// ~/.gemini/config/ the way Antigravity 2.0 did, so it still uses the legacy in-folder
    /// mcp_config.json layout. The two apps coexist on the same machine, so we expose them
    /// as separate clients rather than trying to autodetect which one to write to.
    /// </summary>
    public class AntigravityIdeConfigurator : JsonFileMcpConfigurator
    {
        public AntigravityIdeConfigurator() : base(new McpClient
        {
            name = "Antigravity IDE",
            windowsConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-ide", "mcp_config.json"),
            macConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-ide", "mcp_config.json"),
            linuxConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-ide", "mcp_config.json"),
            HttpUrlProperty = "serverUrl",
            DefaultUnityFields = { { "disabled", false } },
            StripEnvWhenNotRequired = true
        })
        { }

        // ~/.gemini/antigravity-ide/ is created by the IDE on first launch and holds both
        // its runtime state (annotations/, brain/, conversations/, ...) and its mcp_config.json
        // — presence of the dir is the canonical "Antigravity IDE has been installed and
        // launched at least once" signal.
        public override bool IsInstalled
        {
            get
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return Directory.Exists(Path.Combine(home, ".gemini", "antigravity-ide"));
            }
        }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "Open Antigravity IDE",
            "Click the more_horiz menu in the Agent pane > MCP Servers",
            "Select 'Install' for Unity MCP or use the Configure button above",
            "Restart Antigravity IDE if necessary"
        };
    }
}
