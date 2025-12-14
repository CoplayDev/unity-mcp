using MCPForUnity.Editor.Windows;
using UnityEditor;

namespace MCPForUnity.Editor.MenuItems
{
    public static class MCPForUnityMenu
    {
        [MenuItem("Window/MCP For Unity %#m", priority = 1)]
        public static void ShowMCPWindow()
        {
            MCPForUnityEditorWindow.ShowWindow();
        }
    }
}

