namespace MCPForUnity.Editor.Models
{
    public class McpClient
    {
        public string name;
        public string windowsConfigPath;
        public string macConfigPath;
        public string linuxConfigPath;
        public McpTypes mcpType;
        public string configStatus;
        public McpStatus status = McpStatus.NotConfigured;

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
                McpStatus.Error => configStatus.StartsWith("Error:") ? configStatus : "Error",
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
