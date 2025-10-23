using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Setup;
using MCPForUnity.Editor.Windows;
using UnityEditor;

namespace MCPForUnity.Editor
{
    /// <summary>
    /// Centralized menu items for MCP For Unity
    /// </summary>
    public static class MCPForUnityMenu
    {
        // ========================================
        // Main Menu Items
        // ========================================

        /// <summary>
        /// Show the setup wizard
        /// </summary>
        [MenuItem("Window/MCP For Unity/Setup Wizard", priority = 1)]
        public static void ShowSetupWizard()
        {
            SetupWizard.ShowSetupWizard();
        }

        /// <summary>
        /// Open the main MCP For Unity window
        /// </summary>
        [MenuItem("Window/MCP For Unity/Open MCP Window %#m", priority = 2)]
        public static void OpenMCPWindow()
        {
            MCPForUnityEditorWindow.ShowWindow();
        }

        // ========================================
        // Tool Sync Menu Items
        // ========================================

        /// <summary>
        /// Reimport all Python files in the project
        /// </summary>
        [MenuItem("Window/MCP For Unity/Tool Sync/Reimport Python Files", priority = 99)]
        public static void ReimportPythonFiles()
        {
            PythonToolSyncProcessor.ReimportPythonFiles();
        }

        /// <summary>
        /// Manually sync Python tools to the MCP server
        /// </summary>
        [MenuItem("Window/MCP For Unity/Tool Sync/Sync Python Tools", priority = 100)]
        public static void SyncPythonTools()
        {
            PythonToolSyncProcessor.ManualSync();
        }

        /// <summary>
        /// Toggle auto-sync for Python tools
        /// </summary>
        [MenuItem("Window/MCP For Unity/Tool Sync/Auto-Sync Python Tools", priority = 101)]
        public static void ToggleAutoSync()
        {
            PythonToolSyncProcessor.ToggleAutoSync();
        }

        /// <summary>
        /// Validate menu item (shows checkmark when auto-sync is enabled)
        /// </summary>
        [MenuItem("Window/MCP For Unity/Tool Sync/Auto-Sync Python Tools", true, priority = 101)]
        public static bool ToggleAutoSyncValidate()
        {
            return PythonToolSyncProcessor.ToggleAutoSyncValidate();
        }
    }
}
