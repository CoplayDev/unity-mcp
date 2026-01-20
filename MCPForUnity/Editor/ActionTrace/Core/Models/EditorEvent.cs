using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.ActionTrace.Analysis.Summarization;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json;

namespace MCPForUnity.Editor.ActionTrace.Core.Models
{
    /// <summary>
    /// Immutable class representing a single editor event.
    /// This is the "bedrock" layer - once written, never modified.
    ///
    /// Memory optimization (Pruning):
    /// - Payload can be null for old events (automatically dehydrated by EventStore)
    /// - PrecomputedSummary is always available, even when Payload is null
    /// - This reduces memory from ~10KB to ~100 bytes per old event
    ///
    /// Payload serialization constraints:
    /// - Only JSON-serializable types are allowed: string, number (int/long/float/double/decimal),
    ///   bool, null, array of these types, or Dictionary<string, object> with these value types.
    /// - Unsupported types (UnityEngine.Object, MonoBehaviour, etc.) are logged and skipped.
    /// </summary>
    public sealed class EditorEvent : IEquatable<EditorEvent>
    {
        // Limits to protect memory usage for payloads
        private const int MaxStringLength = 512; // truncate long strings
        private const int MaxCollectionItems = 64; // max items to keep in arrays/lists
        private const int MaxSanitizeDepth = 4; // prevent deep recursion

        /// <summary>
        /// Monotonically increasing sequence number for ordering.
        /// JSON property name: "sequence"
        /// </summary>
        [JsonProperty("sequence")]
        public long Sequence { get; }

        /// <summary>
        /// UTC timestamp in milliseconds since Unix epoch.
        /// JSON property name: "timestamp_unix_ms"
        /// </summary>
        [JsonProperty("timestamp_unix_ms")]
        public long TimestampUnixMs { get; }

        /// <summary>
        /// Event type identifier (e.g., "GameObjectCreated", "ComponentAdded").
        /// JSON property name: "type"
        /// </summary>
        [JsonProperty("type")]
        public string Type { get; }

        /// <summary>
        /// Target identifier (instance ID, asset GUID, or file path).
        /// JSON property name: "target_id"
        /// </summary>
        [JsonProperty("target_id")]
        public string TargetId { get; }

        /// <summary>
        /// Event payload containing additional context data.
        /// All values are guaranteed to be JSON-serializable.
        ///
        /// Can be null for old events (after dehydration).
        /// Use PrecomputedSummary instead when Payload is null.
        /// JSON property name: "payload"
        /// </summary>
        [JsonProperty("payload")]
        public IReadOnlyDictionary<string, object> Payload { get; }

        /// <summary>
        /// Precomputed summary for this event.
        /// Always available, even when Payload has been dehydrated (null).
        /// JSON property name: "precomputed_summary"
        /// </summary>
        [JsonProperty("precomputed_summary")]
        public string PrecomputedSummary { get; private set; }

        /// <summary>
        /// Whether this event's payload has been dehydrated (trimmed to save memory).
        /// JSON property name: "is_dehydrated"
        /// </summary>
        [JsonProperty("is_dehydrated")]
        public bool IsDehydrated { get; private set; }

        public EditorEvent(
            long sequence,
            long timestampUnixMs,
            string type,
            string targetId,
            IReadOnlyDictionary<string, object> payload)
        {
            Sequence = sequence;
            TimestampUnixMs = timestampUnixMs;
            Type = type ?? throw new ArgumentNullException(nameof(type));
            TargetId = targetId ?? throw new ArgumentNullException(nameof(targetId));

            // Validate and sanitize payload to ensure JSON-serializable types
            if (payload == null)
            {
                Payload = null;
                PrecomputedSummary = null;
                IsDehydrated = false;
            }
            else
            {
                Payload = SanitizePayload(payload, type);
                PrecomputedSummary = null; // Will be computed on first access or dehydration
                IsDehydrated = false;
            }
        }

