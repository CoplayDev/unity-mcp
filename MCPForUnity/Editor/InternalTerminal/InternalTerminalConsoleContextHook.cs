using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace WTL.InternalTerminal.Editor
{
    [InitializeOnLoad]
    internal static class InternalTerminalConsoleContextHook
    {
        private static readonly EditorApplication.CallbackFunction GlobalEventHandler = HandleGlobalEvent;

        static InternalTerminalConsoleContextHook()
        {
            SubscribeGlobalEventHandler();
        }

        private static void SubscribeGlobalEventHandler()
        {
            var field = typeof(EditorApplication).GetField("globalEventHandler", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
            {
                return;
            }

            var current = field.GetValue(null) as EditorApplication.CallbackFunction;
            current -= GlobalEventHandler;
            current += GlobalEventHandler;
            field.SetValue(null, current);
        }

        private static void HandleGlobalEvent()
        {
            var current = Event.current;
            if (current == null || !IsContextMenuEvent(current) || !IsConsoleWindow(EditorWindow.focusedWindow))
            {
                return;
            }

            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Add to Agent"), false, () =>
            {
                InternalTerminalAgentContext.AddConsoleEntryToAgent();
            });
            menu.AddItem(new GUIContent("Add All Console to Agent"), false, () =>
            {
                InternalTerminalAgentContext.AddConsoleToAgent();
            });
            menu.ShowAsContext();
            current.Use();
        }

        private static bool IsContextMenuEvent(Event current)
        {
            return current.type == EventType.ContextClick
                || (current.type == EventType.MouseDown && current.button == 1);
        }

        private static bool IsConsoleWindow(EditorWindow window)
        {
            return window != null && window.GetType().FullName == "UnityEditor.ConsoleWindow";
        }
    }
}
