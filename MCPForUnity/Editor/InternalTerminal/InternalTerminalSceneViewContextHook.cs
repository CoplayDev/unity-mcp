using UnityEditor;
using UnityEngine;

namespace WTL.InternalTerminal.Editor
{
    [InitializeOnLoad]
    internal static class InternalTerminalSceneViewContextHook
    {
        static InternalTerminalSceneViewContextHook()
        {
            SceneView.duringSceneGui -= HandleSceneGui;
            SceneView.duringSceneGui += HandleSceneGui;
        }

        private static void HandleSceneGui(SceneView sceneView)
        {
            var current = Event.current;
            if (current == null || !IsContextMenuEvent(current) || Selection.objects == null || Selection.objects.Length == 0)
            {
                return;
            }

            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Add to Agent"), false, () =>
            {
                InternalTerminalAgentContext.AddSelectionToAgent();
            });
            menu.ShowAsContext();
            current.Use();
        }

        private static bool IsContextMenuEvent(Event current)
        {
            return current.type == EventType.ContextClick
                || (current.type == EventType.MouseDown && current.button == 1);
        }
    }
}
