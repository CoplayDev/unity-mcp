using System;
using System.Collections.Generic;
using MCPForUnity.Editor.ActionTrace.Core;
using MCPForUnity.Editor.ActionTrace.Core.Models;
using MCPForUnity.Editor.ActionTrace.Core.Store;
using MCPForUnity.Editor.Helpers;
using UnityEngine;

namespace MCPForUnity.Editor.ActionTrace.Capture
{
    /// <summary>
    /// Centralized event emission layer for the ActionTrace system.
    /// This middle layer decouples the Capture layer (Unity callbacks) from the Data layer (EventStore).
    ///
    /// Benefits:
    /// - EventType constants are managed in one place
    /// - Payload schemas are standardized
    /// - Event naming changes only require updates here
    /// - Capture layer code becomes simpler and more focused
    ///
    /// Usage:
    ///   ActionTraceEventEmitter.EmitComponentAdded(component);
    ///   ActionTraceEventEmitter.EmitAssetImported(assetPath, assetType);
    ///   ActionTraceEventEmitter.Emit("CustomEvent", targetId, payload);
    /// </summary>
    public static class ActionTraceEventEmitter
    {
        /// <summary>
        /// Generic event emission method.
        /// Use this for custom events or when a specific EmitXxx method doesn't exist.
        ///
        /// Usage:
        ///   Emit("MyCustomEvent", "target123", new Dictionary<string, object> { ["key"] = "value" });
        /// </summary>
        public static void Emit(string eventType, string targetId, Dictionary<string, object> payload)
        {
            EmitEvent(eventType, targetId ?? "Unknown", payload);
        }

        /// <summary>
        /// Emit a component added event.
        /// Uses GlobalIdHelper for cross-session stable target IDs.
        /// </summary>
        public static void EmitComponentAdded(Component component)
        {
            if (component == null)
            {
                McpLog.Warn("[ActionTraceEventEmitter] Attempted to emit ComponentAdded with null component");
                return;
            }

            // Use GlobalIdHelper for cross-session stable ID
            string globalId = GlobalIdHelper.ToGlobalIdString(component);

            var payload = new Dictionary<string, object>
            {
                ["component_type"] = component.GetType().Name,
                ["game_object"] = component.gameObject?.name ?? "Unknown"
            };

            EmitEvent(EventTypes.ComponentAdded, globalId, payload);
        }

        /// <summary>
        /// Emit a component removed event.
        /// Uses GlobalIdHelper for cross-session stable target IDs.
        /// </summary>
        public static void EmitComponentRemoved(Component component)
        {
            if (component == null)
            {
                McpLog.Warn("[ActionTraceEventEmitter] Attempted to emit ComponentRemoved with null component");
                return;
            }

            // Use GlobalIdHelper for cross-session stable ID
            string globalId = GlobalIdHelper.ToGlobalIdString(component);

            var payload = new Dictionary<string, object>
            {
                ["component_type"] = component.GetType().Name,
                ["game_object"] = component.gameObject?.name ?? "Unknown"
            };

            EmitEvent(EventTypes.ComponentRemoved, globalId, payload);
        }

        /// <summary>
        /// Emit a GameObject created event.
        /// Uses GlobalIdHelper for cross-session stable target IDs.
        /// </summary>
        public static void EmitGameObjectCreated(GameObject gameObject)
        {
            if (gameObject == null)
            {
                McpLog.Warn("[ActionTraceEventEmitter] Attempted to emit GameObjectCreated with null GameObject");
                return;
            }

            // Use GlobalIdHelper for cross-session stable ID
            string globalId = GlobalIdHelper.ToGlobalIdString(gameObject);

            var payload = new Dictionary<string, object>
            {
                ["name"] = gameObject.name,
                ["instance_id"] = gameObject.GetInstanceID()
            };

            EmitEvent(EventTypes.GameObjectCreated, globalId, payload);
        }

