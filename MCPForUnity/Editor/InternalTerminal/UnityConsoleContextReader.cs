using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace WTL.InternalTerminal.Editor
{
    internal static class UnityConsoleContextReader
    {
        public sealed class ConsoleEntry
        {
            public string Level;
            public string File;
            public int Line;
            public string Message;
            public string Stack;
        }

        public static string ReadFormattedConsole()
        {
            return ReadFormattedConsole(false);
        }

        public static string ReadFormattedConsole(bool activeOnly)
        {
            try
            {
                var entries = ReadEntries(activeOnly);
                return InternalTerminalContextFormatter.FormatConsoleBlock(entries, DateTimeOffset.Now);
            }
            catch (Exception exception)
            {
                return InternalTerminalContextFormatter.FormatConsoleFailure(exception.Message);
            }
        }

        private static List<ConsoleEntry> ReadEntries(bool activeOnly)
        {
            var logEntriesType = typeof(EditorWindow).Assembly.GetType("UnityEditor.LogEntries");
            var logEntryType = typeof(EditorWindow).Assembly.GetType("UnityEditor.LogEntry");
            if (logEntriesType == null || logEntryType == null)
            {
                throw new InvalidOperationException("UnityEditor.LogEntries reflection API was not found.");
            }

            var startGettingEntries = logEntriesType.GetMethod("StartGettingEntries", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var endGettingEntries = logEntriesType.GetMethod("EndGettingEntries", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var getEntryInternal = logEntriesType.GetMethod("GetEntryInternal", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var getCount = logEntriesType.GetMethod("GetCount", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (getEntryInternal == null || getCount == null)
            {
                throw new InvalidOperationException("Unity Console entry accessors were not found.");
            }

            var selectedRows = activeOnly ? GetSelectedRows(logEntriesType) : new List<int>();
            var rowCount = Convert.ToInt32(getCount.Invoke(null, null));
            if (selectedRows.Count == 0)
            {
                if (activeOnly)
                {
                    selectedRows = GetActiveRows(logEntriesType);
                }

                if (selectedRows.Count == 0 && !activeOnly)
                {
                    for (var row = 0; row < rowCount; row++)
                    {
                        selectedRows.Add(row);
                    }
                }
            }

            var entries = new List<ConsoleEntry>();
            var started = false;
            try
            {
                if (startGettingEntries != null)
                {
                    startGettingEntries.Invoke(null, null);
                    started = true;
                }

                foreach (var row in selectedRows)
                {
                    if (row < 0 || row >= rowCount)
                    {
                        continue;
                    }

                    var entryObject = Activator.CreateInstance(logEntryType);
                    getEntryInternal.Invoke(null, new[] { (object)row, entryObject });
                    entries.Add(ConvertEntry(entryObject));
                }
            }
            finally
            {
                if (started && endGettingEntries != null)
                {
                    endGettingEntries.Invoke(null, null);
                }
            }

            return entries;
        }

        private static List<int> GetSelectedRows(Type logEntriesType)
        {
            var rows = GetSelectedRowsFromConsoleWindows();
            if (rows.Count > 0)
            {
                return rows;
            }

            var getFirstSelectedEntryPos = logEntriesType.GetMethod("GetFirstSelectedEntryPos", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var getNextSelectedEntryPos = logEntriesType.GetMethod("GetNextSelectedEntryPos", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (getFirstSelectedEntryPos == null || getNextSelectedEntryPos == null)
            {
                return rows;
            }

            var row = Convert.ToInt32(getFirstSelectedEntryPos.Invoke(null, null));
            while (row >= 0)
            {
                rows.Add(row);
                row = Convert.ToInt32(getNextSelectedEntryPos.Invoke(null, new object[] { row }));
            }

            return rows;
        }

        private static List<int> GetSelectedRowsFromConsoleWindows()
        {
            var rows = new List<int>();
            var consoleWindowType = typeof(EditorWindow).Assembly.GetType("UnityEditor.ConsoleWindow");
            if (consoleWindowType == null)
            {
                return rows;
            }

            var windows = Resources.FindObjectsOfTypeAll(consoleWindowType);
            foreach (var window in windows)
            {
                var listView = GetFieldValue(window, "m_ListView");
                var selectedItems = listView == null ? null : GetFieldValue(listView, "selectedItems") as bool[];
                if (selectedItems == null)
                {
                    continue;
                }

                for (var row = 0; row < selectedItems.Length; row++)
                {
                    if (selectedItems[row])
                    {
                        rows.Add(row);
                    }
                }

                if (rows.Count > 0)
                {
                    return rows;
                }
            }

            return rows;
        }

        private static List<int> GetActiveRows(Type logEntriesType)
        {
            var rows = new List<int>();
            var consoleWindowType = typeof(EditorWindow).Assembly.GetType("UnityEditor.ConsoleWindow");
            if (consoleWindowType == null)
            {
                return rows;
            }

            var windows = Resources.FindObjectsOfTypeAll(consoleWindowType);
            foreach (var window in windows)
            {
                var listView = GetFieldValue(window, "m_ListView");
                var row = listView == null ? -1 : GetIntOrDefault(listView, "row", -1);
                if (row >= 0)
                {
                    rows.Add(row);
                    return rows;
                }

                var lastActiveEntryIndex = GetIntOrDefault(window, "m_LastActiveEntryIndex", -1);
                var activeRow = GetRowIndexFromGlobalIndex(logEntriesType, lastActiveEntryIndex);
                if (activeRow >= 0)
                {
                    rows.Add(activeRow);
                    return rows;
                }
            }

            return rows;
        }

        private static int GetRowIndexFromGlobalIndex(Type logEntriesType, int globalLineIndex)
        {
            if (globalLineIndex < 0)
            {
                return -1;
            }

            var getEntryRowIndex = logEntriesType.GetMethod("GetEntryRowIndex", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (getEntryRowIndex == null)
            {
                return -1;
            }

            try
            {
                return Convert.ToInt32(getEntryRowIndex.Invoke(null, new object[] { globalLineIndex, -1 }));
            }
            catch (TargetParameterCountException)
            {
                return Convert.ToInt32(getEntryRowIndex.Invoke(null, new object[] { globalLineIndex }));
            }
        }

        private static ConsoleEntry ConvertEntry(object entryObject)
        {
            var message = GetString(entryObject, "message");
            var file = GetString(entryObject, "file");
            var line = GetInt(entryObject, "line");
            var stackStart = GetInt(entryObject, "callstackTextStartUTF16");

            return new ConsoleEntry
            {
                Level = GetLevel(GetInt(entryObject, "mode")),
                File = file,
                Line = line,
                Message = ExtractMessage(message, stackStart),
                Stack = ExtractStack(message, stackStart)
            };
        }

        private static string ExtractMessage(string message, int stackStart)
        {
            if (string.IsNullOrEmpty(message))
            {
                return string.Empty;
            }

            if (stackStart > 0 && stackStart <= message.Length)
            {
                return message.Substring(0, stackStart).TrimEnd();
            }

            return message.TrimEnd();
        }

        private static string ExtractStack(string message, int stackStart)
        {
            if (string.IsNullOrEmpty(message) || stackStart <= 0 || stackStart > message.Length)
            {
                return string.Empty;
            }

            return message.Substring(stackStart).Trim();
        }

        private static string GetLevel(int mode)
        {
            if ((mode & (1 << 0)) != 0 || (mode & (1 << 4)) != 0)
            {
                return "error";
            }

            if ((mode & (1 << 1)) != 0)
            {
                return "warning";
            }

            return "log";
        }

        private static string GetString(object target, string name)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return field == null ? string.Empty : Convert.ToString(field.GetValue(target));
        }

        private static int GetInt(object target, string name)
        {
            return GetIntOrDefault(target, name, 0);
        }

        private static int GetIntOrDefault(object target, string name, int defaultValue)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
            {
                return defaultValue;
            }

            var value = field.GetValue(target);
            return value == null ? defaultValue : Convert.ToInt32(value);
        }

        private static object GetFieldValue(object target, string name)
        {
            if (target == null)
            {
                return null;
            }

            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return field == null ? null : field.GetValue(target);
        }
    }
}
