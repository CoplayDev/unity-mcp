using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Services;
using MCPForUnity.External.Tommy;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Codex CLI specific configuration helpers. Handles TOML snippet
    /// generation and lightweight parsing so Codex can join the auto-setup
    /// flow alongside JSON-based clients.
    /// </summary>
    public static class CodexConfigHelper
    {
        private static void AddDevModeArgs(TomlArray args)
        {
            if (args == null) return;
            // Use central helper that checks both DevModeForceServerRefresh AND local path detection.
            // Note: --reinstall is not supported by uvx, use --no-cache --refresh instead
            if (!AssetPathUtility.ShouldForceUvxRefresh()) return;
            args.Add(new TomlString { Value = "--no-cache" });
            args.Add(new TomlString { Value = "--refresh" });
        }

        public static string BuildCodexServerBlock(string uvPath)
        {
            var table = new TomlTable();
            var mcpServers = new TomlTable();
            var unityMCP = new TomlTable();

            // Check transport preference
            bool useHttpTransport = EditorPrefs.GetBool(MCPForUnity.Editor.Constants.EditorPrefKeys.UseHttpTransport, true);

            if (useHttpTransport)
            {
                // HTTP mode: Use url field
                string httpUrl = HttpEndpointUtility.GetMcpRpcUrl();
                unityMCP["url"] = new TomlString { Value = httpUrl };

                // Enable Codex's Rust MCP client for HTTP/SSE transport
                EnsureRmcpClientFeature(table);
            }
            else
            {
                // Stdio mode: Use command and args
                var (uvxPath, _, packageName) = AssetPathUtility.GetUvxCommandParts();

                unityMCP["command"] = uvxPath;

                var args = new TomlArray();
                AddDevModeArgs(args);
                // Use centralized helper for beta server / prerelease args
                foreach (var arg in AssetPathUtility.GetBetaServerFromArgsList())
                {
                    args.Add(new TomlString { Value = arg });
                }
                args.Add(new TomlString { Value = packageName });
                args.Add(new TomlString { Value = "--transport" });
                args.Add(new TomlString { Value = "stdio" });

                unityMCP["args"] = args;

                // Add Windows-specific environment configuration for stdio mode
                var platformService = MCPServiceLocator.Platform;
                if (platformService.IsWindows())
                {
                    var envTable = new TomlTable { IsInline = true };
                    envTable["SystemRoot"] = new TomlString { Value = platformService.GetSystemRoot() };
                    unityMCP["env"] = envTable;
                }

                // Allow extra time for uvx to download packages on first run
                unityMCP["startup_timeout_sec"] = new TomlInteger { Value = 60 };
            }

            mcpServers["unityMCP"] = unityMCP;
            table["mcp_servers"] = mcpServers;

            using var writer = new StringWriter();
            table.WriteTo(writer);
            return writer.ToString();
        }

        public static string UpsertCodexServerBlock(string existingToml, string uvPath)
        {
            // Parse existing TOML or create new root table
            var root = TryParseToml(existingToml) ?? new TomlTable();

            bool useHttpTransport = EditorPrefs.GetBool(MCPForUnity.Editor.Constants.EditorPrefKeys.UseHttpTransport, true);

            // Ensure mcp_servers table exists
            if (!root.TryGetNode("mcp_servers", out var mcpServersNode) || !(mcpServersNode is TomlTable))
            {
                root["mcp_servers"] = new TomlTable();
            }
            var mcpServers = root["mcp_servers"] as TomlTable;

            // Create or update unityMCP table
            mcpServers["unityMCP"] = CreateUnityMcpTable(uvPath);

            if (useHttpTransport)
            {
                EnsureRmcpClientFeature(root);
            }

            // Serialize back to TOML
            using var writer = new StringWriter();
            root.WriteTo(writer);
            return writer.ToString();
        }

        public static bool TryParseCodexServer(string toml, out string command, out string[] args)
        {
            return TryParseCodexServer(toml, out command, out args, out _);
        }

        public static bool TryParseCodexServer(string toml, out string command, out string[] args, out string url)
        {
            command = null;
            args = null;
            url = null;

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

            // Check for HTTP mode (url field)
            url = GetTomlString(unity, "url");
            if (!string.IsNullOrEmpty(url))
            {
                // HTTP mode detected - return true with url
                return true;
            }

            // Check for stdio mode (command + args)
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
        /// <param name="uvPath">Path to uv executable (used as fallback if uvx is not available)</param>
        private static TomlTable CreateUnityMcpTable(string uvPath)
        {
            var unityMCP = new TomlTable();

            // Check transport preference
            bool useHttpTransport = EditorPrefs.GetBool(MCPForUnity.Editor.Constants.EditorPrefKeys.UseHttpTransport, true);

            if (useHttpTransport)
            {
                // HTTP mode: Use url field
                string httpUrl = HttpEndpointUtility.GetMcpRpcUrl();
                unityMCP["url"] = new TomlString { Value = httpUrl };
            }
            else
            {
                // Stdio mode: Use command and args
                var (uvxPath, _, packageName) = AssetPathUtility.GetUvxCommandParts();

                unityMCP["command"] = new TomlString { Value = uvxPath };

                var argsArray = new TomlArray();
                AddDevModeArgs(argsArray);
                // Use centralized helper for beta server / prerelease args
                foreach (var arg in AssetPathUtility.GetBetaServerFromArgsList())
                {
                    argsArray.Add(new TomlString { Value = arg });
                }
                argsArray.Add(new TomlString { Value = packageName });
                argsArray.Add(new TomlString { Value = "--transport" });
                argsArray.Add(new TomlString { Value = "stdio" });
                unityMCP["args"] = argsArray;

                // Add Windows-specific environment configuration for stdio mode
                var platformService = MCPServiceLocator.Platform;
                if (platformService.IsWindows())
                {
                    var envTable = new TomlTable { IsInline = true };
                    envTable["SystemRoot"] = new TomlString { Value = platformService.GetSystemRoot() };
                    unityMCP["env"] = envTable;
                }

                // Allow extra time for uvx to download packages on first run
                unityMCP["startup_timeout_sec"] = new TomlInteger { Value = 60 };
            }

            return unityMCP;
        }

        /// <summary>
        /// Ensures the features table contains the rmcp_client flag for HTTP/SSE transport.
        /// </summary>
        private static void EnsureRmcpClientFeature(TomlTable root)
        {
            if (root == null) return;

            if (!root.TryGetNode("features", out var featuresNode) || featuresNode is not TomlTable features)
            {
                features = new TomlTable();
                root["features"] = features;
            }

            features["rmcp_client"] = new TomlBoolean { Value = true };
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
