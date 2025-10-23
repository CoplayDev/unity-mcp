using System.IO;
using System.Linq;
using MCPForUnity.Editor.Data;
using MCPForUnity.Editor.Services;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Automatically syncs Python tools to the MCP server when:
    /// - PythonToolsAsset is modified
    /// - Python files are imported/reimported
    /// - Unity starts up
    /// </summary>
    [InitializeOnLoad]
    public class PythonToolSyncProcessor : AssetPostprocessor
    {
        private const string SyncEnabledKey = "MCPForUnity.AutoSyncEnabled";
        private static bool _isSyncing = false;

        static PythonToolSyncProcessor()
        {
            // Sync on Unity startup
            EditorApplication.delayCall += () =>
            {
                if (IsAutoSyncEnabled())
                {
                    SyncAllTools();
                }
            };
        }

        /// <summary>
        /// Called after any assets are imported, deleted, or moved
        /// </summary>
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            // Prevent infinite loop - don't process if we're currently syncing
            if (_isSyncing || !IsAutoSyncEnabled())
                return;

            bool needsSync = false;

            // Only check for .py file changes, not PythonToolsAsset changes
            // (PythonToolsAsset changes are internal state updates from syncing)
            foreach (string path in importedAssets.Concat(movedAssets))
            {
                // Check if any .py files were modified
                if (path.EndsWith(".py"))
                {
                    needsSync = true;
                    break;
                }
            }

            // Check if any .py files were deleted
            if (!needsSync && deletedAssets.Any(path => path.EndsWith(".py")))
            {
                needsSync = true;
            }

            if (needsSync)
            {
                SyncAllTools();
            }
        }

        /// <summary>
        /// Syncs all Python tools from all PythonToolsAsset instances to the MCP server
        /// </summary>
        public static void SyncAllTools()
        {
            // Prevent re-entrant calls
            if (_isSyncing)
            {
                McpLog.Warn("Sync already in progress, skipping...");
                return;
            }

            _isSyncing = true;
            try
            {
                if (!ServerPathResolver.TryFindEmbeddedServerSource(out string srcPath))
                {
                    McpLog.Warn("Cannot sync Python tools: MCP server source not found");
                    return;
                }

                string toolsDir = Path.Combine(srcPath, "tools", "custom");

                var result = MCPServiceLocator.ToolSync.SyncProjectTools(toolsDir);

                if (result.Success)
                {
                    if (result.CopiedCount > 0 || result.SkippedCount > 0)
                    {
                        McpLog.Info($"Python tools synced: {result.CopiedCount} copied, {result.SkippedCount} skipped");
                    }
                }
                else
                {
                    McpLog.Error($"Python tool sync failed with {result.ErrorCount} errors");
                    foreach (var msg in result.Messages)
                    {
                        McpLog.Error($"  - {msg}");
                    }
                }
            }
            catch (System.Exception ex)
            {
                McpLog.Error($"Python tool sync exception: {ex.Message}");
            }
            finally
            {
                _isSyncing = false;
            }
        }

        /// <summary>
        /// Checks if auto-sync is enabled (default: true)
        /// </summary>
        public static bool IsAutoSyncEnabled()
        {
            return EditorPrefs.GetBool(SyncEnabledKey, true);
        }

        /// <summary>
        /// Enables or disables auto-sync
        /// </summary>
        public static void SetAutoSyncEnabled(bool enabled)
        {
            EditorPrefs.SetBool(SyncEnabledKey, enabled);
            McpLog.Info($"Python tool auto-sync {(enabled ? "enabled" : "disabled")}");
        }

        /// <summary>
        /// Reimport all Python files in the project
        /// </summary>
        public static void ReimportPythonFiles()
        {
            // Find all Python files (imported as TextAssets by PythonFileImporter)
            var pythonGuids = AssetDatabase.FindAssets("t:TextAsset", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.EndsWith(".py", System.StringComparison.OrdinalIgnoreCase))
                .ToArray();

            foreach (string path in pythonGuids)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }

            int count = pythonGuids.Length;
            McpLog.Info($"Reimported {count} Python files");
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// Manually trigger sync
        /// </summary>
        public static void ManualSync()
        {
            McpLog.Info("Manually syncing Python tools...");
            SyncAllTools();
        }

        /// <summary>
        /// Toggle auto-sync
        /// </summary>
        public static void ToggleAutoSync()
        {
            SetAutoSyncEnabled(!IsAutoSyncEnabled());
        }

        /// <summary>
        /// Validate menu item (shows checkmark when enabled)
        /// </summary>
        public static bool ToggleAutoSyncValidate()
        {
            Menu.SetChecked("Window/MCP For Unity/Tool Sync/Auto-Sync Python Tools", IsAutoSyncEnabled());
            return true;
        }
    }
}
