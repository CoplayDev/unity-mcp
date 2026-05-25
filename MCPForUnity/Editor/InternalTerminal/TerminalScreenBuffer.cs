using System;
using System.Collections.Generic;
using UnityEngine;

namespace WTL.InternalTerminal.Editor
{
    internal sealed class TerminalScreenBuffer
    {
        public struct Cell
        {
            public string Text;
            public Color Foreground;
            public Color Background;
            public int Width;
            public bool Bold;
            public bool Italic;
            public bool Underline;
            public bool Inverse;
            public bool Invisible;
        }

        private Cell[][] lines = Array.Empty<Cell[]>();
        private int cols = 120;
        private int rows = 34;
        private int cursorX;
        private int cursorY;
        private int viewportY;
        private int baseY;
        private int bufferLength;
        private bool cursorVisible = true;
        private bool alternate;

        public int Cols => cols;
        public int Rows => rows;
        public int CursorX => cursorX;
        public int CursorY => cursorY;
        public int ViewportY => viewportY;
        public int BaseY => baseY;
        public int BufferLength => bufferLength;
        public bool CursorVisible => cursorVisible;
        public bool Alternate => alternate;
        public IReadOnlyList<Cell[]> Lines => lines;

        public void Clear()
        {
            lines = Array.Empty<Cell[]>();
            cursorX = 0;
            cursorY = 0;
            viewportY = 0;
            baseY = 0;
            bufferLength = 0;
            cursorVisible = true;
            alternate = false;
        }

        public void Resize(int newCols, int newRows)
        {
            cols = Mathf.Max(2, newCols);
            rows = Mathf.Max(2, newRows);
            cursorX = Mathf.Clamp(cursorX, 0, Mathf.Max(0, cols - 1));
            cursorY = Mathf.Clamp(cursorY, 0, Mathf.Max(0, rows - 1));

            var resized = new Cell[rows][];
            for (var y = 0; y < rows; y++)
            {
                resized[y] = CreateEmptyRow(cols);
                if (y >= lines.Length)
                {
                    continue;
                }

                var source = lines[y];
                Array.Copy(source, resized[y], Mathf.Min(cols, source.Length));
            }

            lines = resized;
        }

        public void ApplyScreenJson(string json)
        {
            cols = JsonInt(json, "cols", cols);
            rows = JsonInt(json, "rows", rows);
            cursorX = Mathf.Clamp(JsonInt(json, "cursorX", 0), 0, Mathf.Max(0, cols - 1));
            cursorY = JsonInt(json, "cursorY", 0);
            cursorVisible = JsonBool(json, "cursorVisible", cursorY >= 0);
            if (cursorVisible)
            {
                cursorY = Mathf.Clamp(cursorY, 0, Mathf.Max(0, rows - 1));
            }
            viewportY = Mathf.Max(0, JsonInt(json, "viewportY", viewportY));
            baseY = Mathf.Max(0, JsonInt(json, "baseY", baseY));
            bufferLength = Mathf.Max(rows, JsonInt(json, "bufferLength", bufferLength));
            alternate = JsonBool(json, "alternate", alternate);

            var runsStart = json.IndexOf("\"runs\"", StringComparison.Ordinal);
            if (runsStart >= 0)
            {
                var runsArrayStart = json.IndexOf('[', runsStart);
                if (runsArrayStart >= 0)
                {
                    var runsParser = new ScreenParser(json, runsArrayStart);
                    lines = runsParser.ParseRuns(rows, cols);
                    return;
                }
            }

            var cellsStart = json.IndexOf("\"cells\"", StringComparison.Ordinal);
            if (cellsStart < 0)
            {
                return;
            }

            var arrayStart = json.IndexOf('[', cellsStart);
            if (arrayStart < 0)
            {
                return;
            }

            var parser = new ScreenParser(json, arrayStart);
            lines = parser.ParseCells(rows, cols);
        }

        private static int JsonInt(string json, string key, int fallback)
        {
            var marker = "\"" + key + "\":";
            var index = json.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0)
            {
                return fallback;
            }

            index += marker.Length;
            while (index < json.Length && char.IsWhiteSpace(json[index]))
            {
                index++;
            }

            var start = index;
            while (index < json.Length && (char.IsDigit(json[index]) || json[index] == '-'))
            {
                index++;
            }

            return int.TryParse(json.Substring(start, index - start), out var value) ? value : fallback;
        }

        private static bool JsonBool(string json, string key, bool fallback)
        {
            var marker = "\"" + key + "\":";
            var index = json.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0)
            {
                return fallback;
            }

            index += marker.Length;
            while (index < json.Length && char.IsWhiteSpace(json[index]))
            {
                index++;
            }

            if (index + 4 <= json.Length && string.CompareOrdinal(json, index, "true", 0, 4) == 0)
            {
                return true;
            }

