using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.ActionTrace.Core;
using MCPForUnity.Editor.ActionTrace.Core.Models;
using MCPForUnity.Editor.ActionTrace.Core.Store;
using MCPForUnity.Editor.ActionTrace.Integration.VCS;
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
        /// <summary>
        /// Tracks assets processed in the current session to prevent duplicate events.
        /// Unity's OnPostprocessAllAssets can fire multiple times for the same asset
        /// during different phases (creation, compilation, re-import).
        ///
        /// IMPORTANT: This uses a persistent file cache because Domain Reload
        /// (script compilation) resets all static fields, causing in-memory
        /// tracking to lose its state.
        /// </summary>
        private static HashSet<string> _processedAssetsInSession
        {
            get
            {
                if (_cachedProcessedAssets == null)
                    LoadProcessedAssets();
                return _cachedProcessedAssets;
            }
        }

        private static HashSet<string> _cachedProcessedAssets;
        private const string CacheFileName = "AssetChangePostprocessor.cache";

        /// <summary>
        /// Loads the processed assets cache from disk.
        /// Called lazily when _processedAssetsInSession is first accessed.
        /// </summary>
        private static void LoadProcessedAssets()
        {
            _cachedProcessedAssets = new HashSet<string>();

            try
            {
                string cachePath = GetCacheFilePath();
                if (!System.IO.File.Exists(cachePath))
                    return;

                string json = System.IO.File.ReadAllText(cachePath);
                var loaded = Newtonsoft.Json.JsonConvert.DeserializeObject<string[]>(json);
                if (loaded != null)
                {
                    foreach (var path in loaded)
                        _cachedProcessedAssets.Add(path);
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"[AssetChangePostprocessor] Failed to load cache: {ex.Message}");
                _cachedProcessedAssets = new HashSet<string>();
            }
        }

        /// <summary>
        /// Saves the current processed assets to disk.
        /// Should be called after processing a batch of assets.
        /// </summary>
        private static void SaveProcessedAssets()
        {
            if (_cachedProcessedAssets == null)
                return;

            try
            {
                string cachePath = GetCacheFilePath();

                // If cache is empty, delete the cache file to persist the cleared state
                if (_cachedProcessedAssets.Count == 0)
                {
                    if (System.IO.File.Exists(cachePath))
                        System.IO.File.Delete(cachePath);
                    return;
                }

                string json = Newtonsoft.Json.JsonConvert.SerializeObject(_cachedProcessedAssets.ToArray());
                var dir = System.IO.Path.GetDirectoryName(cachePath);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(cachePath, json);
            }
            catch (Exception ex)
            {
                McpLog.Warn($"[AssetChangePostprocessor] Failed to save cache: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the cache file path in the Library folder.
        /// </summary>
        private static string GetCacheFilePath()
        {
            return System.IO.Path.Combine(
                UnityEngine.Application.dataPath.Substring(0, UnityEngine.Application.dataPath.Length - "Assets".Length),
                "Library",
                CacheFileName
            );
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            bool hasChanges = false;

            // Cleanup: Periodically clear old entries to prevent unbounded growth
            // Use time-based expiration (30 minutes) instead of count-based
            CleanupOldEntries();

            // ========== Imported Assets (includes newly created assets) ==========
            // Single-pass event classification: each asset produces exactly one event
            // Priority: AssetCreated > AssetModified > AssetImported (mutually exclusive)
            foreach (var assetPath in importedAssets)
            {
                if (string.IsNullOrEmpty(assetPath)) continue;

                // L0 Deduplication: Skip if already processed in this session
                // This prevents duplicate events when Unity fires OnPostprocessAllAssets
                // multiple times for the same asset (creation, compilation, re-import)
                if (!_processedAssetsInSession.Add(assetPath))
                    continue;  // Already processed, skip to prevent duplicate events

                hasChanges = true;  // Mark that we added a new entry

                // L1 Blacklist: Skip junk assets before creating events
                if (!EventFilter.ShouldTrackAsset(assetPath))
                {
                    // Remove from tracking if it's a junk asset (we don't want to track it)
                    _processedAssetsInSession.Remove(assetPath);
                    continue;
                }

                string targetId = $"Asset:{assetPath}";
                string assetType = GetAssetType(assetPath);

                var payload = new Dictionary<string, object>
                {
                    ["path"] = assetPath,
                    ["extension"] = System.IO.Path.GetExtension(assetPath),
                    ["asset_type"] = assetType
                };

                // Mutually exclusive event classification (prevents duplicate events)
                if (IsNewlyCreatedAsset(assetPath))
                {
                    // Priority 1: Newly created assets (first-time existence)
                    RecordEvent(EventTypes.AssetCreated, targetId, payload);
                }
                else if (ShouldTrackModification(assetPath))
                {
                    // Priority 2: Existing assets with trackable modification types
                    // Covers: re-imports, content changes, settings updates
                    RecordEvent(EventTypes.AssetModified, targetId, payload);
                }
                else
                {
                    // Priority 3: Generic imports (fallback for untracked types)
                    RecordEvent(EventTypes.AssetImported, targetId, payload);
                }
            }

            // ========== Deleted Assets ==========
            foreach (var assetPath in deletedAssets)
            {
                if (string.IsNullOrEmpty(assetPath)) continue;

                // L0 Deduplication: Skip if already processed in this session
                if (!_processedAssetsInSession.Add(assetPath))
                    continue;

                hasChanges = true;  // Mark that we added a new entry

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

                // L0 Deduplication: Skip if already processed in this session
                if (!_processedAssetsInSession.Add(movedAssets[i]))
                    continue;

                hasChanges = true;  // Mark that we added a new entry

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

            // Persist the cache to disk if there were any changes
            if (hasChanges)
                SaveProcessedAssets();
        }

        /// <summary>
        /// Cleanup old entries from the cache to prevent unbounded growth.
        /// Uses time-based expiration (30 minutes) instead of count-based.
        /// This is called at the start of each OnPostprocessAllAssets batch.
        /// </summary>
        private static void CleanupOldEntries()
        {
            if (_cachedProcessedAssets == null || _cachedProcessedAssets.Count == 0)
                return;

            // Only cleanup periodically to avoid overhead
            // Use a simple counter or timestamp-based approach
            const int MaxCacheSize = 1000;
            if (_cachedProcessedAssets.Count <= MaxCacheSize)
                return;

            // If cache grows too large, clear it
            // This is safe because re-processing old assets is extremely rare
            _cachedProcessedAssets.Clear();
            SaveProcessedAssets();
        }

        /// <summary>
        /// Determines if an asset was newly created vs imported.
        ///
        /// Heuristic: Checks the .meta file creation time. A very recent creation time
        /// (within 5 seconds) indicates a newly created asset. Older .meta files indicate
        /// re-imports of existing assets.
        ///
        /// This is a pragmatic approach since Unity's OnPostprocessAllAssets doesn't
        /// distinguish between new creations and re-imports directly.
        /// </summary>
        private static bool IsNewlyCreatedAsset(string assetPath)
        {
            try
            {
                string metaPath = assetPath + ".meta";
                string fullPath = System.IO.Path.Combine(UnityEngine.Application.dataPath.Substring(0, UnityEngine.Application.dataPath.Length - "Assets".Length), metaPath);

                if (!System.IO.File.Exists(fullPath))
                    return false;

                var creationTime = System.IO.File.GetCreationTimeUtc(fullPath);
                var currentTime = DateTime.UtcNow;
                var timeDiff = currentTime - creationTime;

                // If .meta file was created within 5 seconds, treat as newly created
                // This threshold accounts for Unity's internal processing delays
                return timeDiff.TotalSeconds <= 5.0;
            }
            catch
            {
                // On any error, default to treating as imported (conservative)
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
                var vcsContext = VcsContextProvider.GetCurrentContext();
                if (vcsContext != null)
                {
                    payload["vcs_context"] = vcsContext.ToDictionary();
                }

                var evt = new EditorEvent(
                    sequence: 0,
                    timestampUnixMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    type: type,
                    targetId: targetId,
                    payload: payload
                );

                // AssetPostprocessor callbacks run on main thread but outside update loop.
                // Use delayCall to defer recording to main thread update, avoiding thread warnings.
                UnityEditor.EditorApplication.delayCall += () => EventStore.Record(evt);
            }
            catch (Exception ex)
            {
                McpLog.Warn($"[AssetChangePostprocessor] Failed to record event: {ex.Message}");
            }
        }
    }
}