        /// <summary>
        /// Constructor for creating a dehydrated (trimmed) event.
        /// Used internally by EventStore for memory optimization.
        /// </summary>
        private EditorEvent(
            long sequence,
            long timestampUnixMs,
            string type,
            string targetId,
            string precomputedSummary)
        {
            Sequence = sequence;
            TimestampUnixMs = timestampUnixMs;
            Type = type;
            TargetId = targetId;
            Payload = null;  // Dehydrated - no payload
            PrecomputedSummary = precomputedSummary;
            IsDehydrated = true;
        }

        /// <summary>
        /// Dehydrate this event to save memory.
        /// - Generates PrecomputedSummary from Payload
        /// - Sets Payload to null (releasing large objects)
        /// - Marks event as IsDehydrated
        ///
        /// Call this when event becomes "cold" (old but still needed for history).
        /// </summary>
        public EditorEvent Dehydrate()
        {
            if (IsDehydrated)
                return this;  // Already dehydrated

            // Generate summary if not already computed
            var summary = PrecomputedSummary ?? ComputeSummary();

            // Return new dehydrated event (immutable pattern)
            return new EditorEvent(
                Sequence,
                TimestampUnixMs,
                Type,
                TargetId,
                summary
            );
        }

        /// <summary>
        /// Get the precomputed summary, computing it if necessary.
        /// This is lazy-evaluated to avoid unnecessary computation.
        /// </summary>
        public string GetSummary()
        {
            if (PrecomputedSummary != null)
                return PrecomputedSummary;

            // Compute and cache (this mutates the object, but it's just a string field)
            PrecomputedSummary = ComputeSummary();
            return PrecomputedSummary;
        }

        /// <summary>
        /// Compute the summary for this event.
        /// This is called by GetSummary() or Dehydrate().
        /// Delegates to EventSummarizer for rich summaries.
        /// </summary>
        private string ComputeSummary()
        {
            return EventSummarizer.Summarize(this);
        }

        /// <summary>
        /// Validate and sanitize payload values to ensure JSON serializability.
        /// Converts values to safe types and logs warnings for unsupported types.
        /// </summary>
        private static Dictionary<string, object> SanitizePayload(
            IReadOnlyDictionary<string, object> payload,
            string eventType)
        {
            var sanitized = new Dictionary<string, object>();

            foreach (var kvp in payload)
            {
                var value = SanitizeValue(kvp.Value, kvp.Key, eventType, 0);
                if (value != null || kvp.Value == null)
                {
                    // Only add if not filtered out (null values are allowed)
                    sanitized[kvp.Key] = value;
                }
            }

            return sanitized;
        }

        /// <summary>
        /// Recursively validate and sanitize a single value.
        /// Returns null for unsupported types (which will be filtered out).
        /// </summary>
        private static object SanitizeValue(object value, string key, string eventType, int depth)
        {
            if (value == null)
                return null;

            if (depth > MaxSanitizeDepth)
            {
                // Depth exceeded: return placeholder to avoid deep structures
                return "<truncated:depth>";
            }

            // Primitive JSON-serializable types
            if (value is string s)
            {
                if (s.Length > MaxStringLength)
                    return s.Substring(0, MaxStringLength) + "...";
                return s;
            }
            if (value is bool)
                return value;

            // Numeric types - convert to consistent types
            if (value is int i) return i;
            if (value is long l) return l;
            if (value is float f) return f;
            if (value is double d) return d;
            if (value is decimal m) return m;
            if (value is uint ui) return ui;
            if (value is ulong ul) return ul;
            if (value is short sh) return sh;
            if (value is ushort ush) return ush;
            if (value is byte b) return b;
            if (value is sbyte sb) return sb;
            if (value is char c) return c.ToString();  // Char as string

            // Arrays - handle native arrays (int[], string[], etc.)
            if (value.GetType().IsArray)
            {
                return SanitizeArray((Array)value, key, eventType, depth + 1);
            }

            // Generic collections - use non-generic interface for broader compatibility
            // This handles List<T>, IEnumerable<T>, HashSet<T>, etc. with any element type
            if (value is IEnumerable enumerable && !(value is string) && !(value is IDictionary))
            {
                return SanitizeEnumerable(enumerable, key, eventType, depth + 1);
            }

