using System;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Services.Transport;
using UnityEditor;

namespace MCPForUnity.Editor
{
    // HTTP is a plugin-hub pull model: the editor dials ws://127.0.0.1:<port>/hub/plugin
    // StartStdioForCi only listens, so an editor booted that way never registers
    public static class McpHttpCiBoot
    {
        private const string PortEnv = "UNITY_MCP_HTTP_PORT";

        // Never persisted: sibling checkouts share one EditorPrefs file, so the last writer wins
        public static bool TryGetCiPort(out int port)
        {
            port = 0;
            string raw = Environment.GetEnvironmentVariable(PortEnv);
            return !string.IsNullOrWhiteSpace(raw)
                   && int.TryParse(raw, out port)
                   && port > 0
                   && port <= 65535;
        }

        public static void StartHttpForCi()
        {
            if (!TryGetCiPort(out _))
            {
                McpLog.Error($"[MCPForUnity] StartHttpForCi: {PortEnv} not set or invalid; cannot start HTTP transport");
                return;
            }

            // Defer the connect so MCPServiceLocator and friends are initialized.
            EditorApplication.delayCall += async () =>
            {
                try
                {
                    if (!await MCPServiceLocator.TransportManager.StartAsync(TransportMode.Http))
                        McpLog.Error("[MCPForUnity] StartHttpForCi: HTTP transport failed to start");
                }
                catch (Exception e)
                {
                    McpLog.Error($"[MCPForUnity] StartHttpForCi: HTTP transport threw on start: {e}");
                }
            };
        }
    }
}
