using MCPForUnity.Editor.Setup;
using MCPForUnity.Editor.Windows;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.MenuItems
{
    public static class MCPForUnityMenu
    {
        [MenuItem("Window/MCP For Unity/Setup Window", priority = 1)]
        public static void ShowSetupWindow()
        {
            SetupWindowService.ShowSetupWindow();
        }

        [MenuItem("Window/MCP For Unity/Toggle MCP Window %#m", priority = 2)]
        public static void ToggleMCPWindow()
        {
            if (EditorWindow.HasOpenInstances<MCPForUnityEditorWindow>())
            {
                foreach (
                    var window in UnityEngine.Resources.FindObjectsOfTypeAll<MCPForUnityEditorWindow>()
                )
                {
                    if (window == null)
                        continue;

                    try
                    {
                        // Try the normal Close
                        window.Close();
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning(
                            $"MCP window Close() threw: {ex.GetType().Name}: {ex.Message}\nFalling back to DestroyImmediate."
                        );
                        try
                        {
                            UnityEngine.Object.DestroyImmediate(window);
                        }
                        catch
                        {
                            // Ignore any exceptions during DestroyImmediate
                        }
                    }
                }
            }
            else
            {
                MCPForUnityEditorWindow.ShowWindow();
            }
        }
    }
}
