using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Models;

namespace MCPForUnity.Editor.Helpers
{
    public static class ConfigJsonBuilder
    {
        public static string BuildManualConfigJson(string uvPath, string pythonDir, McpClient client)
        {
            var root = new JObject();
            bool isVSCode = client?.mcpType == McpTypes.VSCode;
            JObject container;
            if (isVSCode)
            {
                container = EnsureObject(root, "servers");
            }
            else
            {
                container = EnsureObject(root, "mcpServers");
            }

            var unity = new JObject();
            PopulateUnityNode(unity, uvPath, pythonDir, client, isVSCode);

            container["unityMCP"] = unity;

            return root.ToString(Formatting.Indented);
        }

        public static JObject ApplyUnityServerToExistingConfig(JObject root, string uvPath, string serverSrc, McpClient client)
        {
            if (root == null) root = new JObject();
            bool isVSCode = client?.mcpType == McpTypes.VSCode;
            JObject container = isVSCode ? EnsureObject(root, "servers") : EnsureObject(root, "mcpServers");
            JObject unity = container["unityMCP"] as JObject ?? new JObject();
            PopulateUnityNode(unity, uvPath, serverSrc, client, isVSCode);

            container["unityMCP"] = unity;
            return root;
        }

        /// <summary>
        /// Checks if we should use remote uvx (Asset Store install without embedded server)
        /// </summary>
        private static bool ShouldUseRemoteUvx(string directory)
        {
            // If no directory provided, use remote
            if (string.IsNullOrEmpty(directory))
                return true;

            // If no embedded server exists, use remote
            if (!ServerInstaller.HasEmbeddedServer())
                return true;

            return false;
        }

        /// <summary>
        /// Centralized builder that applies all caveats consistently.
        /// - Sets command/args with provided directory OR remote uvx for Asset Store
        /// - Ensures env exists
        /// - Adds type:"stdio" for VSCode
        /// - Adds disabled:false for Windsurf/Kiro only when missing
        /// </summary>
        private static void PopulateUnityNode(JObject unity, string uvPath, string directory, McpClient client, bool isVSCode)
        {
            // Check if we should use remote uvx (Asset Store without embedded server)
            bool useRemote = ShouldUseRemoteUvx(directory);

            if (useRemote)
            {
                // Asset Store install - use remote uvx
                string version = AssetPathUtility.GetPackageVersion();
                string remoteUrl = $"git+https://github.com/CoplayDev/unity-mcp@v{version}#subdirectory=MCPForUnity/UnityMcpServer~/src";
                
                unity["command"] = "uvx";
                unity["args"] = JArray.FromObject(new[] { "--from", remoteUrl, "mcp-for-unity" });
            }
            else
            {
                // Git/embedded install - use local path
                unity["command"] = uvPath;

                // For Cursor (non-VSCode) on macOS, prefer a no-spaces symlink path to avoid arg parsing issues in some runners
                string effectiveDir = directory;
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            bool isCursor = !isVSCode && (client == null || client.mcpType != McpTypes.VSCode);
            if (isCursor && !string.IsNullOrEmpty(directory))
            {
                // Replace canonical path segment with the symlink path if present
                const string canonical = "/Library/Application Support/";
                const string symlinkSeg = "/Library/AppSupport/";
                try
                {
                    // Normalize to full path style
                    if (directory.Contains(canonical))
                    {
                        var candidate = directory.Replace(canonical, symlinkSeg).Replace('\\', '/');
                        if (System.IO.Directory.Exists(candidate))
                        {
                            effectiveDir = candidate;
                        }
                    }
                    else
                    {
                        // If installer returned XDG-style on macOS, map to canonical symlink
                        string norm = directory.Replace('\\', '/');
                        int idx = norm.IndexOf("/.local/share/UnityMCP/", System.StringComparison.Ordinal);
                        if (idx >= 0)
                        {
                            string home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Personal) ?? string.Empty;
                            string suffix = norm.Substring(idx + "/.local/share/".Length); // UnityMCP/...
                            string candidate = System.IO.Path.Combine(home, "Library", "AppSupport", suffix).Replace('\\', '/');
                            if (System.IO.Directory.Exists(candidate))
                            {
                                effectiveDir = candidate;
                            }
                        }
                    }
                }
                catch { /* fallback to original directory on any error */ }
            }
#endif

                unity["args"] = JArray.FromObject(new[] { "run", "--directory", effectiveDir, "server.py" });
            }

            if (isVSCode)
            {
                unity["type"] = "stdio";
            }
            else
            {
                // Remove type if it somehow exists from previous clients
                if (unity["type"] != null) unity.Remove("type");
            }

            if (client != null && (client.mcpType == McpTypes.Windsurf || client.mcpType == McpTypes.Kiro))
            {
                if (unity["env"] == null)
                {
                    unity["env"] = new JObject();
                }

                if (unity["disabled"] == null)
                {
                    unity["disabled"] = false;
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
    }
}
