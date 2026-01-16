using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.ActionTrace.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MCPForUnity.Editor.ActionTrace.Capture
{
    /// <summary>
    /// Captures Unity editor events and records them to the EventStore.
    /// Uses debouncing to avoid spamming for rapid successive changes.
    /// Updated to use EventType constants for type safety.
    /// </summary>
    [InitializeOnLoad]
    public static class UnityEventHooks
    {
        private static DateTime _lastHierarchyChange;
        private static readonly object _lock = new();

        static UnityEventHooks()
        {
            // Monitor GameObject/component creation
            ObjectFactory.componentWasAdded += OnComponentAdded;

            // Monitor hierarchy changes (with debouncing)
            EditorApplication.hierarchyChanged += OnHierarchyChanged;

            // Monitor selection changes (P2.3: Selection Tracking)
            Selection.selectionChanged += OnSelectionChanged;

            // Monitor play mode state changes
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

            // Monitor scene saving - use EditorSceneManager, not EditorApplication
            EditorSceneManager.sceneSaving += OnSceneSaving;

            // Monitor scene opening - use EditorSceneManager, not EditorApplication
            EditorSceneManager.sceneOpened += OnSceneOpened;
        }

        private static void OnComponentAdded(Component component)
        {
            if (component == null) return;

            var payload = new Dictionary<string, object>
            {
                ["component_type"] = component.GetType().Name,
                ["game_object"] = component.gameObject?.name ?? "Unknown"
            };

            RecordEvent(EventTypes.ComponentAdded, component.GetInstanceID().ToString(), payload);
        }

        /// <summary>
        /// Handles Selection changes (P2.3: Selection Tracking).
        /// Records what the user is currently focusing on for AI context awareness.
        /// </summary>
        private static void OnSelectionChanged()
        {
            if (Selection.activeObject == null)
                return;

            var payload = new Dictionary<string, object>
            {
                ["name"] = Selection.activeObject.name,
                ["type"] = Selection.activeObject.GetType().Name,
                ["instance_id"] = Selection.activeObject.GetInstanceID()
            };

            // Add path for GameObject/Component selections
            if (Selection.activeObject is GameObject go)
            {
                payload["path"] = GetGameObjectPath(go);
            }
            else if (Selection.activeObject is Component comp)
            {
                payload["path"] = GetGameObjectPath(comp.gameObject);
                payload["component_type"] = comp.GetType().Name;
            }

            RecordEvent(EventTypes.SelectionChanged, Selection.activeObject.GetInstanceID().ToString(), payload);
        }

        /// <summary>
        /// Gets the full Hierarchy path for a GameObject.
        /// Example: "Level1/Player/Arm/Hand"
        /// </summary>
        private static string GetGameObjectPath(GameObject obj)
        {
            if (obj == null)
                return "Unknown";

            var path = obj.name;
            var parent = obj.transform.parent;

            while (parent != null)
            {
                path = $"{parent.name}/{path}";
                parent = parent.parent;
            }

            return path;
        }

        private static void OnHierarchyChanged()
        {
            var now = DateTime.Now;
            lock (_lock)
            {
                // Debounce: ignore changes within 200ms of the last one
                if ((now - _lastHierarchyChange).TotalMilliseconds < 200)
                {
                    return;
                }
                _lastHierarchyChange = now;
            }

            RecordEvent(EventTypes.HierarchyChanged, "Scene", new Dictionary<string, object>());
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            var payload = new Dictionary<string, object>
            {
                ["state"] = state.ToString()
            };

            RecordEvent(EventTypes.PlayModeChanged, "Editor", payload);
        }

        private static void OnSceneSaving(Scene scene, string path)
        {
            var payload = new Dictionary<string, object>
            {
                ["scene_name"] = scene.name,
                ["path"] = path
            };

            RecordEvent(EventTypes.SceneSaving, scene.name, payload);
        }

        private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            var payload = new Dictionary<string, object>
            {
                ["scene_name"] = scene.name,
                ["path"] = scene.path,
                ["mode"] = mode.ToString()
            };

            RecordEvent(EventTypes.SceneOpened, scene.name, payload);
        }

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
                    sequence: 0,  // Will be assigned by EventStore.Record
                    timestampUnixMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    type: type,
                    targetId: targetId,
                    payload: payload
                );

                // Apply sampling middleware to protect from event floods.
                // If sampling filters this event, do not record it here.
                if (SamplingMiddleware.ShouldRecord(evt))
                {
                    Core.EventStore.Record(evt);
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"[UnityEventHooks] Failed to record event: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Asset postprocessor for tracking asset changes.
    /// Uses Unity's AssetPostprocessor callback pattern, not event subscription.
    /// Updated to use EventType constants for type safety.
    /// </summary>
    internal sealed class AssetChangePostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            // ========== Imported Assets ==========
            foreach (var assetPath in importedAssets)
            {
                if (string.IsNullOrEmpty(assetPath)) continue;

                // L1 Blacklist: Skip junk assets before creating events
                if (!EventFilter.ShouldTrackAsset(assetPath))
                    continue;

                var payload = new Dictionary<string, object>
                {
                    ["path"] = assetPath,
                    ["extension"] = System.IO.Path.GetExtension(assetPath)
                };

                // Determine asset type
                if (assetPath.EndsWith(".cs"))
                {
                    payload["asset_type"] = "script";
                }
                else if (assetPath.EndsWith(".unity"))
                {
                    payload["asset_type"] = "scene";
                }
                else if (assetPath.EndsWith(".prefab"))
                {
                    payload["asset_type"] = "prefab";
                }
                else if (assetPath.EndsWith(".mat"))
                {
                    payload["asset_type"] = "material";
                }

                RecordEvent(EventTypes.AssetImported, assetPath, payload);
            }

            // ========== Deleted Assets ==========
            foreach (var assetPath in deletedAssets)
            {
                if (string.IsNullOrEmpty(assetPath)) continue;

                // L1 Blacklist: Skip junk assets
                if (!EventFilter.ShouldTrackAsset(assetPath))
                    continue;

                var payload = new Dictionary<string, object>
                {
                    ["path"] = assetPath
                };

                RecordEvent(EventTypes.AssetDeleted, assetPath, payload);
            }

            // ========== Moved Assets ==========
            for (int i = 0; i < movedAssets.Length; i++)
            {
                if (string.IsNullOrEmpty(movedAssets[i])) continue;

                var fromPath = i < movedFromAssetPaths.Length ? movedFromAssetPaths[i] : "";

                // L1 Blacklist: Skip junk assets
                if (!EventFilter.ShouldTrackAsset(movedAssets[i]))
                    continue;

                var payload = new Dictionary<string, object>
                {
                    ["to_path"] = movedAssets[i],
                    ["from_path"] = fromPath
                };

                RecordEvent(EventTypes.AssetMoved, movedAssets[i], payload);
            }
        }

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
                Debug.LogWarning($"[AssetChangePostprocessor] Failed to record event: {ex.Message}");
            }
        }
    }
}
