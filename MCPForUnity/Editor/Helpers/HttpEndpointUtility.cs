using System;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Models;
using MCPForUnity.Editor.Services;
using UnityEditor;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Helper methods for managing HTTP endpoint URLs used by the MCP bridge.
    /// Ensures the stored value is always the base URL (without trailing path),
    /// and provides convenience accessors for specific endpoints.
    ///
    /// HTTP Local and HTTP Remote use separate EditorPrefs keys so that switching
    /// between scopes does not overwrite the other scope's URL.
    /// </summary>
    public static class HttpEndpointUtility
    {
        private const string LocalPrefKey = EditorPrefKeys.HttpBaseUrl;
        private const string RemotePrefKey = EditorPrefKeys.HttpRemoteBaseUrl;
        private const string DefaultLocalBaseUrl = "http://localhost:8080";
        private const string DefaultRemoteBaseUrl = "";

        /// <summary>
        /// Returns the normalized base URL for the currently active HTTP scope.
        /// If the scope is "remote", returns the remote URL; otherwise returns the local URL.
        /// </summary>
        public static string GetBaseUrl()
        {
            return IsRemoteScope() ? GetRemoteBaseUrl() : GetLocalBaseUrl();
        }

        /// <summary>
        /// Saves a user-provided URL to the currently active HTTP scope's pref.
        /// </summary>
        public static void SaveBaseUrl(string userValue)
        {
            if (IsRemoteScope())
            {
                SaveRemoteBaseUrl(userValue);
            }
            else
            {
                SaveLocalBaseUrl(userValue);
            }
        }

        /// <summary>
        /// Returns the normalized local HTTP base URL (always reads local pref).
        /// </summary>
        public static string GetLocalBaseUrl()
        {
            string stored = EditorPrefs.GetString(LocalPrefKey, DefaultLocalBaseUrl);
            return NormalizeBaseUrl(stored, DefaultLocalBaseUrl);
        }

        /// <summary>
        /// Saves a user-provided URL to the local HTTP pref.
        /// </summary>
        public static void SaveLocalBaseUrl(string userValue)
        {
            string normalized = NormalizeBaseUrl(userValue, DefaultLocalBaseUrl);
            EditorPrefs.SetString(LocalPrefKey, normalized);
        }

        /// <summary>
        /// Returns the normalized remote HTTP base URL (always reads remote pref).
        /// Returns empty string if no remote URL is configured.
        /// </summary>
        public static string GetRemoteBaseUrl()
        {
            string stored = EditorPrefs.GetString(RemotePrefKey, DefaultRemoteBaseUrl);
            if (string.IsNullOrWhiteSpace(stored))
            {
                return DefaultRemoteBaseUrl;
            }
            return NormalizeBaseUrl(stored, DefaultRemoteBaseUrl);
        }

        /// <summary>
        /// Saves a user-provided URL to the remote HTTP pref.
        /// </summary>
        public static void SaveRemoteBaseUrl(string userValue)
        {
            if (string.IsNullOrWhiteSpace(userValue))
            {
                EditorPrefs.SetString(RemotePrefKey, DefaultRemoteBaseUrl);
                return;
            }
            string normalized = NormalizeBaseUrl(userValue, DefaultRemoteBaseUrl);
            EditorPrefs.SetString(RemotePrefKey, normalized);
        }

        /// <summary>
        /// Builds the JSON-RPC endpoint for the currently active scope (base + /mcp).
        /// </summary>
        public static string GetMcpRpcUrl()
        {
            return AppendPathSegment(GetBaseUrl(), "mcp");
        }

        /// <summary>
        /// Builds the local JSON-RPC endpoint (local base + /mcp).
        /// </summary>
        public static string GetLocalMcpRpcUrl()
        {
            return AppendPathSegment(GetLocalBaseUrl(), "mcp");
        }

        /// <summary>
        /// Builds the remote JSON-RPC endpoint (remote base + /mcp).
        /// Returns empty string if no remote URL is configured.
        /// </summary>
        public static string GetRemoteMcpRpcUrl()
        {
            string remoteBase = GetRemoteBaseUrl();
            return string.IsNullOrEmpty(remoteBase) ? string.Empty : AppendPathSegment(remoteBase, "mcp");
        }

        /// <summary>
        /// Builds the endpoint used when POSTing custom-tool registration payloads.
        /// </summary>
        public static string GetRegisterToolsUrl()
        {
            return AppendPathSegment(GetBaseUrl(), "register-tools");
        }

        /// <summary>
        /// Returns true if the active HTTP transport scope is "remote".
        /// </summary>
        public static bool IsRemoteScope()
        {
            string scope = EditorConfigurationCache.Instance.HttpTransportScope;
            return string.Equals(scope, "remote", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns the <see cref="ConfiguredTransport"/> that matches the current server-side
        /// transport selection (Stdio, Http, or HttpRemote).
        /// Centralises the 3-way determination so callers avoid duplicated logic.
        /// </summary>
        public static ConfiguredTransport GetCurrentServerTransport()
        {
            bool useHttp = EditorConfigurationCache.Instance.UseHttpTransport;
            if (!useHttp) return ConfiguredTransport.Stdio;
            return IsRemoteScope() ? ConfiguredTransport.HttpRemote : ConfiguredTransport.Http;
        }

        /// <summary>
        /// Normalizes a URL so that we consistently store just the base (no trailing slash/path).
        /// </summary>
        private static string NormalizeBaseUrl(string value, string defaultUrl)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultUrl;
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