            if (index + 5 <= json.Length && string.CompareOrdinal(json, index, "false", 0, 5) == 0)
            {
                return false;
            }

            return fallback;
        }

        private sealed class ScreenParser
        {
            private readonly string json;
            private int index;

            public ScreenParser(string json, int start)
            {
                this.json = json;
                index = start;
            }

            public Cell[][] ParseCells(int rows, int cols)
            {
                var result = new Cell[rows][];
                for (var row = 0; row < rows; row++)
                {
                    result[row] = CreateEmptyRow(cols);
                }

                SkipTo('[');
                index++;
                for (var y = 0; y < rows && index < json.Length; y++)
                {
                    SkipWhitespaceAndCommas();
                    if (index >= json.Length || json[index] != '[')
                    {
                        break;
                    }

                    index++;
                    for (var x = 0; x < cols && index < json.Length; x++)
                    {
                        SkipWhitespaceAndCommas();
                        if (index >= json.Length || json[index] != '{')
                        {
                            break;
                        }

                        result[y][x] = ParseCell();
                    }

                    SkipTo(']');
                    if (index < json.Length)
                    {
                        index++;
                    }
                }

                return result;
            }

            public Cell[][] ParseRuns(int rows, int cols)
            {
                var result = new Cell[rows][];
                for (var row = 0; row < rows; row++)
                {
                    result[row] = CreateEmptyRow(cols);
                }

                SkipTo('[');
                index++;
                for (var y = 0; y < rows && index < json.Length; y++)
                {
                    SkipWhitespaceAndCommas();
                    if (index >= json.Length || json[index] != '[')
                    {
                        break;
                    }

                    index++;
                    while (index < json.Length)
                    {
                        SkipWhitespaceAndCommas();
                        if (index >= json.Length || json[index] == ']')
                        {
                            break;
                        }

                        if (json[index] != '{')
                        {
                            break;
                        }

                        ApplyRun(result[y], ParseRun(), cols);
                    }

                    SkipTo(']');
                    if (index < json.Length)
                    {
                        index++;
                    }
                }

                return result;
            }

            private Run ParseRun()
            {
                var objectStart = index;
                var depth = 0;
                var inString = false;
                var escaped = false;
                do
                {
                    var ch = json[index];
                    if (inString)
                    {
                        if (escaped)
                        {
                            escaped = false;
                        }
                        else if (ch == '\\')
                        {
                            escaped = true;
                        }
                        else if (ch == '"')
                        {
                            inString = false;
                        }
                    }
                    else if (ch == '"')
                    {
                        inString = true;
                    }
                    else if (ch == '{')
                    {
                        depth++;
                    }
                    else if (ch == '}')
                    {
                        depth--;
                    }

                    index++;
                }
                while (index < json.Length && depth > 0);

                var runJson = json.Substring(objectStart, index - objectStart);
                var flags = JsonInt(runJson, "flags", 0);
                return new Run
                {
                    X = JsonInt(runJson, "x", 0),
                    Text = JsonString(runJson, "text"),
                    Foreground = ColorFromRgb(JsonInt(runJson, "fg", TerminalPalette.DefaultForegroundRgb)),
                    Background = ColorFromRgb(JsonInt(runJson, "bg", TerminalPalette.DefaultBackgroundRgb)),
                    Width = Mathf.Clamp(JsonInt(runJson, "w", 1), 0, 2),
                    Bold = (flags & 1) != 0,
                    Italic = (flags & 2) != 0,
                    Underline = (flags & 4) != 0,
                    Inverse = (flags & 8) != 0,
                    Invisible = (flags & 16) != 0
                };
            }

            private static void ApplyRun(Cell[] row, Run run, int cols)
            {
                var x = Mathf.Clamp(run.X, 0, Mathf.Max(0, cols - 1));
                foreach (var textElement in EnumerateTextElements(run.Text))
                {
                    if (x >= cols)
                    {
                        break;
                    }

                    var width = run.Width == 0 ? 0 : GuessCellWidth(textElement, run.Width);
                    row[x] = new Cell
                    {
                        Text = textElement,
                        Foreground = run.Foreground,
                        Background = run.Background,
                        Width = width,
                        Bold = run.Bold,
                        Italic = run.Italic,
                        Underline = run.Underline,
                        Inverse = run.Inverse,
                        Invisible = run.Invisible
                    };

                    if (width == 2 && x + 1 < cols)
                    {
                        row[x + 1] = new Cell
                        {
                            Text = string.Empty,
                            Foreground = run.Foreground,
                            Background = run.Background,
                            Width = 0,
                            Bold = run.Bold,
                            Italic = run.Italic,
                            Underline = run.Underline,
                            Inverse = run.Inverse,
                            Invisible = run.Invisible
                        };
                    }

                    x += Mathf.Max(1, width);
                }
            }

            private static IEnumerable<string> EnumerateTextElements(string text)
            {
                var enumerator = System.Globalization.StringInfo.GetTextElementEnumerator(text);
                while (enumerator.MoveNext())
                {
                    yield return enumerator.GetTextElement();
                }
            }

            private static int GuessCellWidth(string textElement, int fallback)
            {
                return fallback;
            }

            private Cell ParseCell()
            {
                var objectStart = index;
                var depth = 0;
                var inString = false;
                var escaped = false;
                do
                {
                    var ch = json[index];
                    if (inString)
                    {
                        if (escaped)
                        {
                            escaped = false;
                        }
                        else if (ch == '\\')
                        {
                            escaped = true;
                        }
                        else if (ch == '"')
                        {
                            inString = false;
                        }
                    }
                    else if (ch == '"')
                    {
                        inString = true;
                    }
                    else if (ch == '{')
                    {
                        depth++;
                    }
                    else if (ch == '}')
                    {
                        depth--;
                    }

                    index++;
                }
                while (index < json.Length && depth > 0);

                var cellJson = json.Substring(objectStart, index - objectStart);
                var flags = JsonInt(cellJson, "flags", 0);

                return new Cell
                {
                    Text = JsonString(cellJson, "ch"),
                    Foreground = ColorFromRgb(JsonInt(cellJson, "fg", TerminalPalette.DefaultForegroundRgb)),
                    Background = ColorFromRgb(JsonInt(cellJson, "bg", TerminalPalette.DefaultBackgroundRgb)),
                    Width = Mathf.Clamp(JsonInt(cellJson, "w", 1), 0, 2),
                    Bold = (flags & 1) != 0,
                    Italic = (flags & 2) != 0,
                    Underline = (flags & 4) != 0,
                    Inverse = (flags & 8) != 0,
                    Invisible = (flags & 16) != 0
                };
            }

            private struct Run
            {
                public int X;
                public string Text;
                public Color Foreground;
                public Color Background;
                public int Width;
                public bool Bold;
                public bool Italic;
                public bool Underline;
                public bool Inverse;
                public bool Invisible;
            }

            private void SkipWhitespaceAndCommas()
            {
                while (index < json.Length && (char.IsWhiteSpace(json[index]) || json[index] == ','))
                {
                    index++;
                }
            }

            private void SkipTo(char ch)
            {
                while (index < json.Length && json[index] != ch)
                {
                    index++;
                }
            }

            private static string JsonString(string json, string key)
            {
                var marker = "\"" + key + "\":";
                var index = json.IndexOf(marker, StringComparison.Ordinal);
                if (index < 0)
                {
                    return " ";
                }

                index += marker.Length;
                while (index < json.Length && char.IsWhiteSpace(json[index]))
                {
                    index++;
                }

                if (index >= json.Length || json[index] != '"')
                {
                    return " ";
                }

                index++;
                var chars = new List<char>();
                while (index < json.Length)
                {
                    var ch = json[index++];
                    if (ch == '"')
                    {
                        break;
                    }

                    if (ch == '\\' && index < json.Length)
                    {
                        var escaped = json[index++];
                        if (escaped == 'n') chars.Add('\n');
                        else if (escaped == 'r') chars.Add('\r');
                        else if (escaped == 't') chars.Add('\t');
                        else if (escaped == '"' || escaped == '\\' || escaped == '/') chars.Add(escaped);
                        else if (escaped == 'u' && index + 4 <= json.Length)
                        {
                            var hex = json.Substring(index, 4);
                            if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var value))
                            {
                                chars.Add((char)value);
                            }
                            index += 4;
                        }
                    }
                    else
                    {
                        chars.Add(ch);
                    }
                }

                return chars.Count == 0 ? " " : new string(chars.ToArray());
            }
        }

        private static Color ColorFromRgb(int rgb)
        {
            var r = ((rgb >> 16) & 0xff) / 255f;
            var g = ((rgb >> 8) & 0xff) / 255f;
            var b = (rgb & 0xff) / 255f;
            return new Color(r, g, b, 1f);
        }

        private static Cell[] CreateEmptyRow(int cols)
        {
            var row = new Cell[cols];
            for (var index = 0; index < cols; index++)
            {
                row[index] = new Cell { Text = " ", Foreground = TerminalPalette.Foreground, Background = TerminalPalette.Background, Width = 1 };
            }
            return row;
        }
    }

    internal static class TerminalPalette
    {
        public const int DefaultBackgroundRgb = 0x14161a;
        public const int DefaultForegroundRgb = 0xe8edf2;
        public static readonly Color Background = new Color(20f / 255f, 22f / 255f, 26f / 255f, 1f);
        public static readonly Color Foreground = new Color(232f / 255f, 237f / 255f, 242f / 255f, 1f);
    }
}
