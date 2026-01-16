using System;
using System.Collections.Generic;
using MCPForUnity.Editor.ActionTrace.Core;
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
    /// </summary>
    public static class ActionTraceEventEmitter
    {
        /// <summary>
        /// Emit a component added event.
        /// </summary>
        public static void EmitComponentAdded(Component component)
        {
            if (component == null)
            {
                Debug.LogWarning("[ActionTraceEventEmitter] Attempted to emit ComponentAdded with null component");
                return;
            }

            var payload = new Dictionary<string, object>
            {
                ["component_type"] = component.GetType().Name,
                ["game_object"] = component.gameObject?.name ?? "Unknown"
            };

            EmitEvent(EventTypes.ComponentAdded, component.GetInstanceID().ToString(), payload);
        }

        /// <summary>
        /// Emit a component removed event.
        /// </summary>
        public static void EmitComponentRemoved(Component component)
        {
            if (component == null)
            {
                Debug.LogWarning("[ActionTraceEventEmitter] Attempted to emit ComponentRemoved with null component");
                return;
            }

            var payload = new Dictionary<string, object>
            {
                ["component_type"] = component.GetType().Name,
                ["game_object"] = component.gameObject?.name ?? "Unknown"
            };

            EmitEvent(EventTypes.ComponentRemoved, component.GetInstanceID().ToString(), payload);
        }

        /// <summary>
        /// Emit a GameObject created event.
        /// </summary>
        public static void EmitGameObjectCreated(GameObject gameObject)
        {
            if (gameObject == null)
            {
                Debug.LogWarning("[ActionTraceEventEmitter] Attempted to emit GameObjectCreated with null GameObject");
                return;
            }

            var payload = new Dictionary<string, object>
            {
                ["name"] = gameObject.name,
                ["instance_id"] = gameObject.GetInstanceID()
            };

            EmitEvent(EventTypes.GameObjectCreated, gameObject.GetInstanceID().ToString(), payload);
        }

        /// <summary>
        /// Emit a GameObject destroyed event.
        /// </summary>
        public static void EmitGameObjectDestroyed(int instanceId, string name)
        {
            var payload = new Dictionary<string, object>
            {
                ["name"] = name,
                ["instance_id"] = instanceId
            };

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
        /// </summary>
        public static void EmitSceneSaving(string sceneName, string path)
        {
            var payload = new Dictionary<string, object>
            {
                ["scene_name"] = sceneName,
                ["path"] = path
            };

            EmitEvent(EventTypes.SceneSaving, sceneName, payload);
        }

        /// <summary>
        /// Emit a scene saved event.
        /// </summary>
        public static void EmitSceneSaved(string sceneName, string path)
        {
            var payload = new Dictionary<string, object>
            {
                ["scene_name"] = sceneName,
                ["path"] = path
            };

            EmitEvent(EventTypes.SceneSaved, sceneName, payload);
        }

        /// <summary>
        /// Emit a scene opened event.
        /// </summary>
        public static void EmitSceneOpened(string sceneName, string path, string mode)
        {
            var payload = new Dictionary<string, object>
            {
                ["scene_name"] = sceneName,
                ["path"] = path,
                ["mode"] = mode
            };

            EmitEvent(EventTypes.SceneOpened, sceneName, payload);
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
        /// </summary>
        public static void EmitAssetImported(string assetPath, string assetType = null)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                Debug.LogWarning("[ActionTraceEventEmitter] Attempted to emit AssetImported with null or empty path");
                return;
            }

            var payload = new Dictionary<string, object>
            {
                ["path"] = assetPath,
                ["extension"] = System.IO.Path.GetExtension(assetPath)
            };

            if (!string.IsNullOrEmpty(assetType))
            {
                payload["asset_type"] = assetType;
            }
            else
            {
                // Auto-detect asset type
                payload["asset_type"] = DetectAssetType(assetPath);
            }

            EmitEvent(EventTypes.AssetImported, assetPath, payload);
        }

        /// <summary>
        /// Emit an asset deleted event.
        /// </summary>
        public static void EmitAssetDeleted(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                Debug.LogWarning("[ActionTraceEventEmitter] Attempted to emit AssetDeleted with null or empty path");
                return;
            }

            var payload = new Dictionary<string, object>
            {
                ["path"] = assetPath,
                ["extension"] = System.IO.Path.GetExtension(assetPath)
            };

            EmitEvent(EventTypes.AssetDeleted, assetPath, payload);
        }

        /// <summary>
        /// Emit an asset moved event.
        /// </summary>
        public static void EmitAssetMoved(string fromPath, string toPath)
        {
            if (string.IsNullOrEmpty(toPath))
            {
                Debug.LogWarning("[ActionTraceEventEmitter] Attempted to emit AssetMoved with null or empty destination path");
                return;
            }

            var payload = new Dictionary<string, object>
            {
                ["from_path"] = fromPath ?? string.Empty,
                ["to_path"] = toPath,
                ["extension"] = System.IO.Path.GetExtension(toPath)
            };

            EmitEvent(EventTypes.AssetMoved, toPath, payload);
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
        /// </summary>
        public static void EmitAssetModified(string assetPath, string assetType, IReadOnlyDictionary<string, object> changes)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                Debug.LogWarning("[ActionTraceEventEmitter] AssetModified with null path");
                return;
            }

            var payload = new Dictionary<string, object>
            {
                ["path"] = assetPath,
                ["asset_type"] = assetType ?? "Unknown",
                ["changes"] = changes ?? new Dictionary<string, object>(),
                ["source"] = "mcp_tool"  // Indicates this change came from an MCP tool call
            };

            EmitEvent(EventTypes.AssetModified, assetPath, payload);
        }

        /// <summary>
        /// Emit an asset created event via MCP tool (manage_asset).
        /// </summary>
        public static void EmitAssetCreated(string assetPath, string assetType)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                Debug.LogWarning("[ActionTraceEventEmitter] AssetCreated with null path");
                return;
            }

            var payload = new Dictionary<string, object>
            {
                ["path"] = assetPath,
                ["asset_type"] = assetType ?? "Unknown",
                ["source"] = "mcp_tool"
            };

            EmitEvent(EventTypes.AssetCreated, assetPath, payload);
        }

        /// <summary>
        /// Emit an asset deleted event via MCP tool (manage_asset).
        /// </summary>
        public static void EmitAssetDeleted(string assetPath, string assetType)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                Debug.LogWarning("[ActionTraceEventEmitter] AssetDeleted with null path");
                return;
            }

            var payload = new Dictionary<string, object>
            {
                ["path"] = assetPath,
                ["asset_type"] = assetType ?? "Unknown",
                ["source"] = "mcp_tool"
            };

            EmitEvent(EventTypes.AssetDeleted, assetPath, payload);
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

                EventStore.Record(evt);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ActionTraceEventEmitter] Failed to emit {eventType} event: {ex.Message}");
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
