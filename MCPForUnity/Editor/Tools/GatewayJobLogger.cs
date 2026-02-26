using System;
using System.IO;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Logs completed gateway jobs to a JSON-lines file when logging is enabled.
    /// Default log file: <c>{ProjectRoot}/Logs/mcp-gateway-jobs.jsonl</c>
    /// Path is configurable via EditorPrefs.
    /// </summary>
    internal static class GatewayJobLogger
    {
        /// <summary>
        /// Default log path relative to project root.
        /// </summary>
        public static readonly string DefaultLogPath = Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", "Logs", "mcp-gateway-jobs.jsonl"));

        /// <summary>
        /// Returns true when gateway job logging is enabled via EditorPrefs.
        /// </summary>
        public static bool IsEnabled
        {
            get => EditorPrefs.GetBool(EditorPrefKeys.GatewayJobLogging, false);
            set => EditorPrefs.SetBool(EditorPrefKeys.GatewayJobLogging, value);
        }

        /// <summary>
        /// The current log file path. Returns the EditorPrefs override if set,
        /// otherwise the default path.
        /// </summary>
        public static string LogPath
        {
            get
            {
                string custom = EditorPrefs.GetString(EditorPrefKeys.GatewayJobLogPath, "");
                return string.IsNullOrWhiteSpace(custom) ? DefaultLogPath : custom;
            }
            set => EditorPrefs.SetString(EditorPrefKeys.GatewayJobLogPath, value ?? "");
        }

        /// <summary>
        /// Write a completed job to the log file as a single JSON line.
        /// </summary>
        public static void Log(BatchJob job)
        {
            if (job == null) return;

            try
            {
                var entry = new JObject
                {
                    ["ticket"] = job.Ticket,
                    ["agent"] = job.Agent,
                    ["label"] = job.Label,
                    ["tier"] = job.Tier.ToString().ToLowerInvariant(),
                    ["status"] = job.Status.ToString().ToLowerInvariant(),
                    ["atomic"] = job.Atomic,
                    ["created_at"] = job.CreatedAt.ToString("O"),
                    ["completed_at"] = job.CompletedAt?.ToString("O"),
                    ["command_count"] = job.Commands?.Count ?? 0,
                    ["current_index"] = job.CurrentIndex
                };

                if (!string.IsNullOrEmpty(job.Error))
                    entry["error"] = job.Error;

                if (job.Commands != null && job.Commands.Count > 0)
                {
                    var tools = new JArray();
                    foreach (var cmd in job.Commands)
                        tools.Add(cmd.Tool);
                    entry["tools"] = tools;
                }

                string path = LogPath;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.AppendAllText(path, entry.ToString(Formatting.None) + "\n");
            }
            catch (Exception ex)
            {
                McpLog.Debug($"[GatewayJobLogger] Failed to log job {job.Ticket}: {ex.Message}");
            }
        }
    }
}
