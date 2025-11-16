using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using MCPForUnity.Editor.Constants;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Provides shared utilities for deriving deterministic project identity information
    /// used by transport clients (hash, name, persistent session id).
    /// </summary>
    internal static class ProjectIdentityUtility
    {
        private const string SessionPrefKey = EditorPrefKeys.WebSocketSessionId;

        /// <summary>
        /// Returns the SHA1 hash of the current project path (truncated to 8 characters).
        /// Matches the legacy hash used by the stdio bridge and server registry.
        /// </summary>
        public static string GetProjectHash()
        {
            try
            {
                string projectPath = Application.dataPath ?? string.Empty;
                using SHA1 sha1 = SHA1.Create();
                byte[] bytes = Encoding.UTF8.GetBytes(projectPath);
                byte[] hashBytes = sha1.ComputeHash(bytes);
                var sb = new StringBuilder();
                foreach (byte b in hashBytes)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString(0, Math.Min(8, sb.Length));
            }
            catch (Exception)
            {
                return "default";
            }
        }

        /// <summary>
        /// Returns a human friendly project name derived from the Assets directory path.
        /// </summary>
        public static string GetProjectName()
        {
            try
            {
                string projectPath = Application.dataPath;
                if (string.IsNullOrEmpty(projectPath))
                {
                    return "Unknown";
                }

                projectPath = projectPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (projectPath.EndsWith("Assets", StringComparison.OrdinalIgnoreCase))
                {
                    projectPath = projectPath[..^6].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }

                string name = Path.GetFileName(projectPath);
                return string.IsNullOrEmpty(name) ? "Unknown" : name;
            }
            catch
            {
                return "Unknown";
            }
        }

        /// <summary>
        /// Retrieves a persistent session id for the plugin, creating one if absent.
        /// </summary>
        public static string GetOrCreateSessionId()
        {
            try
            {
                string sessionId = EditorPrefs.GetString(SessionPrefKey, string.Empty);
                if (string.IsNullOrEmpty(sessionId))
                {
                    sessionId = Guid.NewGuid().ToString();
                    EditorPrefs.SetString(SessionPrefKey, sessionId);
                }
                return sessionId;
            }
            catch
            {
                // If prefs are unavailable (e.g. during batch tests) fall back to runtime guid.
                return Guid.NewGuid().ToString();
            }
        }

        /// <summary>
        /// Clears the persisted session id (mainly for tests).
        /// </summary>
        public static void ResetSessionId()
        {
            try
            {
                if (EditorPrefs.HasKey(SessionPrefKey))
                {
                    EditorPrefs.DeleteKey(SessionPrefKey);
                }
            }
            catch
            {
                // Ignore
            }
        }
    }
}
