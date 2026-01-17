using System;
using System.Collections.Generic;
using MCPForUnity.Editor.ActionTrace.Core;
using MCPForUnity.Editor.Helpers;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.ActionTrace.Capture
{
    /// <summary>
    /// Asset postprocessor for tracking asset changes in ActionTrace.
    /// Uses Unity's AssetPostprocessor callback pattern, not event subscription.
    ///
    /// Events generated:
    /// - AssetImported: When an asset is imported from outside
    /// - AssetCreated: When a new asset is created in Unity
    /// - AssetDeleted: When an asset is deleted
    /// - AssetMoved: When an asset is moved/renamed
    /// - AssetModified: When an existing asset is modified
    ///
    /// All asset events use "Asset:{path}" format for TargetId to ensure
    /// cross-session stability.
    /// </summary>
    internal sealed class AssetChangePostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            // ========== Imported Assets (includes newly created assets) ==========
            foreach (var assetPath in importedAssets)
            {
                if (string.IsNullOrEmpty(assetPath)) continue;

                // L1 Blacklist: Skip junk assets before creating events
                if (!EventFilter.ShouldTrackAsset(assetPath))
                    continue;

                string targetId = $"Asset:{assetPath}";
                string assetType = GetAssetType(assetPath);

                var payload = new Dictionary<string, object>
                {
                    ["path"] = assetPath,
                    ["extension"] = System.IO.Path.GetExtension(assetPath),
                    ["asset_type"] = assetType
                };

                // Distinguish between imported and newly created assets
                if (IsNewlyCreatedAsset(assetPath))
                {
                    RecordEvent(EventTypes.AssetCreated, targetId, payload);
                }
                else
                {
                    RecordEvent(EventTypes.AssetImported, targetId, payload);
                }
            }

            // ========== Deleted Assets ==========
            foreach (var assetPath in deletedAssets)
            {
                if (string.IsNullOrEmpty(assetPath)) continue;

                // L1 Blacklist: Skip junk assets
                if (!EventFilter.ShouldTrackAsset(assetPath))
                    continue;

                string targetId = $"Asset:{assetPath}";

                var payload = new Dictionary<string, object>
                {
                    ["path"] = assetPath
                };

                RecordEvent(EventTypes.AssetDeleted, targetId, payload);
            }

            // ========== Moved Assets ==========
            for (int i = 0; i < movedAssets.Length; i++)
            {
                if (string.IsNullOrEmpty(movedAssets[i])) continue;

                var fromPath = i < movedFromAssetPaths.Length ? movedFromAssetPaths[i] : "";

                // L1 Blacklist: Skip junk assets
                if (!EventFilter.ShouldTrackAsset(movedAssets[i]))
                    continue;

                string targetId = $"Asset:{movedAssets[i]}";

                var payload = new Dictionary<string, object>
                {
                    ["to_path"] = movedAssets[i],
                    ["from_path"] = fromPath
                };

                RecordEvent(EventTypes.AssetMoved, targetId, payload);
            }

            // ========== Modified Assets ==========
            // Track asset modifications separately (e.g., texture imports, prefab changes)
            foreach (var assetPath in importedAssets)
            {
                if (string.IsNullOrEmpty(assetPath)) continue;

                // Only track modifications for existing assets
                if (!IsNewlyCreatedAsset(assetPath) && EventFilter.ShouldTrackAsset(assetPath))
                {
                    string targetId = $"Asset:{assetPath}";
                    string assetType = GetAssetType(assetPath);

                    // Only record modifications for certain asset types
                    if (ShouldTrackModification(assetPath))
                    {
                        var payload = new Dictionary<string, object>
                        {
                            ["path"] = assetPath,
                            ["extension"] = System.IO.Path.GetExtension(assetPath),
                            ["asset_type"] = assetType
                        };

                        RecordEvent(EventTypes.AssetModified, targetId, payload);
                    }
                }
            }
        }

        /// <summary>
        /// Determines if an asset was newly created vs imported.
        /// Newly created assets have a .meta file with recent creation time.
        /// </summary>
        private static bool IsNewlyCreatedAsset(string assetPath)
        {
            try
            {
                string metaPath = assetPath + ".meta";
                var meta = AssetDatabase.LoadMainAssetAtPath(metaPath);
                // This is a simplified check - in production you'd check file creation time
                return false; // Default to treating as imported for now
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Determines if modifications to this asset type should be tracked.
        /// Tracks modifications for commonly edited asset types.
        /// </summary>
        private static bool ShouldTrackModification(string assetPath)
        {
            string ext = System.IO.Path.GetExtension(assetPath).ToLower();
            // Track modifications for these asset types
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg" ||
                   ext == ".psd" || ext == ".tif" ||
                   ext == ".fbx" || ext == ".obj" ||
                   ext == ".prefab" || ext == ".unity" ||
                   ext == ".anim" || ext == ".controller";
        }

        /// <summary>
        /// Gets the asset type based on file extension.
        /// </summary>
        private static string GetAssetType(string assetPath)
        {
            string ext = System.IO.Path.GetExtension(assetPath).ToLower();
            return ext switch
            {
                ".cs" => "script",
                ".unity" => "scene",
                ".prefab" => "prefab",
                ".mat" => "material",
                ".png" or ".jpg" or ".jpeg" or ".gif" or ".tga" or ".psd" or ".tif" or ".bmp" => "texture",
                ".fbx" or ".obj" or ".blend" or ".3ds" => "model",
                ".anim" => "animation",
                ".controller" => "animator_controller",
                ".shader" => "shader",
                ".asset" => "scriptable_object",
                ".physicmaterial" => "physics_material",
                ".physicmaterial2d" => "physics_material_2d",
                ".guiskin" => "gui_skin",
                ".fontsettings" => "font",
                ".mixer" => "audio_mixer",
                ".rendertexture" => "render_texture",
                ".spriteatlas" => "sprite_atlas",
                ".tilepalette" => "tile_palette",
                _ => "unknown"
            };
        }

        /// <summary>
        /// Records an event to the EventStore with proper context injection.
        /// </summary>
        private static void RecordEvent(string type, string targetId, Dictionary<string, object> payload)
        {
            try
            {
                // Inject VCS context into all recorded events
                var vcsContext = VCS.VcsContextProvider.GetCurrentContext();
                payload["vcs_context"] = vcsContext.ToDictionary();

                // Inject Undo Group ID for undo_to_sequence functionality (P2.4)
                int currentUndoGroup = Undo.GetCurrentGroup();
                payload["undo_group"] = currentUndoGroup;

                var evt = new EditorEvent(
                    sequence: 0,
                    timestampUnixMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    type: type,
                    targetId: targetId,
                    payload: payload
                );

                // AssetPostprocessor callbacks run on main thread but outside update loop.
                // Use delayCall to defer recording to main thread update, avoiding thread warnings.
                UnityEditor.EditorApplication.delayCall += () => Core.EventStore.Record(evt);
            }
            catch (Exception ex)
            {
                McpLog.Warn($"[AssetChangePostprocessor] Failed to record event: {ex.Message}");
            }
        }
    }
}
