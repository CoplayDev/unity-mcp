using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using MCPForUnity.Editor.ActionTrace.Core;

namespace MCPForUnity.Editor.ActionTrace.Query
{
    /// <summary>
    /// Generates human-readable summaries for editor events.
    ///
    /// Uses event metadata templates for most events, with special handling
    /// for complex cases like PropertyModified.
    ///
    /// Template Syntax:
    /// - {key} - Simple placeholder replacement
    /// - {if:key, then} - Conditional: insert 'then' if key exists and has meaningful value
    /// - {if:key, then, else} - Conditional with else branch
    /// - {if_any:key1,key2, then} - Insert 'then' if ANY key has meaningful value
    /// - {if_all:key1,key2, then} - Insert 'then' if ALL keys have meaningful value
    /// - {eq:key, value, then} - Insert 'then' if key equals value
    /// - {ne:key, value, then} - Insert 'then' if key does not equal value
    /// - {format:key, format} - Format key value (supports: upper, lower, trim, truncate:N)
    /// - {target_id} - GameObject/Target ID for AI tool invocation
    /// - {property_path_no_m} - Strip "m_" prefix from Unity properties
    /// - {start_value_readable} - Format start value for display
    /// - {end_value_readable} - Format end value for display
    ///
    /// To add summary for a new event:
    /// 1. Add SummaryTemplate to the event's metadata in EventTypes.Metadata
    /// 2. That's it! No need to add a separate SummarizeXxx method.
    /// </summary>
    public static class EventSummarizer
    {
        // Precompiled regex patterns for template processing
        private static readonly Regex IfPattern = new Regex(@"\{if:([^,}]+),\s*([^}]*)\}", RegexOptions.Compiled);
        private static readonly Regex IfElsePattern = new Regex(@"\{if:([^,}]+),\s*([^,}]+),\s*([^}]*)\}", RegexOptions.Compiled);
        private static readonly Regex IfAnyPattern = new Regex(@"\{if_any:([^}]+),\s*([^}]*)\}", RegexOptions.Compiled);
        private static readonly Regex IfAllPattern = new Regex(@"\{if_all:([^}]+),\s*([^}]*)\}", RegexOptions.Compiled);
        private static readonly Regex EqPattern = new Regex(@"\{eq:([^,}]+),\s*([^,}]+),\s*([^}]*)\}", RegexOptions.Compiled);
        private static readonly Regex NePattern = new Regex(@"\{ne:([^,}]+),\s*([^,}]+),\s*([^}]*)\}", RegexOptions.Compiled);
        private static readonly Regex FormatPattern = new Regex(@"\{format:([^,}]+),\s*([^}]*)\}", RegexOptions.Compiled);

        // Formatting constants
        private const int DefaultTruncateLength = 9;
        private const int ReadableValueMaxLength = 50;
        private const int FormattedValueMaxLength = 100;
        private const int TruncatedSuffixLength = 3;

        /// <summary>
        /// Generate a human-readable summary for an event.
        /// Uses metadata templates when available, falls back to special handlers.
        /// </summary>
        public static string Summarize(EditorEvent evt)
        {
            // Special cases that need complex logic
            string specialSummary = GetSpecialCaseSummary(evt);
            if (specialSummary != null)
                return specialSummary;

            // Use metadata template
            var meta = EventTypes.Metadata.Get(evt.Type);
            if (!string.IsNullOrEmpty(meta.SummaryTemplate))
            {
                return FormatTemplate(meta.SummaryTemplate, evt);
            }

            // Default fallback
            return $"{evt.Type} on {GetTargetName(evt)}";
        }

