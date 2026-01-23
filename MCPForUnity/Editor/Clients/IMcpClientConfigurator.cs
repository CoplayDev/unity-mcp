using MCPForUnity.Editor.Models;

namespace MCPForUnity.Editor.Clients
{
    /// <summary>
    /// Contract for MCP client configurators. Each client is responsible for
    /// status detection, auto-configure, and manual snippet/steps.
    /// </summary>
    public interface IMcpClientConfigurator
    {
        /// <summary>Stable identifier (e.g., "cursor").</summary>
        string Id { get; }

        /// <summary>Display name shown in the UI.</summary>
        string DisplayName { get; }

        /// <summary>Current status cached by the configurator.</summary>
        McpStatus Status { get; }

        /// <summary>
        /// The transport type the client is currently configured for.
        /// Returns Unknown if the client is not configured or the transport cannot be determined.
        /// </summary>
        ConfiguredTransport ConfiguredTransport { get; }

        /// <summary>True if this client supports auto-configure.</summary>
        bool SupportsAutoConfigure { get; }

        /// <summary>Label to show on the configure button for the current state.</summary>
        string GetConfigureActionLabel();

        /// <summary>Returns the platform-specific config path (or message for CLI-managed clients).</summary>
        string GetConfigPath();

        /// <summary>Checks and updates status; returns current status.</summary>
        McpStatus CheckStatus(bool attemptAutoRewrite = true);

        /// <summary>Runs auto-configuration (register/write file/CLI etc.).</summary>
        void Configure();

        /// <summary>Returns the manual configuration snippet (JSON/TOML/commands).</summary>
        string GetManualSnippet();

        /// <summary>Returns ordered human-readable installation steps.</summary>
        System.Collections.Generic.IList<string> GetInstallationSteps();
    }
}