        /// <summary>
        /// Emit a GameObject destroyed event.
        /// Uses GlobalIdHelper for cross-session stable target IDs.
        ///
        /// Call this before the GameObject is destroyed:
        ///   EmitGameObjectDestroyed(gameObject);  // Preferred
        ///   EmitGameObjectDestroyed(globalId, name);  // Alternative
        /// </summary>
        public static void EmitGameObjectDestroyed(GameObject gameObject)
        {
            if (gameObject == null)
            {
                McpLog.Warn("[ActionTraceEventEmitter] Attempted to emit GameObjectDestroyed with null GameObject");
                return;
            }

            // Use GlobalIdHelper for cross-session stable ID
            string globalId = GlobalIdHelper.ToGlobalIdString(gameObject);

            var payload = new Dictionary<string, object>
            {
                ["name"] = gameObject.name,
                ["instance_id"] = gameObject.GetInstanceID()
            };

            EmitEvent(EventTypes.GameObjectDestroyed, globalId, payload);
        }

        /// <summary>
        /// Emit a GameObject destroyed event (alternative overload for when only instanceId is available).
        /// This overload is used when GameObject is already destroyed or unavailable.
        ///
        /// Priority:
        /// 1. Use EmitGameObjectDestroyed(GameObject) when GameObject is available - provides stable GlobalId
        /// 2. This fallback when only instanceId is known - ID may not be cross-session stable
        /// </summary>
        public static void EmitGameObjectDestroyed(int instanceId, string name)
        {
            var payload = new Dictionary<string, object>
            {
                ["name"] = name,
                ["instance_id"] = instanceId
            };

            // Fallback: use InstanceID when GameObject is unavailable (not cross-session stable)
            EmitEvent(EventTypes.GameObjectDestroyed, instanceId.ToString(), payload);
        }

        /// <summary>
        /// Emit a hierarchy changed event.
        /// </summary>
        public static void EmitHierarchyChanged()
        {
            var payload = new Dictionary<string, object>
            {
                ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            EmitEvent(EventTypes.HierarchyChanged, "Scene", payload);
        }

        /// <summary>
        /// Emit a play mode state changed event.
        /// </summary>
        public static void EmitPlayModeChanged(string state)
        {
            var payload = new Dictionary<string, object>
            {
                ["state"] = state
            };

            EmitEvent(EventTypes.PlayModeChanged, "Editor", payload);
        }

        /// <summary>
        /// Emit a scene saving event.
        /// Uses Asset:{path} format for cross-session stable target IDs.
        /// </summary>
        public static void EmitSceneSaving(string sceneName, string path)
        {
            // Use scene path as stable identifier (Asset: prefix for consistency with GlobalIdHelper)
            string targetId = string.IsNullOrEmpty(path) ? sceneName : $"Asset:{path}";

            var payload = new Dictionary<string, object>
            {
                ["scene_name"] = sceneName,
                ["path"] = path
            };

            EmitEvent(EventTypes.SceneSaving, targetId, payload);
        }

        /// <summary>
        /// Emit a scene saved event.
        /// Uses Asset:{path} format for cross-session stable target IDs.
        /// </summary>
        public static void EmitSceneSaved(string sceneName, string path)
        {
            // Use scene path as stable identifier (Asset: prefix for consistency with GlobalIdHelper)
            string targetId = string.IsNullOrEmpty(path) ? sceneName : $"Asset:{path}";

            var payload = new Dictionary<string, object>
            {
                ["scene_name"] = sceneName,
                ["path"] = path
            };

            EmitEvent(EventTypes.SceneSaved, targetId, payload);
        }

        /// <summary>
        /// Emit a scene opened event.
        /// Uses Asset:{path} format for cross-session stable target IDs.
        /// </summary>
        public static void EmitSceneOpened(string sceneName, string path, string mode)
        {
            // Use scene path as stable identifier (Asset: prefix for consistency with GlobalIdHelper)
            string targetId = string.IsNullOrEmpty(path) ? sceneName : $"Asset:{path}";

            var payload = new Dictionary<string, object>
            {
                ["scene_name"] = sceneName,
                ["path"] = path,
                ["mode"] = mode
            };

            EmitEvent(EventTypes.SceneOpened, targetId, payload);
        }

        /// <summary>
        /// Emit a new scene created event.
        /// </summary>
        public static void EmitNewSceneCreated()
        {
            var payload = new Dictionary<string, object>
            {
                ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            EmitEvent(EventTypes.NewSceneCreated, "Scene", payload);
        }

        /// <summary>
        /// Emit an asset imported event.
        /// Uses Asset:{path} format for cross-session stable target IDs.
        /// </summary>
        public static void EmitAssetImported(string assetPath, string assetType = null)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                McpLog.Warn("[ActionTraceEventEmitter] Attempted to emit AssetImported with null or empty path");
                return;
            }

            string targetId = $"Asset:{assetPath}";

            var resolvedAssetType = !string.IsNullOrEmpty(assetType)
                ? assetType
                : DetectAssetType(assetPath);

            var payload = new Dictionary<string, object>
            {
                ["path"] = assetPath,
                ["extension"] = System.IO.Path.GetExtension(assetPath),
                ["asset_type"] = resolvedAssetType
            };

            EmitEvent(EventTypes.AssetImported, targetId, payload);
        }

