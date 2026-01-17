using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditor;

namespace MCPForUnity.Editor.ActionTrace.Capture
{
    /// <summary>
    /// Rule-based filter configuration for event filtering.
    /// Rules are evaluated in order; first match wins.
    /// </summary>
    [Serializable]
    public sealed class FilterRule
    {
        public string Name;
        public bool Enabled = true;

        [Tooltip("Rule type: Prefix=Directory prefix match, Extension=File extension, Regex=Regular expression, GameObject=GameObject name")]
        public RuleType Type;

        [Tooltip("Pattern to match (e.g., 'Library/', '.meta', '.*\\.tmp$')")]
        public string Pattern;

        [Tooltip("Action when matched: Block=Filter out, Allow=Allow through")]
        public FilterAction Action = FilterAction.Block;

        [Tooltip("Priority for conflict resolution. Higher values evaluated first.")]
        public int Priority;

        [NonSerialized]
        private Regex _cachedRegex;

        private Regex GetRegex()
        {
            if (_cachedRegex != null) return _cachedRegex;
            if (!string.IsNullOrEmpty(Pattern))
            {
                try
                {
                    _cachedRegex = new Regex(Pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
                }
                catch
                {
                    // Invalid regex, return null
                }
            }
            return _cachedRegex;
        }

        public bool Matches(string path, string gameObjectName)
        {
            if (string.IsNullOrEmpty(Pattern)) return false;

            return Type switch
            {
                RuleType.Prefix => path?.StartsWith(Pattern, StringComparison.OrdinalIgnoreCase) == true,
                RuleType.Extension => path?.EndsWith(Pattern, StringComparison.OrdinalIgnoreCase) == true,
                RuleType.Regex => GetRegex()?.IsMatch(path ?? "") == true,
                RuleType.GameObject => GetRegex()?.IsMatch(gameObjectName ?? "") == true
                                    || gameObjectName?.Equals(Pattern, StringComparison.OrdinalIgnoreCase) == true,
                _ => false
            };
        }

        public void InvalidateCache()
        {
            _cachedRegex = null;
        }
    }

    /// <summary>
    /// Types of filter rules.
    /// </summary>
    public enum RuleType
    {
        Prefix,      // Directory prefix matching (fast)
        Extension,   // File extension matching (fast)
        Regex,       // Full regex pattern (slow, flexible)
        GameObject   // GameObject name matching
    }

    /// <summary>
    /// Filter action when a rule matches.
    /// </summary>
    public enum FilterAction
    {
        Block,   // Filter out the event
        Allow    // Allow the event through
    }

    /// <summary>
    /// Configurable event filter settings.
    /// Stored as part of ActionTraceSettings for persistence.
    /// </summary>
    [Serializable]
    public sealed class EventFilterSettings
    {
        [Tooltip("Custom filter rules. Evaluated in priority order.")]
        public List<FilterRule> CustomRules = new();

        [Tooltip("Enable default junk filters (Library/, Temp/, etc.)")]
        public bool EnableDefaultFilters = true;

        [Tooltip("Enable special handling for .meta files")]
        public bool EnableMetaFileHandling = true;

        [Tooltip("Minimum GameObject name length to avoid filtering unnamed objects")]
        public int MinGameObjectNameLength = 2;

