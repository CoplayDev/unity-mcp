using System;

namespace MCPForUnity.Editor.Security
{
    /// <summary>
    /// Read-only environment-variable override for provider keys, intended for CI/headless
    /// and power users. Resolution order is env → secure store (see <see cref="SecureKeyStore"/>).
    /// Env values are never written back to any store.
    /// </summary>
    internal static class EnvKeyOverride
    {
        /// <summary>e.g. "tripo" → "MCPFORUNITY_TRIPO_API_KEY".</summary>
        internal static string EnvVarName(string providerId)
            => "MCPFORUNITY_" + (providerId ?? string.Empty).ToUpperInvariant() + "_API_KEY";

        internal static bool TryGet(string providerId, out string apiKey)
        {
            apiKey = null;
            if (string.IsNullOrEmpty(providerId)) return false;
            string v = Environment.GetEnvironmentVariable(EnvVarName(providerId));
            if (string.IsNullOrEmpty(v)) return false;
            apiKey = v;
            return true;
        }
    }
}
