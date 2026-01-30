namespace MCPForUnity.Editor.Models
{
    // Enum representing the various status states for MCP clients
    public enum McpStatus
    {
        NotConfigured, // Not set up yet
        Configured, // Successfully configured
        Running, // Service is running
        Connected, // Successfully connected
        IncorrectPath, // Configuration has incorrect paths
        CommunicationError, // Connected but communication issues
        NoResponse, // Connected but not responding
        MissingConfig, // Config file exists but missing required elements
        UnsupportedOS, // OS is not supported
        Error, // General error state
    }

    /// <summary>
    /// Represents the transport type a client is configured to use.
    /// Used to detect mismatches between server and client transport settings.
    /// </summary>
    public enum ConfiguredTransport
    {
        Unknown,    // Could not determine transport type
        Stdio,      // Client configured for stdio transport
        Http,       // Client configured for HTTP local transport
        HttpRemote  // Client configured for HTTP remote-hosted transport
    }
}

