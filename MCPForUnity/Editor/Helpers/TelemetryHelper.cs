using System;
using System.Collections.Generic;
using System.Threading;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Services.Transport.Transports;
using UnityEngine;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Unity Bridge telemetry helper for collecting usage analytics
    /// Following privacy-first approach with easy opt-out mechanisms
    /// </summary>
    public static class TelemetryHelper
    {
        private const string TELEMETRY_DISABLED_KEY = EditorPrefKeys.TelemetryDisabled;
        private const string CUSTOMER_UUID_KEY = EditorPrefKeys.CustomerUuid;
        private static Action<Dictionary<string, object>> s_sender;

        /// <summary>
        /// Check if telemetry is enabled (can be disabled via Environment Variable or EditorPrefs)
        /// </summary>
        public static bool IsEnabled
        {
            get
            {
                // Check environment variables first
                var envDisable = Environment.GetEnvironmentVariable("DISABLE_TELEMETRY");
                if (!string.IsNullOrEmpty(envDisable) &&
                    (envDisable.ToLower() == "true" || envDisable == "1"))
                {
                    return false;
                }

                var unityMcpDisable = Environment.GetEnvironmentVariable("UNITY_MCP_DISABLE_TELEMETRY");
                if (!string.IsNullOrEmpty(unityMcpDisable) &&
                    (unityMcpDisable.ToLower() == "true" || unityMcpDisable == "1"))
                {
                    return false;
                }

                // Honor protocol-wide opt-out as well
                var mcpDisable = Environment.GetEnvironmentVariable("MCP_DISABLE_TELEMETRY");
                if (!string.IsNullOrEmpty(mcpDisable) &&
                    (mcpDisable.Equals("true", StringComparison.OrdinalIgnoreCase) || mcpDisable == "1"))
                {
                    return false;
                }

                // Check EditorPrefs
                return !UnityEditor.EditorPrefs.GetBool(TELEMETRY_DISABLED_KEY, false);
            }
        }

        /// <summary>
        /// Get or generate customer UUID for anonymous tracking
        /// </summary>
        public static string GetCustomerUUID()
        {
            var uuid = UnityEditor.EditorPrefs.GetString(CUSTOMER_UUID_KEY, "");
            if (string.IsNullOrEmpty(uuid))
            {
                uuid = System.Guid.NewGuid().ToString();
                UnityEditor.EditorPrefs.SetString(CUSTOMER_UUID_KEY, uuid);
            }
            return uuid;
        }

        /// <summary>
        /// Disable telemetry (stored in EditorPrefs)
        /// </summary>
        public static void DisableTelemetry()
        {
            UnityEditor.EditorPrefs.SetBool(TELEMETRY_DISABLED_KEY, true);
        }

        /// <summary>
        /// Enable telemetry (stored in EditorPrefs)
        /// </summary>
        public static void EnableTelemetry()
        {
            UnityEditor.EditorPrefs.SetBool(TELEMETRY_DISABLED_KEY, false);
        }

        /// <summary>
        /// Send telemetry data to MCP server for processing
        /// This is a lightweight bridge - the actual telemetry logic is in the MCP server
        /// </summary>
        public static void RecordEvent(string eventType, Dictionary<string, object> data = null)
        {
            if (!IsEnabled)
                return;

            try
            {
                var telemetryData = new Dictionary<string, object>
                {
                    ["event_type"] = eventType,
                    ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    ["customer_uuid"] = GetCustomerUUID(),
                    ["unity_version"] = Application.unityVersion,
                    ["platform"] = Application.platform.ToString(),
                    ["source"] = "unity_bridge"
                };

                if (data != null)
                {
                    telemetryData["data"] = data;
                }

                // Send to MCP server via existing bridge communication
                // The MCP server will handle actual telemetry transmission
                SendTelemetryToMcpServer(telemetryData);
            }
            catch (Exception e)
            {
                // Never let telemetry errors interfere with functionality
                if (IsDebugEnabled())
                {
                    McpLog.Warn($"Telemetry error (non-blocking): {e.Message}");
                }
            }
        }

        /// <summary>
        /// Allows the bridge to register a concrete sender for telemetry payloads.
        /// </summary>
        public static void RegisterTelemetrySender(Action<Dictionary<string, object>> sender)
        {
            Interlocked.Exchange(ref s_sender, sender);
        }

        public static void UnregisterTelemetrySender()
        {
            Interlocked.Exchange(ref s_sender, null);
        }

        /// <summary>
        /// Record bridge startup event
        /// </summary>
        public static void RecordBridgeStartup()
        {
            RecordEvent("bridge_startup", new Dictionary<string, object>
            {
                ["bridge_version"] = AssetPathUtility.GetPackageVersion(),
                ["auto_connect"] = StdioBridgeHost.IsAutoConnectMode()
            });
        }

        /// <summary>
        /// Record bridge connection event
        /// </summary>
        public static void RecordBridgeConnection(bool success, string error = null)
        {
            var data = new Dictionary<string, object>
            {
                ["success"] = success
            };

            if (!string.IsNullOrEmpty(error))
            {
                data["error"] = error.Substring(0, Math.Min(200, error.Length));
            }

            RecordEvent("bridge_connection", data);
        }

        /// <summary>
        /// Record tool execution from Unity side
        /// </summary>
        public static void RecordToolExecution(string toolName, bool success, float durationMs, string error = null)
        {
            var data = new Dictionary<string, object>
            {
                ["tool_name"] = toolName,
                ["success"] = success,
                ["duration_ms"] = Math.Round(durationMs, 2)
            };

            if (!string.IsNullOrEmpty(error))
            {
                data["error"] = error.Substring(0, Math.Min(200, error.Length));
            }

            RecordEvent("tool_execution_unity", data);
        }

        private static void SendTelemetryToMcpServer(Dictionary<string, object> telemetryData)
        {
            var sender = Volatile.Read(ref s_sender);
            if (sender != null)
            {
                try
                {
                    sender(telemetryData);
                    return;
                }
                catch (Exception e)
                {
                    if (IsDebugEnabled())
                    {
                        McpLog.Warn($"Telemetry sender error (non-blocking): {e.Message}");
                    }
                }
            }

            // Fallback: log when debug is enabled
            if (IsDebugEnabled())
            {
                McpLog.Info($"Telemetry: {telemetryData["event_type"]}");
            }
        }

        private static bool IsDebugEnabled()
        {
            try
            {
                return UnityEditor.EditorPrefs.GetBool(EditorPrefKeys.DebugLogs, false);
            }
            catch
            {
                return false;
            }
        }
    }
}
