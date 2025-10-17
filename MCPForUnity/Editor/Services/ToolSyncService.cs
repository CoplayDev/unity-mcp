using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using UnityEditor;

namespace MCPForUnity.Editor.Services
{
    public class ToolSyncService : IToolSyncService
    {
        private readonly IPythonToolRegistryService _registryService;

        public ToolSyncService(IPythonToolRegistryService registryService = null)
        {
            _registryService = registryService ?? MCPServiceLocator.PythonToolRegistry;
        }

        public ToolSyncResult SyncProjectTools(string destToolsDir)
        {
            var result = new ToolSyncResult();

            try
            {
                Directory.CreateDirectory(destToolsDir);

                // Get all PythonToolsAsset instances in the project
                var registries = _registryService.GetAllRegistries().ToList();

                if (!registries.Any())
                {
                    McpLog.Info("No PythonToolsAsset found. Create one via Assets > Create > MCP For Unity > Python Tools");
                    return result;
                }

                var syncedFiles = new HashSet<string>();

                // Batch all asset modifications together to minimize reimports
                AssetDatabase.StartAssetEditing();
                try
                {
                    foreach (var registry in registries)
                    {
                        foreach (var file in registry.GetValidFiles())
                        {
                            try
                            {
                                // Check if needs syncing (hash-based or always)
                                if (_registryService.NeedsSync(registry, file))
                                {
                                    string destPath = Path.Combine(destToolsDir, file.name + ".py");

                                    // Write the Python file content
                                    File.WriteAllText(destPath, file.text);

                                    // Record sync
                                    _registryService.RecordSync(registry, file);

                                    result.CopiedCount++;
                                    syncedFiles.Add(destPath);
                                    McpLog.Info($"Synced Python tool: {file.name}.py");
                                }
                                else
                                {
                                    string destPath = Path.Combine(destToolsDir, file.name + ".py");
                                    syncedFiles.Add(destPath);
                                    result.SkippedCount++;
                                }
                            }
                            catch (Exception ex)
                            {
                                result.ErrorCount++;
                                result.Messages.Add($"Failed to sync {file.name}: {ex.Message}");
                            }
                        }

                        // Cleanup stale states in registry
                        registry.CleanupStaleStates();
                        EditorUtility.SetDirty(registry);
                    }

                    // Cleanup stale Python files in destination
                    CleanupStaleFiles(destToolsDir, syncedFiles);
                }
                finally
                {
                    // End batch editing - this triggers a single asset refresh
                    AssetDatabase.StopAssetEditing();
                }

                // Save all modified registries
                AssetDatabase.SaveAssets();
            }
            catch (Exception ex)
            {
                result.ErrorCount++;
                result.Messages.Add($"Sync failed: {ex.Message}");
            }

            return result;
        }

        private void CleanupStaleFiles(string destToolsDir, HashSet<string> currentFiles)
        {
            try
            {
                if (!Directory.Exists(destToolsDir)) return;

                // Find all .py files in destination that aren't in our current set
                var existingFiles = Directory.GetFiles(destToolsDir, "*.py");

                foreach (var file in existingFiles)
                {
                    if (!currentFiles.Contains(file))
                    {
                        try
                        {
                            File.Delete(file);
                            McpLog.Info($"Cleaned up stale tool: {Path.GetFileName(file)}");
                        }
                        catch (Exception ex)
                        {
                            McpLog.Warn($"Failed to cleanup {file}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Failed to cleanup stale files: {ex.Message}");
            }
        }
    }
}
