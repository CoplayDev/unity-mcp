using System.Collections.Generic;
using MCPForUnity.Editor.ActionTrace.Core.Models;

namespace MCPForUnity.Editor.ActionTrace.Metadata.Descriptors
{
    /// <summary>
    /// Interface for event descriptors.
    /// Descriptors encapsulate event type metadata, payload schema, and summarization logic.
    /// This decouples EventSummarizer from payload structure changes.
    /// </summary>
    public interface IEventDescriptor
    {
        /// <summary>
        /// The event type this descriptor handles.
        /// Must match one of the constants in EventTypes.
        /// </summary>
        string EventType { get; }

        /// <summary>
        /// Generate a human-readable summary for this event type.
        /// </summary>
        string Summarize(EditorEvent evt);

        /// <summary>
        /// Validate and extract payload fields.
        /// Returns a sanitized payload dictionary with all required fields.
        /// Can be used to validate payload structure before recording.
        /// </summary>
        Dictionary<string, object> ExtractPayload(Dictionary<string, object> rawPayload);
    }

    /// <summary>
    /// Base class for event descriptors with common functionality.
    /// </summary>
    public abstract class EventDescriptorBase : IEventDescriptor
    {
        public abstract string EventType { get; }

        public abstract string Summarize(EditorEvent evt);

        public virtual Dictionary<string, object> ExtractPayload(Dictionary<string, object> rawPayload)
        {
            // Default implementation: pass through payload as-is
            return rawPayload != null
                ? new Dictionary<string, object>(rawPayload)
                : new Dictionary<string, object>();
        }

        /// <summary>
        /// Helper method to safely get a string value from payload.
        /// Guards against null events, null payloads, and null values.
        /// </summary>
        protected string GetString(EditorEvent evt, string key, string defaultValue = "")
        {
            if (evt?.Payload != null && evt.Payload.TryGetValue(key, out var value))
            {
                return value?.ToString() ?? defaultValue;
            }
            return defaultValue;
        }

        /// <summary>
        /// Helper method to safely get a target name from payload.
        /// Tries multiple common keys like "name", "game_object", "scene_name", etc.
        /// Guards against null events, null payloads, and null values.
        /// </summary>
        protected string GetTargetName(EditorEvent evt)
        {
            if (evt?.Payload != null)
            {
                if (evt.Payload.TryGetValue("name", out var name) && name != null)
                    return name.ToString();

                if (evt.Payload.TryGetValue("game_object", out var goName) && goName != null)
                    return goName.ToString();

                if (evt.Payload.TryGetValue("scene_name", out var sceneName) && sceneName != null)
                    return sceneName.ToString();

                if (evt.Payload.TryGetValue("component_type", out var componentType) && componentType != null)
                    return componentType.ToString();

                if (evt.Payload.TryGetValue("path", out var path) && path != null)
                    return path.ToString();
            }

            // Fall back to target ID
            return evt?.TargetId ?? "";
        }
    }
}
