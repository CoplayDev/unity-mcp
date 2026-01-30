using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Helpers
{
    public static class ConfigJsonBuilder
    {
        public static string BuildManualConfigJson(string uvPath, McpClient client)
        {
            var root = new JObject();
            bool isVSCode = client?.IsVsCodeLayout == true;
            JObject container = isVSCode ? EnsureObject(root, "servers") : EnsureObject(root, "mcpServers");

            var unity = new JObject();
            PopulateUnityNode(unity, uvPath, client, isVSCode);

            container["unityMCP"] = unity;

            return root.ToString(Formatting.Indented);
        }

        public static JObject ApplyUnityServerToExistingConfig(JObject root, string uvPath, McpClient client)
        {
            if (root == null) root = new JObject();
            bool isVSCode = client?.IsVsCodeLayout == true;
            JObject container = isVSCode ? EnsureObject(root, "servers") : EnsureObject(root, "mcpServers");
            JObject unity = container["unityMCP"] as JObject ?? new JObject();
            PopulateUnityNode(unity, uvPath, client, isVSCode);

            container["unityMCP"] = unity;
            return root;
        }

        /// <summary>
        /// Centralized builder that applies all caveats consistently.
        /// - Sets command/args with uvx and package version
        /// - Ensures env exists
        /// - Adds transport configuration (HTTP or stdio)
        /// - Adds disabled:false for Windsurf/Kiro only when missing
        /// </summary>
        private static void PopulateUnityNode(JObject unity, string uvPath, McpClient client, bool isVSCode)
        {
            // Get transport preference (default to HTTP)
            bool prefValue = EditorConfigurationCache.Instance.UseHttpTransport;
            bool clientSupportsHttp = client?.SupportsHttpTransport != false;
            bool useHttpTransport = clientSupportsHttp && prefValue;
            string httpProperty = string.IsNullOrEmpty(client?.HttpUrlProperty) ? "url" : client.HttpUrlProperty;
            var urlPropsToRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "url", "serverUrl" };
            urlPropsToRemove.Remove(httpProperty);

            if (useHttpTransport)
            {
                // HTTP mode: Use URL, no command
                string httpUrl = HttpEndpointUtility.GetMcpRpcUrl();
                unity[httpProperty] = httpUrl;

                foreach (var prop in urlPropsToRemove)
                {
                    if (unity[prop] != null) unity.Remove(prop);
                }

                // Remove command/args if they exist from previous config
                if (unity["command"] != null) unity.Remove("command");
                if (unity["args"] != null) unity.Remove("args");

                // Only include API key header for remote-hosted mode
                if (HttpEndpointUtility.IsRemoteScope())
                {
                    string apiKey = EditorPrefs.GetString(EditorPrefKeys.ApiKey, string.Empty);
                    if (!string.IsNullOrEmpty(apiKey))
                    {
                        var headers = new JObject { [AuthConstants.ApiKeyHeader] = apiKey };
                        unity["headers"] = headers;
                    }
                    else
                    {
                        if (unity["headers"] != null) unity.Remove("headers");
                    }
                }
                else
                {
                    // Local HTTP doesn't use API keys; remove any stale headers
                    if (unity["headers"] != null) unity.Remove("headers");
                }

                if (isVSCode)
                {
                    unity["type"] = "http";
                }
                // Also add type for Claude Code (uses mcpServers layout but needs type field)
                else if (client?.name == "Claude Code")
                {
                    unity["type"] = "http";
                }
            }
            else
            {
                // Stdio mode: Use uvx command
                var (uvxPath, fromUrl, packageName) = AssetPathUtility.GetUvxCommandParts();

                var toolArgs = BuildUvxArgs(fromUrl, packageName);

                unity["command"] = uvxPath;
                unity["args"] = JArray.FromObject(toolArgs.ToArray());

                // Remove url/serverUrl if they exist from previous config
                if (unity["url"] != null) unity.Remove("url");
                if (unity["serverUrl"] != null) unity.Remove("serverUrl");

                if (isVSCode)
                {
                    unity["type"] = "stdio";
                }
            }

            // Remove type for non-VSCode clients (except Claude Code which needs it)
            if (!isVSCode && client?.name != "Claude Code" && unity["type"] != null)
            {
                unity.Remove("type");
            }

            bool requiresEnv = client?.EnsureEnvObject == true;
            bool stripEnv = client?.StripEnvWhenNotRequired == true;

            if (requiresEnv)
            {
                if (unity["env"] == null)
                {
                    unity["env"] = new JObject();
                }
            }
            else if (stripEnv && unity["env"] != null)
            {
                unity.Remove("env");
            }

            if (client?.DefaultUnityFields != null)
            {
                foreach (var kvp in client.DefaultUnityFields)
                {
                    if (unity[kvp.Key] == null)
                    {
                        unity[kvp.Key] = kvp.Value != null ? JToken.FromObject(kvp.Value) : JValue.CreateNull();
                    }
                }
            }
        }

        private static JObject EnsureObject(JObject parent, string name)
        {
            if (parent[name] is JObject o) return o;
            var created = new JObject();
            parent[name] = created;
            return created;
        }

        private static IList<string> BuildUvxArgs(string fromUrl, string packageName)
        {
            // Dev mode: force a fresh install/resolution (avoids stale cached builds while iterating).
            // `--no-cache` avoids reading from cache; `--refresh` ensures metadata is revalidated.
            // Note: --reinstall is not supported by uvx and will cause a warning.
            // Keep ordering consistent with other uvx builders: dev flags first, then --from <url>, then package name.
            var args = new List<string>();

            // Use central helper that checks both DevModeForceServerRefresh AND local path detection.
            if (AssetPathUtility.ShouldForceUvxRefresh())
            {
                args.Add("--no-cache");
                args.Add("--refresh");
            }

            // Use centralized helper for beta server / prerelease args
            foreach (var arg in AssetPathUtility.GetBetaServerFromArgsList())
            {
                args.Add(arg);
            }
            args.Add(packageName);

            args.Add("--transport");
            args.Add("stdio");

            return args;
        }

    }
}