        /// <summary>
        /// Format a template string with event data.
        ///
        /// Processing order (later patterns can use results of earlier):
        /// 1. Conditionals (if, if_any, if_all, eq, ne)
        /// 2. Format directives
        /// 3. Simple placeholders
        /// 4. Special placeholders
        /// </summary>
        private static string FormatTemplate(string template, EditorEvent evt)
        {
            if (string.IsNullOrEmpty(template))
                return string.Empty;

            string result = template;

            // Process conditionals first (in order of specificity)
            result = ProcessIfElse(result, evt);
            result = ProcessIfAny(result, evt);
            result = ProcessIfAll(result, evt);
            result = ProcessSimpleIf(result, evt);
            result = ProcessEq(result, evt);
            result = ProcessNe(result, evt);

            // Process format directives
            result = ProcessFormat(result, evt);

            // Build result with StringBuilder for efficient replacements
            var sb = new StringBuilder(result);

            // Handle regular placeholders
            foreach (var kvp in evt.Payload ?? new Dictionary<string, object>())
            {
                string placeholder = "{" + kvp.Key + "}";
                int index = 0;
                while ((index = sb.ToString().IndexOf(placeholder, index, StringComparison.Ordinal)) >= 0)
                {
                    string value = FormatValue(kvp.Value);
                    sb.Remove(index, placeholder.Length);
                    sb.Insert(index, value);
                }
            }

            // Special placeholders
            sb.Replace("{type}", evt.Type ?? "");
            sb.Replace("{target}", GetTargetName(evt) ?? "");
            sb.Replace("{target_id}", evt.TargetId ?? "");
            sb.Replace("{time}", FormatTime(evt.TimestampUnixMs));
            sb.Replace("{property_path_no_m}", StripMPrefix(evt, "property_path"));
            sb.Replace("{start_value_readable}", GetReadableValue(evt, "start_value"));
            sb.Replace("{end_value_readable}", GetReadableValue(evt, "end_value"));

            return sb.ToString();
        }

        /// <summary>
        /// Process {if:key, then, else} conditionals with else branch.
        /// </summary>
        private static string ProcessIfElse(string template, EditorEvent evt)
        {
            return IfElsePattern.Replace(template, match =>
            {
                string key = match.Groups[1].Value.Trim();
                string thenText = match.Groups[2].Value.Trim();
                string elseText = match.Groups[3].Value.Trim();
                return HasMeaningfulValue(evt, key) ? thenText : elseText;
            });
        }

        /// <summary>
        /// Process {if_any:key1,key2, then} - true if ANY key has meaningful value.
        /// </summary>
        private static string ProcessIfAny(string template, EditorEvent evt)
        {
            return IfAnyPattern.Replace(template, match =>
            {
                string keys = match.Groups[1].Value;
                string thenText = match.Groups[2].Value.Trim();
                string[] keyList = keys.Split(',');

                foreach (string key in keyList)
                {
                    if (HasMeaningfulValue(evt, key.Trim()))
                        return thenText;
                }
                return "";
            });
        }

        /// <summary>
        /// Process {if_all:key1,key2, then} - true only if ALL keys have meaningful values.
        /// </summary>
        private static string ProcessIfAll(string template, EditorEvent evt)
        {
            return IfAllPattern.Replace(template, match =>
            {
                string keys = match.Groups[1].Value;
                string thenText = match.Groups[2].Value.Trim();
                string[] keyList = keys.Split(',');

                foreach (string key in keyList)
                {
                    if (!HasMeaningfulValue(evt, key.Trim()))
                        return "";
                }
                return thenText;
            });
        }

        /// <summary>
        /// Process simple {if:key, then} conditionals (without else).
        /// Done after if_else to avoid double-processing.
        /// </summary>
        private static string ProcessSimpleIf(string template, EditorEvent evt)
        {
            return IfPattern.Replace(template, match =>
            {
                // Skip if this looks like part of an already-processed pattern
                if (match.Value.Contains(",,")) return match.Value;

                string key = match.Groups[1].Value.Trim();
                string thenText = match.Groups[2].Value.Trim();
                return HasMeaningfulValue(evt, key) ? thenText : "";
            });
        }

        /// <summary>
        /// Process {eq:key, value, then} - insert 'then' if key equals value.
        /// </summary>
        private static string ProcessEq(string template, EditorEvent evt)
        {
            return EqPattern.Replace(template, match =>
            {
                string key = match.Groups[1].Value.Trim();
                string expectedValue = match.Groups[2].Value.Trim();
                string thenText = match.Groups[3].Value.Trim();

                string actualValue = GetPayloadStringValue(evt, key);
                return string.Equals(actualValue, expectedValue, StringComparison.Ordinal) ? thenText : "";
            });
        }

        /// <summary>
        /// Process {ne:key, value, then} - insert 'then' if key does not equal value.
        /// </summary>
        private static string ProcessNe(string template, EditorEvent evt)
        {
            return NePattern.Replace(template, match =>
            {
                string key = match.Groups[1].Value.Trim();
                string expectedValue = match.Groups[2].Value.Trim();
                string thenText = match.Groups[3].Value.Trim();

                string actualValue = GetPayloadStringValue(evt, key);
                return !string.Equals(actualValue, expectedValue, StringComparison.Ordinal) ? thenText : "";
            });
        }

