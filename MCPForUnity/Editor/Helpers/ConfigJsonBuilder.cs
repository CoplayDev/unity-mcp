using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Clients.Configurators;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Helpers
{
    public static class ConfigJsonBuilder
    {
        private const string AuthTokenInputKey = "Authorization";
        public static string BuildManualConfigJson(string uvPath, McpClient client)
        {
            var root = new JObject();
            bool isVSCode = client?.IsVsCodeLayout == true;
            JObject container = isVSCode ? EnsureObject(root, "servers") : EnsureObject(root, "mcpServers");

            var unity = new JObject();
            PopulateUnityNode(root, unity, uvPath, client, isVSCode);

            container["unityMCP"] = unity;

            return root.ToString(Formatting.Indented);
        }

        public static JObject ApplyUnityServerToExistingConfig(JObject root, string uvPath, McpClient client)
        {
            if (root == null) root = new JObject();
            bool isVSCode = client?.IsVsCodeLayout == true;
            JObject container = isVSCode ? EnsureObject(root, "servers") : EnsureObject(root, "mcpServers");
            JObject unity = container["unityMCP"] as JObject ?? new JObject();
            PopulateUnityNode(root, unity, uvPath, client, isVSCode);

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
        private static void PopulateUnityNode(JObject root, JObject unity, string uvPath, McpClient client, bool isVSCode)
        {
            // Get transport preference (default to HTTP)
            bool useHttpTransport = client?.SupportsHttpTransport != false && EditorPrefs.GetBool(EditorPrefKeys.UseHttpTransport, true);
            string httpProperty = string.IsNullOrEmpty(client?.HttpUrlProperty) ? "url" : client.HttpUrlProperty;
            var urlPropsToRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "url", "serverUrl" };
            urlPropsToRemove.Remove(httpProperty);

            if (useHttpTransport)
            {
                // HTTP mode: Use URL, no command
                string httpUrl = HttpEndpointUtility.GetMcpRpcUrl();
                unity[httpProperty] = httpUrl;

                bool authEnabled = AuthPreferencesUtility.GetAuthEnabled();
                if (authEnabled)
                {
                    // Align with GitHub-style input binding: Authorization header references an input
                    var headers = unity["headers"] as JObject ?? new JObject();
                    headers["Authorization"] = "${input:Authorization}";
                    unity["headers"] = headers;

                    // VS Code-style inputs array so ${input:Authorization} can be resolved
                    var inputs = root["inputs"] as JArray ?? new JArray();
                    var existing = inputs
                        .OfType<JObject>()
                        .FirstOrDefault(o => string.Equals((string)o["id"], AuthTokenInputKey, StringComparison.Ordinal));
                    if (existing == null)
                    {
                        existing = new JObject();
                        inputs.Add(existing);
                    }

                    existing["id"] = AuthTokenInputKey;
                    existing["type"] = "promptString";
                    existing["description"] = "Authorization header (e.g., Bearer <token>)";
                    existing["password"] = true;

                    root["inputs"] = inputs;
                }
                else
                {
                    if (unity["headers"] is JObject headers && headers.ContainsKey("Authorization"))
                    {
                        headers.Remove("Authorization");
                        if (!headers.Properties().Any())
                        {
                            unity.Remove("headers");
                        }
                        else
                        {
                            unity["headers"] = headers;
                        }
                    }

                    if (root["inputs"] is JArray inputs)
                    {
                        var remaining = new JArray(inputs
                            .OfType<JObject>()
                            .Where(o => !string.Equals((string)o["id"], AuthTokenInputKey, StringComparison.Ordinal))
                            .Select(o => (JToken)o));

                        if (!remaining.Any())
                        {
                            root.Remove("inputs");
                        }
                        else
                        {
                            root["inputs"] = remaining;
                        }
                    }
                }

                foreach (var prop in urlPropsToRemove)
                {
                    if (unity[prop] != null) unity.Remove(prop);
                }

                // Remove command/args if they exist from previous config
                if (unity["command"] != null) unity.Remove("command");
                if (unity["args"] != null) unity.Remove("args");

                if (isVSCode)
                {
                    unity["type"] = "http";
                }
            }
            else
            {
                // Stdio mode: Use uvx command
                var (uvxPath, fromUrl, packageName) = AssetPathUtility.GetUvxCommandParts();

                var toolArgs = BuildUvxArgs(fromUrl, packageName);

                if (ShouldUseWindowsCmdShim(client))
                {
                    unity["command"] = ResolveCmdPath();

                    var cmdArgs = new List<string> { "/c", uvxPath };
                    cmdArgs.AddRange(toolArgs);

                    unity["args"] = JArray.FromObject(cmdArgs.ToArray());
                }
                else
                {
                    unity["command"] = uvxPath;
                    unity["args"] = JArray.FromObject(toolArgs.ToArray());
                }

                // Remove url/serverUrl if they exist from previous config
                if (unity["url"] != null) unity.Remove("url");
                if (unity["serverUrl"] != null) unity.Remove("serverUrl");

                if (isVSCode)
                {
                    unity["type"] = "stdio";
                }
            }

            // Remove type for non-VSCode clients
            if (!isVSCode && unity["type"] != null)
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
            var args = new List<string> { packageName };

            if (!string.IsNullOrEmpty(fromUrl))
            {
                args.Insert(0, fromUrl);
                args.Insert(0, "--from");
            }

            args.Add("--transport");
            args.Add("stdio");

            return args;
        }

        private static bool ShouldUseWindowsCmdShim(McpClient client)
        {
            if (client == null)
            {
                return false;
            }

            return Application.platform == RuntimePlatform.WindowsEditor &&
                   string.Equals(client.name, ClaudeDesktopConfigurator.ClientName, StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveCmdPath()
        {
            var comSpec = Environment.GetEnvironmentVariable("ComSpec");
            if (!string.IsNullOrEmpty(comSpec) && File.Exists(comSpec))
            {
                return comSpec;
            }

            string system32Cmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
            return File.Exists(system32Cmd) ? system32Cmd : "cmd.exe";
        }
    }
}
