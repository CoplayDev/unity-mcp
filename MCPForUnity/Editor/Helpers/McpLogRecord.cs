using System;
using System.IO;
using MCPForUnity.Editor.Constants;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Helpers
{
    internal static class McpLogRecord
    {
        private static readonly string LogDir = Path.Combine(Application.dataPath, "UnityMCP", "Log");
        private static readonly string LogPath = Path.Combine(LogDir, "mcp.log");
        private static readonly string ErrorLogPath = Path.Combine(LogDir, "mcpError.log");
        private const long MaxLogSizeBytes = 1024 * 1024; // 1 MB
        private static bool _sessionStarted;
        private static readonly object _logLock = new();
        private static volatile bool _isEnabledCached;

        [InitializeOnLoadMethod]
        private static void RefreshFromPrefs()
        {
            _isEnabledCached = EditorPrefs.GetBool(EditorPrefKeys.LogRecordEnabled, false);
        }

        internal static bool IsEnabled
        {
            get => _isEnabledCached;
            set
            {
                EditorPrefs.SetBool(EditorPrefKeys.LogRecordEnabled, value);
                _isEnabledCached = value;
            }
        }

        internal static void Log(string commandType, JObject parameters, string type, string status, long durationMs, string error = null)
        {
            if (!IsEnabled) return;

            try
            {
                var entry = new JObject
                {
                    ["ts"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    ["tool"] = commandType,
                    ["type"] = type,
                    ["status"] = status,
                    ["ms"] = durationMs
                };

                var action = parameters?.Value<string>("action");
                if (!string.IsNullOrEmpty(action))
                    entry["action"] = action;

                if (parameters != null)
                    entry["params"] = parameters;

                if (error != null)
                    entry["error"] = error;

                var line = entry.ToString(Formatting.None);

                lock (_logLock)
                {
                    if (!_sessionStarted)
                    {
                        _sessionStarted = true;
                        var sessionEntry = new JObject
                        {
                            ["ts"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                            ["event"] = "session_start",
                            ["unity"] = Application.unityVersion
                        };
                        RotateAndAppend(LogPath, sessionEntry.ToString(Formatting.None));
                    }

                    RotateAndAppend(LogPath, line);

                    if (status == "ERROR")
                    {
                        RotateAndAppend(ErrorLogPath, line);
                    }
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"[McpLogRecord] Failed to write log: {ex.Message}");
            }
        }

        private static void RotateAndAppend(string path, string line)
        {
            Directory.CreateDirectory(LogDir);
            RotateIfNeeded(path);
            File.AppendAllText(path, line + Environment.NewLine);
        }

        private static void RotateIfNeeded(string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                var info = new FileInfo(path);
                if (info.Length <= MaxLogSizeBytes) return;

                var lines = File.ReadAllLines(path);
                var half = lines.Length / 2;
                File.WriteAllLines(path, lines[half..]);
            }
            catch
            {
                // Best-effort rotation
            }
        }
    }
}
