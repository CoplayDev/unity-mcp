using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace WTL.InternalTerminal.Editor
{
    public static class InternalTerminalAgentContext
    {
        public static void AddSelectionToAgent()
        {
            AddObjectsToAgent(Selection.objects);
        }

        public static void AddObjectToAgent(Object item)
        {
            var text = FormatObject(item);
            if (!string.IsNullOrEmpty(text))
            {
                PasteTextToAgent(text + "\n");
            }
        }

        public static void AddObjectsToAgent(IEnumerable<Object> items)
        {
            PasteTextToAgent(FormatObjects(items));
        }

        public static void AddAssetPathToAgent(string path)
        {
            var text = FormatAssetPath(path);
            if (!string.IsNullOrEmpty(text))
            {
                PasteTextToAgent(text + "\n");
            }
        }

        public static void AddConsoleToAgent()
        {
            PasteTextToAgent(UnityConsoleContextReader.ReadFormattedConsole());
        }

        public static void AddConsoleEntryToAgent()
        {
            PasteTextToAgent(UnityConsoleContextReader.ReadFormattedConsole(true));
        }

        public static void PasteTextToAgent(string text)
        {
            InternalTerminalWindow.PasteToActiveTerminal(text);
        }

        public static string FormatObject(Object item)
        {
            return InternalTerminalContextFormatter.FormatObject(item);
        }

        public static string FormatObjects(IEnumerable<Object> items)
        {
            return InternalTerminalContextFormatter.FormatObjects(items);
        }

        public static string FormatAssetPath(string path)
        {
            return InternalTerminalContextFormatter.FormatAssetPath(path);
        }
    }
}
