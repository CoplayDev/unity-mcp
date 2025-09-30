#region LICENSE

/*
 * MIT License
 * 
 * Copyright (c) 2020 Denis Zhidkikh
 * 
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 */

#endregion

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MCPForUnity.External.Tommy
{
    #region TOML Nodes

    public abstract class TomlNode : IEnumerable
    {
        public virtual bool HasValue { get; } = false;
        public virtual bool IsArray { get; } = false;
        public virtual bool IsTable { get; } = false;
        public virtual bool IsString { get; } = false;
        public virtual bool IsInteger { get; } = false;
        public virtual bool IsFloat { get; } = false;
        public bool IsDateTime => IsDateTimeLocal || IsDateTimeOffset;
        public virtual bool IsDateTimeLocal { get; } = false;
        public virtual bool IsDateTimeOffset { get; } = false;
        public virtual bool IsBoolean { get; } = false;
        public virtual string Comment { get; set; }
        public virtual int CollapseLevel { get; set; }

        public virtual TomlTable AsTable => this as TomlTable;
        public virtual TomlString AsString => this as TomlString;
        public virtual TomlInteger AsInteger => this as TomlInteger;
        public virtual TomlFloat AsFloat => this as TomlFloat;
        public virtual TomlBoolean AsBoolean => this as TomlBoolean;
        public virtual TomlDateTimeLocal AsDateTimeLocal => this as TomlDateTimeLocal;
        public virtual TomlDateTimeOffset AsDateTimeOffset => this as TomlDateTimeOffset;
        public virtual TomlDateTime AsDateTime => this as TomlDateTime;
        public virtual TomlArray AsArray => this as TomlArray;

        public virtual int ChildrenCount => 0;

        public virtual TomlNode this[string key]
        {
            get => null;
            set { }
        }

        public virtual TomlNode this[int index]
        {
            get => null;
            set { }
        }

        public virtual IEnumerable<TomlNode> Children
        {
            get { yield break; }
        }

        public virtual IEnumerable<string> Keys
        {
            get { yield break; }
        }

        public IEnumerator GetEnumerator() => Children.GetEnumerator();

        public virtual bool TryGetNode(string key, out TomlNode node)
        {
            node = null;
            return false;
        }

        public virtual bool HasKey(string key) => false;

        public virtual bool HasItemAt(int index) => false;

        public virtual void Add(string key, TomlNode node) { }

        public virtual void Add(TomlNode node) { }

        public virtual void Delete(TomlNode node) { }

        public virtual void Delete(string key) { }

        public virtual void Delete(int index) { }

        public virtual void AddRange(IEnumerable<TomlNode> nodes)
        {
            foreach (var tomlNode in nodes) Add(tomlNode);
        }

        public virtual void WriteTo(TextWriter tw, string name = null) => tw.WriteLine(ToInlineToml());

        public virtual string ToInlineToml() => ToString();

        #region Native type to TOML cast

        public static implicit operator TomlNode(string value) => new TomlString { Value = value };

        public static implicit operator TomlNode(bool value) => new TomlBoolean { Value = value };

        public static implicit operator TomlNode(long value) => new TomlInteger { Value = value };

        public static implicit operator TomlNode(float value) => new TomlFloat { Value = value };

        public static implicit operator TomlNode(double value) => new TomlFloat { Value = value };

        public static implicit operator TomlNode(DateTime value) => new TomlDateTimeLocal { Value = value };

        public static implicit operator TomlNode(DateTimeOffset value) => new TomlDateTimeOffset { Value = value };

        public static implicit operator TomlNode(TomlNode[] nodes)
        {
            var result = new TomlArray();
            result.AddRange(nodes);
            return result;
        }

        #endregion

        #region TOML to native type cast

        public static implicit operator string(TomlNode value) => value.ToString();

        public static implicit operator int(TomlNode value) => (int)value.AsInteger.Value;

        public static implicit operator long(TomlNode value) => value.AsInteger.Value;

        public static implicit operator float(TomlNode value) => (float)value.AsFloat.Value;

        public static implicit operator double(TomlNode value) => value.AsFloat.Value;

        public static implicit operator bool(TomlNode value) => value.AsBoolean.Value;

        public static implicit operator DateTime(TomlNode value) => value.AsDateTimeLocal.Value;

        public static implicit operator DateTimeOffset(TomlNode value) => value.AsDateTimeOffset.Value;

        #endregion
    }

    public class TomlString : TomlNode
    {
        public override bool HasValue { get; } = true;
        public override bool IsString { get; } = true;
        public bool IsMultiline { get; set; }
        public bool MultilineTrimFirstLine { get; set; }
        public bool PreferLiteral { get; set; }

        public string Value { get; set; }

        public override string ToString() => Value;

        public override string ToInlineToml()
        {
            // Automatically convert literal to non-literal if there are too many literal string symbols
            if (Value.IndexOf(new string(TomlSyntax.LITERAL_STRING_SYMBOL, IsMultiline ? 3 : 1), StringComparison.Ordinal) != -1 && PreferLiteral) PreferLiteral = false;
            var quotes = new string(PreferLiteral ? TomlSyntax.LITERAL_STRING_SYMBOL : TomlSyntax.BASIC_STRING_SYMBOL,
                                    IsMultiline ? 3 : 1);
            var result = PreferLiteral ? Value : Value.Escape(!IsMultiline);
            if (IsMultiline)
                result = result.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
            if (IsMultiline && (MultilineTrimFirstLine || !MultilineTrimFirstLine && result.StartsWith(Environment.NewLine)))
                result = $"{Environment.NewLine}{result}";
            return $"{quotes}{result}{quotes}";
        }
    }

    public class TomlInteger : TomlNode
    {
        public enum Base
        {
            Binary = 2,
            Octal = 8,
            Decimal = 10,
            Hexadecimal = 16
        }

        public override bool IsInteger { get; } = true;
        public override bool HasValue { get; } = true;
        public Base IntegerBase { get; set; } = Base.Decimal;

        public long Value { get; set; }

        public override string ToString() => Value.ToString();

        public override string ToInlineToml() =>
            IntegerBase != Base.Decimal
                ? $"0{TomlSyntax.BaseIdentifiers[(int)IntegerBase]}{Convert.ToString(Value, (int)IntegerBase)}"
                : Value.ToString(CultureInfo.InvariantCulture);
    }

    public class TomlFloat : TomlNode, IFormattable
    {
        public override bool IsFloat { get; } = true;
        public override bool HasValue { get; } = true;

        public double Value { get; set; }

        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public string ToString(string format, IFormatProvider formatProvider) => Value.ToString(format, formatProvider);

        public string ToString(IFormatProvider formatProvider) => Value.ToString(formatProvider);

        public override string ToInlineToml() =>
            Value switch
            {
                var v when double.IsNaN(v) => TomlSyntax.NAN_VALUE,
                var v when double.IsPositiveInfinity(v) => TomlSyntax.INF_VALUE,
                var v when double.IsNegativeInfinity(v) => TomlSyntax.NEG_INF_VALUE,
                var v => v.ToString("G", CultureInfo.InvariantCulture).ToLowerInvariant()
            };
    }

    public class TomlBoolean : TomlNode
    {
        public override bool IsBoolean { get; } = true;
        public override bool HasValue { get; } = true;

        public bool Value { get; set; }

        public override string ToString() => Value.ToString();

        public override string ToInlineToml() => Value ? TomlSyntax.TRUE_VALUE : TomlSyntax.FALSE_VALUE;
    }

    public class TomlDateTime : TomlNode, IFormattable
    {
        public int SecondsPrecision { get; set; }
        public override bool HasValue { get; } = true;
        public virtual string ToString(string format, IFormatProvider formatProvider) => string.Empty;
        public virtual string ToString(IFormatProvider formatProvider) => string.Empty;
        protected virtual string ToInlineTomlInternal() => string.Empty;

        public override string ToInlineToml() => ToInlineTomlInternal()
                                                .Replace(TomlSyntax.RFC3339EmptySeparator, TomlSyntax.ISO861Separator)
                                                .Replace(TomlSyntax.ISO861ZeroZone, TomlSyntax.RFC3339ZeroZone);
    }

    public class TomlDateTimeOffset : TomlDateTime
    {
        public override bool IsDateTimeOffset { get; } = true;
        public DateTimeOffset Value { get; set; }

        public override string ToString() => Value.ToString(CultureInfo.CurrentCulture);
        public override string ToString(IFormatProvider formatProvider) => Value.ToString(formatProvider);

        public override string ToString(string format, IFormatProvider formatProvider) =>
            Value.ToString(format, formatProvider);

        protected override string ToInlineTomlInternal() => Value.ToString(TomlSyntax.RFC3339Formats[SecondsPrecision]);
    }

    public class TomlDateTimeLocal : TomlDateTime
    {
        public enum DateTimeStyle
        {
            Date,
            Time,
            DateTime
        }

        public override bool IsDateTimeLocal { get; } = true;
        public DateTimeStyle Style { get; set; } = DateTimeStyle.DateTime;
        public DateTime Value { get; set; }

        public override string ToString() => Value.ToString(CultureInfo.CurrentCulture);

        public override string ToString(IFormatProvider formatProvider) => Value.ToString(formatProvider);

        public override string ToString(string format, IFormatProvider formatProvider) =>
            Value.ToString(format, formatProvider);

        public override string ToInlineToml() =>
            Style switch
            {
                DateTimeStyle.Date => Value.ToString(TomlSyntax.LocalDateFormat),
                DateTimeStyle.Time => Value.ToString(TomlSyntax.RFC3339LocalTimeFormats[SecondsPrecision]),
                var _ => Value.ToString(TomlSyntax.RFC3339LocalDateTimeFormats[SecondsPrecision])
            };
    }

    public class TomlArray : TomlNode
    {
        private List<TomlNode> values;

        public override bool HasValue { get; } = true;
        public override bool IsArray { get; } = true;
        public bool IsMultiline { get; set; }
        public bool IsTableArray { get; set; }
        public List<TomlNode> RawArray => values ??= new List<TomlNode>();

        public override TomlNode this[int index]
        {
            get
            {
                if (index < RawArray.Count) return RawArray[index];
                var lazy = new TomlLazy(this);
                this[index] = lazy;
                return lazy;
            }
            set
            {
                if (index == RawArray.Count)
                    RawArray.Add(value);
                else
                    RawArray[index] = value;
            }
        }

        public override int ChildrenCount => RawArray.Count;

        public override IEnumerable<TomlNode> Children => RawArray.AsEnumerable();

        public override void Add(TomlNode node) => RawArray.Add(node);

        public override void AddRange(IEnumerable<TomlNode> nodes) => RawArray.AddRange(nodes);

        public override void Delete(TomlNode node) => RawArray.Remove(node);

        public override void Delete(int index) => RawArray.RemoveAt(index);

        public override string ToString() => ToString(false);

        public string ToString(bool multiline)
        {
            var sb = new StringBuilder();
            sb.Append(TomlSyntax.ARRAY_START_SYMBOL);
            if (ChildrenCount != 0)
            {
                var arrayStart = multiline ? $"{Environment.NewLine}  " : " ";
                var arraySeparator = multiline ? $"{TomlSyntax.ITEM_SEPARATOR}{Environment.NewLine}  " : $"{TomlSyntax.ITEM_SEPARATOR} ";
                var arrayEnd = multiline ? Environment.NewLine : " ";
                sb.Append(arrayStart)
                  .Append(arraySeparator.Join(RawArray.Select(n => n.ToInlineToml())))
                  .Append(arrayEnd);
            }
            sb.Append(TomlSyntax.ARRAY_END_SYMBOL);
            return sb.ToString();
        }

        public override void WriteTo(TextWriter tw, string name = null)
        {
            // If it's a normal array, write it as usual
            if (!IsTableArray)
            {
                tw.WriteLine(ToString(IsMultiline));
                return;
            }

            if (!(Comment is null))
            {
                tw.WriteLine();
                Comment.AsComment(tw);
            }
            tw.Write(TomlSyntax.ARRAY_START_SYMBOL);
            tw.Write(TomlSyntax.ARRAY_START_SYMBOL);
            tw.Write(name);
            tw.Write(TomlSyntax.ARRAY_END_SYMBOL);
            tw.Write(TomlSyntax.ARRAY_END_SYMBOL);
            tw.WriteLine();

            var first = true;

            foreach (var tomlNode in RawArray)
            {
                if (!(tomlNode is TomlTable tbl))
                    throw new TomlFormatException("The array is marked as array table but contains non-table nodes!");

                // Ensure it's parsed as a section
                tbl.IsInline = false;

                if (!first)
                {
                    tw.WriteLine();

                    Comment?.AsComment(tw);
                    tw.Write(TomlSyntax.ARRAY_START_SYMBOL);
                    tw.Write(TomlSyntax.ARRAY_START_SYMBOL);
                    tw.Write(name);
                    tw.Write(TomlSyntax.ARRAY_END_SYMBOL);
                    tw.Write(TomlSyntax.ARRAY_END_SYMBOL);
                    tw.WriteLine();
                }

                first = false;

                // Don't write section since it's already written here
                tbl.WriteTo(tw, name, false);
            }
        }
    }

    public class TomlTable : TomlNode
    {
        private Dictionary<string, TomlNode> children;
        internal bool isImplicit;

        public override bool HasValue { get; } = false;
        public override bool IsTable { get; } = true;
        public bool IsInline { get; set; }
        public Dictionary<string, TomlNode> RawTable => children ??= new Dictionary<string, TomlNode>();

        public override TomlNode this[string key]
        {
            get
            {
                if (RawTable.TryGetValue(key, out var result)) return result;
                var lazy = new TomlLazy(this);
                RawTable[key] = lazy;
                return lazy;
            }
            set => RawTable[key] = value;
        }

        public override int ChildrenCount => RawTable.Count;
        public override IEnumerable<TomlNode> Children => RawTable.Select(kv => kv.Value);
        public override IEnumerable<string> Keys => RawTable.Select(kv => kv.Key);
        public override bool HasKey(string key) => RawTable.ContainsKey(key);
        public override void Add(string key, TomlNode node) => RawTable.Add(key, node);
        public override bool TryGetNode(string key, out TomlNode node) => RawTable.TryGetValue(key, out node);
        public override void Delete(TomlNode node) => RawTable.Remove(RawTable.First(kv => kv.Value == node).Key);
        public override void Delete(string key) => RawTable.Remove(key);

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(TomlSyntax.INLINE_TABLE_START_SYMBOL);

            if (ChildrenCount != 0)
            {
                var collapsed = CollectCollapsedItems(normalizeOrder: false);

                if (collapsed.Count != 0)
                    sb.Append(' ')
                      .Append($"{TomlSyntax.ITEM_SEPARATOR} ".Join(collapsed.Select(n =>
                                                                       $"{n.Key} {TomlSyntax.KEY_VALUE_SEPARATOR} {n.Value.ToInlineToml()}")));
                sb.Append(' ');
            }

            sb.Append(TomlSyntax.INLINE_TABLE_END_SYMBOL);
            return sb.ToString();
        }

        private LinkedList<KeyValuePair<string, TomlNode>> CollectCollapsedItems(string prefix = "", int level = 0, bool normalizeOrder = true)
        {
            var nodes = new LinkedList<KeyValuePair<string, TomlNode>>();
            var postNodes = normalizeOrder ? new LinkedList<KeyValuePair<string, TomlNode>>() : nodes;

            foreach (var keyValuePair in RawTable)
            {
                var node = keyValuePair.Value;
                var key = keyValuePair.Key.AsKey();

                if (node is TomlTable tbl)
                {
                    var subnodes = tbl.CollectCollapsedItems($"{prefix}{key}.", level + 1, normalizeOrder);
                    // Write main table first before writing collapsed items
                    if (subnodes.Count == 0 && node.CollapseLevel == level)
                    {
                        postNodes.AddLast(new KeyValuePair<string, TomlNode>($"{prefix}{key}", node));
                    }
                    foreach (var kv in subnodes)
                        postNodes.AddLast(kv);
                }
                else if (node.CollapseLevel == level)
                    nodes.AddLast(new KeyValuePair<string, TomlNode>($"{prefix}{key}", node));
            }

            if (normalizeOrder)
                foreach (var kv in postNodes)
                    nodes.AddLast(kv);

            return nodes;
        }

        public override void WriteTo(TextWriter tw, string name = null) => WriteTo(tw, name, true);

        internal void WriteTo(TextWriter tw, string name, bool writeSectionName)
        {
            // The table is inline table
            if (IsInline && name != null)
            {
                tw.WriteLine(ToInlineToml());
                return;
            }

            var collapsedItems = CollectCollapsedItems();

            if (collapsedItems.Count == 0)
                return;

            var hasRealValues = !collapsedItems.All(n => n.Value is TomlTable { IsInline: false } or TomlArray { IsTableArray: true });

            Comment?.AsComment(tw);

            if (name != null && (hasRealValues || Comment != null) && writeSectionName)
            {
                tw.Write(TomlSyntax.ARRAY_START_SYMBOL);
                tw.Write(name);
                tw.Write(TomlSyntax.ARRAY_END_SYMBOL);
                tw.WriteLine();
            }
            else if (Comment != null) // Add some spacing between the first node and the comment
            {
                tw.WriteLine();
            }

            var namePrefix = name == null ? "" : $"{name}.";
            var first = true;

            foreach (var collapsedItem in collapsedItems)
            {
                var key = collapsedItem.Key;
                if (collapsedItem.Value is TomlArray { IsTableArray: true } or TomlTable { IsInline: false })
                {
                    if (!first) tw.WriteLine();
                    first = false;
                    collapsedItem.Value.WriteTo(tw, $"{namePrefix}{key}");
                    continue;
                }
                first = false;

                collapsedItem.Value.Comment?.AsComment(tw);
                tw.Write(key);
                tw.Write(' ');
                tw.Write(TomlSyntax.KEY_VALUE_SEPARATOR);
                tw.Write(' ');

                collapsedItem.Value.WriteTo(tw, $"{namePrefix}{key}");
            }
        }
    }

    internal class TomlLazy : TomlNode
    {
        private readonly TomlNode parent;
        private TomlNode replacement;

        public TomlLazy(TomlNode parent) => this.parent = parent;

        public override TomlNode this[int index]
        {
            get => Set<TomlArray>()[index];
            set => Set<TomlArray>()[index] = value;
        }

        public override TomlNode this[string key]
        {
            get => Set<TomlTable>()[key];
            set => Set<TomlTable>()[key] = value;
        }

        public override void Add(TomlNode node) => Set<TomlArray>().Add(node);

        public override void Add(string key, TomlNode node) => Set<TomlTable>().Add(key, node);

        public override void AddRange(IEnumerable<TomlNode> nodes) => Set<TomlArray>().AddRange(nodes);

        private TomlNode Set<T>() where T : TomlNode, new()
        {
            if (replacement != null) return replacement;

            var newNode = new T
            {
                Comment = Comment
            };

            if (parent.IsTable)
            {
                var key = parent.Keys.FirstOrDefault(s => parent.TryGetNode(s, out var node) && node.Equals(this));
                if (key == null) return default(T);

                parent[key] = newNode;
            }
            else if (parent.IsArray)
            {
                var index = parent.Children.TakeWhile(child => child != this).Count();
                if (index == parent.ChildrenCount) return default(T);
                parent[index] = newNode;
            }
            else
            {
                return default(T);
            }

            replacement = newNode;
            return newNode;
        }
    }

    #endregion

    #region Parser

    public class TOMLParser : IDisposable
    {
        public enum ParseState
        {
            None,
            KeyValuePair,
            SkipToNextLine,
            Table
        }

        private readonly TextReader reader;
        private ParseState currentState;
        private int line, col;
        private List<TomlSyntaxException> syntaxErrors;

        public TOMLParser(TextReader reader)
        {
            this.reader = reader;
            line = col = 0;
        }

        public bool ForceASCII { get; set; }

        public void Dispose() => reader?.Dispose();

        public TomlTable Parse()
        {
            syntaxErrors = new List<TomlSyntaxException>();
            line = col = 1;
            var rootNode = new TomlTable();
            var currentNode = rootNode;
            currentState = ParseState.None;
            var keyParts = new List<string>();
            var arrayTable = false;
            StringBuilder latestComment = null;
            var firstComment = true;

            int currentChar;
            while ((currentChar = reader.Peek()) >= 0)
            {
                var c = (char)currentChar;

                if (currentState == ParseState.None)
                {
                    // Skip white space
                    if (TomlSyntax.IsWhiteSpace(c)) goto consume_character;

                    if (TomlSyntax.IsNewLine(c))
                    {
                        // Check if there are any comments and so far no items being declared
                        if (latestComment != null && firstComment)
                        {
                            rootNode.Comment = latestComment.ToString().TrimEnd();
                            latestComment = null;
                            firstComment = false;
                        }

                        if (TomlSyntax.IsLineBreak(c))
                            AdvanceLine();

                        goto consume_character;
                    }

                    // Start of a comment; ignore until newline
                    if (c == TomlSyntax.COMMENT_SYMBOL)
                    {
                        latestComment ??= new StringBuilder();
                        latestComment.AppendLine(ParseComment());
                        AdvanceLine(1);
                        continue;
                    }

                    // Encountered a non-comment value. The comment must belong to it (ignore possible newlines)!
                    firstComment = false;

                    if (c == TomlSyntax.TABLE_START_SYMBOL)
                    {
                        currentState = ParseState.Table;
                        goto consume_character;
                    }

                    if (TomlSyntax.IsBareKey(c) || TomlSyntax.IsQuoted(c))
                    {
                        currentState = ParseState.KeyValuePair;
                    }
                    else
                    {
                        AddError($"Unexpected character \"{c}\"");
                        continue;
                    }
                }

                if (currentState == ParseState.KeyValuePair)
                {
                    var keyValuePair = ReadKeyValuePair(keyParts);

                    if (keyValuePair == null)
                    {
                        latestComment = null;
                        keyParts.Clear();

                        if (currentState != ParseState.None)
                            AddError("Failed to parse key-value pair!");
                        continue;
                    }

                    keyValuePair.Comment = latestComment?.ToString()?.TrimEnd();
                    var inserted = InsertNode(keyValuePair, currentNode, keyParts);
                    latestComment = null;
                    keyParts.Clear();
                    if (inserted)
                        currentState = ParseState.SkipToNextLine;
                    continue;
                }

                if (currentState == ParseState.Table)
                {
                    if (keyParts.Count == 0)
                    {
                        // We have array table
                        if (c == TomlSyntax.TABLE_START_SYMBOL)
                        {
                            // Consume the character
                            ConsumeChar();
                            arrayTable = true;
                        }

                        if (!ReadKeyName(ref keyParts, TomlSyntax.TABLE_END_SYMBOL))
                        {
                            keyParts.Clear();
                            continue;
                        }

                        if (keyParts.Count == 0)
                        {
                            AddError("Table name is emtpy.");
                            arrayTable = false;
                            latestComment = null;
                            keyParts.Clear();
                        }

                        continue;
                    }

                    if (c == TomlSyntax.TABLE_END_SYMBOL)
                    {
                        if (arrayTable)
                        {
                            // Consume the ending bracket so we can peek the next character
                            ConsumeChar();
                            var nextChar = reader.Peek();
                            if (nextChar < 0 || (char)nextChar != TomlSyntax.TABLE_END_SYMBOL)
                            {
                                AddError($"Array table {".".Join(keyParts)} has only one closing bracket.");
                                keyParts.Clear();
                                arrayTable = false;
                                latestComment = null;
                                continue;
                            }
                        }

                        currentNode = CreateTable(rootNode, keyParts, arrayTable);
                        if (currentNode != null)
                        {
                            currentNode.IsInline = false;
                            currentNode.Comment = latestComment?.ToString()?.TrimEnd();
                        }

                        keyParts.Clear();
                        arrayTable = false;
                        latestComment = null;

                        if (currentNode == null)
                        {
                            if (currentState != ParseState.None)
                                AddError("Error creating table array!");
                            // Reset a node to root in order to try and continue parsing
                            currentNode = rootNode;
                            continue;
                        }

                        currentState = ParseState.SkipToNextLine;
                        goto consume_character;
                    }

                    if (keyParts.Count != 0)
                    {
                        AddError($"Unexpected character \"{c}\"");
                        keyParts.Clear();
                        arrayTable = false;
                        latestComment = null;
                    }
                }

                if (currentState == ParseState.SkipToNextLine)
                {
                    if (TomlSyntax.IsWhiteSpace(c) || c == TomlSyntax.NEWLINE_CARRIAGE_RETURN_CHARACTER)
                        goto consume_character;

                    if (c is TomlSyntax.COMMENT_SYMBOL or TomlSyntax.NEWLINE_CHARACTER)
                    {
                        currentState = ParseState.None;
                        AdvanceLine();

                        if (c == TomlSyntax.COMMENT_SYMBOL)
                        {
                            col++;
                            ParseComment();
                            continue;
                        }

                        goto consume_character;
                    }

                    AddError($"Unexpected character \"{c}\" at the end of the line.");
                }

            consume_character:
                reader.Read();
                col++;
            }

            if (currentState != ParseState.None && currentState != ParseState.SkipToNextLine)
                AddError("Unexpected end of file!");

            if (syntaxErrors.Count > 0)
                throw new TomlParseException(rootNode, syntaxErrors);

            return rootNode;
        }

        private bool AddError(string message, bool skipLine = true)
        {
            syntaxErrors.Add(new TomlSyntaxException(message, currentState, line, col));
            // Skip the whole line in hope that it was only a single faulty value (and non-multiline one at that)
            if (skipLine)
            {
                reader.ReadLine();
                AdvanceLine(1);
            }
            currentState = ParseState.None;
            return false;
        }

        private void AdvanceLine(int startCol = 0)
        {
            line++;
            col = startCol;
        }

        private int ConsumeChar()
        {
            col++;
            return reader.Read();
        }

        #region Key-Value pair parsing

        /**
         * Reads a single key-value pair.
         * Assumes the cursor is at the first character that belong to the pair (including possible whitespace).
         * Consumes all characters that belong to the key and the value (ignoring possible trailing whitespace at the end).
         * 
         * Example:
         * foo = "bar"  ==> foo = "bar"
         * ^                           ^
         */
        private TomlNode ReadKeyValuePair(List<string> keyParts)
        {
            int cur;
            while ((cur = reader.Peek()) >= 0)
            {
                var c = (char)cur;

                if (TomlSyntax.IsQuoted(c) || TomlSyntax.IsBareKey(c))
                {
                    if (keyParts.Count != 0)
                    {
                        AddError("Encountered extra characters in key definition!");
                        return null;
                    }

                    if (!ReadKeyName(ref keyParts, TomlSyntax.KEY_VALUE_SEPARATOR))
                        return null;

                    continue;
                }

                if (TomlSyntax.IsWhiteSpace(c))
                {
                    ConsumeChar();
                    continue;
                }

                if (c == TomlSyntax.KEY_VALUE_SEPARATOR)
                {
                    ConsumeChar();
                    return ReadValue();
                }

                AddError($"Unexpected character \"{c}\" in key name.");
                return null;
            }

            return null;
        }

        /**
         * Reads a single value.
         * Assumes the cursor is at the first character that belongs to the value (including possible starting whitespace).
         * Consumes all characters belonging to the value (ignoring possible trailing whitespace at the end).
         * 
         * Example:
         * "test"  ==> "test"
         * ^                 ^
         */
        private TomlNode ReadValue(bool skipNewlines = false)
        {
            int cur;
            while ((cur = reader.Peek()) >= 0)
            {
                var c = (char)cur;

                if (TomlSyntax.IsWhiteSpace(c))
                {
                    ConsumeChar();
                    continue;
                }

                if (c == TomlSyntax.COMMENT_SYMBOL)
                {
                    AddError("No value found!");
                    return null;
                }

                if (TomlSyntax.IsNewLine(c))
                {
                    if (skipNewlines)
                    {
                        reader.Read();
                        AdvanceLine(1);
                        continue;
                    }

                    AddError("Encountered a newline when expecting a value!");
                    return null;
                }

                if (TomlSyntax.IsQuoted(c))
                {
                    var isMultiline = IsTripleQuote(c, out var excess);

                    // Error occurred in triple quote parsing
                    if (currentState == ParseState.None)
                        return null;

                    var value = isMultiline
                        ? ReadQuotedValueMultiLine(c)
                        : ReadQuotedValueSingleLine(c, excess);

                    if (value is null)
                        return null;

                    return new TomlString
                    {
                        Value = value,
                        IsMultiline = isMultiline,
                        PreferLiteral = c == TomlSyntax.LITERAL_STRING_SYMBOL
                    };
                }

                return c switch
                {
                    TomlSyntax.INLINE_TABLE_START_SYMBOL => ReadInlineTable(),
                    TomlSyntax.ARRAY_START_SYMBOL => ReadArray(),
                    var _ => ReadTomlValue()
                };
            }

            return null;
        }

        /**
         * Reads a single key name.
         * Assumes the cursor is at the first character belonging to the key (with possible trailing whitespace if `skipWhitespace = true`).
         * Consumes all the characters until the `until` character is met (but does not consume the character itself).
         * 
         * Example 1:
         * foo.bar  ==>  foo.bar           (`skipWhitespace = false`, `until = ' '`)
         * ^                    ^
         * 
         * Example 2:
         * [ foo . bar ] ==>  [ foo . bar ]     (`skipWhitespace = true`, `until = ']'`)
         * ^                             ^
         */
        private bool ReadKeyName(ref List<string> parts, char until)
        {
            var buffer = new StringBuilder();
            var quoted = false;
            var prevWasSpace = false;
            int cur;
            while ((cur = reader.Peek()) >= 0)
            {
                var c = (char)cur;

                // Reached the final character
                if (c == until) break;

                if (TomlSyntax.IsWhiteSpace(c))
                {
                    prevWasSpace = true;
                    goto consume_character;
                }

                if (buffer.Length == 0) prevWasSpace = false;

                if (c == TomlSyntax.SUBKEY_SEPARATOR)
                {
                    if (buffer.Length == 0 && !quoted)
                        return AddError($"Found an extra subkey separator in {".".Join(parts)}...");

                    parts.Add(buffer.ToString());
                    buffer.Length = 0;
                    quoted = false;
                    prevWasSpace = false;
                    goto consume_character;
                }

                if (prevWasSpace)
                    return AddError("Invalid spacing in key name");

                if (TomlSyntax.IsQuoted(c))
                {
                    if (quoted)

                        return AddError("Expected a subkey separator but got extra data instead!");

                    if (buffer.Length != 0)
                        return AddError("Encountered a quote in the middle of subkey name!");

                    // Consume the quote character and read the key name
                    col++;
                    buffer.Append(ReadQuotedValueSingleLine((char)reader.Read()));
                    quoted = true;
                    continue;
                }

                if (TomlSyntax.IsBareKey(c))
                {
                    buffer.Append(c);
                    goto consume_character;
                }

                // If we see an invalid symbol, let the next parser handle it
                break;

            consume_character:
                reader.Read();
                col++;
            }

            if (buffer.Length == 0 && !quoted)
                return AddError($"Found an extra subkey separator in {".".Join(parts)}...");

            parts.Add(buffer.ToString());

            return true;
        }

        #endregion

        #region Non-string value parsing

        /**
         * Reads the whole raw value until the first non-value character is encountered.
         * Assumes the cursor start position at the first value character and consumes all characters that may be related to the value.
         * Example:
         * 
         * 1_0_0_0  ==>  1_0_0_0
         * ^                    ^
         */
        private string ReadRawValue()
        {
            var result = new StringBuilder();
            int cur;
            while ((cur = reader.Peek()) >= 0)
            {
                var c = (char)cur;
                if (c == TomlSyntax.COMMENT_SYMBOL || TomlSyntax.IsNewLine(c) || TomlSyntax.IsValueSeparator(c)) break;
                result.Append(c);
                ConsumeChar();
            }

            // Replace trim with manual space counting?
            return result.ToString().Trim();
        }

        /**
         * Reads and parses a non-string, non-composite TOML value.
         * Assumes the cursor at the first character that is related to the value (with possible spaces).
         * Consumes all the characters that are related to the value.
         * 
         * Example
         * 1_0_0_0 # This is a comment
         * <newline>
         *     ==>  1_0_0_0 # This is a comment
         *     ^                                                  ^
         */
        private TomlNode ReadTomlValue()
        {
            var value = ReadRawValue();
            TomlNode node = value switch
            {
                var v when TomlSyntax.IsBoolean(v) => bool.Parse(v),
                var v when TomlSyntax.IsNaN(v) => double.NaN,
                var v when TomlSyntax.IsPosInf(v) => double.PositiveInfinity,
                var v when TomlSyntax.IsNegInf(v) => double.NegativeInfinity,
                var v when TomlSyntax.IsInteger(v) => long.Parse(value.RemoveAll(TomlSyntax.INT_NUMBER_SEPARATOR),
                                                                 CultureInfo.InvariantCulture),
                var v when TomlSyntax.IsFloat(v) => double.Parse(value.RemoveAll(TomlSyntax.INT_NUMBER_SEPARATOR),
                                                                 CultureInfo.InvariantCulture),
                var v when TomlSyntax.IsIntegerWithBase(v, out var numberBase) => new TomlInteger
                {
                    Value = Convert.ToInt64(value.Substring(2).RemoveAll(TomlSyntax.INT_NUMBER_SEPARATOR), numberBase),
                    IntegerBase = (TomlInteger.Base)numberBase
                },
                var _ => null
            };
            if (node != null) return node;

            // Normalize by removing space separator
            value = value.Replace(TomlSyntax.RFC3339EmptySeparator, TomlSyntax.ISO861Separator);
            if (StringUtils.TryParseDateTime<DateTime>(value,
                                             TomlSyntax.RFC3339LocalDateTimeFormats,
                                             DateTimeStyles.AssumeLocal,
                                             DateTime.TryParseExact,
                                             out var dateTimeResult,
                                             out var precision))
                return new TomlDateTimeLocal
                {
                    Value = dateTimeResult,
                    SecondsPrecision = precision
                };

            if (DateTime.TryParseExact(value,
                                       TomlSyntax.LocalDateFormat,
                                       CultureInfo.InvariantCulture,
                                       DateTimeStyles.AssumeLocal,
                                       out dateTimeResult))
                return new TomlDateTimeLocal
                {
                    Value = dateTimeResult,
                    Style = TomlDateTimeLocal.DateTimeStyle.Date
                };

            if (StringUtils.TryParseDateTime(value,
                                             TomlSyntax.RFC3339LocalTimeFormats,
                                             DateTimeStyles.AssumeLocal,
                                             DateTime.TryParseExact,
                                             out dateTimeResult,
                                             out precision))
                return new TomlDateTimeLocal
                {
                    Value = dateTimeResult,
                    Style = TomlDateTimeLocal.DateTimeStyle.Time,
                    SecondsPrecision = precision
                };

            if (StringUtils.TryParseDateTime<DateTimeOffset>(value,
                                                             TomlSyntax.RFC3339Formats,
                                                             DateTimeStyles.None,
                                                             DateTimeOffset.TryParseExact,
                                                             out var dateTimeOffsetResult,
                                                             out precision))
                return new TomlDateTimeOffset
                {
                    Value = dateTimeOffsetResult,
                    SecondsPrecision = precision
                };

            AddError($"Value \"{value}\" is not a valid TOML value!");
            return null;
        }

        /**
         * Reads an array value.
         * Assumes the cursor is at the start of the array definition. Reads all character until the array closing bracket.
         * 
         * Example:
         * [1, 2, 3]  ==>  [1, 2, 3]
         * ^                        ^
         */
        private TomlArray ReadArray()
        {
            // Consume the start of array character
            ConsumeChar();
            var result = new TomlArray();
            TomlNode currentValue = null;
            var expectValue = true;

            int cur;
            while ((cur = reader.Peek()) >= 0)
            {
                var c = (char)cur;

                if (c == TomlSyntax.ARRAY_END_SYMBOL)
                {
                    ConsumeChar();
                    break;
                }

                if (c == TomlSyntax.COMMENT_SYMBOL)
                {
                    reader.ReadLine();
                    AdvanceLine(1);
                    continue;
                }

                if (TomlSyntax.IsWhiteSpace(c) || TomlSyntax.IsNewLine(c))
                {
                    if (TomlSyntax.IsLineBreak(c))
                        AdvanceLine();
                    goto consume_character;
                }

                if (c == TomlSyntax.ITEM_SEPARATOR)
                {
                    if (currentValue == null)
                    {
                        AddError("Encountered multiple value separators");
                        return null;
                    }

                    result.Add(currentValue);
                    currentValue = null;
                    expectValue = true;
                    goto consume_character;
                }

                if (!expectValue)
                {
                    AddError("Missing separator between values");
                    return null;
                }
                currentValue = ReadValue(true);
                if (currentValue == null)
                {
                    if (currentState != ParseState.None)
                        AddError("Failed to determine and parse a value!");
                    return null;
                }
                expectValue = false;

                continue;
            consume_character:
                ConsumeChar();
            }

            if (currentValue != null) result.Add(currentValue);
            return result;
        }

        /**
         * Reads an inline table.
         * Assumes the cursor is at the start of the table definition. Reads all character until the table closing bracket.
         * 
         * Example:
         * { test = "foo", value = 1 }  ==>  { test = "foo", value = 1 }
         * ^                                                            ^
         */
        private TomlNode ReadInlineTable()
        {
            ConsumeChar();
            var result = new TomlTable { IsInline = true };
            TomlNode currentValue = null;
            var separator = false;
            var keyParts = new List<string>();
            int cur;
            while ((cur = reader.Peek()) >= 0)
            {
                var c = (char)cur;

                if (c == TomlSyntax.INLINE_TABLE_END_SYMBOL)
                {
                    ConsumeChar();
                    break;
                }

                if (c == TomlSyntax.COMMENT_SYMBOL)
                {
                    AddError("Incomplete inline table definition!");
                    return null;
                }

                if (TomlSyntax.IsNewLine(c))
                {
                    AddError("Inline tables are only allowed to be on single line");
                    return null;
                }

                if (TomlSyntax.IsWhiteSpace(c))
                    goto consume_character;

                if (c == TomlSyntax.ITEM_SEPARATOR)
                {
                    if (currentValue == null)
                    {
                        AddError("Encountered multiple value separators in inline table!");
                        return null;
                    }

                    if (!InsertNode(currentValue, result, keyParts))
                        return null;
                    keyParts.Clear();
                    currentValue = null;
                    separator = true;
                    goto consume_character;
                }

                separator = false;
                currentValue = ReadKeyValuePair(keyParts);
                continue;

            consume_character:
                ConsumeChar();
            }

            if (separator)
            {
                AddError("Trailing commas are not allowed in inline tables.");
                return null;
            }

            if (currentValue != null && !InsertNode(currentValue, result, keyParts))
                return null;

            return result;
        }

        #endregion

        #region String parsing

        /**
         * Checks if the string value a multiline string (i.e. a triple quoted string).
         * Assumes the cursor is at the first quote character. Consumes the least amount of characters needed to determine if the string is multiline.
         * 
         * If the result is false, returns the consumed character through the `excess` variable.
         * 
         * Example 1:
         * """test"""  ==>  """test"""
         * ^                   ^
         * 
         * Example 2:
         * "test"  ==>  "test"         (doesn't return the first quote)
         * ^             ^
         * 
         * Example 3:
         * ""  ==>  ""        (returns the extra `"` through the `excess` variable)
         * ^          ^
         */
        private bool IsTripleQuote(char quote, out char excess)
        {
            // Copypasta, but it's faster...

            int cur;
            // Consume the first quote
            ConsumeChar();
            if ((cur = reader.Peek()) < 0)
            {
                excess = '\0';
                return AddError("Unexpected end of file!");
            }

            if ((char)cur != quote)
            {
                excess = '\0';
                return false;
            }

            // Consume the second quote
            excess = (char)ConsumeChar();
            if ((cur = reader.Peek()) < 0 || (char)cur != quote) return false;

            // Consume the final quote
            ConsumeChar();
            excess = '\0';
            return true;
        }

        /**
         * A convenience method to process a single character within a quote.
         */
        private bool ProcessQuotedValueCharacter(char quote,
                                                 bool isNonLiteral,
                                                 char c,
                                                 StringBuilder sb,
                                                 ref bool escaped)
        {
            if (TomlSyntax.MustBeEscaped(c))
                return AddError($"The character U+{(int)c:X8} must be escaped in a string!");

            if (escaped)
            {
                sb.Append(c);
                escaped = false;
                return false;
            }

            if (c == quote)
            {
                if (!isNonLiteral && reader.Peek() == quote)
                {
                    reader.Read();
                    col++;
                    sb.Append(quote);
                    return false;
                }

                return true;
            }
            if (isNonLiteral && c == TomlSyntax.ESCAPE_SYMBOL)
                escaped = true;
            if (c == TomlSyntax.NEWLINE_CHARACTER)
                return AddError("Encountered newline in single line string!");

            sb.Append(c);
            return false;
        }

        /**
         * Reads a single-line string.
         * Assumes the cursor is at the first character that belongs to the string.
         * Consumes all characters that belong to the string (including the closing quote).
         * 
         * Example:
         * "test"  ==>  "test"
         * ^                 ^
         */
        private string ReadQuotedValueSingleLine(char quote, char initialData = '\0')
        {
            var isNonLiteral = quote == TomlSyntax.BASIC_STRING_SYMBOL;
            var sb = new StringBuilder();
            var escaped = false;

            if (initialData != '\0')
            {
                var shouldReturn =
                    ProcessQuotedValueCharacter(quote, isNonLiteral, initialData, sb, ref escaped);
                if (currentState == ParseState.None) return null;
                if (shouldReturn)
                    if (isNonLiteral)
                    {
                        if (sb.ToString().TryUnescape(out var res, out var ex)) return res;
                        AddError(ex.Message);
                        return null;
                    }
                    else
                        return sb.ToString();
            }

            int cur;
            var readDone = false;
            while ((cur = reader.Read()) >= 0)
            {
                // Consume the character
                col++;
                var c = (char)cur;
                readDone = ProcessQuotedValueCharacter(quote, isNonLiteral, c, sb, ref escaped);
                if (readDone)
                {
                    if (currentState == ParseState.None) return null;
                    break;
                }
            }

            if (!readDone)
            {
                AddError("Unclosed string.");
                return null;
            }

            if (!isNonLiteral) return sb.ToString();
            if (sb.ToString().TryUnescape(out var unescaped, out var unescapedEx)) return unescaped;
            AddError(unescapedEx.Message);
            return null;
        }

        /**
         * Reads a multiline string.
         * Assumes the cursor is at the first character that belongs to the string.
         * Consumes all characters that belong to the string and the three closing quotes.
         * 
         * Example:
         * """test"""  ==>  """test"""
         * ^                       ^
         */
        private string ReadQuotedValueMultiLine(char quote)
        {
            var isBasic = quote == TomlSyntax.BASIC_STRING_SYMBOL;
            var sb = new StringBuilder();
            var escaped = false;
            var skipWhitespace = false;
            var skipWhitespaceLineSkipped = false;
            var quotesEncountered = 0;
            var first = true;
            int cur;
            while ((cur = ConsumeChar()) >= 0)
            {
                var c = (char)cur;
                if (TomlSyntax.MustBeEscaped(c, true))
                {
                    AddError($"The character U+{(int)c:X8} must be escaped!");
                    return null;
                }
                // Trim the first newline
                if (first && TomlSyntax.IsNewLine(c))
                {
                    if (TomlSyntax.IsLineBreak(c))
                        first = false;
                    else
                        AdvanceLine();
                    continue;
                }

                first = false;
                //TODO: Reuse ProcessQuotedValueCharacter
                // Skip the current character if it is going to be escaped later
                if (escaped)
                {
                    sb.Append(c);
                    escaped = false;
                    continue;
                }

                // If we are currently skipping empty spaces, skip
                if (skipWhitespace)
                {
                    if (TomlSyntax.IsEmptySpace(c))
                    {
                        if (TomlSyntax.IsLineBreak(c))
                        {
                            skipWhitespaceLineSkipped = true;
                            AdvanceLine();
                        }
                        continue;
                    }

                    if (!skipWhitespaceLineSkipped)
                    {
                        AddError("Non-whitespace character after trim marker.");
                        return null;
                    }

                    skipWhitespaceLineSkipped = false;
                    skipWhitespace = false;
                }

                // If we encounter an escape sequence...
                if (isBasic && c == TomlSyntax.ESCAPE_SYMBOL)
                {
                    var next = reader.Peek();
                    var nc = (char)next;
                    if (next >= 0)
                    {
                        // ...and the next char is empty space, we must skip all whitespaces
                        if (TomlSyntax.IsEmptySpace(nc))
                        {
                            skipWhitespace = true;
                            continue;
                        }

                        // ...and we have \" or \, skip the character
                        if (nc == quote || nc == TomlSyntax.ESCAPE_SYMBOL) escaped = true;
                    }
                }

                // Count the consecutive quotes
                if (c == quote)
                    quotesEncountered++;
                else
                    quotesEncountered = 0;

                // If the are three quotes, count them as closing quotes
                if (quotesEncountered == 3) break;

                sb.Append(c);
            }

            // TOML actually allows to have five ending quotes like
            // """"" => "" belong to the string + """ is the actual ending
            quotesEncountered = 0;
            while ((cur = reader.Peek()) >= 0)
            {
                var c = (char)cur;
                if (c == quote && ++quotesEncountered < 3)
                {
                    sb.Append(c);
                    ConsumeChar();
                }
                else break;
            }

            // Remove last two quotes (third one wasn't included by default)
            sb.Length -= 2;
            if (!isBasic) return sb.ToString();
            if (sb.ToString().TryUnescape(out var res, out var ex)) return res;
            AddError(ex.Message);
            return null;
        }

        #endregion

        #region Node creation

        private bool InsertNode(TomlNode node, TomlNode root, IList<string> path)
        {
            var latestNode = root;
            if (path.Count > 1)
                for (var index = 0; index < path.Count - 1; index++)
                {
                    var subkey = path[index];
                    if (latestNode.TryGetNode(subkey, out var currentNode))
                    {
                        if (currentNode.HasValue)
                            return AddError($"The key {".".Join(path)} already has a value assigned to it!");
                    }
                    else
                    {
                        currentNode = new TomlTable();
                        latestNode[subkey] = currentNode;
                    }

                    latestNode = currentNode;
                    if (latestNode is TomlTable { IsInline: true })
                        return AddError($"Cannot assign {".".Join(path)} because it will edit an immutable table.");
                }

            if (latestNode.HasKey(path[path.Count - 1]))
                return AddError($"The key {".".Join(path)} is already defined!");
            latestNode[path[path.Count - 1]] = node;
            node.CollapseLevel = path.Count - 1;
            return true;
        }

        private TomlTable CreateTable(TomlNode root, IList<string> path, bool arrayTable)
        {
            if (path.Count == 0) return null;
            var latestNode = root;
            for (var index = 0; index < path.Count; index++)
            {
                var subkey = path[index];

                if (latestNode.TryGetNode(subkey, out var node))
                {
                    if (node.IsArray && arrayTable)
                    {
                        var arr = (TomlArray)node;

                        if (!arr.IsTableArray)
                        {
                            AddError($"The array {".".Join(path)} cannot be redefined as an array table!");
                            return null;
                        }

                        if (index == path.Count - 1)
                        {
                            latestNode = new TomlTable();
                            arr.Add(latestNode);
                            break;
                        }

                        latestNode = arr[arr.ChildrenCount - 1];
                        continue;
                    }

                    if (node is TomlTable { IsInline: true })
                    {
                        AddError($"Cannot create table {".".Join(path)} because it will edit an immutable table.");
                        return null;
                    }

                    if (node.HasValue)
                    {
                        if (!(node is TomlArray { IsTableArray: true } array))
                        {
                            AddError($"The key {".".Join(path)} has a value assigned to it!");
                            return null;
                        }

                        latestNode = array[array.ChildrenCount - 1];
                        continue;
                    }

                    if (index == path.Count - 1)
                    {
                        if (arrayTable && !node.IsArray)
                        {
                            AddError($"The table {".".Join(path)} cannot be redefined as an array table!");
                            return null;
                        }

                        if (node is TomlTable { isImplicit: false })
                        {
                            AddError($"The table {".".Join(path)} is defined multiple times!");
                            return null;
                        }
                    }
                }
                else
                {
                    if (index == path.Count - 1 && arrayTable)
                    {
                        var table = new TomlTable();
                        var arr = new TomlArray
                        {
                            IsTableArray = true
                        };
                        arr.Add(table);
                        latestNode[subkey] = arr;
                        latestNode = table;
                        break;
                    }

                    node = new TomlTable { isImplicit = true };
                    latestNode[subkey] = node;
                }

                latestNode = node;
            }

            var result = (TomlTable)latestNode;
            result.isImplicit = false;
            return result;
        }

        #endregion

        #region Misc parsing

        private string ParseComment()
        {
            ConsumeChar();
            var commentLine = reader.ReadLine()?.Trim() ?? "";
            if (commentLine.Any(ch => TomlSyntax.MustBeEscaped(ch)))
                AddError("Comment must not contain control characters other than tab.", false);
            return commentLine;
        }
        #endregion
    }

    #endregion

    public static class TOML
    {
        public static bool ForceASCII { get; set; } = false;

        public static TomlTable Parse(TextReader reader)
        {
            using var parser = new TOMLParser(reader) { ForceASCII = ForceASCII };
            return parser.Parse();
        }
    }

    #region Exception Types

    public class TomlFormatException : Exception
    {
        public TomlFormatException(string message) : base(message) { }
    }

    public class TomlParseException : Exception
    {
        public TomlParseException(TomlTable parsed, IEnumerable<TomlSyntaxException> exceptions) :
            base("TOML file contains format errors")
        {
            ParsedTable = parsed;
            SyntaxErrors = exceptions;
        }

        public TomlTable ParsedTable { get; }

        public IEnumerable<TomlSyntaxException> SyntaxErrors { get; }
    }

    public class TomlSyntaxException : Exception
    {
        public TomlSyntaxException(string message, TOMLParser.ParseState state, int line, int col) : base(message)
        {
            ParseState = state;
            Line = line;
            Column = col;
        }

        public TOMLParser.ParseState ParseState { get; }

        public int Line { get; }

        public int Column { get; }
    }

    #endregion

    #region Parse utilities

    internal static class TomlSyntax
    {
        #region Type Patterns

        public const string TRUE_VALUE = "true";
        public const string FALSE_VALUE = "false";
        public const string NAN_VALUE = "nan";
        public const string POS_NAN_VALUE = "+nan";
        public const string NEG_NAN_VALUE = "-nan";
        public const string INF_VALUE = "inf";
        public const string POS_INF_VALUE = "+inf";
        public const string NEG_INF_VALUE = "-inf";

        public static bool IsBoolean(string s) => s is TRUE_VALUE or FALSE_VALUE;

        public static bool IsPosInf(string s) => s is INF_VALUE or POS_INF_VALUE;

        public static bool IsNegInf(string s) => s == NEG_INF_VALUE;

        public static bool IsNaN(string s) => s is NAN_VALUE or POS_NAN_VALUE or NEG_NAN_VALUE;

        public static bool IsInteger(string s) => IntegerPattern.IsMatch(s);

        public static bool IsFloat(string s) => FloatPattern.IsMatch(s);

        public static bool IsIntegerWithBase(string s, out int numberBase)
        {
            numberBase = 10;
            var match = BasedIntegerPattern.Match(s);
            if (!match.Success) return false;
            IntegerBases.TryGetValue(match.Groups["base"].Value, out numberBase);
            return true;
        }

        /**
         * A pattern to verify the integer value according to the TOML specification.
         */
        public static readonly Regex IntegerPattern =
            new(@"^(\+|-)?(?!_)(0|(?!0)(_?\d)*)$", RegexOptions.Compiled);

        /**
         * A pattern to verify a special 0x, 0o and 0b forms of an integer according to the TOML specification.
         */
        public static readonly Regex BasedIntegerPattern =
            new(@"^0(?<base>x|b|o)(?!_)(_?[0-9A-F])*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /**
         * A pattern to verify the float value according to the TOML specification.
         */
        public static readonly Regex FloatPattern =
            new(@"^(\+|-)?(?!_)(0|(?!0)(_?\d)+)(((e(\+|-)?(?!_)(_?\d)+)?)|(\.(?!_)(_?\d)+(e(\+|-)?(?!_)(_?\d)+)?))$",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /**
         * A helper dictionary to map TOML base codes into the radii.
         */
        public static readonly Dictionary<string, int> IntegerBases = new()
        {
            ["x"] = 16,
            ["o"] = 8,
            ["b"] = 2
        };

        /**
         * A helper dictionary to map non-decimal bases to their TOML identifiers
         */
        public static readonly Dictionary<int, string> BaseIdentifiers = new()
        {
            [2] = "b",
            [8] = "o",
            [16] = "x"
        };

        public const string RFC3339EmptySeparator = " ";
        public const string ISO861Separator = "T";
        public const string ISO861ZeroZone = "+00:00";
        public const string RFC3339ZeroZone = "Z";

        /**
         * Valid date formats with timezone as per RFC3339.
         */
        public static readonly string[] RFC3339Formats =
        {
            "yyyy'-'MM-ddTHH':'mm':'ssK", "yyyy'-'MM-ddTHH':'mm':'ss'.'fK", "yyyy'-'MM-ddTHH':'mm':'ss'.'ffK",
            "yyyy'-'MM-ddTHH':'mm':'ss'.'fffK", "yyyy'-'MM-ddTHH':'mm':'ss'.'ffffK",
            "yyyy'-'MM-ddTHH':'mm':'ss'.'fffffK", "yyyy'-'MM-ddTHH':'mm':'ss'.'ffffffK",
            "yyyy'-'MM-ddTHH':'mm':'ss'.'fffffffK"
        };

        /**
         * Valid date formats without timezone (assumes local) as per RFC3339.
         */
        public static readonly string[] RFC3339LocalDateTimeFormats =
        {
            "yyyy'-'MM-ddTHH':'mm':'ss", "yyyy'-'MM-ddTHH':'mm':'ss'.'f", "yyyy'-'MM-ddTHH':'mm':'ss'.'ff",
            "yyyy'-'MM-ddTHH':'mm':'ss'.'fff", "yyyy'-'MM-ddTHH':'mm':'ss'.'ffff",
            "yyyy'-'MM-ddTHH':'mm':'ss'.'fffff", "yyyy'-'MM-ddTHH':'mm':'ss'.'ffffff",
            "yyyy'-'MM-ddTHH':'mm':'ss'.'fffffff"
        };

        /**
         * Valid full date format as per TOML spec.
         */
        public static readonly string LocalDateFormat = "yyyy'-'MM'-'dd";

        /**
         * Valid time formats as per TOML spec.
         */
        public static readonly string[] RFC3339LocalTimeFormats =
        {
            "HH':'mm':'ss", "HH':'mm':'ss'.'f", "HH':'mm':'ss'.'ff", "HH':'mm':'ss'.'fff", "HH':'mm':'ss'.'ffff",
            "HH':'mm':'ss'.'fffff", "HH':'mm':'ss'.'ffffff", "HH':'mm':'ss'.'fffffff"
        };

        #endregion

        #region Character definitions

        public const char ARRAY_END_SYMBOL = ']';
        public const char ITEM_SEPARATOR = ',';
        public const char ARRAY_START_SYMBOL = '[';
        public const char BASIC_STRING_SYMBOL = '\"';
        public const char COMMENT_SYMBOL = '#';
        public const char ESCAPE_SYMBOL = '\\';
        public const char KEY_VALUE_SEPARATOR = '=';
        public const char NEWLINE_CARRIAGE_RETURN_CHARACTER = '\r';
        public const char NEWLINE_CHARACTER = '\n';
        public const char SUBKEY_SEPARATOR = '.';
        public const char TABLE_END_SYMBOL = ']';
        public const char TABLE_START_SYMBOL = '[';
        public const char INLINE_TABLE_START_SYMBOL = '{';
        public const char INLINE_TABLE_END_SYMBOL = '}';
        public const char LITERAL_STRING_SYMBOL = '\'';
        public const char INT_NUMBER_SEPARATOR = '_';

        public static readonly char[] NewLineCharacters = { NEWLINE_CHARACTER, NEWLINE_CARRIAGE_RETURN_CHARACTER };

        public static bool IsQuoted(char c) => c is BASIC_STRING_SYMBOL or LITERAL_STRING_SYMBOL;

        public static bool IsWhiteSpace(char c) => c is ' ' or '\t';

        public static bool IsNewLine(char c) => c is NEWLINE_CHARACTER or NEWLINE_CARRIAGE_RETURN_CHARACTER;

        public static bool IsLineBreak(char c) => c == NEWLINE_CHARACTER;

        public static bool IsEmptySpace(char c) => IsWhiteSpace(c) || IsNewLine(c);

        public static bool IsBareKey(char c) =>
            c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-';

        public static bool MustBeEscaped(char c, bool allowNewLines = false)
        {
            var result = c is (>= '\u0000' and <= '\u0008') or '\u000b' or '\u000c' or (>= '\u000e' and <= '\u001f') or '\u007f';
            if (!allowNewLines)
                result |= c is >= '\u000a' and <= '\u000e';
            return result;
        }

        public static bool IsValueSeparator(char c) =>
            c is ITEM_SEPARATOR or ARRAY_END_SYMBOL or INLINE_TABLE_END_SYMBOL;

        #endregion
    }

    internal static class StringUtils
    {
        public static string AsKey(this string key)
        {
            var quote = key == string.Empty || key.Any(c => !TomlSyntax.IsBareKey(c));
            return !quote ? key : $"{TomlSyntax.BASIC_STRING_SYMBOL}{key.Escape()}{TomlSyntax.BASIC_STRING_SYMBOL}";
        }

        public static string Join(this string self, IEnumerable<string> subItems)
        {
            var sb = new StringBuilder();
            var first = true;

            foreach (var subItem in subItems)
            {
                if (!first) sb.Append(self);
                first = false;
                sb.Append(subItem);
            }

            return sb.ToString();
        }

        public delegate bool TryDateParseDelegate<T>(string s, string format, IFormatProvider ci, DateTimeStyles dts, out T dt);

        public static bool TryParseDateTime<T>(string s,
                                               string[] formats,
                                               DateTimeStyles styles,
                                               TryDateParseDelegate<T> parser,
                                               out T dateTime,
                                               out int parsedFormat)
        {
            parsedFormat = 0;
            dateTime = default;
            for (var i = 0; i < formats.Length; i++)
            {
                var format = formats[i];
                if (!parser(s, format, CultureInfo.InvariantCulture, styles, out dateTime)) continue;
                parsedFormat = i;
                return true;
            }

            return false;
        }

        public static void AsComment(this string self, TextWriter tw)
        {
            foreach (var line in self.Split(TomlSyntax.NEWLINE_CHARACTER))
                tw.WriteLine($"{TomlSyntax.COMMENT_SYMBOL} {line.Trim()}");
        }

        public static string RemoveAll(this string txt, char toRemove)
        {
            var sb = new StringBuilder(txt.Length);
            foreach (var c in txt.Where(c => c != toRemove))
                sb.Append(c);
            return sb.ToString();
        }

        public static string Escape(this string txt, bool escapeNewlines = true)
        {
            var stringBuilder = new StringBuilder(txt.Length + 2);
            for (var i = 0; i < txt.Length; i++)
            {
                var c = txt[i];

                static string CodePoint(string txt, ref int i, char c) => char.IsSurrogatePair(txt, i)
                    ? $"\\U{char.ConvertToUtf32(txt, i++):X8}"
                    : $"\\u{(ushort)c:X4}";

                stringBuilder.Append(c switch
                {
                    '\b' => @"\b",
                    '\t' => @"\t",
                    '\n' when escapeNewlines => @"\n",
                    '\f' => @"\f",
                    '\r' when escapeNewlines => @"\r",
                    '\\' => @"\\",
                    '\"' => @"\""",
                    var _ when TomlSyntax.MustBeEscaped(c, !escapeNewlines) || TOML.ForceASCII && c > sbyte.MaxValue =>
                        CodePoint(txt, ref i, c),
                    var _ => c
                });
            }

            return stringBuilder.ToString();
        }

        public static bool TryUnescape(this string txt, out string unescaped, out Exception exception)
        {
            try
            {
                exception = null;
                unescaped = txt.Unescape();
                return true;
            }
            catch (Exception e)
            {
                exception = e;
                unescaped = null;
                return false;
            }
        }

        public static string Unescape(this string txt)
        {
            if (string.IsNullOrEmpty(txt)) return txt;
            var stringBuilder = new StringBuilder(txt.Length);
            for (var i = 0; i < txt.Length;)
            {
                var num = txt.IndexOf('\\', i);
                var next = num + 1;
                if (num < 0 || num == txt.Length - 1) num = txt.Length;
                stringBuilder.Append(txt, i, num - i);
                if (num >= txt.Length) break;
                var c = txt[next];

                static string CodePoint(int next, string txt, ref int num, int size)
                {
                    if (next + size >= txt.Length) throw new Exception("Undefined escape sequence!");
                    num += size;
                    return char.ConvertFromUtf32(Convert.ToInt32(txt.Substring(next + 1, size), 16));
                }

                stringBuilder.Append(c switch
                {
                    'b' => "\b",
                    't' => "\t",
                    'n' => "\n",
                    'f' => "\f",
                    'r' => "\r",
                    '\'' => "\'",
                    '\"' => "\"",
                    '\\' => "\\",
                    'u' => CodePoint(next, txt, ref num, 4),
                    'U' => CodePoint(next, txt, ref num, 8),
                    var _ => throw new Exception("Undefined escape sequence!")
                });
                i = num + 2;
            }

            return stringBuilder.ToString();
        }
    }

    #endregion
}
