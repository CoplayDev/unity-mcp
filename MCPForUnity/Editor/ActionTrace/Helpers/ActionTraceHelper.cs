using System;
using UnityEngine;
using UnityEditor;
using MCPForUnity.Editor.ActionTrace.Core.Models;

namespace MCPForUnity.Editor.ActionTrace.Helpers
{
    /// <summary>
    /// Helper utilities for ActionTrace feature.
    ///
    /// Centralized common formatting and conversion methods
    /// to avoid code duplication across ActionTrace components.
    /// </summary>
    public static class ActionTraceHelper
    {
        /// <summary>
        /// Extracts a string value from event Payload by key.
        /// Returns defaultValue (null) if not present or value is null.
        /// </summary>
        public static string GetPayloadString(this EditorEvent evt, string key, string defaultValue = null)
        {
            if (evt.Payload == null)
                return defaultValue;

            if (evt.Payload.TryGetValue(key, out var value))
                return value?.ToString();

            return defaultValue;
        }
        /// <summary>
        /// Formats a tool name for display.
        /// Converts snake_case to Title Case.
        ///
        /// Examples:
        /// - "manage_gameobject" → "Manage GameObject"
        /// - "add_ActionTrace_note" → "Add ActionTrace Note"
        /// - "get_ActionTrace" → "Get ActionTrace"
        ///
        /// Used in:
        /// - TransactionAggregator (summary generation)
        /// - UndoGroupManager (Undo group names)
        /// </summary>
        public static string FormatToolName(string toolName)
        {
            if (string.IsNullOrEmpty(toolName))
                return "AI Operation";

            // Convert snake_case to Title Case with spaces
            // Examples: "manage_gameobject" → "Manage GameObject"
            return System.Text.RegularExpressions.Regex.Replace(
                toolName,
                "(^|_)([a-z])",
                match =>
                {
                    // If starts with underscore, replace underscore with space and uppercase
                    // If at start, just uppercase
                    return match.Groups[1].Value == "_"
                        ? " " + match.Groups[2].Value.ToUpper()
                        : match.Groups[2].Value.ToUpper();
                }
            );
        }

        /// <summary>
        /// Formats duration for display.
        /// Converts milliseconds to human-readable "X.Xs" format.
        ///
        /// Examples:
        /// - 500 → "0.5s"
        /// - 1500 → "1.5s"
        /// - 2340 → "2.3s"
        ///
        /// Used in:
        /// - TransactionAggregator (AtomicOperation.DurationMs display)
        /// </summary>
        public static string FormatDuration(long milliseconds)
        {
            return $"{milliseconds / 1000.0:F1}s";
        }

        /// <summary>
        /// Formats duration from a timestamp range.
        ///
        /// Parameters:
        ///   startMs: Start timestamp in milliseconds
        ///   endMs: End timestamp in milliseconds
        ///
        /// Returns:
        ///   Human-readable duration string (e.g., "2.3s")
        /// </summary>
        public static string FormatDurationFromRange(long startMs, long endMs)
        {
            return FormatDuration(endMs - startMs);
        }

    }
}
