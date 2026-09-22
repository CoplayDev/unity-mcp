using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Services.Server
{
    /// <summary>
    /// Manages PID files and handshake state for the local HTTP server.
    /// EditorPrefs are per user, not per editor, so every key is suffixed with the project hash and
    /// port; the "this editor process launched it" marker lives in SessionState, which survives
    /// domain reloads and dies with the process.
    /// </summary>
    public class PidFileManager : IPidFileManager
    {
        internal const string LaunchedPortSessionKey = "MCPForUnity.LocalHttpServer.LaunchedPort";

        /// <inheritdoc/>
        public string GetPidDirectory()
        {
            return Path.Combine(GetProjectRootPath(), "Library", "MCPForUnity", "RunState");
        }

        /// <inheritdoc/>
        public string GetPidFilePath(int port)
        {
            string dir = GetPidDirectory();
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, $"mcp_http_{port}.pid");
        }

        /// <inheritdoc/>
        public bool TryReadPid(string pidFilePath, out int pid)
        {
            pid = 0;
            try
            {
                if (string.IsNullOrEmpty(pidFilePath) || !File.Exists(pidFilePath))
                {
                    return false;
                }

                string text = File.ReadAllText(pidFilePath).Trim();
                if (int.TryParse(text, out pid))
                {
                    return pid > 0;
                }

                // Best-effort: tolerate accidental extra whitespace/newlines.
                var firstLine = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (int.TryParse(firstLine, out pid))
                {
                    return pid > 0;
                }

                pid = 0;
                return false;
            }
            catch
            {
                pid = 0;
                return false;
            }
        }

        /// <inheritdoc/>
        public void DeletePidFile(string pidFilePath)
        {
            try
            {
                if (!string.IsNullOrEmpty(pidFilePath) && File.Exists(pidFilePath))
                {
                    File.Delete(pidFilePath);
                }
            }
            catch { }
        }

        /// <inheritdoc/>
        public void StoreHandshake(int port, string pidFilePath, string instanceToken)
        {
            if (port <= 0)
            {
                return;
            }

            try
            {
                if (!string.IsNullOrEmpty(pidFilePath))
                {
                    EditorPrefs.SetString(Key(EditorPrefKeys.LocalHttpServerPidFilePath, port), pidFilePath);
                }
            }
            catch { }

            try
            {
                if (!string.IsNullOrEmpty(instanceToken))
                {
                    EditorPrefs.SetString(Key(EditorPrefKeys.LocalHttpServerInstanceToken, port), instanceToken);
                }
            }
            catch { }

            try { SessionState.SetInt(LaunchedPortSessionKey, port); } catch { }
        }

        /// <inheritdoc/>
        public bool TryGetHandshake(int port, out string pidFilePath, out string instanceToken)
        {
            pidFilePath = null;
            instanceToken = null;
            if (port <= 0)
            {
                return false;
            }

            try
            {
                pidFilePath = EditorPrefs.GetString(Key(EditorPrefKeys.LocalHttpServerPidFilePath, port), string.Empty);
                instanceToken = EditorPrefs.GetString(Key(EditorPrefKeys.LocalHttpServerInstanceToken, port), string.Empty);
                if (string.IsNullOrEmpty(pidFilePath) || string.IsNullOrEmpty(instanceToken))
                {
                    pidFilePath = null;
                    instanceToken = null;
                    return false;
                }
                return true;
            }
            catch
            {
                pidFilePath = null;
                instanceToken = null;
                return false;
            }
        }

        /// <inheritdoc/>
        public bool TryGetLaunchedPort(out int port)
        {
            port = 0;
            try { port = SessionState.GetInt(LaunchedPortSessionKey, 0); } catch { port = 0; }
            return port > 0;
        }

        /// <inheritdoc/>
        public void StoreTracking(int pid, int port, string argsHash = null)
        {
            if (port <= 0)
            {
                return;
            }

            try { EditorPrefs.SetInt(Key(EditorPrefKeys.LocalHttpServerPid, port), pid); } catch { }
            try { EditorPrefs.SetString(Key(EditorPrefKeys.LocalHttpServerStartedUtc, port), DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)); } catch { }
            try
            {
                if (!string.IsNullOrEmpty(argsHash))
                {
                    EditorPrefs.SetString(Key(EditorPrefKeys.LocalHttpServerPidArgsHash, port), argsHash);
                }
                else
                {
                    EditorPrefs.DeleteKey(Key(EditorPrefKeys.LocalHttpServerPidArgsHash, port));
                }
            }
            catch { }
        }

        /// <inheritdoc/>
        public bool TryGetStoredPid(int expectedPort, out int pid)
        {
            pid = 0;
            if (expectedPort <= 0)
            {
                return false;
            }

            try
            {
                int storedPid = EditorPrefs.GetInt(Key(EditorPrefKeys.LocalHttpServerPid, expectedPort), 0);
                string storedUtc = EditorPrefs.GetString(Key(EditorPrefKeys.LocalHttpServerStartedUtc, expectedPort), string.Empty);

                if (storedPid <= 0)
                {
                    return false;
                }

                // Only trust the stored PID for a short window to avoid PID reuse issues.
                // (We still verify the PID is listening on the expected port before killing.)
                if (!string.IsNullOrEmpty(storedUtc)
                    && DateTime.TryParse(storedUtc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var startedAt))
                {
                    if ((DateTime.UtcNow - startedAt) > TimeSpan.FromHours(6))
                    {
                        return false;
                    }
                }

                pid = storedPid;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <inheritdoc/>
        public string GetStoredArgsHash(int port)
        {
            try
            {
                return EditorPrefs.GetString(Key(EditorPrefKeys.LocalHttpServerPidArgsHash, port), string.Empty);
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <inheritdoc/>
        public void ClearTracking(int port)
        {
            if (port <= 0)
            {
                return;
            }

            try { EditorPrefs.DeleteKey(Key(EditorPrefKeys.LocalHttpServerPid, port)); } catch { }
            try { EditorPrefs.DeleteKey(Key(EditorPrefKeys.LocalHttpServerStartedUtc, port)); } catch { }
            try { EditorPrefs.DeleteKey(Key(EditorPrefKeys.LocalHttpServerPidArgsHash, port)); } catch { }
            try { EditorPrefs.DeleteKey(Key(EditorPrefKeys.LocalHttpServerPidFilePath, port)); } catch { }
            try { EditorPrefs.DeleteKey(Key(EditorPrefKeys.LocalHttpServerInstanceToken, port)); } catch { }
            try
            {
                if (SessionState.GetInt(LaunchedPortSessionKey, 0) == port)
                {
                    SessionState.EraseInt(LaunchedPortSessionKey);
                }
            }
            catch { }
        }

        private static string Key(string baseKey, int port)
        {
            return $"{baseKey}.{ProjectIdentityUtility.GetProjectHash()}.{port}";
        }

        /// <inheritdoc/>
        public string ComputeShortHash(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            try
            {
                using var sha = SHA256.Create();
                byte[] bytes = Encoding.UTF8.GetBytes(input);
                byte[] hash = sha.ComputeHash(bytes);
                // 8 bytes => 16 hex chars is plenty as a stable fingerprint for our purposes.
                var sb = new StringBuilder(16);
                for (int i = 0; i < 8 && i < hash.Length; i++)
                {
                    sb.Append(hash[i].ToString("x2"));
                }
                return sb.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetProjectRootPath()
        {
            try
            {
                // Application.dataPath is ".../<Project>/Assets"
                return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            }
            catch
            {
                return Application.dataPath;
            }
        }
    }
}
