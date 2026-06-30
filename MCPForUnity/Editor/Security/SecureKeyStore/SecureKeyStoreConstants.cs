namespace MCPForUnity.Editor.Security
{
    /// <summary>Shared constants for the secure key store.</summary>
    internal static class SecureKeyStoreConstants
    {
        /// <summary>Service / target namespace used in the OS secure store.</summary>
        internal const string ServiceName = "MCPForUnity.AssetGen";

        /// <summary>Known asset-generation provider ids (lowercase).</summary>
        internal static readonly string[] ProviderIds =
        {
            "tripo", "meshy", "sketchfab", "fal", "openrouter"
        };
    }
}
