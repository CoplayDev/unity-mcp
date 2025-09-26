using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MCPForUnity.External.Tommy;
using Newtonsoft.Json;

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

                string dir = McpConfigFileHelper.ExtractDirectoryArg(args);
                if (string.IsNullOrEmpty(dir)) return false;

                return McpConfigFileHelper.PathsEqual(dir, pythonDir);
            }
            catch
            {
                return false;
            }
        }

        public static string BuildCodexServerBlock(string uvPath, string serverSrc)
        {
            string argsArray = FormatTomlStringArray(new[] { "run", "--directory", serverSrc, "server.py" });
            return $"[mcp_servers.unityMCP]{Environment.NewLine}" +
                   $"command = \"{EscapeTomlString(uvPath)}\"{Environment.NewLine}" +
                   $"args = {argsArray}";
        }

        public static string UpsertCodexServerBlock(string existingToml, string newBlock)
        {
            if (string.IsNullOrWhiteSpace(existingToml))
            {
                return newBlock.TrimEnd() + Environment.NewLine;
            }

            StringBuilder sb = new StringBuilder();
            using StringReader reader = new StringReader(existingToml);
            string line;
            bool inTarget = false;
            bool replaced = false;
            while ((line = reader.ReadLine()) != null)
            {
                string trimmed = line.Trim();
                bool isSection = trimmed.StartsWith("[") && trimmed.EndsWith("]") && !trimmed.StartsWith("[[");
                if (isSection)
                {
                    bool isTarget = string.Equals(trimmed, "[mcp_servers.unityMCP]", StringComparison.OrdinalIgnoreCase);
                    if (isTarget)
                    {
                        if (!replaced)
                        {
                            if (sb.Length > 0 && sb[^1] != '\n') sb.AppendLine();
                            sb.AppendLine(newBlock.TrimEnd());
                            replaced = true;
                        }
                        inTarget = true;
                        continue;
                    }

                    if (inTarget)
                    {
                        inTarget = false;
                    }
                }

                if (inTarget)
                {
                    continue;
                }

                sb.AppendLine(line);
            }

            if (!replaced)
            {
                if (sb.Length > 0 && sb[^1] != '\n') sb.AppendLine();
                sb.AppendLine(newBlock.TrimEnd());
            }

            return sb.ToString().TrimEnd() + Environment.NewLine;
        }

        public static bool TryParseCodexServer(string toml, out string command, out string[] args)
        {
            command = null;
            args = null;
            if (string.IsNullOrWhiteSpace(toml)) return false;

            try
            {
                using var reader = new StringReader(toml);
                TomlTable root = TOML.Parse(reader);
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
            catch (TomlParseException)
            {
                return false;
            }
            catch (TomlSyntaxException)
            {
                return false;
            }
            catch (FormatException)
            {
                return false;
            }
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

        private static string FormatTomlStringArray(IEnumerable<string> values)
        {
            if (values == null) return "[]";
            StringBuilder sb = new StringBuilder();
            sb.Append('[');
            bool first = true;
            foreach (string value in values)
            {
                if (!first)
                {
                    sb.Append(", ");
                }
                sb.Append('"').Append(EscapeTomlString(value ?? string.Empty)).Append('"');
                first = false;
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static string EscapeTomlString(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"");
        }

    }
}
