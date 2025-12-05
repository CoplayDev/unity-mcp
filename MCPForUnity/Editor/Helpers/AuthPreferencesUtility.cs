using System;
using System.Linq;
using MCPForUnity.Editor.Constants;
using UnityEditor;

namespace MCPForUnity.Editor.Helpers
{
    internal static class AuthPreferencesUtility
    {
        internal static bool GetAuthEnabled()
        {
            return EditorPrefs.GetBool(EditorPrefKeys.AuthEnabled, false);
        }

        internal static void SetAuthEnabled(bool enabled)
        {
            EditorPrefs.SetBool(EditorPrefKeys.AuthEnabled, enabled);
        }

        internal static string GetAllowedIpsRaw()
        {
            return EditorPrefs.GetString(EditorPrefKeys.AllowedIps, "*");
        }

        internal static string[] GetAllowedIps()
        {
            var raw = GetAllowedIpsRaw();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new[] { "*" };
            }

            return raw
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .DefaultIfEmpty("*")
                .ToArray();
        }

        internal static void SetAllowedIps(string csv)
        {
            string value = string.IsNullOrWhiteSpace(csv) ? "*" : csv.Trim();
            EditorPrefs.SetString(EditorPrefKeys.AllowedIps, value);
        }

        internal static string GetAuthToken()
        {
            return EditorPrefs.GetString(EditorPrefKeys.AuthToken, string.Empty);
        }

        internal static void SetAuthToken(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                EditorPrefs.DeleteKey(EditorPrefKeys.AuthToken);
            }
            else
            {
                EditorPrefs.SetString(EditorPrefKeys.AuthToken, token);
            }
        }

        internal static string GenerateNewToken()
        {
            // 32 bytes -> 43 base64 chars without padding; safe for headers
            var bytes = new byte[32];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            return Convert.ToBase64String(bytes).TrimEnd('=');
        }
    }
}
