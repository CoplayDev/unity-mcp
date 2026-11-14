using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Models;

namespace MCPForUnity.Editor.Clients.Configurators
{
    public class WorkBuddyConfigurator : JsonFileMcpConfigurator
    {
        public WorkBuddyConfigurator() : base(new McpClient
        {
            name = "WorkBuddy",
            windowsConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".workbuddy", "mcp.json"),
            macConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".workbuddy", "mcp.json"),
            linuxConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".workbuddy", "mcp.json"),
        })
        { }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "Install and launch WorkBuddy to create its '~/.workbuddy' directory",
            "Click Configure to add UnityMCP to '~/.workbuddy/mcp.json'",
            "In WorkBuddy, open Plugins > MCP Servers and enable UnityMCP",
            "Restart WorkBuddy or reconnect the server if needed"
        };
    }
}