        /// <summary>
        /// Get default built-in filter rules.
        /// These are always active when EnableDefaultFilters is true.
        /// </summary>
        public static readonly List<FilterRule> DefaultRules = new()
        {
            new() { Name = "Library Directory", Type = RuleType.Prefix, Pattern = "Library/", Action = FilterAction.Block, Priority = 100 },
            new() { Name = "Temp Directory", Type = RuleType.Prefix, Pattern = "Temp/", Action = FilterAction.Block, Priority = 100 },
            new() { Name = "obj Directory", Type = RuleType.Prefix, Pattern = "obj/", Action = FilterAction.Block, Priority = 100 },
            new() { Name = "Logs Directory", Type = RuleType.Prefix, Pattern = "Logs/", Action = FilterAction.Block, Priority = 100 },
            new() { Name = "__pycache__", Type = RuleType.Regex, Pattern = @"__pycache__", Action = FilterAction.Block, Priority = 100 },
            new() { Name = ".git Directory", Type = RuleType.Prefix, Pattern = ".git/", Action = FilterAction.Block, Priority = 100 },
            new() { Name = ".vs Directory", Type = RuleType.Prefix, Pattern = ".vs/", Action = FilterAction.Block, Priority = 100 },
            new() { Name = ".pyc Files", Type = RuleType.Extension, Pattern = ".pyc", Action = FilterAction.Block, Priority = 90 },
            new() { Name = ".pyo Files", Type = RuleType.Extension, Pattern = ".pyo", Action = FilterAction.Block, Priority = 90 },
            new() { Name = ".tmp Files", Type = RuleType.Extension, Pattern = ".tmp", Action = FilterAction.Block, Priority = 90 },
            new() { Name = ".temp Files", Type = RuleType.Extension, Pattern = ".temp", Action = FilterAction.Block, Priority = 90 },
            new() { Name = ".cache Files", Type = RuleType.Extension, Pattern = ".cache", Action = FilterAction.Block, Priority = 90 },
            new() { Name = ".bak Files", Type = RuleType.Extension, Pattern = ".bak", Action = FilterAction.Block, Priority = 90 },
            new() { Name = ".swp Files", Type = RuleType.Extension, Pattern = ".swp", Action = FilterAction.Block, Priority = 90 },
            new() { Name = ".DS_Store", Type = RuleType.Extension, Pattern = ".DS_Store", Action = FilterAction.Block, Priority = 90 },
            new() { Name = "Thumbs.db", Type = RuleType.Extension, Pattern = "Thumbs.db", Action = FilterAction.Block, Priority = 90 },
            new() { Name = ".csproj Files", Type = RuleType.Extension, Pattern = ".csproj", Action = FilterAction.Block, Priority = 80 },
            new() { Name = ".sln Files", Type = RuleType.Extension, Pattern = ".sln", Action = FilterAction.Block, Priority = 80 },
            new() { Name = ".suo Files", Type = RuleType.Extension, Pattern = ".suo", Action = FilterAction.Block, Priority = 80 },
            new() { Name = ".user Files", Type = RuleType.Extension, Pattern = ".user", Action = FilterAction.Block, Priority = 80 },
            new() { Name = "Unnamed GameObjects", Type = RuleType.Regex, Pattern = @"^GameObject\d+$", Action = FilterAction.Block, Priority = 70 },
            new() { Name = "Generated Colliders", Type = RuleType.Regex, Pattern = @"^Collider\d+$", Action = FilterAction.Block, Priority = 70 },
            new() { Name = "EditorOnly Objects", Type = RuleType.Prefix, Pattern = "EditorOnly", Action = FilterAction.Block, Priority = 70 },
        };

        /// <summary>
        /// Add a new custom rule.
        /// </summary>
        public FilterRule AddRule(string name, RuleType type, string pattern, FilterAction action, int priority = 50)
        {
            var rule = new FilterRule
            {
                Name = name,
                Type = type,
                Pattern = pattern,
                Action = action,
                Priority = priority,
                Enabled = true
            };
            CustomRules.Add(rule);
            return rule;
        }

