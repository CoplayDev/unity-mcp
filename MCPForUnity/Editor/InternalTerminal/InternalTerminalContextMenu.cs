using UnityEditor;
using UnityEngine;

namespace WTL.InternalTerminal.Editor
{
    internal static class InternalTerminalContextMenu
    {
        private const int MenuPriority = -10000;

        [MenuItem("Assets/Add to Agent", false, MenuPriority)]
        private static void AddAssetsToAgent()
        {
            PasteSelectionToAgent();
        }

        [MenuItem("Assets/Add to Agent", true)]
        private static bool ValidateAddAssetsToAgent()
        {
            return Selection.objects != null && Selection.objects.Length > 0;
        }

        [MenuItem("GameObject/Add to Agent", false, MenuPriority)]
        private static void AddGameObjectsToAgent()
        {
            PasteSelectionToAgent();
        }

        [MenuItem("GameObject/Add to Agent", true)]
        private static bool ValidateAddGameObjectsToAgent()
        {
            return Selection.gameObjects != null && Selection.gameObjects.Length > 0;
        }

        [MenuItem("CONTEXT/Component/Add to Agent", false, MenuPriority)]
        private static void AddComponentToAgent(MenuCommand command)
        {
            var component = command.context as Component;
            if (component != null)
            {
                InternalTerminalAgentContext.AddObjectToAgent(component);
            }
        }

        [MenuItem("Window/WTL/Internal Terminal/Add Selection to Agent", false, 20)]
        private static void AddSelectionToAgent()
        {
            PasteSelectionToAgent();
        }

        [MenuItem("Window/WTL/Internal Terminal/Add Console to Agent", false, 21)]
        private static void AddConsoleToAgent()
        {
            InternalTerminalAgentContext.AddConsoleToAgent();
        }

        [MenuItem("Window/WTL/Internal Terminal/Add Console Entry to Agent", false, 22)]
        private static void AddConsoleEntryToAgent()
        {
            InternalTerminalAgentContext.AddConsoleEntryToAgent();
        }

        private static void PasteSelectionToAgent()
        {
            InternalTerminalAgentContext.AddSelectionToAgent();
        }
    }
}
