using System.Collections.Generic;
using System.Text;

namespace MCPForUnity.Editor.Windows.ActionTraceEditorWindow
{
    /// <summary>
    /// Cache manager for ActionTrace window.
    /// Manages icon cache and string builder reuse to optimize performance.
    /// </summary>
    internal sealed class ActionTraceCache
    {
        private readonly Dictionary<string, string> _iconCache = new();
        private readonly StringBuilder _stringBuilder = new();

        /// <summary>
        /// Get or create cached event type icon.
        /// </summary>
        public string GetEventTypeIcon(string eventType)
        {
            if (string.IsNullOrEmpty(eventType))
                return "•";

            if (_iconCache.TryGetValue(eventType, out var icon))
                return icon;

            icon = eventType switch
            {
                "ASSET_CHANGE" => "📝",
                "COMPILATION" => "⚙️",
                "PROPERTY_EDIT" => "🔧",
                "SCENE_SAVE" => "🎨",
                "SELECTION" => "📦",
                "MENU_ACTION" => "🔨",
                "BUILD_START" => "💾",
                "ERROR" => "⚠️",
                _ => "•"
            };

            _iconCache[eventType] = icon;
            return icon;
        }

        /// <summary>
        /// Get the cached StringBuilder for efficient string concatenation.
        /// </summary>
        public StringBuilder GetStringBuilder()
        {
            _stringBuilder.Clear();
            return _stringBuilder;
        }

        /// <summary>
        /// Clear all caches to prevent memory leaks.
        /// </summary>
        public void ClearAll()
        {
            _iconCache.Clear();
            _stringBuilder.Clear();
        }
    }
}
