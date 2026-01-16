using System;
using MCPForUnity.Editor.ActionTrace.Core;

namespace MCPForUnity.Editor.ActionTrace.Query
{
    /// <summary>
    /// Generates human-readable summaries for editor events.
    /// Hardcoded summaries for MVP - will be enhanced in Phase 2 with semantic analysis.
    /// </summary>
    public static class EventSummarizer
    {
        /// <summary>
        /// Generate a human-readable summary for an event.
        /// P1.2: Added support for AINote events.
        /// </summary>
        public static string Summarize(EditorEvent evt)
        {
            return evt.Type switch
            {
                EventTypes.ComponentAdded => SummarizeComponentAdded(evt),
                EventTypes.PropertyModified => SummarizePropertyModified(evt),
                EventTypes.SelectionPropertyModified => SummarizeSelectionPropertyModified(evt),
                EventTypes.HierarchyChanged => "Scene hierarchy changed",
                EventTypes.AssetImported => SummarizeAssetImported(evt),
                EventTypes.AssetDeleted => SummarizeAssetDeleted(evt),
                EventTypes.AssetMoved => SummarizeAssetMoved(evt),
                EventTypes.PlayModeChanged => SummarizePlayModeChanged(evt),
                EventTypes.SceneSaving => SummarizeSceneSaving(evt),
                EventTypes.SceneOpened => SummarizeSceneOpened(evt),
                "AINote" => SummarizeAINote(evt),  // P1.2: AI comment support
                _ => $"{evt.Type} on {GetTargetName(evt)}"
            };
        }

        private static string SummarizeComponentAdded(EditorEvent evt)
        {
            if (evt.Payload != null && evt.Payload.TryGetValue("component_type", out var componentType))
            {
                return $"Added {componentType} component to {GetGameObjectName(evt)}";
            }
            return $"Added component to {GetTargetName(evt)}";
        }

        private static string SummarizeAssetImported(EditorEvent evt)
        {
            if (evt.Payload != null && evt.Payload.TryGetValue("path", out var path))
            {
                if (evt.Payload.TryGetValue("asset_type", out var assetType))
                {
                    return $"Imported {assetType}: {path}";
                }
                return $"Imported asset: {path}";
            }
            return "Asset imported";
        }

        private static string SummarizeAssetDeleted(EditorEvent evt)
        {
            if (evt.Payload != null && evt.Payload.TryGetValue("path", out var path))
            {
                return $"Deleted asset: {path}";
            }
            return "Asset deleted";
        }

        private static string SummarizeAssetMoved(EditorEvent evt)
        {
            if (evt.Payload != null &&
                evt.Payload.TryGetValue("to_path", out var toPath) &&
                evt.Payload.TryGetValue("from_path", out var fromPath))
            {
                return $"Moved asset from {fromPath} to {toPath}";
            }
            return "Asset moved";
        }

        private static string SummarizePlayModeChanged(EditorEvent evt)
        {
            if (evt.Payload != null && evt.Payload.TryGetValue("state", out var state))
            {
                return $"Play mode changed to {state}";
            }
            return "Play mode changed";
        }

        private static string SummarizeSceneSaving(EditorEvent evt)
        {
            if (evt.Payload != null && evt.Payload.TryGetValue("scene_name", out var sceneName))
            {
                return $"Saving scene: {sceneName}";
            }
            return "Scene saving";
        }

        private static string SummarizeSceneOpened(EditorEvent evt)
        {
            if (evt.Payload != null && evt.Payload.TryGetValue("scene_name", out var sceneName))
            {
                return $"Opened scene: {sceneName}";
            }
            return "Scene opened";
        }

        private static string GetTargetName(EditorEvent evt)
        {
            // Try to get a human-readable name from payload
            if (evt.Payload != null && evt.Payload.TryGetValue("name", out var name))
            {
                return name.ToString();
            }
            if (evt.Payload != null && evt.Payload.TryGetValue("game_object", out var goName))
            {
                return goName.ToString();
            }
            if (evt.Payload != null && evt.Payload.TryGetValue("scene_name", out var sceneName))
            {
                return sceneName.ToString();
            }
            // Fall back to target ID
            return evt.TargetId;
        }

        private static string GetGameObjectName(EditorEvent evt)
        {
            if (evt.Payload != null && evt.Payload.TryGetValue("game_object", out var goName))
            {
                return goName.ToString();
            }
            return GetTargetName(evt);
        }

