using System;
using MCPForUnity.Editor.Constants;
using UnityEditor;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Helper methods for managing HTTP endpoint URLs used by the MCP bridge.
    /// Ensures the stored value is always the base URL (without trailing path),
    /// and provides convenience accessors for specific endpoints.
    /// </summary>
    public static class HttpEndpointUtility
    {
        private const string PrefKey = EditorPrefKeys.HttpBaseUrl;
        private const string DefaultBaseUrl = "http://localhost:8080";

        /// <summary>
        /// Returns the normalized base URL currently stored in EditorPrefs.
        /// </summary>
        public static string GetBaseUrl()
        {
            string stored = EditorPrefs.GetString(PrefKey, DefaultBaseUrl);
            return NormalizeBaseUrl(stored);
        }

        /// <summary>
        /// Saves a user-provided URL after normalizing it to a base form.
        /// </summary>
        public static void SaveBaseUrl(string userValue)
        {
            string normalized = NormalizeBaseUrl(userValue);
            EditorPrefs.SetString(PrefKey, normalized);
        }

        /// <summary>
        /// Builds the JSON-RPC endpoint used by FastMCP clients (base + /mcp).
        /// </summary>
        public static string GetMcpRpcUrl()
        {
            return AppendPathSegment(GetBaseUrl(), "mcp");
        }

        /// <summary>
        /// Builds the endpoint used when POSTing custom-tool registration payloads.
        /// </summary>
        public static string GetRegisterToolsUrl()
        {
            return AppendPathSegment(GetBaseUrl(), "register-tools");
        }

        /// <summary>
        /// Normalizes a URL so that we consistently store just the base (no trailing slash/path).
        /// </summary>
        private static string NormalizeBaseUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return DefaultBaseUrl;
            }

            string trimmed = value.Trim();

            // Ensure scheme exists; default to http:// if user omitted it.
            if (!trimmed.Contains("://"))
            {
                trimmed = $"http://{trimmed}";
            }

            // Remove trailing slash segments.
            trimmed = trimmed.TrimEnd('/');

            // Strip trailing "/mcp" (case-insensitive) if provided.
            if (trimmed.EndsWith("/mcp", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed[..^4];
            }

            return trimmed;
        }

        private static string AppendPathSegment(string baseUrl, string segment)
        {
            return $"{baseUrl.TrimEnd('/')}/{segment}";
        }
    }
}
