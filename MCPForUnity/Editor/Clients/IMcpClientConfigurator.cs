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

        /// <summary>
        /// True if this client appears installed on the user's machine. Used to filter
        /// "configure all detected" so we don't write configs for apps the user doesn't have.
        /// Implementations should be cheap (filesystem stat or cached path lookup).
        /// </summary>
        bool IsInstalled { get; }

        /// <summary>
        /// Transports this client can be configured with. Order is "preference if user has no opinion";
        /// the configure path picks the user's global preference if present in this list, else falls back to the first entry.
        /// </summary>
        System.Collections.Generic.IReadOnlyList<ConfiguredTransport> SupportedTransports { get; }

        /// <summary>Label to show on the configure button for the current state.</summary>
        string GetConfigureActionLabel();

        /// <summary>Returns the platform-specific config path (or message for CLI-managed clients).</summary>
        string GetConfigPath();

        /// <summary>Checks and updates status; returns current status.</summary>
        McpStatus CheckStatus(bool attemptAutoRewrite = true);

        /// <summary>Runs auto-configuration (register/write file/CLI etc.). Always idempotent
        /// — calling twice with the same settings is safe and is what the bulk "Configure All"
        /// path relies on to refresh transport / server-version drift across every detected
        /// client.</summary>
        void Configure();

        /// <summary>
        /// Removes UnityMCP from this client's config (JSON entry, CLI registration, etc.).
        /// Default is a no-op for client types that don't yet implement removal (Codex TOML);
        /// callers should treat this as best-effort. The UI's per-client button routes here
        /// when <see cref="Status"/> is <see cref="McpStatus.Configured"/>.
        /// </summary>
        void Unregister();

        /// <summary>Returns the manual configuration snippet (JSON/TOML/commands).</summary>
        string GetManualSnippet();

        /// <summary>Returns ordered human-readable installation steps.</summary>
        System.Collections.Generic.IList<string> GetInstallationSteps();

        /// <summary>True if this client supports skill installation/sync.</summary>
        bool SupportsSkills { get; }

        /// <summary>Returns the absolute path where skills should be installed, or null if unsupported.</summary>
        string GetSkillInstallPath();
    }
}
