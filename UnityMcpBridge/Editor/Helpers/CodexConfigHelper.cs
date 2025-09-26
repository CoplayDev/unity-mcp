using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
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
            if (string.IsNullOrEmpty(toml)) return false;

            using StringReader reader = new StringReader(toml);
            string line;
            bool inTarget = false;
            while ((line = reader.ReadLine()) != null)
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;

                string headerCandidate = StripTomlComment(trimmed).Trim();
                bool isSection = headerCandidate.StartsWith("[") && headerCandidate.EndsWith("]") && !headerCandidate.StartsWith("[[");
                if (isSection)
                {
                    inTarget = string.Equals(headerCandidate, "[mcp_servers.unityMCP]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (!inTarget) continue;

                if (trimmed.StartsWith("command", StringComparison.OrdinalIgnoreCase))
                {
                    int eq = trimmed.IndexOf('=');
                    if (eq >= 0)
                    {
                        string raw = trimmed[(eq + 1)..];
                        command = ParseTomlStringValue(raw);
                    }
                }
                else if (trimmed.StartsWith("args", StringComparison.OrdinalIgnoreCase))
                {
                    int eq = trimmed.IndexOf('=');
                    if (eq >= 0)
                    {
                        string raw = trimmed[(eq + 1)..].Trim();
                        string aggregated = CollectTomlArray(raw, reader);
                        args = ParseTomlStringArray(aggregated);
                    }
                }
            }

            return !string.IsNullOrEmpty(command) && args != null;
        }

        private static string CollectTomlArray(string firstSegment, StringReader reader)
        {
            StringBuilder buffer = new StringBuilder();
            string sanitizedFirst = StripTomlComment(firstSegment ?? string.Empty).Trim();
            buffer.Append(sanitizedFirst);

            if (IsTomlArrayComplete(buffer.ToString()))
            {
                return buffer.ToString();
            }

            string nextLine;
            while ((nextLine = reader.ReadLine()) != null)
            {
                string sanitizedNext = StripTomlComment(nextLine).Trim();
                buffer.AppendLine();
                buffer.Append(sanitizedNext);

                if (IsTomlArrayComplete(buffer.ToString()))
                {
                    break;
                }
            }

            return buffer.ToString();
        }

        private static bool IsTomlArrayComplete(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            bool inDouble = false;
            bool inSingle = false;
            bool escape = false;
            int depth = 0;
            bool sawOpen = false;

            foreach (char c in text)
            {
                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (c == '\\')
                {
                    if (inDouble)
                    {
                        escape = true;
                    }
                    continue;
                }

                if (c == '"' && !inSingle)
                {
                    inDouble = !inDouble;
                    continue;
                }

                if (c == '\'' && !inDouble)
                {
                    inSingle = !inSingle;
                    continue;
                }

                if (inDouble || inSingle)
                {
                    continue;
                }

                if (c == '[')
                {
                    depth++;
                    sawOpen = true;
                }
                else if (c == ']')
                {
                    if (depth > 0)
                    {
                        depth--;
                    }
                }
            }

            if (!sawOpen) return false;

            if (depth > 0) return false;

            int closingIndex = text.LastIndexOf(']');
            return closingIndex >= 0;
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

        private static string ParseTomlStringValue(string value)
        {
            if (value == null) return null;
            string trimmed = StripTomlComment(value).Trim();
            if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
            {
                return UnescapeTomlBasicString(trimmed.Substring(1, trimmed.Length - 2));
            }
            if (trimmed.Length >= 2 && trimmed[0] == '\'' && trimmed[^1] == '\'')
            {
                return trimmed.Substring(1, trimmed.Length - 2);
            }
            return trimmed.Trim();
        }

        private static string[] ParseTomlStringArray(string value)
        {
            if (value == null) return null;
            string cleaned = StripTomlComment(value).Trim();
            if (!cleaned.StartsWith("[") || !cleaned.EndsWith("]")) return null;

            cleaned = Regex.Replace(cleaned, @",(?=\s*\])", string.Empty);

            try
            {
                return JsonConvert.DeserializeObject<string[]>(cleaned);
            }
            catch
            {
                if (cleaned.IndexOf('"') < 0 && cleaned.IndexOf('\'') >= 0)
                {
                    string alt = cleaned.Replace('\'', '\"');
                    try { return JsonConvert.DeserializeObject<string[]>(alt); } catch { }
                }
            }
            return null;
        }

        private static string StripTomlComment(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            bool inDouble = false;
            bool inSingle = false;
            bool escape = false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (escape)
                {
                    escape = false;
                    continue;
                }
                if (c == '\\' && inDouble)
                {
                    escape = true;
                    continue;
                }
                if (c == '"' && !inSingle)
                {
                    inDouble = !inDouble;
                    continue;
                }
                if (c == '\'' && !inDouble)
                {
                    inSingle = !inSingle;
                    continue;
                }
                if (c == '#' && !inSingle && !inDouble)
                {
                    return value.Substring(0, i).TrimEnd();
                }
            }
            return value.Trim();
        }

        private static string UnescapeTomlBasicString(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            StringBuilder sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '\\' && i + 1 < value.Length)
                {
                    char next = value[++i];
                    sb.Append(next switch
                    {
                        '\\' => '\\',
                        '"' => '"',
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        'b' => '\b',
                        'f' => '\f',
                        _ => next
                    });
                    continue;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