        /// <summary>
        /// Generates a human-readable summary for property modification events.
        /// Format: "Changed {ComponentType}.{PropertyPath} from {StartValue} to {EndValue}"
        /// </summary>
        private static string SummarizePropertyModified(EditorEvent evt)
        {
            // Try to get component type and property path
            string componentType = null;
            string propertyPath = null;
            string targetName = null;

            if (evt.Payload != null && evt.Payload.TryGetValue("component_type", out var compType))
                componentType = compType.ToString();

            if (evt.Payload != null && evt.Payload.TryGetValue("property_path", out var propPath))
                propertyPath = propPath.ToString();

            if (evt.Payload != null && evt.Payload.TryGetValue("target_name", out var tgtName))
                targetName = tgtName.ToString();

            // Get readable values (strip quotes from JSON strings)
            string startValue = GetReadableValue(evt, "start_value");
            string endValue = GetReadableValue(evt, "end_value");

            // Format: "Changed Light.m_Intensity from 1.0 to 5.0"
            if (!string.IsNullOrEmpty(componentType) && !string.IsNullOrEmpty(propertyPath))
            {
                // Strip "m_" prefix from Unity serialized property names for readability
                string readableProperty = propertyPath.StartsWith("m_")
                    ? propertyPath.Substring(2)
                    : propertyPath;

                if (!string.IsNullOrEmpty(startValue) && !string.IsNullOrEmpty(endValue))
                {
                    return $"Changed {componentType}.{readableProperty} from {startValue} to {endValue}";
                }
                return $"Changed {componentType}.{readableProperty}";
            }

            // Fallback to target name if available
            if (!string.IsNullOrEmpty(targetName))
            {
                if (!string.IsNullOrEmpty(propertyPath))
                {
                    string readableProperty = propertyPath.StartsWith("m_")
                        ? propertyPath.Substring(2)
                        : propertyPath;
                    return $"Changed {targetName}.{readableProperty}";
                }
                return $"Changed {targetName}";
            }

            return "Property modified";
        }

        /// <summary>
        /// Extracts a readable value from the payload, handling JSON formatting.
        /// Removes quotes from string values and limits length.
        /// </summary>
        private static string GetReadableValue(EditorEvent evt, string key)
        {
            if (evt.Payload == null || !evt.Payload.TryGetValue(key, out var value))
                return null;

            string valueStr = value.ToString();
            if (string.IsNullOrEmpty(valueStr))
                return null;

            // Remove quotes from JSON string values
            if (valueStr.StartsWith("\"") && valueStr.EndsWith("\"") && valueStr.Length > 1)
            {
                valueStr = valueStr.Substring(1, valueStr.Length - 2);
            }

            // Truncate long values (e.g., long vectors)
            if (valueStr.Length > 50)
            {
                valueStr = valueStr.Substring(0, 47) + "...";
            }

            return valueStr;
        }

        /// <summary>
        /// P2.4: Generate a human-readable summary for SelectionPropertyModified events.
        /// Similar to PropertyModified but with selection context emphasized.
        /// Format: "Changed {ComponentType}.{PropertyPath} from {StartValue} to {EndValue} (selected)"
        /// </summary>
        private static string SummarizeSelectionPropertyModified(EditorEvent evt)
        {
            // Reuse PropertyModified logic but add selection indicator
            string baseSummary = SummarizePropertyModified(evt);
            return $"{baseSummary} (selected)";
        }

        /// <summary>
        /// P1.2: Generate a human-readable summary for AI Note events.
        /// Format: "AI Note: {note}" or "AI Note from {agent_id}: {note}"
        /// </summary>
        private static string SummarizeAINote(EditorEvent evt)
        {
            if (evt.Payload == null)
                return "AI Note";

            // Get the note text
            if (evt.Payload.TryGetValue("note", out var note))
            {
                string noteText = note?.ToString() ?? "Empty note";

                // Get agent_id if available
                if (evt.Payload.TryGetValue("agent_id", out var agentId))
                {
                    string agent = agentId?.ToString();
                    if (!string.IsNullOrEmpty(agent) && agent != "unknown")
                    {
                        return $"AI Note ({agent}): {noteText}";
                    }
                }

                return $"AI Note: {noteText}";
            }

            return "AI Note";
        }
    }
}
