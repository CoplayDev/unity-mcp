using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MCPForUnity.External.Tommy;
using MCPForUnity.Editor.Services;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Codex CLI specific configuration helpers. Handles TOML snippet
    /// generation and lightweight parsing so Codex can join the auto-setup
    /// flow alongside JSON-based clients.
    /// </summary>
    public static class CodexConfigHelper
    {
        public static bool IsCodexConfigured(string pythonDir)
        {
            try
            {
                string basePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (string.IsNullOrEmpty(basePath)) return false;

                string configPath = Path.Combine(basePath, ".codex", "config.toml");
                if (!File.Exists(configPath)) return false;

                string toml = File.ReadAllText(configPath);
                if (!TryParseCodexServer(toml, out _, out var args)) return false;

                string dir = McpConfigurationHelper.ExtractDirectoryArg(args);
                if (string.IsNullOrEmpty(dir)) return false;

                return McpConfigurationHelper.PathsEqual(dir, pythonDir);
            }
            catch
            {
                return false;
            }
        }

        public static string BuildCodexServerBlock(string uvPath, string serverSrc)
        {
            var table = new TomlTable();
            var mcpServers = new TomlTable();

            mcpServers["unityMCP"] = CreateUnityMcpTable(uvPath, serverSrc);
            table["mcp_servers"] = mcpServers;

            using var writer = new StringWriter();
            table.WriteTo(writer);
            return writer.ToString();
        }

        public static string UpsertCodexServerBlock(string existingToml, string uvPath, string serverSrc)
        {
            // Parse existing TOML or create new root table
            var root = TryParseToml(existingToml) ?? new TomlTable();

            // Ensure mcp_servers table exists
            if (!root.TryGetNode("mcp_servers", out var mcpServersNode) || !(mcpServersNode is TomlTable))
            {
                root["mcp_servers"] = new TomlTable();
            }
            var mcpServers = root["mcp_servers"] as TomlTable;

            // Create or update unityMCP table
            mcpServers["unityMCP"] = CreateUnityMcpTable(uvPath, serverSrc);

            // Serialize back to TOML
            using var writer = new StringWriter();
            root.WriteTo(writer);
            return writer.ToString();
        }

        public static bool TryParseCodexServer(string toml, out string command, out string[] args)
        {
            command = null;
            args = null;

            var root = TryParseToml(toml);
            if (root == null) return false;

            if (!TryGetTable(root, "mcp_servers", out var servers)
                && !TryGetTable(root, "mcpServers", out servers))
            {
                return false;
            }

            if (!TryGetTable(servers, "unityMCP", out var unity))
            {
                return false;
            }

            command = GetTomlString(unity, "command");
            args = GetTomlStringArray(unity, "args");

            return !string.IsNullOrEmpty(command) && args != null;
        }

        /// <summary>
        /// Safely parses TOML string, returning null on failure
        /// </summary>
        private static TomlTable TryParseToml(string toml)
        {
            if (string.IsNullOrWhiteSpace(toml)) return null;

            try
            {
                using var reader = new StringReader(toml);
                return TOML.Parse(reader);
            }
            catch (TomlParseException)
            {
                return null;
            }
            catch (TomlSyntaxException)
            {
                return null;
            }
            catch (FormatException)
            {
                return null;
            }
        }

        /// <summary>
        /// Creates a TomlTable for the unityMCP server configuration
        /// </summary>
        /// <param name="uvPath">Path to uv executable</param>
        /// <param name="serverSrc">Path to server source directory</param>
        private static TomlTable CreateUnityMcpTable(string uvPath, string serverSrc)
        {
            var unityMCP = new TomlTable();
            unityMCP["command"] = new TomlString { Value = uvPath };

            var argsArray = new TomlArray();
            argsArray.Add(new TomlString { Value = "run" });
            argsArray.Add(new TomlString { Value = "--directory" });
            argsArray.Add(new TomlString { Value = serverSrc });
            argsArray.Add(new TomlString { Value = "server.py" });
            unityMCP["args"] = argsArray;

            // Add Windows-specific environment configuration, see: https://github.com/CoplayDev/unity-mcp/issues/315
            var platformService = MCPServiceLocator.Platform;
            if (platformService.IsWindows())
            {
                var envTable = new TomlTable { IsInline = true };
                envTable["SystemRoot"] = new TomlString { Value = platformService.GetSystemRoot() };
                unityMCP["env"] = envTable;
            }

            return unityMCP;
        }

        private static bool TryGetTable(TomlTable parent, string key, out TomlTable table)
        {
            table = null;
            if (parent == null) return false;

            if (parent.TryGetNode(key, out var node))
            {
                if (node is TomlTable tbl)
                {
                    table = tbl;
                    return true;
                }

                if (node is TomlArray array)
                {
                    var firstTable = array.Children.OfType<TomlTable>().FirstOrDefault();
                    if (firstTable != null)
                    {
                        table = firstTable;
                        return true;
                    }
                }
            }

            return false;
        }

        private static string GetTomlString(TomlTable table, string key)
        {
            if (table != null && table.TryGetNode(key, out var node))
            {
                if (node is TomlString str) return str.Value;
                if (node.HasValue) return node.ToString();
            }
            return null;
        }

        private static string[] GetTomlStringArray(TomlTable table, string key)
        {
            if (table == null) return null;
            if (!table.TryGetNode(key, out var node)) return null;

            if (node is TomlArray array)
            {
                List<string> values = new List<string>();
                foreach (TomlNode element in array.Children)
                {
                    if (element is TomlString str)
                    {
                        values.Add(str.Value);
                    }
                    else if (element.HasValue)
                    {
                        values.Add(element.ToString());
                    }
                }

                return values.Count > 0 ? values.ToArray() : Array.Empty<string>();
            }

            if (node is TomlString single)
            {
                return new[] { single.Value };
            }

            return null;
        }
    }
}