        /// <summary>
        /// Remove a rule by name.
        /// </summary>
        public bool RemoveRule(string name)
        {
            var rule = CustomRules.Find(r => r.Name == name);
            if (rule != null)
            {
                CustomRules.Remove(rule);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Get all active rules (default + custom, sorted by priority).
        /// </summary>
        public List<FilterRule> GetActiveRules()
        {
            var rules = new List<FilterRule>();

            if (EnableDefaultFilters)
            {
                rules.AddRange(DefaultRules.Where(r => r.Enabled));
            }

            rules.AddRange(CustomRules.Where(r => r.Enabled));

            // Sort by priority descending (higher priority first)
            rules.Sort((a, b) => b.Priority.CompareTo(a.Priority));

            return rules;
        }
    }

    /// <summary>
    /// First line of defense: Capture-layer blacklist to filter out system junk.
    ///
    /// Philosophy: Blacklist at capture layer = "Record everything EXCEPT known garbage"
    /// - Preserves serendipity: AI can see unexpected but important changes
    /// - Protects memory: Prevents EventStore from filling with junk entries
    ///
    /// The filter now supports configurable rules via EventFilterSettings.
    /// Default rules are always applied unless explicitly disabled.
    /// Custom rules can be added for project-specific filtering.
    /// </summary>
    public static class EventFilter
    {
        private static EventFilterSettings _settings;

        /// <summary>
        /// Current filter settings.
        /// If null, default settings will be used.
        /// </summary>
        public static EventFilterSettings Settings
        {
            get => _settings ??= new EventFilterSettings();
            set => _settings = value;
        }

        /// <summary>
        /// Reset to default settings.
        /// </summary>
        public static void ResetToDefaults()
        {
            _settings = new EventFilterSettings();
        }

        // ========== Public API ==========

        /// <summary>
        /// Determines if a given path should be filtered as junk.
        ///
        /// Uses configured rules, evaluated in priority order.
        /// First matching rule decides the outcome.
        ///
        /// Returns: true if the path should be filtered out, false otherwise.
        /// </summary>
        public static bool IsJunkPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            var rules = Settings.GetActiveRules();

            foreach (var rule in rules)
            {
                if (rule.Matches(path, null))
                {
                    return rule.Action == FilterAction.Block;
                }
            }

            return false; // Default: allow through
        }

        /// <summary>
        /// Checks if an asset path should generate an event.
        /// This includes additional logic for assets beyond path filtering.
        /// </summary>
        public static bool ShouldTrackAsset(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return true;

            // Check base junk filter
            if (IsJunkPath(assetPath))
                return false;

            // Special handling for .meta files
            if (Settings.EnableMetaFileHandling && assetPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            {
                string basePath = assetPath.Substring(0, assetPath.Length - 5);

                // Track .meta for important asset types
                if (basePath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) ||
                    basePath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                return false; // Skip .meta for everything else
            }

            // Never filter assets in Resources folder
            if (assetPath.Contains("/Resources/", StringComparison.OrdinalIgnoreCase))
                return true;

            return true;
        }

        /// <summary>
        /// Checks if a GameObject name should be filtered.
        /// </summary>
        public static bool IsJunkGameObject(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            // Check minimum length
            if (name.Length < Settings.MinGameObjectNameLength)
                return true;

            var rules = Settings.GetActiveRules();

            foreach (var rule in rules)
            {
                // Only check GameObject-specific rules
                if (rule.Type == RuleType.GameObject || rule.Type == RuleType.Regex)
                {
                    if (rule.Matches(null, name))
                    {
                        return rule.Action == FilterAction.Block;
                    }
                }
            }

            return false;
        }

        // ========== Runtime Configuration ==========

        /// <summary>
        /// Adds a custom filter rule at runtime.
        /// </summary>
        public static FilterRule AddRule(string name, RuleType type, string pattern, FilterAction action, int priority = 50)
        {
            return Settings.AddRule(name, type, pattern, action, priority);
        }

        /// <summary>
        /// Adds a junk directory prefix at runtime.
        /// </summary>
        public static void AddJunkDirectoryPrefix(string prefix)
        {
            AddRule($"Custom: {prefix}", RuleType.Prefix, prefix, FilterAction.Block, 50);
        }

        /// <summary>
        /// Adds a junk file extension at runtime.
        /// </summary>
        public static void AddJunkExtension(string extension)
        {
            string ext = extension.StartsWith(".") ? extension : $".{extension}";
            AddRule($"Custom: {ext}", RuleType.Extension, ext, FilterAction.Block, 50);
        }

        /// <summary>
        /// Adds a regex pattern for junk matching at runtime.
        /// </summary>
        public static void AddJunkPattern(string regexPattern)
        {
            AddRule($"Custom Regex: {regexPattern}", RuleType.Regex, regexPattern, FilterAction.Block, 50);
        }

        /// <summary>
        /// Allow a specific path pattern (create an allow rule).
        /// </summary>
        public static void AllowPath(string pattern, int priority = 60)
        {
            AddRule($"Allow: {pattern}", RuleType.Regex, pattern, FilterAction.Allow, priority);
        }

        // ========== Diagnostic Info ==========

        /// <summary>
        /// Gets diagnostic information about the filter configuration.
        /// </summary>
        public static string GetDiagnosticInfo()
        {
            var rules = Settings.GetActiveRules();
            int blockRules = rules.Count(r => r.Action == FilterAction.Block);
            int allowRules = rules.Count(r => r.Action == FilterAction.Allow);

            return $"EventFilter Configuration:\n" +
                   $"  - Default Filters: {(Settings.EnableDefaultFilters ? "Enabled" : "Disabled")}\n" +
                   $"  - Meta File Handling: {(Settings.EnableMetaFileHandling ? "Enabled" : "Disabled")}\n" +
                   $"  - Total Rules: {rules.Count}\n" +
                   $"  - Block Rules: {blockRules}\n" +
                   $"  - Allow Rules: {allowRules}\n" +
                   $"  - Custom Rules: {Settings.CustomRules.Count}";
        }

        /// <summary>
        /// Test a path against all rules and return the result.
        /// Useful for debugging filter behavior.
        /// </summary>
        public static (bool filtered, FilterRule matchingRule) TestPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return (false, null);

            var rules = Settings.GetActiveRules();

            foreach (var rule in rules)
            {
                if (rule.Matches(path, null))
                {
                    return (rule.Action == FilterAction.Block, rule);
                }
            }

            return (false, null);
        }

        /// <summary>
        /// Get all rules that would match a given path.
        /// </summary>
        public static List<(FilterRule rule, bool wouldBlock)> GetMatchingRules(string path)
        {
            var result = new List<(FilterRule, bool)>();

            if (string.IsNullOrEmpty(path))
                return result;

            var rules = Settings.GetActiveRules();

            foreach (var rule in rules)
            {
                if (rule.Matches(path, null))
                {
                    result.Add((rule, rule.Action == FilterAction.Block));
                }
            }

            return result;
        }
    }
}
