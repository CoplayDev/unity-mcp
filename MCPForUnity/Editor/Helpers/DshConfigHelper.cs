using System;
using System.Collections.Generic;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// DeepSeek Harness (DSH) specific configuration helpers. DSH mounts external MCP
    /// servers as Cordis configuration rows for the <c>@deepseek-ai/dsh-mcp-client</c>
    /// plugin, stored in a YAML patch layer (by default <c>$DSH_HOME/cordis.patch.yml</c>,
    /// with <c>$DSH_HOME</c> defaulting to <c>~/.dsh</c>).
    ///
    /// User patch files may contain arbitrary YAML features this package has no parser
    /// for (e.g. <c>!!js</c> JS-expression tags, anchors, unrelated server rows). This
    /// helper therefore manages one marker-fenced block and never parses or rewrites
    /// anything outside it: upsert replaces only the fenced region, and status checks
    /// parse only the fenced region's known shape.
    /// </summary>
    public static class DshConfigHelper
    {
        public const string ServerId = "unitymcp";
        public const string ServerName = "unityMCP";
        public const string PluginName = "@deepseek-ai/dsh-mcp-client";

        public const string BeginMarker = "# >>> MCP for Unity (unityMCP) managed block - do not edit inside >>>";
        public const string EndMarker = "# <<< MCP for Unity (unityMCP) <<<";

        /// <summary>
        /// Builds the fenced YAML patch block that mounts the Unity MCP server in DSH.
        /// All values are passed in so the method is pure and unit-testable.
        /// </summary>
        /// <param name="useHttp">true writes a <c>streamable-http</c> row, false a <c>stdio</c> row.</param>
        /// <param name="httpUrl">Streamable HTTP MCP RPC endpoint (used when <paramref name="useHttp"/>).</param>
        /// <param name="uvxCommand">uvx executable path (used for stdio).</param>
        /// <param name="uvxArgs">stdio arguments after the command (dev flags, package source, package name, --transport stdio).</param>
        /// <param name="apiKeyHeader">Optional request header name for HTTP auth (e.g. X-API-Key).</param>
        /// <param name="apiKey">Optional request header value for HTTP auth.</param>
        /// <param name="windowsSystemRoot">SystemRoot env override for stdio children on Windows, or null.</param>
        public static string BuildUnityMcpBlock(
            bool useHttp,
            string httpUrl,
            string uvxCommand,
            string[] uvxArgs,
            string apiKeyHeader,
            string apiKey,
            string windowsSystemRoot)
        {
            var lines = new List<string>
            {
                BeginMarker,
                "- insert:",
                $"    - id: {ServerId}",
                $"      name: '{PluginName}'",
                "      config:",
                $"        serverName: {ServerName}",
            };

            if (useHttp)
            {
                lines.Add("        transport: streamable-http");
                lines.Add($"        url: {Quote(httpUrl ?? string.Empty)}");
                if (!string.IsNullOrEmpty(apiKeyHeader) && !string.IsNullOrEmpty(apiKey))
                {
                    lines.Add("        headers:");
                    lines.Add($"          {apiKeyHeader}: {Quote(apiKey)}");
                }
            }
            else
            {
                lines.Add("        transport: stdio");
                lines.Add($"        command: {Quote(uvxCommand ?? string.Empty)}");
                lines.Add("        args:");
                foreach (string arg in uvxArgs ?? Array.Empty<string>())
                {
                    lines.Add($"          - {Quote(arg ?? string.Empty)}");
                }

                if (!string.IsNullOrEmpty(windowsSystemRoot))
                {
                    lines.Add("        env:");
                    lines.Add($"          SystemRoot: {Quote(windowsSystemRoot)}");
                }
            }

            lines.Add(EndMarker);
            return string.Join("\n", lines) + "\n";
        }

        /// <summary>
        /// Returns the rows between the fenced markers (exclusive), or null when no
        /// complete block is present. Never reads content outside the markers.
        /// </summary>
        public static string ExtractManagedBlock(string fileText)
        {
            BlockRange? range = FindBlockRange(fileText);
            if (range == null) return null;

            int contentStart = fileText.IndexOf('\n', range.Value.MarkerBegin);
            if (contentStart < 0 || contentStart + 1 > range.Value.MarkerEnd) return string.Empty;
            return fileText.Substring(contentStart + 1, range.Value.MarkerEnd - contentStart - 1);
        }

        /// <summary>
        /// Replaces the existing managed block in place, or appends the block at the end
        /// of the document. All bytes outside the managed block are preserved verbatim.
        /// </summary>
        public static string UpsertManagedBlock(string fileText, string block)
        {
            if (block == null) throw new ArgumentNullException(nameof(block));
            string normalized = block.TrimEnd('\r', '\n') + "\n";

            if (string.IsNullOrWhiteSpace(fileText))
            {
                return normalized;
            }

            BlockRange? range = FindBlockRange(fileText);
            if (range == null)
            {
                string separator = fileText.EndsWith("\n") ? string.Empty : "\n";
                return fileText + separator + normalized;
            }

            return fileText.Substring(0, range.Value.BlockStart)
                + normalized
                + fileText.Substring(range.Value.BlockEnd);
        }

        /// <summary>
        /// Removes the managed block, leaving the rest of the document untouched.
        /// Returns the original text when no block is present.
        /// </summary>
        public static string RemoveManagedBlock(string fileText)
        {
            BlockRange? range = FindBlockRange(fileText);
            if (range == null) return fileText;

            string before = fileText.Substring(0, range.Value.BlockStart);
            string after = fileText.Substring(range.Value.BlockEnd);

            // Collapse a doubled blank line left behind by the removal.
            if (before.EndsWith("\n\n") && after.StartsWith("\n", StringComparison.Ordinal))
            {
                before = before.Substring(0, before.Length - 1);
            }

            return before + after;
        }

        /// <summary>True when the file already contains a complete managed block.</summary>
        public static bool HasManagedBlock(string fileText)
        {
            return FindBlockRange(fileText) != null;
        }

        /// <summary>
        /// Parses the managed block emitted by <see cref="BuildUnityMcpBlock"/>.
        /// Line-based by design: the block's shape is ours, so no YAML parser is needed
        /// and foreign YAML can never confuse the result.
        /// </summary>
        public static bool TryParseManagedBlock(
            string block,
            out string transport,
            out string url,
            out string command,
            out string[] args,
            out string headerName,
            out string headerValue)
        {
            transport = null;
            url = null;
            command = null;
            args = null;
            headerName = null;
            headerValue = null;

            if (string.IsNullOrWhiteSpace(block)) return false;

            string[] lines = block.Replace("\r\n", "\n").Split('\n');
            var argList = new List<string>();
            bool inArgs = false;
            bool inHeaders = false;
            int sectionIndent = -1;

            foreach (string rawLine in lines)
            {
                if (string.IsNullOrWhiteSpace(rawLine)) continue;
                int indent = rawLine.Length - rawLine.TrimStart().Length;
                string line = rawLine.Trim();

                if (inArgs && indent > sectionIndent && line.StartsWith("- ", StringComparison.Ordinal))
                {
                    argList.Add(Unquote(line.Substring(2).Trim()));
                    continue;
                }

                if (inHeaders && indent > sectionIndent && TrySplitKeyValue(line, out string hKey, out string hValue))
                {
                    if (headerName == null)
                    {
                        headerName = hKey;
                        headerValue = Unquote(hValue);
                    }
                    continue;
                }

                inArgs = false;
                inHeaders = false;

                if (line.StartsWith("transport:", StringComparison.Ordinal))
                {
                    transport = Unquote(line.Substring("transport:".Length).Trim());
                }
                else if (line.StartsWith("url:", StringComparison.Ordinal))
                {
                    url = Unquote(line.Substring("url:".Length).Trim());
                }
                else if (line.StartsWith("command:", StringComparison.Ordinal))
                {
                    command = Unquote(line.Substring("command:".Length).Trim());
                }
                else if (line.StartsWith("args:", StringComparison.Ordinal))
                {
                    inArgs = true;
                    sectionIndent = indent;
                }
                else if (line.StartsWith("headers:", StringComparison.Ordinal))
                {
                    inHeaders = true;
                    sectionIndent = indent;
                }
            }

            args = argList.ToArray();
            return transport != null;
        }

        /// <summary>Single-quotes a YAML scalar; embedded single quotes are doubled.</summary>
        public static string Quote(string value)
        {
            return "'" + (value ?? string.Empty).Replace("'", "''") + "'";
        }

        /// <summary>Strips single quotes written by <see cref="Quote"/>; plain scalars pass through.</summary>
        public static string Unquote(string value)
        {
            if (value == null) return null;
            value = value.Trim();
            if (value.Length >= 2 && value[0] == '\'' && value[value.Length - 1] == '\'')
            {
                return value.Substring(1, value.Length - 2).Replace("''", "'");
            }
            return value;
        }

        private readonly struct BlockRange
        {
            /// <summary>Start of the begin-marker line (equal to MarkerBegin).</summary>
            public readonly int BlockStart;
            /// <summary>One past the end of the end-marker line (its newline included).</summary>
            public readonly int BlockEnd;
            /// <summary>Index of the first character of the begin marker.</summary>
            public readonly int MarkerBegin;
            /// <summary>Index of the first character of the end marker.</summary>
            public readonly int MarkerEnd;

            public BlockRange(int blockStart, int blockEnd, int markerBegin, int markerEnd)
            {
                BlockStart = blockStart;
                BlockEnd = blockEnd;
                MarkerBegin = markerBegin;
                MarkerEnd = markerEnd;
            }
        }

        /// <summary>Locates the fenced block; null when no complete marker pair exists.</summary>
        private static BlockRange? FindBlockRange(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;

            int markerBegin = IndexOfMarkerLine(text, BeginMarker, 0);
            if (markerBegin < 0) return null;

            int markerEnd = IndexOfMarkerLine(text, EndMarker, markerBegin + BeginMarker.Length);
            if (markerEnd < 0) return null;

            int blockEnd = text.IndexOf('\n', markerEnd);
            blockEnd = blockEnd < 0 ? text.Length : blockEnd + 1;
            return new BlockRange(markerBegin, blockEnd, markerBegin, markerEnd);
        }

        /// <summary>Finds the next occurrence of marker at the start of its own line.</summary>
        private static int IndexOfMarkerLine(string text, string marker, int startIndex)
        {
            int searchFrom = Math.Max(0, startIndex);
            while (searchFrom <= text.Length - marker.Length)
            {
                int idx = text.IndexOf(marker, searchFrom, StringComparison.Ordinal);
                if (idx < 0) return -1;
                bool atLineStart = idx == 0 || text[idx - 1] == '\n';
                if (atLineStart) return idx;
                searchFrom = idx + 1;
            }
            return -1;
        }

        private static bool TrySplitKeyValue(string line, out string key, out string value)
        {
            key = null;
            value = null;
            int colon = line.IndexOf(':');
            if (colon <= 0) return false;
            key = line.Substring(0, colon).Trim();
            value = line.Substring(colon + 1).Trim();
            return key.Length > 0 && value.Length > 0;
        }
    }
}
