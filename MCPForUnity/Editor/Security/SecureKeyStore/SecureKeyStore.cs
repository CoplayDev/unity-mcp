using UnityEngine;

namespace MCPForUnity.Editor.Security
{
    /// <summary>
    /// Entry point for at-rest provider key storage. Selects the best OS secure store for
    /// the current platform and layers the read-only environment-variable override on top.
    /// Keys never transit the MCP bridge; only the C# editor side reads them, at call time.
    /// </summary>
    public static class SecureKeyStore
    {
        private static ISecureKeyStore _current;

        public static ISecureKeyStore Current => _current ??= Build();

        /// <summary>Test seam: substitute an in-memory or temp-dir store.</summary>
        internal static void OverrideForTests(ISecureKeyStore store) => _current = store;

        /// <summary>Test seam: clear the cached instance.</summary>
        internal static void ResetForTests() => _current = null;

        private static ISecureKeyStore Build() => new EnvOverlayKeyStore(SelectPlatform());

        private static ISecureKeyStore SelectPlatform()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.OSXEditor:
                    return new MacKeychainKeyStore();
                case RuntimePlatform.WindowsEditor:
                    return new WindowsCredentialKeyStore();
                case RuntimePlatform.LinuxEditor:
                    return LinuxSecretToolKeyStore.IsAvailable()
                        ? (ISecureKeyStore)new LinuxSecretToolKeyStore()
                        : new EncryptedFileKeyStore();
                default:
                    return new EncryptedFileKeyStore();
            }
        }
    }

    /// <summary>
    /// Wraps a platform store so an <c>MCPFORUNITY_&lt;PROVIDER&gt;_API_KEY</c> environment
    /// variable takes precedence on reads. Writes always go to the underlying store.
    /// </summary>
    internal sealed class EnvOverlayKeyStore : ISecureKeyStore
    {
        private readonly ISecureKeyStore _inner;

        public EnvOverlayKeyStore(ISecureKeyStore inner) { _inner = inner; }

        public bool TryGet(string providerId, out string apiKey)
        {
            if (EnvKeyOverride.TryGet(providerId, out apiKey)) return true;
            return _inner.TryGet(providerId, out apiKey);
        }

        public bool Has(string providerId)
            => EnvKeyOverride.TryGet(providerId, out _) || _inner.Has(providerId);

        public void Set(string providerId, string apiKey) => _inner.Set(providerId, apiKey);

        public void Delete(string providerId) => _inner.Delete(providerId);
    }
}
