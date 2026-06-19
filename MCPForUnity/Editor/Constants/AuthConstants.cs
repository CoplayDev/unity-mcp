namespace MCPForUnity.Editor.Constants
{
    /// <summary>
    /// Protocol-level constants for API key authentication.
    /// </summary>
    internal static class AuthConstants
    {
        internal const string ApiKeyHeader = "X-API-Key";

        /// <summary>
        /// Header carrying the local-bridge shared secret in HTTP-local mode
        /// (harden/security, R5). Distinct from the remote-hosted API key.
        /// </summary>
        internal const string BridgeTokenHeader = "X-Bridge-Token";
    }
}
