using System.Collections.Generic;

namespace MCPForUnity.Editor.Models
{
    public class McpClient
    {
        public string name;
        public string windowsConfigPath;
        public string macConfigPath;
        public string linuxConfigPath;
        public string configStatus;
        public McpStatus status = McpStatus.NotConfigured;
        public ConfiguredTransport configuredTransport = ConfiguredTransport.Unknown;

        // Capability flags/config for JSON-based configurators
        public bool IsVsCodeLayout; // Whether the config file follows VS Code layout (env object at root)
        public bool SupportsHttpTransport = true; // Whether the MCP server supports HTTP transport
        public bool EnsureEnvObject; // Whether to ensure the env object is present in the config
        public bool StripEnvWhenNotRequired; // Whether to strip the env object when not required
        public string HttpUrlProperty = "url"; // The property name for the HTTP URL in the config
        public Dictionary<string, object> DefaultUnityFields = new();

        // Helper method to convert the enum to a display string
        public string GetStatusDisplayString()
        {
            return status switch
            {
                McpStatus.NotConfigured => "Not Configured",
                McpStatus.Configured => "Configured",
                McpStatus.Running => "Running",
                McpStatus.Connected => "Connected",
                McpStatus.IncorrectPath => "Incorrect Path",
                McpStatus.CommunicationError => "Communication Error",
                McpStatus.NoResponse => "No Response",
                McpStatus.UnsupportedOS => "Unsupported OS",
                McpStatus.MissingConfig => "Missing MCPForUnity Config",
                McpStatus.Error => configStatus?.StartsWith("Error:") == true ? configStatus : "Error",
                _ => "Unknown",
            };
        }

        // Helper method to set both status enum and string for backward compatibility
        public void SetStatus(McpStatus newStatus, string errorDetails = null)
        {
            status = newStatus;

            if (newStatus == McpStatus.Error && !string.IsNullOrEmpty(errorDetails))
            {
                configStatus = $"Error: {errorDetails}";
            }
            else
            {
                configStatus = GetStatusDisplayString();
            }
        }
    }
}
