using UnityEditor;
using UnityEngine;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Constants;

namespace MCPForUnity.Editor.MenuItems
{
    /// <summary>
    /// Menu items for custom tool management
    /// </summary>
    public static class CustomToolsMenuItems
    {
        [MenuItem("Window/MCP For Unity/Custom Tools/Register All Tools")]
        public static void RegisterAllTools()
        {
            CustomToolRegistrationProcessor.RegisterAllTools();
        }

        [MenuItem("Window/MCP For Unity/Custom Tools/Force Re-registration")]
        public static void ForceReregistration()
        {
            CustomToolRegistrationProcessor.ForceReregistration();
        }

        [MenuItem("Window/MCP For Unity/Custom Tools/Show Tool Info")]
        public static void ShowToolInfo()
        {
            string info = CustomToolRegistrationProcessor.GetToolInfo();
            Debug.Log($"<b>MCP Custom Tools Info:</b>\n{info}");

            // Also show in dialog
            EditorUtility.DisplayDialog("MCP Custom Tools", info, "OK");
        }

        [MenuItem("Window/MCP For Unity/Custom Tools/Enable Registration")]
        public static void EnableRegistration()
        {
            CustomToolRegistrationProcessor.IsRegistrationEnabled = true;
            EditorPrefs.SetBool(EditorPrefKeys.CustomToolRegistrationEnabled, true);
            Debug.Log("MCP Custom Tool Registration enabled");
        }

        [MenuItem("Window/MCP For Unity/Custom Tools/Disable Registration")]
        public static void DisableRegistration()
        {
            CustomToolRegistrationProcessor.IsRegistrationEnabled = false;
            EditorPrefs.SetBool(EditorPrefKeys.CustomToolRegistrationEnabled, false);
            Debug.Log("MCP Custom Tool Registration disabled");
        }

        [MenuItem("Window/MCP For Unity/Custom Tools/Enable Registration", true)]
        public static bool EnableRegistrationValidate()
        {
            return !CustomToolRegistrationProcessor.IsRegistrationEnabled;
        }

        [MenuItem("Window/MCP For Unity/Custom Tools/Disable Registration", true)]
        public static bool DisableRegistrationValidate()
        {
            return CustomToolRegistrationProcessor.IsRegistrationEnabled;
        }
    }
}
