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
    /// Log file: <c>{ProjectRoot}/Logs/mcp-gateway-jobs.jsonl</c>
    /// </summary>
    internal static class GatewayJobLogger
    {
        static readonly string LogPath = Path.Combine(
            Application.dataPath, "..", "Logs", "mcp-gateway-jobs.jsonl");

        /// <summary>
        /// Returns true when gateway job logging is enabled via EditorPrefs.
        /// </summary>
        public static bool IsEnabled =>
            EditorPrefs.GetBool(EditorPrefKeys.GatewayJobLogging, false);

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

                string dir = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.AppendAllText(LogPath, entry.ToString(Formatting.None) + "\n");
            }
            catch (Exception ex)
            {
                McpLog.Debug($"[GatewayJobLogger] Failed to log job {job.Ticket}: {ex.Message}");
            }
        }
    }
}
