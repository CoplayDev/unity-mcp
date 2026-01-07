#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MCPForUnity.Editor.Helpers;
using UnityEngine;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Component resolver that delegates to UnityTypeResolver.
    /// Kept for backwards compatibility.
    /// </summary>
    internal static class ComponentResolver
    {
        /// <summary>
        /// Resolve a Component/MonoBehaviour type by short or fully-qualified name.
        /// Delegates to UnityTypeResolver.TryResolve with Component constraint.
        /// </summary>
        public static bool TryResolve(string nameOrFullName, out Type type, out string error)
        {
            return UnityTypeResolver.TryResolve(nameOrFullName, out type, out error, typeof(Component));
        }

        /// <summary>
        /// Gets all accessible property and field names from a component type.
        /// </summary>
        public static List<string> GetAllComponentProperties(Type componentType)
        {
            if (componentType == null) return new List<string>();

            var properties = componentType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                         .Where(p => p.CanRead && p.CanWrite)
                                         .Select(p => p.Name);

            var fields = componentType.GetFields(BindingFlags.Public | BindingFlags.Instance)
                                     .Where(f => !f.IsInitOnly && !f.IsLiteral)
                                     .Select(f => f.Name);

            // Also include SerializeField private fields (common in Unity)
            var serializeFields = componentType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                                              .Where(f => f.GetCustomAttribute<SerializeField>() != null)
                                              .Select(f => f.Name);

            return properties.Concat(fields).Concat(serializeFields).Distinct().OrderBy(x => x).ToList();
        }

        /// <summary>
        /// Suggests the most likely property matches for a user's input using fuzzy matching.
        /// Uses Levenshtein distance, substring matching, and common naming pattern heuristics.
        /// </summary>
        public static List<string> GetFuzzyPropertySuggestions(string userInput, List<string> availableProperties)
        {
            if (string.IsNullOrWhiteSpace(userInput) || !availableProperties.Any())
                return new List<string>();

            var cacheKey = $"{userInput.ToLowerInvariant()}:{string.Join(",", availableProperties)}";
            if (PropertySuggestionCache.TryGetValue(cacheKey, out var cached))
                return cached;

            try
            {
                var suggestions = GetRuleBasedSuggestions(userInput, availableProperties);
                PropertySuggestionCache[cacheKey] = suggestions;
                return suggestions;
            }
            catch (Exception ex)
            {
                McpLog.Warn($"[Property Matching] Error getting suggestions for '{userInput}': {ex.Message}");
                return new List<string>();
            }
        }

        private static readonly Dictionary<string, List<string>> PropertySuggestionCache = new();

        /// <summary>
        /// Rule-based suggestions that mimic AI behavior for property matching.
        /// This provides immediate value while we could add real AI integration later.
        /// </summary>
        private static List<string> GetRuleBasedSuggestions(string userInput, List<string> availableProperties)
        {
            var suggestions = new List<string>();
            var cleanedInput = userInput.ToLowerInvariant().Replace(" ", "").Replace("-", "").Replace("_", "");

            foreach (var property in availableProperties)
            {
                var cleanedProperty = property.ToLowerInvariant().Replace(" ", "").Replace("-", "").Replace("_", "");

                if (cleanedProperty == cleanedInput)
                {
                    suggestions.Add(property);
                    continue;
                }

                var inputWords = userInput.ToLowerInvariant().Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
                if (inputWords.All(word => cleanedProperty.Contains(word.ToLowerInvariant())))
                {
                    suggestions.Add(property);
                    continue;
                }

                if (LevenshteinDistance(cleanedInput, cleanedProperty) <= Math.Max(2, cleanedInput.Length / 4))
                {
                    suggestions.Add(property);
                }
            }

            return suggestions.OrderBy(s => LevenshteinDistance(cleanedInput, s.ToLowerInvariant().Replace(" ", "")))
                             .Take(3)
                             .ToList();
        }

        /// <summary>
        /// Calculates Levenshtein distance between two strings for similarity matching.
        /// </summary>
        private static int LevenshteinDistance(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1)) return s2?.Length ?? 0;
            if (string.IsNullOrEmpty(s2)) return s1.Length;

            var matrix = new int[s1.Length + 1, s2.Length + 1];

            for (int i = 0; i <= s1.Length; i++) matrix[i, 0] = i;
            for (int j = 0; j <= s2.Length; j++) matrix[0, j] = j;

            for (int i = 1; i <= s1.Length; i++)
            {
                for (int j = 1; j <= s2.Length; j++)
                {
                    int cost = (s2[j - 1] == s1[i - 1]) ? 0 : 1;
                    matrix[i, j] = Math.Min(Math.Min(
                        matrix[i - 1, j] + 1,
                        matrix[i, j - 1] + 1),
                        matrix[i - 1, j - 1] + cost);
                }
            }

            return matrix[s1.Length, s2.Length];
        }
    }
}