        /// <summary>
        /// Process {format:key, format} - format key value.
        /// Supported formats: upper, lower, trim, truncate:N, capitalize
        /// </summary>
        private static string ProcessFormat(string template, EditorEvent evt)
        {
            return FormatPattern.Replace(template, match =>
            {
                string key = match.Groups[1].Value.Trim();
                string format = match.Groups[2].Value.Trim();

                string value = GetPayloadStringValue(evt, key);
                if (string.IsNullOrEmpty(value))
                    return "";

                return format switch
                {
                    "upper" => value.ToUpperInvariant(),
                    "lower" => value.ToLowerInvariant(),
                    "trim" => value.Trim(),
                    "capitalize" => Capitalize(value),
                    _ when format.StartsWith("truncate:") => Truncate(value, ParseInt(format, DefaultTruncateLength)),
                    _ => value
                };
            });
        }

        /// <summary>
        /// Gets a string value from payload, or empty string if key doesn't exist.
        /// </summary>
        private static string GetPayloadStringValue(EditorEvent evt, string key)
        {
            if (evt.Payload != null && evt.Payload.TryGetValue(key, out var value))
            {
                return value?.ToString() ?? "";
            }
            return "";
        }

        /// <summary>
        /// Parse integer from format string (e.g., "truncate:20" -> 20).
        /// </summary>
        private static int ParseInt(string format, int defaultValue)
        {
            int colonIdx = format.IndexOf(':');
            if (colonIdx >= 0 && int.TryParse(format.AsSpan(colonIdx + 1), out int result))
                return result;
            return defaultValue;
        }

        /// <summary>
        /// Capitalize first letter of string.
        /// </summary>
        private static string Capitalize(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;
            return char.ToUpperInvariant(value[0]) + value.Substring(1);
        }

        /// <summary>
        /// Truncate string to max length, adding "..." if truncated.
        /// </summary>
        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
                return value;
            return value.Substring(0, Math.Max(0, maxLength - TruncatedSuffixLength)) + "...";
        }

        /// <summary>
        /// Get summary for events that need special handling.
        /// Returns null if no special handling is needed.
        /// </summary>
        private static string GetSpecialCaseSummary(EditorEvent evt)
        {
            return evt.Type switch
            {
                EventTypes.PropertyModified => SummarizePropertyModified(evt),
                EventTypes.SelectionPropertyModified => SummarizeSelectionPropertyModified(evt),
                _ => null
            };
        }

        /// <summary>
        /// Generate a human-readable summary for property modification events.
        /// Format: "Changed {ComponentType}.{PropertyPath} from {StartValue} to {EndValue} (GameObject:{target_id})"
        /// Strips "m_" prefix from Unity serialized property names.
        /// Includes GameObject ID for AI tool invocation.
        /// </summary>
        private static string SummarizePropertyModified(EditorEvent evt)
        {
            if (evt.Payload == null)
                return "Property modified";

            string componentType = GetPayloadString(evt, "component_type");
            string propertyPath = GetPayloadString(evt, "property_path");
            string targetName = GetPayloadString(evt, "target_name");

            // Strip "m_" prefix from Unity serialized property names
            string readableProperty = propertyPath?.StartsWith("m_") == true
                ? propertyPath.Substring(2)
                : propertyPath;

            string startValue = GetReadableValue(evt, "start_value");
            string endValue = GetReadableValue(evt, "end_value");

            // Build base summary
            string baseSummary;
            if (!string.IsNullOrEmpty(componentType) && !string.IsNullOrEmpty(readableProperty))
            {
                if (!string.IsNullOrEmpty(startValue) && !string.IsNullOrEmpty(endValue))
                {
                    baseSummary = $"Changed {componentType}.{readableProperty} from {startValue} to {endValue}";
                }
                else
                {
                    baseSummary = $"Changed {componentType}.{readableProperty}";
                }
            }
            else if (!string.IsNullOrEmpty(targetName))
            {
                if (!string.IsNullOrEmpty(readableProperty))
                {
                    baseSummary = $"Changed {targetName}.{readableProperty}";
                }
                else
                {
                    baseSummary = $"Changed {targetName}";
                }
            }
            else
            {
                return "Property modified";
            }

            // Append GameObject ID for AI tool invocation
            if (!string.IsNullOrEmpty(evt.TargetId))
            {
                return $"{baseSummary} (GameObject:{evt.TargetId})";
            }
            return baseSummary;
        }

