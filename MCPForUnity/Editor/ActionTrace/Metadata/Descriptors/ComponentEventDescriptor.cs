using System;
using System.Collections.Generic;
using MCPForUnity.Editor.ActionTrace.Core;
using MCPForUnity.Editor.ActionTrace.Core.Models;

namespace MCPForUnity.Editor.ActionTrace.Metadata.Descriptors
{
    /// <summary>
    /// Descriptor for ComponentAdded events.
    /// </summary>
    public sealed class ComponentAddedDescriptor : EventDescriptorBase
    {
        public override string EventType => EventTypes.ComponentAdded;

        public override string Summarize(EditorEvent evt)
        {
            var componentType = GetString(evt, "component_type", "Component");
            var targetName = GetString(evt, "game_object", GetTargetName(evt));
            return $"Added {componentType} component to {targetName}";
        }

        public override Dictionary<string, object> ExtractPayload(Dictionary<string, object> rawPayload)
        {
            if (rawPayload == null)
                return new Dictionary<string, object>();

            return new Dictionary<string, object>
            {
                ["component_type"] = rawPayload.GetValueOrDefault("component_type", "Unknown"),
                ["game_object"] = rawPayload.GetValueOrDefault("game_object", "Unknown")
            };
        }
    }

    /// <summary>
    /// Descriptor for HierarchyChanged events.
    /// </summary>
    public sealed class HierarchyChangedDescriptor : EventDescriptorBase
    {
        public override string EventType => EventTypes.HierarchyChanged;

        public override string Summarize(EditorEvent evt)
        {
            return "Scene hierarchy changed";
        }

        public override Dictionary<string, object> ExtractPayload(Dictionary<string, object> rawPayload)
        {
            return new Dictionary<string, object>
            {
                ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }
    }

    /// <summary>
    /// Descriptor for AssetImported events.
    /// </summary>
    public sealed class AssetImportedDescriptor : EventDescriptorBase
    {
        public override string EventType => EventTypes.AssetImported;

        public override string Summarize(EditorEvent evt)
        {
            var path = GetString(evt, "path", "Unknown");
            var assetType = GetString(evt, "asset_type", "asset");

            // More specific summaries for known asset types
            if (assetType == "script")
                return $"Imported script: {path}";
            if (assetType == "scene")
                return $"Imported scene: {path}";
            if (assetType == "prefab")
                return $"Imported prefab: {path}";
            if (assetType == "texture")
                return $"Imported texture: {path}";
            if (assetType == "audio")
                return $"Imported audio: {path}";

            return $"Imported {assetType}: {path}";
        }

        public override Dictionary<string, object> ExtractPayload(Dictionary<string, object> rawPayload)
        {
            if (rawPayload == null)
                return new Dictionary<string, object>();

            var path = rawPayload.GetValueOrDefault("path", string.Empty)?.ToString() ?? string.Empty;
            var extension = System.IO.Path.GetExtension(path);

            return new Dictionary<string, object>
            {
                ["path"] = path,
                ["extension"] = extension,
                ["asset_type"] = rawPayload.GetValueOrDefault("asset_type", DetectAssetType(extension))
            };
        }

        private static string DetectAssetType(string extension)
        {
            if (string.IsNullOrEmpty(extension))
                return "unknown";

            return extension.ToLower() switch
            {
                ".cs" => "script",
                ".unity" => "scene",
                ".prefab" => "prefab",
                ".mat" => "material",
                ".png" or ".jpg" or ".jpeg" => "texture",
                ".wav" or ".mp3" or ".ogg" => "audio",
                ".fbx" => "model",
                ".anim" => "animation",
                ".controller" => "animator_controller",
                _ => "unknown"
            };
        }
    }

    /// <summary>
    /// Descriptor for PlayModeChanged events.
    /// </summary>
    public sealed class PlayModeChangedDescriptor : EventDescriptorBase
    {
        public override string EventType => EventTypes.PlayModeChanged;

        public override string Summarize(EditorEvent evt)
        {
            var state = GetString(evt, "state", "Unknown");
            return $"Play mode changed to {state}";
        }

        public override Dictionary<string, object> ExtractPayload(Dictionary<string, object> rawPayload)
        {
            return new Dictionary<string, object>
            {
                ["state"] = rawPayload?.GetValueOrDefault("state", "Unknown") ?? "Unknown"
            };
        }
    }

    /// <summary>
    /// Descriptor for SceneSaving events.
    /// </summary>
    public sealed class SceneSavingDescriptor : EventDescriptorBase
    {
        public override string EventType => EventTypes.SceneSaving;

        public override string Summarize(EditorEvent evt)
        {
            var sceneName = GetString(evt, "scene_name", "Scene");
            return $"Saving scene: {sceneName}";
        }

        public override Dictionary<string, object> ExtractPayload(Dictionary<string, object> rawPayload)
        {
            return new Dictionary<string, object>
            {
                ["scene_name"] = rawPayload?.GetValueOrDefault("scene_name", "Unknown") ?? "Unknown",
                ["path"] = rawPayload?.GetValueOrDefault("path", string.Empty) ?? string.Empty
            };
        }
    }

    /// <summary>
    /// Descriptor for SceneOpened events.
    /// </summary>
    public sealed class SceneOpenedDescriptor : EventDescriptorBase
    {
        public override string EventType => EventTypes.SceneOpened;

        public override string Summarize(EditorEvent evt)
        {
            var sceneName = GetString(evt, "scene_name", "Scene");
            return $"Opened scene: {sceneName}";
        }

        public override Dictionary<string, object> ExtractPayload(Dictionary<string, object> rawPayload)
        {
            return new Dictionary<string, object>
            {
                ["scene_name"] = rawPayload?.GetValueOrDefault("scene_name", "Unknown") ?? "Unknown",
                ["path"] = rawPayload?.GetValueOrDefault("path", string.Empty) ?? string.Empty,
                ["mode"] = rawPayload?.GetValueOrDefault("mode", "Unknown") ?? "Unknown"
            };
        }
    }
}