        /// <summary>
        /// Emit an asset deleted event.
        /// Uses Asset:{path} format for cross-session stable target IDs.
        /// </summary>
        public static void EmitAssetDeleted(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                McpLog.Warn("[ActionTraceEventEmitter] Attempted to emit AssetDeleted with null or empty path");
                return;
            }

            string targetId = $"Asset:{assetPath}";

            var payload = new Dictionary<string, object>
            {
                ["path"] = assetPath,
                ["extension"] = System.IO.Path.GetExtension(assetPath)
            };

            EmitEvent(EventTypes.AssetDeleted, targetId, payload);
        }

        /// <summary>
        /// Emit an asset moved event.
        /// Uses Asset:{toPath} format for cross-session stable target IDs.
        /// </summary>
        public static void EmitAssetMoved(string fromPath, string toPath)
        {
            if (string.IsNullOrEmpty(toPath))
            {
                McpLog.Warn("[ActionTraceEventEmitter] Attempted to emit AssetMoved with null or empty destination path");
                return;
            }

            string targetId = $"Asset:{toPath}";

            var payload = new Dictionary<string, object>
            {
                ["from_path"] = fromPath ?? string.Empty,
                ["to_path"] = toPath,
                ["extension"] = System.IO.Path.GetExtension(toPath)
            };

            EmitEvent(EventTypes.AssetMoved, targetId, payload);
        }

        /// <summary>
        /// Emit a script compiled event.
        /// </summary>
        public static void EmitScriptCompiled(int scriptCount, double durationMs)
        {
            var payload = new Dictionary<string, object>
            {
                ["script_count"] = scriptCount,
                ["duration_ms"] = durationMs
            };

            EmitEvent(EventTypes.ScriptCompiled, "Scripts", payload);
        }

        /// <summary>
        /// Emit a script compilation failed event.
        /// </summary>
        public static void EmitScriptCompilationFailed(int errorCount, string[] errors)
        {
            var payload = new Dictionary<string, object>
            {
                ["error_count"] = errorCount,
                ["errors"] = errors ?? Array.Empty<string>()
            };

            EmitEvent(EventTypes.ScriptCompilationFailed, "Scripts", payload);
        }

        /// <summary>
        /// Emit a build started event.
        /// </summary>
        public static void EmitBuildStarted(string platform, string buildPath)
        {
            var payload = new Dictionary<string, object>
            {
                ["platform"] = platform,
                ["build_path"] = buildPath,
                ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            EmitEvent(EventTypes.BuildStarted, "Build", payload);
        }

        /// <summary>
        /// Emit a build completed event.
        /// </summary>
        public static void EmitBuildCompleted(string platform, string buildPath, double durationMs, long sizeBytes)
        {
            var payload = new Dictionary<string, object>
            {
                ["platform"] = platform,
                ["build_path"] = buildPath,
                ["duration_ms"] = durationMs,
                ["size_bytes"] = sizeBytes
            };

            EmitEvent(EventTypes.BuildCompleted, "Build", payload);
        }

        /// <summary>
        /// Emit a build failed event.
        /// </summary>
        public static void EmitBuildFailed(string platform, string errorMessage)
        {
            var payload = new Dictionary<string, object>
            {
                ["platform"] = platform,
                ["error_message"] = errorMessage
            };

            EmitEvent(EventTypes.BuildFailed, "Build", payload);
        }

        // ========================================================================
        // Asset Modification Events (for ManageAsset integration)
        // ========================================================================

        /// <summary>
        /// Emit an asset modified event via MCP tool (manage_asset).
        /// Uses Asset:{path} format for cross-session stable target IDs.
        /// </summary>
        public static void EmitAssetModified(string assetPath, string assetType, IReadOnlyDictionary<string, object> changes)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                McpLog.Warn("[ActionTraceEventEmitter] AssetModified with null path");
                return;
            }

