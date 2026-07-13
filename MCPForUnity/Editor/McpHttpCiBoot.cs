using System;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Services.Transport;
using UnityEditor;

namespace MCPForUnity.Editor
{
    // HTTP is a plugin-hub pull model: the bridge waits at ws://127.0.0.1:<port>/hub/plugin
    // and the editor must dial in. StartStdioForCi forces stdio and only listens, so an
    // editor booted that way never registers and every call returns no_unity_session.
    //
    // The port comes from UNITY_MCP_HTTP_PORT rather than a pref because sibling checkouts
    // of one project share an EditorPrefs file, so a persisted HttpUrl is clobbered by
    // whichever editor wrote last.
    public static class McpHttpCiBoot
    {
        private const string PortEnv = "UNITY_MCP_HTTP_PORT";

        // Re-asserted on every domain load so a reload cannot resume a sibling's port
        [InitializeOnLoadMethod]
        private static void ReassertEndpointFromEnv()
        {
            ApplyEndpointFromEnv();
        }

        public static void StartHttpForCi()
        {
            if (!ApplyEndpointFromEnv())
            {
                McpLog.Error($"[MCPForUnity] StartHttpForCi: {PortEnv} not set or invalid; cannot start HTTP transport");
                return;
            }

            // Defer the connect so MCPServiceLocator and friends are initialized.
            EditorApplication.delayCall += () =>
            {
                _ = MCPServiceLocator.TransportManager.StartAsync(TransportMode.Http);
            };
        }

        private static bool ApplyEndpointFromEnv()
        {
            string portStr = Environment.GetEnvironmentVariable(PortEnv);
            if (string.IsNullOrWhiteSpace(portStr) || !int.TryParse(portStr, out int port) || port <= 0)
            {
                return false;
            }

            EditorPrefs.SetBool(EditorPrefKeys.UseHttpTransport, true);
            EditorPrefs.SetString(EditorPrefKeys.HttpTransportScope, "local");
            EditorPrefs.SetString(EditorPrefKeys.HttpBaseUrl, $"http://127.0.0.1:{port}");
            return true;
        }
    }
}
