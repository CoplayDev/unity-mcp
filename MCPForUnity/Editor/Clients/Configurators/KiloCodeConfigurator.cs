using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Models;

namespace MCPForUnity.Editor.Clients.Configurators
{
    public class KiloCodeConfigurator : JsonFileMcpConfigurator
    {
        public KiloCodeConfigurator() : base(new McpClient
        {
            name = "Kilo Code",
            // Kilo Code v7.0.33+ moved MCP config out of the VS Code extension's
            // globalStorage/mcp_settings.json to a CLI-style kilo.jsonc under ~/.config/kilo.
            // The new schema (https://app.kilo.ai/config.json) uses an "mcp" container,
            // type:"remote" for HTTP servers, type:"local" for stdio, and an "enabled" flag.
            // ~/.config/kilo/kilo.jsonc on every OS (UserProfile resolves to C:\Users\<user> on Windows).
            windowsConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "kilo", "kilo.jsonc"),
            macConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "kilo", "kilo.jsonc"),
            linuxConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "kilo", "kilo.jsonc"),
            IsVsCodeLayout = false,
            ServerContainerKey = "mcp",
            HttpTypeValue = "remote",
            StdioTypeValue = "local",
            SchemaUrl = "https://app.kilo.ai/config.json",
            DefaultUnityFields = { { "enabled", true } }
        })
        { }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "Install or update Kilo Code (v7.0.33 or newer)",
            "Open the Kilo Code MCP Servers view\nOR edit the config file at the path above (~/.config/kilo/kilo.jsonc)",
            "Paste the configuration JSON into the \"mcp\" object",
            "Save and restart Kilo Code"
        };
    }
}
