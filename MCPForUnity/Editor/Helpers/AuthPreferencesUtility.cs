using System;
using System.IO;
using MCPForUnity.Editor.Constants;
using UnityEditor;

namespace MCPForUnity.Editor.Helpers
{
    internal static class AuthPreferencesUtility
    {
        private static string ApiKeyPrefKey => EditorPrefKeys.AuthToken;

        internal static string GetApiKey()
        {
            // Prefer EditorPrefs for quick access
            string apiKey = EditorPrefs.GetString(ApiKeyPrefKey, string.Empty);

            if (string.IsNullOrEmpty(apiKey))
            {
                apiKey = TryReadApiKeyFromDisk();
                if (string.IsNullOrEmpty(apiKey))
                {
                    apiKey = GenerateNewApiKey();
                }

                EditorPrefs.SetString(ApiKeyPrefKey, apiKey);
                TryPersistApiKey(apiKey);
            }

            return apiKey;
        }

        internal static void SetApiKey(string apiKey)
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                apiKey = GenerateNewApiKey();
            }

            EditorPrefs.SetString(ApiKeyPrefKey, apiKey);
            TryPersistApiKey(apiKey);
        }

        internal static string GenerateNewApiKey()
        {
            // 32 bytes -> ~43 base64url chars without padding; mirrors server token_urlsafe
            var bytes = new byte[32];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            string base64 = Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');

            return base64;
        }

        internal static string GetApiKeyFilePath()
        {
            // Keep UI in lockstep with server path resolution (supports UNITY_MCP_HOME override)
            string overrideRoot = Environment.GetEnvironmentVariable("UNITY_MCP_HOME");
            if (!string.IsNullOrEmpty(overrideRoot))
            {
            return Path.Combine(overrideRoot, "api_key");
            }

    #if UNITY_EDITOR_WIN
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(root, "UnityMCP", "api_key");
    #elif UNITY_EDITOR_OSX
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Library", "Application Support", "UnityMCP");
            return Path.Combine(root, "api_key");
    #else
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), ".local", "share", "UnityMCP");
            return Path.Combine(root, "api_key");
    #endif
        }

        private static string TryReadApiKeyFromDisk()
        {
            try
            {
                string path = GetApiKeyFilePath();
                if (!File.Exists(path))
                {
                    return string.Empty;
                }

                string content = File.ReadAllText(path).Trim();
                return content;
            }
            catch (Exception)
            {
                // Fall back to generating a new key if reading fails
                return string.Empty;
            }
        }

        private static void TryPersistApiKey(string apiKey)
        {
            try
            {
                string path = GetApiKeyFilePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, apiKey);
            }
            catch (Exception)
            {
                // Non-fatal: user can still copy the key from the UI
            }
        }
    }
}