            string targetId = $"Asset:{assetPath}";

            var payload = new Dictionary<string, object>
            {
                ["path"] = assetPath,
                ["asset_type"] = assetType ?? "Unknown",
                ["changes"] = changes ?? new Dictionary<string, object>(),
                ["source"] = "mcp_tool"  // Indicates this change came from an MCP tool call
            };

            EmitEvent(EventTypes.AssetModified, targetId, payload);
        }

        /// <summary>
        /// Emit an asset created event via MCP tool (manage_asset).
        /// Uses Asset:{path} format for cross-session stable target IDs.
        /// </summary>
        public static void EmitAssetCreated(string assetPath, string assetType)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                McpLog.Warn("[ActionTraceEventEmitter] AssetCreated with null path");
                return;
            }

            string targetId = $"Asset:{assetPath}";

            var payload = new Dictionary<string, object>
            {
                ["path"] = assetPath,
                ["asset_type"] = assetType ?? "Unknown",
                ["source"] = "mcp_tool"
            };

            EmitEvent(EventTypes.AssetCreated, targetId, payload);
        }

        /// <summary>
        /// Emit an asset deleted event via MCP tool (manage_asset).
        /// Uses Asset:{path} format for cross-session stable target IDs.
        /// </summary>
        public static void EmitAssetDeleted(string assetPath, string assetType)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                McpLog.Warn("[ActionTraceEventEmitter] AssetDeleted with null path");
                return;
            }

            string targetId = $"Asset:{assetPath}";

            var payload = new Dictionary<string, object>
            {
                ["path"] = assetPath,
                ["asset_type"] = assetType ?? "Unknown",
                ["source"] = "mcp_tool"
            };

            EmitEvent(EventTypes.AssetDeleted, targetId, payload);
        }

        /// <summary>
        /// Core event emission method.
        /// All events flow through this method, allowing for centralized error handling and logging.
        /// </summary>
        private static void EmitEvent(string eventType, string targetId, Dictionary<string, object> payload)
        {
            try
            {
                var evt = new EditorEvent(
                    sequence: 0,  // Will be assigned by EventStore.Record
                    timestampUnixMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    type: eventType,
                    targetId: targetId,
                    payload: payload
                );

                // Apply sampling middleware to maintain consistency with ActionTraceRecorder
                if (!SamplingMiddleware.ShouldRecord(evt))
                {
                    return;
                }

                EventStore.Record(evt);
            }
            catch (Exception ex)
            {
                McpLog.Warn($"[ActionTraceEventEmitter] Failed to emit {eventType} event: {ex.Message}");
            }
        }

        /// <summary>
        /// Detect asset type from file extension.
        /// </summary>
        private static string DetectAssetType(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return "unknown";

            var extension = System.IO.Path.GetExtension(assetPath).ToLower();

            return extension switch
            {
                ".cs" => "script",
                ".unity" => "scene",
                ".prefab" => "prefab",
                ".mat" => "material",
                ".png" or ".jpg" or ".jpeg" or ".psd" or ".tga" or ".exr" => "texture",
                ".wav" or ".mp3" or ".ogg" or ".aif" => "audio",
                ".fbx" or ".obj" => "model",
                ".anim" => "animation",
                ".controller" => "animator_controller",
                ".shader" => "shader",
                ".xml" or ".json" or ".yaml" => "data",
                _ => "unknown"
            };
        }
    }
}
