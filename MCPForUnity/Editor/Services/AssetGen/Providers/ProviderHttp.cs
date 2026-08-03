using System;
using System.Text;
using MCPForUnity.Editor.Security;
using MCPForUnity.Editor.Services.AssetGen.Http;

namespace MCPForUnity.Editor.Services.AssetGen.Providers
{
    /// <summary>
    /// Shared HTTP-response helpers for provider adapters: read the response text (falling back to
    /// a UTF-8 decode of the raw body) and truncate long bodies for inclusion in error messages.
    /// </summary>
    internal static class ProviderHttp
    {
        /// <summary>
        /// Throw unless <paramref name="url"/> is an absolute https URL whose host is exactly
        /// <paramref name="allowedHost"/>. Adapters route every auth-bearing request URL through
        /// this so a malicious/MITM'd provider response (e.g. a rogue response_url) can't redirect
        /// the API key to an attacker host. The error is scrubbed of the key.
        /// </summary>
        public static void RequireHost(string url, string allowedHost, string apiKey, string context)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri u)
                || u.Scheme != Uri.UriSchemeHttps
                || !string.Equals(u.Host, allowedHost, StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception(SecretRedactor.Scrub(
                    $"{context}: refusing to send credentials to an unexpected host in URL '{url}' (expected https://{allowedHost}).",
                    apiKey));
            }
        }

        /// <summary>Response text, falling back to a UTF-8 decode of the raw body when Text is empty.</summary>
        public static string BodyText(HttpResult res)
        {
            string text = res?.Text;
            if (string.IsNullOrEmpty(text) && res?.Body != null)
                text = Encoding.UTF8.GetString(res.Body);
            return text;
        }

        /// <summary>Cap a (possibly null) string at 500 chars for inclusion in an error message.</summary>
        public static string Truncate(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Length <= 500 ? s : s.Substring(0, 500) + "…";
        }
    }
}
