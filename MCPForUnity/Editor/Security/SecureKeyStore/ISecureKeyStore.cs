namespace MCPForUnity.Editor.Security
{
    /// <summary>
    /// At-rest storage for AI asset-generation provider API keys.
    ///
    /// Keys are written ONLY to the OS secure store (Keychain / Credential Manager /
    /// libsecret) or an encrypted-file fallback. They are never placed in EditorPrefs,
    /// never sent over the MCP bridge, never returned by any MCP tool, and never logged.
    /// <see cref="Has"/> reports existence only — there is no API that returns all keys.
    /// </summary>
    public interface ISecureKeyStore
    {
        /// <summary>Resolve the key for a provider id (e.g. "tripo"). Returns false when absent.</summary>
        bool TryGet(string providerId, out string apiKey);

        /// <summary>Store (or overwrite) the key for a provider id. Empty/null deletes it.</summary>
        void Set(string providerId, string apiKey);

        /// <summary>Remove the stored key for a provider id (no-op when absent).</summary>
        void Delete(string providerId);

        /// <summary>True when a key exists for the provider id. Never returns the value.</summary>
        bool Has(string providerId);
    }
}
