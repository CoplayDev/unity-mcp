using System;
using System.Net;
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
        private const string DefaultLocalBaseUrl = "http://127.0.0.1:8080";
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
            return NormalizeBaseUrl(stored, DefaultLocalBaseUrl, remoteScope: false);
        }

        /// <summary>
        /// Saves a user-provided URL to the local HTTP pref.
        /// </summary>
        public static void SaveLocalBaseUrl(string userValue)
        {
            string normalized = NormalizeBaseUrl(userValue, DefaultLocalBaseUrl, remoteScope: false);
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
            return NormalizeBaseUrl(stored, DefaultRemoteBaseUrl, remoteScope: true);
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
            string normalized = NormalizeBaseUrl(userValue, DefaultRemoteBaseUrl, remoteScope: true);
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
        /// Returns true when advanced settings allow binding HTTP Local to all interfaces
        /// (e.g. 0.0.0.0 / ::). Disabled by default.
        /// </summary>
        public static bool AllowLanHttpBind()
        {
            return EditorPrefs.GetBool(EditorPrefKeys.AllowLanHttpBind, false);
        }

        /// <summary>
        /// Returns true when advanced settings allow insecure HTTP/WS for remote endpoints.
        /// Disabled by default.
        /// </summary>
        public static bool AllowInsecureRemoteHttp()
        {
            return EditorPrefs.GetBool(EditorPrefKeys.AllowInsecureRemoteHttp, false);
        }

        /// <summary>
        /// Returns true if the host is loopback-only.
        /// </summary>
        public static bool IsLoopbackHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return false;
            }

            string normalized = host.Trim().Trim('[', ']').ToLowerInvariant();
            if (normalized == "localhost")
            {
                return true;
            }

            if (IPAddress.TryParse(normalized, out IPAddress parsedIp))
            {
                return IPAddress.IsLoopback(parsedIp);
            }

            return false;
        }

        /// <summary>
        /// Returns true if the host is a bind-all-interfaces address.
        /// </summary>
        public static bool IsBindAllInterfacesHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return false;
            }

            string normalized = host.Trim().Trim('[', ']').ToLowerInvariant();
            if (IPAddress.TryParse(normalized, out IPAddress parsedIp))
            {
                return parsedIp.Equals(IPAddress.Any) || parsedIp.Equals(IPAddress.IPv6Any);
            }

            return false;
        }

        /// <summary>
        /// Returns true when the URL host is acceptable for HTTP Local launch.
        /// Loopback is always allowed. Bind-all interfaces requires explicit opt-in.
        /// </summary>
        public static bool IsHttpLocalUrlAllowedForLaunch(string url, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(url))
            {
                error = "HTTP Local requires a loopback URL (localhost/127.0.0.1/::1).";
                return false;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                error = $"Invalid URL: {url}";
                return false;
            }

            string host = uri.Host;
            if (IsLoopbackHost(host))
            {
                return true;
            }

            if (IsBindAllInterfacesHost(host))
            {
                if (AllowLanHttpBind())
                {
                    return true;
                }

                error = "Binding to all interfaces (0.0.0.0/::) is disabled by default. " +
                        "Enable \"Allow LAN bind for HTTP Local\" in Advanced Settings to opt in.";
                return false;
            }

            error = "HTTP Local requires a loopback URL (localhost/127.0.0.1/::1).";
            return false;
        }

        /// <summary>
        /// Returns true when remote URL is allowed by current security policy.
        /// HTTPS is required by default; HTTP needs explicit opt-in.
        /// </summary>
        public static bool IsRemoteUrlAllowed(string remoteBaseUrl, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(remoteBaseUrl))
            {
                error = "HTTP Remote requires a configured URL.";
                return false;
            }

            if (!Uri.TryCreate(remoteBaseUrl, UriKind.Absolute, out var uri))
            {
                error = $"Invalid HTTP Remote URL: {remoteBaseUrl}";
                return false;
            }

            if (uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase))
            {
                if (AllowInsecureRemoteHttp())
                {
                    return true;
                }

                error = "HTTP Remote requires HTTPS by default. Enable \"Allow insecure HTTP for HTTP Remote\" in Advanced Settings to opt in.";
                return false;
            }

            error = $"Unsupported URL scheme '{uri.Scheme}'. Use https:// (or http:// only with explicit insecure opt-in).";
            return false;
        }

        /// <summary>
        /// Returns true when the currently configured remote URL satisfies security policy.
        /// </summary>
        public static bool IsCurrentRemoteUrlAllowed(out string error)
        {
            return IsRemoteUrlAllowed(GetRemoteBaseUrl(), out error);
        }

        /// <summary>
        /// Human-readable host requirement for HTTP Local based on current security settings.
        /// </summary>
        public static string GetHttpLocalHostRequirementText()
        {
            return AllowLanHttpBind()
                ? "localhost/127.0.0.1/::1/0.0.0.0/::"
                : "localhost/127.0.0.1/::1";
        }

        /// <summary>
        /// Normalizes a URL so that we consistently store just the base (no trailing slash/path).
        /// </summary>
        private static string NormalizeBaseUrl(string value, string defaultUrl, bool remoteScope)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultUrl;
            }

            string trimmed = value.Trim();

            // Ensure scheme exists.
            // For HTTP Remote, default to https:// to avoid accidental plaintext transport.
            // For HTTP Local, default to http:// for zero-friction local setup.
            if (!trimmed.Contains("://"))
            {
                string defaultScheme = remoteScope ? "https" : "http";
                trimmed = $"{defaultScheme}://{trimmed}";
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
