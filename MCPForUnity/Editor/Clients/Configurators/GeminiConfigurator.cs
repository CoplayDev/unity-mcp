using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Models;
using UnityEditor;

namespace MCPForUnity.Editor.Clients.Configurators
{
    public class GeminiConfigurator : JsonFileMcpConfigurator
    {
        public GeminiConfigurator() : base(new McpClient
        {
            name = "Gemini",
            windowsConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "settings.json"),
            macConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "settings.json"),
            linuxConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "settings.json"),
            HttpUrlProperty = "httpUrl",
        })
        { }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "Ensure Gemini CLI is installed (see https://geminicli.com/docs/get-started/installation/)",
            "Click Register to add UnityMCP via 'gemini mcp add'",
            "The server will be automatically available in Gemini CLI",
            "Use Unregister to remove via 'gemini mcp remove'"
        };
    }
}
