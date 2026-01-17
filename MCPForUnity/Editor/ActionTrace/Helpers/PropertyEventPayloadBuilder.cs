using System;
using System.Collections.Generic;

namespace MCPForUnity.Editor.ActionTrace.Helpers
{
    /// <summary>
    /// Unified payload builder for property modification events.
    /// Ensures consistent payload structure across different trackers.
    /// </summary>
    public static class PropertyEventPayloadBuilder
    {
        /// <summary>
        /// Builds the base payload for a property modification event.
        /// </summary>
        /// <param name="targetName">Name of the modified object</param>
        /// <param name="componentType">Type of the modified component</param>
        /// <param name="propertyPath">Serialized property path</param>
        /// <param name="startValue">JSON formatted start value</param>
        /// <param name="endValue">JSON formatted end value</param>
        /// <param name="valueType">Type name of the property value</param>
        /// <param name="changeCount">Number of merged changes</param>
        public static Dictionary<string, object> BuildPropertyModifiedPayload(
            string targetName,
            string componentType,
            string propertyPath,
            string startValue,
            string endValue,
            string valueType,
            int changeCount = 1)
        {
            return new Dictionary<string, object>
            {
                ["target_name"] = targetName,
                ["component_type"] = componentType,
                ["property_path"] = propertyPath,
                ["start_value"] = startValue,
                ["end_value"] = endValue,
                ["value_type"] = valueType,
                ["change_count"] = changeCount
            };
        }

        /// <summary>
        /// Builds a selection property modified event payload with selection context.
        /// </summary>
        public static Dictionary<string, object> BuildSelectionPropertyModifiedPayload(
            string targetName,
            string componentType,
            string propertyPath,
            string startValue,
            string endValue,
            string valueType,
            string selectionName,
            string selectionType,
            string selectionPath)
        {
            return new Dictionary<string, object>
            {
                ["target_name"] = targetName,
                ["component_type"] = componentType,
                ["property_path"] = propertyPath,
                ["start_value"] = startValue,
                ["end_value"] = endValue,
                ["value_type"] = valueType,
                ["selection_context"] = new Dictionary<string, object>
                {
                    ["selection_name"] = selectionName,
                    ["selection_type"] = selectionType,
                    ["selection_path"] = selectionPath ?? string.Empty
                }
            };
        }
    }
}
