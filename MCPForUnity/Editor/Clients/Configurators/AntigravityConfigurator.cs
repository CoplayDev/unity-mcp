using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Models;
using UnityEditor;

namespace MCPForUnity.Editor.Clients.Configurators
{
    public class AntigravityConfigurator : JsonFileMcpConfigurator
    {
        // Antigravity 2.x migrated its MCP config from ~/.gemini/antigravity/mcp_config.json
        // (where Antigravity also stores its own runtime state — conversations, scratch, etc.)
        // to a dedicated ~/.gemini/config/mcp_config.json. The migration drops a `.migrated`
        // marker in the new location and renames the previous folder to `antigravity-backup`.
        // The old path is no longer read by Antigravity at all, so writing there silently
        // fails to register UnityMCP on every modern install.
        public AntigravityConfigurator() : base(new McpClient
        {
            name = "Antigravity 2.0",
            windowsConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "config", "mcp_config.json"),
            macConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "config", "mcp_config.json"),
            linuxConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "config", "mcp_config.json"),
            HttpUrlProperty = "serverUrl",
            DefaultUnityFields = { { "disabled", false } },
            StripEnvWhenNotRequired = true
        })
        { }

        // Detect Antigravity itself, not just its config dir. ~/.gemini/config/ is created on
        // first launch of Antigravity 2.x, so the inherited ParentDirectoryExists check
        // false-negatives on a fresh install where the user hasn't opened Antigravity yet.
        // ~/.antigravity/ is created by the installer (VS-Code-style support dir) and the
        // macOS app bundle is dropped by the installer; either is conclusive evidence that
        // Antigravity is installed, regardless of whether it has been launched.
        public override bool IsInstalled
        {
            get
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (Directory.Exists(Path.Combine(home, ".antigravity"))) return true;
                if (Directory.Exists(Path.Combine(home, ".gemini", "config"))) return true;
                if (Directory.Exists(Path.Combine(home, ".gemini", "antigravity"))) return true;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    return Directory.Exists("/Applications/Antigravity.app");
                return false;
            }
        }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "Open Antigravity 2.0",
            "Click the more_horiz menu in the Agent pane > MCP Servers",
            "Select 'Install' for Unity MCP or use the Configure button above",
            "Restart Antigravity if necessary"
        };
    }
}
