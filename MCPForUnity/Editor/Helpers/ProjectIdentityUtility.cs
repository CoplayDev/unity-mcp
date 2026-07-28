using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using MCPForUnity.Editor.Constants;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Provides shared utilities for deriving deterministic project identity information
    /// used by transport clients (hash, name, persistent session id).
    /// </summary>
    [InitializeOnLoad]
    internal static class ProjectIdentityUtility
    {
        private const string SessionPrefKey = EditorPrefKeys.SessionId;
        private static bool _legacyKeyCleared;
        private static string _cachedProjectName = "Unknown";
        private static string _cachedProjectHash = "default";
        private static string _fallbackSessionId;
        private static bool _cacheScheduled;

        static ProjectIdentityUtility()
        {
            ScheduleCacheRefresh();
            EditorApplication.projectChanged += ScheduleCacheRefresh;
        }

        private static void ScheduleCacheRefresh()
        {
            if (_cacheScheduled)
            {
                return;
            }

            _cacheScheduled = true;
            EditorApplication.delayCall += CacheIdentityOnMainThread;
        }

        private static void CacheIdentityOnMainThread()
        {
            EditorApplication.delayCall -= CacheIdentityOnMainThread;
            _cacheScheduled = false;
            UpdateIdentityCache();
        }

        private static void UpdateIdentityCache()
        {
            try
            {
                string dataPath = Application.dataPath;
                if (string.IsNullOrEmpty(dataPath))
                {
                    return;
                }

                _cachedProjectHash = ComputeProjectHash(dataPath);
                _cachedProjectName = ComputeProjectName(dataPath);
            }
            catch
            {
                // Ignore and keep defaults
            }
        }

        /// <summary>
        /// Returns the SHA1 hash of the current project path (truncated to 16 characters).
        /// Matches the legacy hash used by the stdio bridge and server registry.
        /// </summary>
        public static string GetProjectHash()
        {
            EnsureIdentityCache();
            return _cachedProjectHash;
        }

        /// <summary>
        /// Returns a human friendly project name derived from the Assets directory path,
        /// or "Unknown" if the name cannot be determined.
        /// </summary>
        public static string GetProjectName()
        {
            EnsureIdentityCache();
            return _cachedProjectName;
        }

        private static string ComputeProjectHash(string dataPath)
        {
            try
            {
                using SHA1 sha1 = SHA1.Create();
                byte[] bytes = Encoding.UTF8.GetBytes(dataPath);
                byte[] hashBytes = sha1.ComputeHash(bytes);
                var sb = new StringBuilder();
                foreach (byte b in hashBytes)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString(0, Math.Min(16, sb.Length)).ToLowerInvariant();
            }
            catch
            {
                return "default";
            }
        }

        private static string ComputeProjectName(string dataPath)
        {
            try
            {
                string projectPath = dataPath;
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
        /// Persists a server-assigned session id.
        /// Safe to call from background threads.
        /// </summary>
        public static void SetSessionId(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return;
            }

            EditorApplication.delayCall += () =>
            {
                try
                {
                    string projectHash = GetProjectHash();
                    string projectSpecificKey = $"{SessionPrefKey}_{projectHash}";
                    EditorPrefs.SetString(projectSpecificKey, sessionId);
                }
                catch (Exception ex)
                {
                    McpLog.Warn($"Failed to persist session ID: {ex.Message}");
                }
            };
        }

        /// <summary>
        /// Retrieves a persistent session id for the plugin, creating one if absent.
        /// The session id is unique per project (scoped by project hash).
        /// </summary>
        public static string GetOrCreateSessionId()
        {
            try
            {
                // Make the session ID project-specific by including the project hash in the key
                string projectHash = GetProjectHash();
                string projectSpecificKey = $"{SessionPrefKey}_{projectHash}";

                string sessionId = EditorPrefs.GetString(projectSpecificKey, string.Empty);
                if (string.IsNullOrEmpty(sessionId))
                {
                    sessionId = Guid.NewGuid().ToString();
                    EditorPrefs.SetString(projectSpecificKey, sessionId);
                }
                return sessionId;
            }
            catch
            {
                // If prefs are unavailable (e.g. during batch tests) fall back to runtime guid.
                if (string.IsNullOrEmpty(_fallbackSessionId))
                {
                    _fallbackSessionId = Guid.NewGuid().ToString();
                }

                return _fallbackSessionId;
            }
        }

        /// <summary>
        /// Clears the persisted session id (mainly for tests).
        /// </summary>
        public static void ResetSessionId()
        {
            try
            {
                // Clear the project-specific session ID
                string projectHash = GetProjectHash();
                string projectSpecificKey = $"{SessionPrefKey}_{projectHash}";

                if (EditorPrefs.HasKey(projectSpecificKey))
                {
                    EditorPrefs.DeleteKey(projectSpecificKey);
                }

                if (!_legacyKeyCleared && EditorPrefs.HasKey(SessionPrefKey))
                {
                    EditorPrefs.DeleteKey(SessionPrefKey);
                    _legacyKeyCleared = true;
                }

                _fallbackSessionId = null;
            }
            catch
            {
                // Ignore
            }
        }

        private static void EnsureIdentityCache()
        {
            // When Application.dataPath is unavailable (e.g., batch mode) we fall back to
            // hashing the current working directory/Assets path so each project still
            // derives a deterministic, per-project session id rather than sharing "default".
            if (!string.IsNullOrEmpty(_cachedProjectHash) && _cachedProjectHash != "default")
            {
                return;
            }

            UpdateIdentityCache();

            if (!string.IsNullOrEmpty(_cachedProjectHash) && _cachedProjectHash != "default")
            {
                return;
            }

            string fallback = TryComputeFallbackProjectHash();
            if (!string.IsNullOrEmpty(fallback))
            {
                _cachedProjectHash = fallback;
            }
        }

        private static string TryComputeFallbackProjectHash()
        {
            try
            {
                string workingDirectory = Directory.GetCurrentDirectory();
                if (string.IsNullOrEmpty(workingDirectory))
                {
                    return "default";
                }

                // Normalise trailing separators so hashes remain stable
                workingDirectory = workingDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return ComputeProjectHash(Path.Combine(workingDirectory, "Assets"));
            }
            catch
            {
                return "default";
            }
        }
    }

    /// <summary>
    /// Stores connection and local-server lifecycle preferences per Unity project while
    /// retaining a single-owner read fallback for legacy machine-global preferences.
    /// </summary>
    internal static class ProjectScopedEditorPrefs
    {
        private const string MigrationOwnerKey = "MCPForUnity.ProjectScopedPrefs.MigrationOwner";
        private const string MigrationMutexName = "MCPForUnity.ProjectPreferenceMigration";
        private static readonly HashSet<string> KnownProjectScopedKeys = new HashSet<string>
        {
            EditorPrefKeys.UseHttpTransport,
            EditorPrefKeys.HttpBaseUrl,
            EditorPrefKeys.HttpTransportScope,
            EditorPrefKeys.LastLocalHttpServerPid,
            EditorPrefKeys.LastLocalHttpServerPort,
            EditorPrefKeys.LastLocalHttpServerStartedUtc,
            EditorPrefKeys.LastLocalHttpServerPidArgsHash,
            EditorPrefKeys.LastLocalHttpServerPidFilePath,
            EditorPrefKeys.LastLocalHttpServerInstanceToken,
        };

        internal static string GetKey(string baseKey, string projectHash = null)
        {
            string hash = string.IsNullOrWhiteSpace(projectHash)
                ? ProjectIdentityUtility.GetProjectHash()
                : projectHash;
            return $"{baseKey}.{hash}";
        }

        internal static string GetMigrationOwnerKey()
        {
            return MigrationOwnerKey;
        }

        internal static string ResolveStorageKey(string baseKey, string projectHash = null)
        {
            return KnownProjectScopedKeys.Contains(baseKey) ? GetKey(baseKey, projectHash) : baseKey;
        }

        internal static bool HasKey(string baseKey)
        {
            return EditorPrefs.HasKey(ResolveStorageKey(baseKey));
        }

        internal static bool GetBool(
            string baseKey,
            bool defaultValue,
            string projectHash = null,
            bool allowLegacyFallback = true)
        {
            string scopedKey = ResolveStorageKey(baseKey, projectHash);
            if (EditorPrefs.HasKey(scopedKey))
            {
                return EditorPrefs.GetBool(scopedKey, defaultValue);
            }

            if (allowLegacyFallback
                && EditorPrefs.HasKey(baseKey)
                && TryClaimLegacyPreference(projectHash))
            {
                bool migrated = EditorPrefs.GetBool(baseKey, defaultValue);
                EditorPrefs.SetBool(scopedKey, migrated);
                return migrated;
            }

            return defaultValue;
        }

        internal static int GetInt(
            string baseKey,
            int defaultValue,
            string projectHash = null,
            bool allowLegacyFallback = true)
        {
            string scopedKey = ResolveStorageKey(baseKey, projectHash);
            if (EditorPrefs.HasKey(scopedKey))
            {
                return EditorPrefs.GetInt(scopedKey, defaultValue);
            }

            if (allowLegacyFallback
                && EditorPrefs.HasKey(baseKey)
                && TryClaimLegacyPreference(projectHash))
            {
                int migrated = EditorPrefs.GetInt(baseKey, defaultValue);
                EditorPrefs.SetInt(scopedKey, migrated);
                return migrated;
            }

            return defaultValue;
        }

        internal static string GetString(
            string baseKey,
            string defaultValue,
            string projectHash = null,
            bool allowLegacyFallback = true)
        {
            string scopedKey = ResolveStorageKey(baseKey, projectHash);
            if (EditorPrefs.HasKey(scopedKey))
            {
                return EditorPrefs.GetString(scopedKey, defaultValue);
            }

            if (allowLegacyFallback
                && EditorPrefs.HasKey(baseKey)
                && TryClaimLegacyPreference(projectHash))
            {
                string migrated = EditorPrefs.GetString(baseKey, defaultValue);
                EditorPrefs.SetString(scopedKey, migrated);
                return migrated;
            }

            return defaultValue;
        }

        internal static void SetBool(string baseKey, bool value)
        {
            EditorPrefs.SetBool(ResolveStorageKey(baseKey), value);
        }

        internal static void SetInt(string baseKey, int value)
        {
            EditorPrefs.SetInt(ResolveStorageKey(baseKey), value);
        }

        internal static void SetString(string baseKey, string value)
        {
            EditorPrefs.SetString(ResolveStorageKey(baseKey), value);
        }

        internal static void DeleteKey(string baseKey)
        {
            EditorPrefs.DeleteKey(ResolveStorageKey(baseKey));
        }

        private static bool TryClaimLegacyPreference(string projectHash)
        {
            string hash = string.IsNullOrWhiteSpace(projectHash)
                ? ProjectIdentityUtility.GetProjectHash()
                : projectHash;
            string ownerKey = GetMigrationOwnerKey();

            Mutex migrationMutex = null;
            bool lockTaken = false;
            try
            {
                migrationMutex = new Mutex(false, MigrationMutexName);
                try
                {
                    lockTaken = migrationMutex.WaitOne(TimeSpan.FromSeconds(2));
                }
                catch (AbandonedMutexException)
                {
                    lockTaken = true;
                }

                if (!lockTaken)
                {
                    return false;
                }

                string owner = EditorPrefs.GetString(ownerKey, string.Empty);
                if (string.IsNullOrEmpty(owner))
                {
                    EditorPrefs.SetString(ownerKey, hash);
                    owner = EditorPrefs.GetString(ownerKey, string.Empty);
                }

                return string.Equals(owner, hash, StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
            finally
            {
                if (lockTaken)
                {
                    try { migrationMutex.ReleaseMutex(); } catch { }
                }

                migrationMutex?.Dispose();
            }
        }
    }
}