            // Dictionaries - use non-generic interface for broader compatibility
            // This handles Dictionary<K,V> with any value type
            if (value is IDictionary dict)
            {
                return SanitizeDictionary(dict, key, eventType, depth + 1);
            }

            // Unsupported type - log warning and filter out
            McpLog.Warn(
                $"[EditorEvent] Unsupported payload type '{value.GetType().Name}' " +
                $"for key '{key}' in event '{eventType}'. Value will be excluded from payload. " +
                $"Supported types: string, number, bool, null, array, List, Dictionary.");

            return null;  // Filter out unsupported types
        }

        /// <summary>
        /// Sanitize a native array.
        /// </summary>
        private static object SanitizeArray(Array array, string key, string eventType, int depth)
        {
            var list = new List<object>(Math.Min(array.Length, MaxCollectionItems));
            int count = 0;
            foreach (var item in array)
            {
                if (count++ >= MaxCollectionItems)
                {
                    list.Add("<truncated:more_items>");
                    break;
                }
                var sanitized = SanitizeValue(item, key, eventType, depth);
                if (sanitized != null || item == null)
                {
                    list.Add(sanitized);
                }
            }
            return list;
        }

        /// <summary>
        /// Sanitize a generic IEnumerable (List<T>, IEnumerable<T>, etc.)
        /// Uses non-generic interface to handle any element type.
        /// </summary>
        private static object SanitizeEnumerable(IEnumerable enumerable, string key, string eventType, int depth)
        {
            var list = new List<object>(MaxCollectionItems);
            int count = 0;
            foreach (var item in enumerable)
            {
                if (count++ >= MaxCollectionItems)
                {
                    list.Add("<truncated:more_items>");
                    break;
                }
                var sanitized = SanitizeValue(item, key, eventType, depth);
                if (sanitized != null || item == null)
                {
                    list.Add(sanitized);
                }
            }
            return list;
        }

        /// <summary>
        /// Sanitize a generic IDictionary (Dictionary<K,V>, etc.)
        /// Uses non-generic interface to handle any key/value types.
        /// Only string keys are supported; other key types are skipped with warning.
        /// </summary>
        private static object SanitizeDictionary(IDictionary dict, string key, string eventType, int depth)
        {
            var result = new Dictionary<string, object>(Math.Min(dict.Count, MaxCollectionItems));
            int count = 0;
            foreach (DictionaryEntry entry in dict)
            {
                if (count++ >= MaxCollectionItems)
                {
                    result["<truncated>"] = "more_items";
                    break;
                }

                // Only support string keys
                if (entry.Key is string stringKey)
                {
                    var sanitizedValue = SanitizeValue(entry.Value, stringKey, eventType, depth);
                    if (sanitizedValue != null || entry.Value == null)
                    {
                        result[stringKey] = sanitizedValue;
                    }
                }
                else
                {
                    McpLog.Warn(
                        $"[EditorEvent] Dictionary key type '{entry.Key?.GetType().Name}' " +
                        $"is not supported. Only string keys are supported. Key will be skipped.");
                }
            }

            return result;
        }

        // ========================================================================
        // FORBIDDEN FIELDS - Do NOT add these properties to EditorEvent:
        // - Importance: Calculate at query time, not stored
        // - Source/AI/Human flags: Use Context layer (ContextMapping side-table)
        // - SessionId: Use Context layer
        // - _ctx: Use Context layer
        // These are intentionally omitted to keep the event layer pure.
        // ========================================================================

        public bool Equals(EditorEvent other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Sequence == other.Sequence
                && TimestampUnixMs == other.TimestampUnixMs
                && Type == other.Type
                && TargetId == other.TargetId;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as EditorEvent);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Sequence, TimestampUnixMs, Type, TargetId);
        }

        public static bool operator ==(EditorEvent left, EditorEvent right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(EditorEvent left, EditorEvent right)
        {
            return !Equals(left, right);
        }
    }
}