        /// <summary>
        /// Generate a human-readable summary for SelectionPropertyModified events.
        /// Same as PropertyModified but adds "(selected)" suffix.
        /// Includes GameObject ID for AI tool invocation.
        /// </summary>
        private static string SummarizeSelectionPropertyModified(EditorEvent evt)
        {
            if (evt.Payload == null)
                return "Property modified (selected)";

            string componentType = GetPayloadString(evt, "component_type");
            string propertyPath = GetPayloadString(evt, "property_path");
            string targetName = GetPayloadString(evt, "target_name");

            // Strip "m_" prefix from Unity serialized property names
            string readableProperty = propertyPath?.StartsWith("m_") == true
                ? propertyPath.Substring(2)
                : propertyPath;

            string startValue = GetReadableValue(evt, "start_value");
            string endValue = GetReadableValue(evt, "end_value");

            // Build base summary
            string baseSummary;
            if (!string.IsNullOrEmpty(componentType) && !string.IsNullOrEmpty(readableProperty))
            {
                if (!string.IsNullOrEmpty(startValue) && !string.IsNullOrEmpty(endValue))
                {
                    baseSummary = $"Changed {componentType}.{readableProperty} from {startValue} to {endValue}";
                }
                else
                {
                    baseSummary = $"Changed {componentType}.{readableProperty}";
                }
            }
            else if (!string.IsNullOrEmpty(targetName))
            {
                if (!string.IsNullOrEmpty(readableProperty))
                {
                    baseSummary = $"Changed {targetName}.{readableProperty}";
                }
                else
                {
                    baseSummary = $"Changed {targetName}";
                }
            }
            else
            {
                return "Property modified (selected)";
            }

            // Append "selected" and GameObject ID for AI tool invocation
            if (!string.IsNullOrEmpty(evt.TargetId))
            {
                return $"{baseSummary} (selected, GameObject:{evt.TargetId})";
            }
            return $"{baseSummary} (selected)";
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
            if (valueStr.Length > ReadableValueMaxLength)
            {
                valueStr = valueStr.Substring(0, ReadableValueMaxLength - TruncatedSuffixLength) + "...";
            }

            return valueStr;
        }

        /// <summary>
        /// Gets a string value from payload, or null if key doesn't exist.
        /// </summary>
        private static string GetPayloadString(EditorEvent evt, string key)
        {
            if (evt.Payload != null && evt.Payload.TryGetValue(key, out var value))
            {
                return value?.ToString();
            }
            return null;
        }

        /// <summary>
        /// Checks if a payload key has a meaningful (non-empty, non-default) value.
        /// </summary>
        private static bool HasMeaningfulValue(EditorEvent evt, string key)
        {
            if (evt.Payload == null || !evt.Payload.TryGetValue(key, out var value))
                return false;

            string valueStr = value?.ToString();
            if (string.IsNullOrEmpty(valueStr))
                return false;

            // Check for common "empty" values
            if (valueStr == "0" || valueStr == "0.0" || valueStr == "false" || valueStr == "null" || valueStr == "unknown")
                return false;

            return true;
        }

        /// <summary>
        /// Format a payload value for display in summaries.
        /// </summary>
        private static string FormatValue(object value)
        {
            if (value == null)
                return "";

            string str = value.ToString();

            // Truncate long strings
            if (str.Length > FormattedValueMaxLength)
                str = str.Substring(0, FormattedValueMaxLength - TruncatedSuffixLength) + "...";

            return str;
        }

        /// <summary>
        /// Strip "m_" prefix from a payload property value.
        /// </summary>
        private static string StripMPrefix(EditorEvent evt, string key)
        {
            string value = GetPayloadString(evt, key);
            if (value?.StartsWith("m_") == true)
                return value.Substring(2);
            return value ?? "";
        }

        /// <summary>
        /// Get a human-readable name for the event target.
        /// Tries payload fields in order: name, game_object, scene_name, component_type, path.
        /// Falls back to TargetId if none found.
        /// </summary>
        private static string GetTargetName(EditorEvent evt)
        {
            // Try to get a human-readable name from payload
            if (evt.Payload != null)
            {
                if (evt.Payload.TryGetValue("name", out var name))
                    return name.ToString();
                if (evt.Payload.TryGetValue("game_object", out var goName))
                    return goName.ToString();
                if (evt.Payload.TryGetValue("scene_name", out var sceneName))
                    return sceneName.ToString();
                if (evt.Payload.TryGetValue("component_type", out var compType))
                    return compType.ToString();
                if (evt.Payload.TryGetValue("path", out var path))
                    return path.ToString();
            }
            // Fall back to target ID
            return evt.TargetId;
        }

        /// <summary>
        /// Format Unix timestamp to HH:mm:ss time string.
        /// </summary>
        private static string FormatTime(long timestampMs)
        {
            var dt = DateTimeOffset.FromUnixTimeMilliseconds(timestampMs).ToLocalTime();
            return dt.ToString("HH:mm:ss");
        }
    }
}
